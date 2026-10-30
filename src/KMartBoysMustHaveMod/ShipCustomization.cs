using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace KMartBoysMustHaveMod
{
    public enum CustomImageSource
    {
        Everyone,
        TrustedPlayersOnly,
        Off
    }

    // ------------------------------------------------------------------
    // Custom sail / banner images (from a URL) and ship names.
    //
    // Sync: values live in the ship's or banner's own network data (ZDO), which
    // vanilla Valheim already replicates to every player and saves with the world.
    // No server mod needed. Players without this mod just see normal sails.
    //
    // Writes go to whoever owns the object: directly if that's us, by claiming it
    // if nobody is steering, otherwise via a small RPC to the current owner. Viewing
    // never touches ownership (so ships don't rubber-band).
    //
    // The sail URL key matches the original Gorko mod, so existing sails keep working.
    // ------------------------------------------------------------------
    internal static class ShipData
    {
        public const string ImageUrlKey = "GORKO_Lives"; // same key as Gorko
        public const string ImageByKey = "KMartBoys_ImageBy";
        public const string ShipNameKey = "KMartBoys_ShipName";
        public const string SetStringRpc = "KMartBoys_SetString";

        public const int UrlCharLimit = 500;
        public const int NameCharLimit = 40;

        private static readonly MethodInfo ShipHaveControllingPlayer =
            AccessTools.Method(typeof(Ship), "HaveControllingPlayer");

        public static string Get(ZNetView nview, string key)
        {
            if (nview == null || !nview.IsValid()) return "";
            return nview.GetZDO().GetString(key, "") ?? "";
        }

        public static void RegisterRpc(ZNetView nview)
        {
            if (nview == null || !nview.IsValid()) return;
            nview.Register<string, string>(SetStringRpc, (sender, key, value) =>
            {
                if (!nview.IsValid() || !nview.IsOwner()) return;
                if (key != ImageUrlKey && key != ImageByKey && key != ShipNameKey) return; // only our keys
                nview.GetZDO().Set(key, Clamp(value, key == ShipNameKey ? NameCharLimit : UrlCharLimit));
            });
        }

        public static void Set(ZNetView nview, Ship ship, string key, string value)
        {
            if (nview == null || !nview.IsValid()) return;
            value = Clamp(value, key == ShipNameKey ? NameCharLimit : UrlCharLimit);

            if (!nview.IsOwner() && !SomeoneIsSteering(ship)) nview.ClaimOwnership();

            if (nview.IsOwner()) nview.GetZDO().Set(key, value);
            else nview.InvokeRPC(SetStringRpc, key, value); // owner (the helmsman) applies it
        }

        private static bool SomeoneIsSteering(Ship ship)
        {
            if (ship == null || ShipHaveControllingPlayer == null) return false;
            try
            {
                return (bool)ShipHaveControllingPlayer.Invoke(ship, null);
            }
            catch
            {
                return true; // be conservative: don't steal a ship we're unsure about
            }
        }

        private static string Clamp(string s, int max)
        {
            if (s == null) return "";
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        public static bool ModifierHeld()
        {
            KeyCode key = KMartBoysPlugin.ShipEditModifier.Value;
            return key == KeyCode.None || ZInput.GetKey(key, false);
        }

        public static string KeyLabel()
        {
            KeyCode key = KMartBoysPlugin.ShipEditModifier.Value;
            return key == KeyCode.None ? "" : key + "+";
        }
    }

    // Downloads each image URL once and shares the texture between all ships/banners.
    internal static class ImageCache
    {
        private const int MaxBytes = 8 * 1024 * 1024;
        private const float RetryFailedAfter = 60f;

        private static readonly Dictionary<string, Texture2D> Loaded = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, float> Failed = new Dictionary<string, float>();
        private static readonly Dictionary<string, List<Action<Texture2D>>> Pending =
            new Dictionary<string, List<Action<Texture2D>>>();

        public static void Get(string url, Action<Texture2D> done)
        {
            Texture2D tex;
            if (Loaded.TryGetValue(url, out tex) && tex != null)
            {
                done(tex);
                return;
            }

            float failedAt;
            if (Failed.TryGetValue(url, out failedAt) && Time.unscaledTime - failedAt < RetryFailedAfter) return;

            List<Action<Texture2D>> waiting;
            if (Pending.TryGetValue(url, out waiting))
            {
                waiting.Add(done);
                return;
            }

            Pending[url] = new List<Action<Texture2D>> { done };
            if (KMartBoysPlugin.Instance != null) KMartBoysPlugin.Instance.StartCoroutine(Download(url));
        }

        private static IEnumerator Download(string url)
        {
            Texture2D result = null;
            using (UnityWebRequest uwr = UnityWebRequest.Get(url))
            {
                uwr.timeout = 20;
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success && uwr.downloadHandler != null)
                {
                    byte[] data = uwr.downloadHandler.data;
                    if (data != null && data.Length > 0 && data.Length <= MaxBytes)
                    {
                        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                        if (ImageConversion.LoadImage(tex, data))
                        {
                            tex.name = "KMartBoys_CustomImage";
                            tex.wrapMode = TextureWrapMode.Clamp;
                            result = tex;
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(tex);
                        }
                    }
                }

                if (result == null)
                    KMartBoysPlugin.Log("Couldn't load image from " + url + " (" + (uwr.error ?? "not a PNG/JPG or too large") + ")");
            }

            List<Action<Texture2D>> waiting;
            Pending.TryGetValue(url, out waiting);
            Pending.Remove(url);

            if (result == null)
            {
                Failed[url] = Time.unscaledTime;
                yield break;
            }

            Loaded[url] = result;
            if (waiting == null) yield break;
            foreach (Action<Texture2D> cb in waiting)
            {
                try
                {
                    cb(result);
                }
                catch (Exception e)
                {
                    KMartBoysPlugin.LogOnce("imagecb", "Applying custom image failed: " + e.Message);
                }
            }
        }
    }

    // Shared logic: watch the object's URL and keep its renderer's texture in sync.
    internal abstract class CustomImageBehaviour : MonoBehaviour
    {
        private const float CheckEvery = 1f;

        protected ZNetView NView;
        private Renderer _renderer;
        private Texture _originalTexture;
        private bool _haveOriginal;
        private string _shownUrl = "";
        private string _wantedUrl = "";

        protected abstract Renderer FindRenderer();

        // Hooks for subclasses (e.g. banners swap in a mesh with a clean UV layout).
        protected virtual void OnImageApplied(Renderer r)
        {
        }

        protected virtual void OnImageRestored(Renderer r)
        {
        }

        // Re-applies the current image (used when a related setting changes).
        protected void Reapply()
        {
            _wantedUrl = null;
        }

        protected virtual void Awake()
        {
            NView = GetComponent<ZNetView>();
            if (NView == null || !NView.IsValid()) return;
            ShipData.RegisterRpc(NView);
            InvokeRepeating(nameof(CheckImage), UnityEngine.Random.Range(0.5f, 1.5f), CheckEvery);
        }

        private void CheckImage()
        {
            try
            {
                if (NView == null || !NView.IsValid()) return;

                string url = ShipData.Get(NView, ShipData.ImageUrlKey);
                if (!Allowed(url)) url = "";
                if (url == _wantedUrl) return;
                _wantedUrl = url;

                if (url.Length == 0)
                {
                    Restore();
                    return;
                }

                ImageCache.Get(url, tex =>
                {
                    if (this == null || _wantedUrl != url) return; // destroyed or changed meanwhile
                    Apply(tex, url);
                });
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("customimage", "Custom image check failed: " + e.Message);
            }
        }

        private bool Allowed(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!KMartBoysPlugin.ModEnabled.Value) return false;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;

            switch (KMartBoysPlugin.ImagesFrom.Value)
            {
                case CustomImageSource.Off:
                    return false;
                case CustomImageSource.TrustedPlayersOnly:
                    string by = ShipData.Get(NView, ShipData.ImageByKey);
                    if (by.Length == 0) return false;
                    if (Player.m_localPlayer != null && by == Player.m_localPlayer.GetPlayerName()) return true;
                    foreach (string name in (KMartBoysPlugin.TrustedPlayers.Value ?? "").Split(','))
                        if (name.Trim().Length > 0 && string.Equals(name.Trim(), by, StringComparison.OrdinalIgnoreCase))
                            return true;
                    return false;
                default:
                    return true;
            }
        }

        private Renderer GetRenderer()
        {
            if (_renderer == null) _renderer = FindRenderer();
            return _renderer;
        }

        private void Apply(Texture2D tex, string url)
        {
            Renderer r = GetRenderer();
            if (r == null || tex == null) return;
            if (!_haveOriginal)
            {
                _originalTexture = r.sharedMaterial != null ? r.sharedMaterial.GetTexture("_MainTex") : null;
                _haveOriginal = true;
            }
            r.material.SetTexture("_MainTex", tex);
            _shownUrl = url;
            OnImageApplied(r);
        }

        private void Restore()
        {
            if (_shownUrl.Length == 0) return;
            Renderer r = GetRenderer();
            if (r != null && _haveOriginal) r.material.SetTexture("_MainTex", _originalTexture);
            _shownUrl = "";
            if (r != null) OnImageRestored(r);
        }

        protected void RequestImageUrl()
        {
            if (TextInput.instance == null) return;
            TextInput.instance.RequestText(new TextCallback(
                () => ShipData.Get(NView, ShipData.ImageUrlKey),
                text =>
                {
                    ShipData.Set(NView, GetComponent<Ship>(), ShipData.ImageUrlKey, text);
                    if (Player.m_localPlayer != null)
                        ShipData.Set(NView, GetComponent<Ship>(), ShipData.ImageByKey, Player.m_localPlayer.GetPlayerName());
                }), "Image URL (PNG/JPG, empty = default)", ShipData.UrlCharLimit);
        }
    }

    // Small adapter so one object can ask for more than one kind of text.
    internal class TextCallback : TextReceiver
    {
        private readonly Func<string> _get;
        private readonly Action<string> _set;

        public TextCallback(Func<string> get, Action<string> set)
        {
            _get = get;
            _set = set;
        }

        public string GetText()
        {
            return _get();
        }

        public void SetText(string text)
        {
            _set(text);
        }
    }

    // Lives on every ship: custom sail image + ship name.
    internal class ShipCustomizer : CustomImageBehaviour, Hoverable, Interactable
    {
        private Ship _ship;

        protected override void Awake()
        {
            _ship = GetComponent<Ship>();
            base.Awake();
        }

        protected override Renderer FindRenderer()
        {
            Renderer fallback = null;
            Transform root = _ship != null && _ship.m_sailObject != null ? _ship.m_sailObject.transform : transform;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "sail_full") return r;
                if (fallback == null && r is SkinnedMeshRenderer) fallback = r;
            }
            return fallback;
        }

        public string ShipName
        {
            get { return KMartBoysPlugin.ShipNamesEnabled.Value ? ShipData.Get(NView, ShipData.ShipNameKey) : ""; }
        }

        public string GetHoverText()
        {
            if (!KMartBoysPlugin.ModEnabled.Value) return "";
            string name = ShipName;
            string text = name.Length > 0 ? "<b>" + name + "</b>" : "";

            if (ShipData.ModifierHeld() && PrivateArea.CheckAccess(transform.position, 0f, false, false))
            {
                string mod = ShipData.KeyLabel();
                if (KMartBoysPlugin.ImagesFrom.Value != CustomImageSource.Off)
                    text += "\n[<color=yellow><b>" + mod + "$KEY_Use</b></color>] Set sail image";
                if (KMartBoysPlugin.ShipNamesEnabled.Value)
                    text += "\n[<color=yellow><b>" + mod + "$KEY_AltPlace + $KEY_Use</b></color>] Name ship";
            }
            return Localization.instance != null ? Localization.instance.Localize(text.Trim('\n')) : text.Trim('\n');
        }

        public string GetHoverName()
        {
            return ShipName;
        }

        public float GetHoverOffset()
        {
            return 0f;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !KMartBoysPlugin.ModEnabled.Value || !ShipData.ModifierHeld()) return false;
            if (user == null || user != Player.m_localPlayer) return false;
            if (!PrivateArea.CheckAccess(transform.position, 0f, true, false)) return false;

            if (alt)
            {
                if (!KMartBoysPlugin.ShipNamesEnabled.Value || TextInput.instance == null) return false;
                TextInput.instance.RequestText(new TextCallback(
                    () => ShipData.Get(NView, ShipData.ShipNameKey),
                    text => ShipData.Set(NView, _ship, ShipData.ShipNameKey, text)),
                    "Ship name", ShipData.NameCharLimit);
                return true;
            }

            if (KMartBoysPlugin.ImagesFrom.Value == CustomImageSource.Off) return false;
            RequestImageUrl();
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }

        public void SetSailUrl(string url)
        {
            ShipData.Set(NView, _ship, ShipData.ImageUrlKey, url);
            if (Player.m_localPlayer != null)
                ShipData.Set(NView, _ship, ShipData.ImageByKey, Player.m_localPlayer.GetPlayerName());
        }
    }

    // Lives on banners: custom banner image.
    //
    // Vanilla banner cloth meshes have a UV layout made for the game's own
    // texture (half the cloth mirrors the other half), so a normal picture shows
    // up cut in half and mirrored. While a custom image is shown we swap in a
    // copy of the mesh whose UVs are a flat projection across the whole cloth,
    // so the full picture covers it once. If the game's mesh can't be read we
    // fall back to a flat two-sided panel of the same size.
    internal class BannerCustomizer : CustomImageBehaviour, Hoverable, Interactable
    {
        private MeshFilter _meshFilter;
        private Mesh _originalMesh;
        private Mesh _customMesh;
        private bool _customFlipped;
        private Vector2 _origScale = Vector2.one;
        private Vector2 _origOffset = Vector2.zero;
        private bool _haveOrigST;
        private bool _lastFix;
        private bool _lastFlip;
        private static bool _loggedMode;

        private void Update()
        {
            // Re-apply when the fit/flip settings change.
            bool fix = KMartBoysPlugin.BannerFitImage.Value;
            bool flip = KMartBoysPlugin.BannerFlipImage.Value;
            if (fix != _lastFix || flip != _lastFlip)
            {
                _lastFix = fix;
                _lastFlip = flip;
                Reapply();
            }
        }

        protected override void OnImageApplied(Renderer r)
        {
            Material mat = r.material;
            if (!_haveOrigST)
            {
                _origScale = mat.GetTextureScale("_MainTex");
                _origOffset = mat.GetTextureOffset("_MainTex");
                _haveOrigST = true;
            }

            if (!KMartBoysPlugin.BannerFitImage.Value)
            {
                RestoreMesh();
                mat.SetTextureScale("_MainTex", _origScale);
                mat.SetTextureOffset("_MainTex", _origOffset);
                return;
            }

            // Our UVs cover 0..1, so drop any atlas tiling/offset the banner material uses.
            mat.SetTextureScale("_MainTex", Vector2.one);
            mat.SetTextureOffset("_MainTex", Vector2.zero);

            if (_meshFilter == null) _meshFilter = r.GetComponent<MeshFilter>();
            if (_meshFilter == null) return;
            if (_originalMesh == null) _originalMesh = _meshFilter.sharedMesh;
            if (_originalMesh == null) return;

            bool flip = KMartBoysPlugin.BannerFlipImage.Value;
            if (_customMesh == null || _customFlipped != flip)
            {
                if (_customMesh != null) Destroy(_customMesh);
                _customMesh = BuildMesh(_originalMesh, flip);
                _customFlipped = flip;
            }
            if (_customMesh != null) _meshFilter.sharedMesh = _customMesh;
        }

        protected override void OnImageRestored(Renderer r)
        {
            RestoreMesh();
            if (_haveOrigST)
            {
                r.material.SetTextureScale("_MainTex", _origScale);
                r.material.SetTextureOffset("_MainTex", _origOffset);
            }
        }

        private void RestoreMesh()
        {
            if (_meshFilter != null && _originalMesh != null) _meshFilter.sharedMesh = _originalMesh;
        }

        private void OnDestroy()
        {
            if (_customMesh != null) Destroy(_customMesh);
        }

        // Axes of the cloth: the thinnest is the normal; vertical (Y) is "up" when possible.
        private static void PickAxes(Bounds b, out int uAxis, out int vAxis, out int nAxis)
        {
            Vector3 size = b.size;
            nAxis = 0;
            if (size[1] < size[nAxis]) nAxis = 1;
            if (size[2] < size[nAxis]) nAxis = 2;
            vAxis = nAxis != 1 ? 1 : (size[0] >= size[2] ? 0 : 2);
            uAxis = 3 - nAxis - vAxis;
        }

        private static Mesh BuildMesh(Mesh src, bool flip)
        {
            Bounds b = src.bounds;
            int u, v, n;
            PickAxes(b, out u, out v, out n);
            Vector3 min = b.min, size = b.size;
            float su = Mathf.Max(size[u], 0.0001f), sv = Mathf.Max(size[v], 0.0001f);

            if (src.isReadable)
            {
                Mesh m = Instantiate(src);
                m.name = src.name + "_KMartBoysFit";
                Vector3[] verts = m.vertices;
                Vector2[] uvs = new Vector2[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    float uu = (verts[i][u] - min[u]) / su;
                    float vv = (verts[i][v] - min[v]) / sv;
                    uvs[i] = new Vector2(flip ? 1f - uu : uu, vv);
                }
                m.uv = uvs;
                LogMode("remapped the banner's own mesh");
                return m;
            }

            LogMode("the banner mesh can't be read, using a flat panel");
            return BuildPanel(b, u, v, n, flip);
        }

        // Two-sided flat panel covering the cloth's bounds; the image reads correctly from both sides.
        private static Mesh BuildPanel(Bounds b, int u, int v, int n, bool flip)
        {
            Vector3 c = b.center;
            Vector3 eu = Vector3.zero, ev = Vector3.zero;
            eu[u] = b.extents[u];
            ev[v] = b.extents[v];

            Vector3[] corners =
            {
                c - eu - ev, c + eu - ev, c + eu + ev, c - eu + ev
            };
            Vector3[] verts = new Vector3[8];
            Vector2[] uvs = new Vector2[8];
            for (int i = 0; i < 4; i++)
            {
                verts[i] = corners[i];
                verts[i + 4] = corners[i];
            }
            float a = flip ? 1f : 0f, z = flip ? 0f : 1f;
            uvs[0] = new Vector2(a, 0f); uvs[1] = new Vector2(z, 0f); uvs[2] = new Vector2(z, 1f); uvs[3] = new Vector2(a, 1f);
            // Back side: mirrored U so the picture isn't backwards when seen from behind.
            uvs[4] = new Vector2(z, 0f); uvs[5] = new Vector2(a, 0f); uvs[6] = new Vector2(a, 1f); uvs[7] = new Vector2(z, 1f);

            Mesh m = new Mesh { name = "KMartBoys_BannerPanel" };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 };
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        private static void LogMode(string what)
        {
            if (_loggedMode) return;
            _loggedMode = true;
            KMartBoysPlugin.Log("Custom banner image: " + what + ".");
        }

        protected override Renderer FindRenderer()
        {
            Renderer fallback = null;
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name == "default") return r;
                if (fallback == null) fallback = r;
            }
            return fallback;
        }

        public string GetHoverText()
        {
            if (!KMartBoysPlugin.ModEnabled.Value || KMartBoysPlugin.ImagesFrom.Value == CustomImageSource.Off) return "";
            if (!ShipData.ModifierHeld() || !PrivateArea.CheckAccess(transform.position, 0f, false, false)) return "";
            string text = "[<color=yellow><b>" + ShipData.KeyLabel() + "$KEY_Use</b></color>] Set banner image";
            return Localization.instance != null ? Localization.instance.Localize(text) : text;
        }

        public string GetHoverName()
        {
            return "";
        }

        public float GetHoverOffset()
        {
            return 0f;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !KMartBoysPlugin.ModEnabled.Value || !ShipData.ModifierHeld()) return false;
            if (KMartBoysPlugin.ImagesFrom.Value == CustomImageSource.Off) return false;
            if (user == null || user != Player.m_localPlayer) return false;
            if (!PrivateArea.CheckAccess(transform.position, 0f, true, false)) return false;
            RequestImageUrl();
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }
    }

    // ---- Hooks ----

    [HarmonyPatch(typeof(Ship), "Awake")]
    internal static class Ship_Awake_Customizer_Patch
    {
        private static void Postfix(Ship __instance)
        {
            if (__instance.GetComponent<ShipCustomizer>() == null) __instance.gameObject.AddComponent<ShipCustomizer>();
        }
    }

    [HarmonyPatch(typeof(Piece), "Awake")]
    internal static class Piece_Awake_Banner_Patch
    {
        private static void Postfix(Piece __instance)
        {
            if (__instance.m_comfortGroup != Piece.ComfortGroup.Banner) return;
            if (__instance.GetComponent<ZNetView>() == null) return;
            if (__instance.GetComponent<BannerCustomizer>() == null) __instance.gameObject.AddComponent<BannerCustomizer>();
        }
    }

    // Taking the helm applies your favorite sail image (if you set one).
    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.Interact))]
    internal static class ShipControlls_Interact_Favorite_Patch
    {
        private static void Postfix(ShipControlls __instance, Humanoid character, bool repeat, bool __result)
        {
            try
            {
                if (!__result || repeat || character == null || character != Player.m_localPlayer) return;
                if (!KMartBoysPlugin.ModEnabled.Value || !KMartBoysPlugin.ApplyFavoriteWhenSteering.Value) return;
                string url = (KMartBoysPlugin.FavoriteSailUrl.Value ?? "").Trim();
                if (url.Length == 0) return;

                Ship ship = __instance.m_ship != null ? __instance.m_ship : __instance.GetComponentInParent<Ship>();
                ShipCustomizer custom = ship != null ? ship.GetComponent<ShipCustomizer>() : null;
                if (custom == null) return;
                if (ShipData.Get(ship.GetComponent<ZNetView>(), ShipData.ImageUrlKey) == url) return;
                custom.SetSailUrl(url);
            }
            catch (Exception e)
            {
                KMartBoysPlugin.LogOnce("favoritesail", "Favorite sail failed: " + e.Message);
            }
        }
    }

    // Show the ship's name at the top of the helm's hover text.
    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.GetHoverText))]
    internal static class ShipControlls_GetHoverText_Name_Patch
    {
        private static void Postfix(ShipControlls __instance, ref string __result)
        {
            if (!KMartBoysPlugin.ModEnabled.Value || !KMartBoysPlugin.ShipNamesEnabled.Value) return;
            Ship ship = __instance.m_ship != null ? __instance.m_ship : __instance.GetComponentInParent<Ship>();
            ShipCustomizer custom = ship != null ? ship.GetComponent<ShipCustomizer>() : null;
            if (custom == null) return;
            string name = custom.ShipName;
            if (name.Length > 0) __result = "<b>" + name + "</b>\n" + __result;
        }
    }
}
