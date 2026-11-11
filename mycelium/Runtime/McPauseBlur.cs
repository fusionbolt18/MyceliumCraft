using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's pause look (blurred, less colourful) while Minecraft's pause menu is open.
    ///
    /// World: Mycelium's own pause menu has a global Volume with its "BlurVolume Profile" (a
    /// depth-of-field blur focused 0.1 units away, so everything is out of focus, plus a colour
    /// adjustment); this switches on a Volume of its own with the same profile and settings.
    ///
    /// UI: that blur skips Mycelium's HUD (overlay UI), and how much it blurs depends on the depth
    /// behind each pixel, so UI routed through it vanished in front of the sky (your report,
    /// 2026-10-18). Instead, a hidden camera of ours draws Mycelium's HUD canvases into a picture,
    /// which is halved and given a Gaussian blur (Resources/MyceliumCraftBlur.shader; an even blur,
    /// whatever is behind), and shown over
    /// the blurred world, under Minecraft's overlay (which stays sharp). Mycelium has one renderer,
    /// whose own effects (Fog, SSAO, ...) are switched off just while our camera draws: the fog
    /// painted over the picture's transparent background and made it cover the whole screen.
    /// </summary>
    sealed class McPauseBlur
    {
        const string ProfileName = "BlurVolume Profile";
        const int BlurRounds = 2; // across then down, this many times (about 6 screen pixels of blur)
        Material blurMaterial;
        GameObject root;
        Volume volume;
        bool on;

        // The UI blur
        Camera uiCamera;
        RenderTexture full, half, quarter; // quarter: half-size scratch picture for the blur
        System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> effects;

        /// <summary>The render effects of Mycelium's (only) renderer: Fog, SSAO, the ignore-fog pass.</summary>
        System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> RendererEffects()
        {
            if (effects != null) return effects;
            effects = new System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature>();
            try
            {
                var field = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var list = field?.GetValue(GraphicsSettings.currentRenderPipeline) as UnityEngine.Rendering.Universal.ScriptableRendererData[];
                if (list != null && list.Length > 0 && list[0] != null) effects.AddRange(list[0].rendererFeatures);
            }
            catch (System.Exception e) { McLog.Info("pause: couldn't read the renderer's effects: " + e.Message); }
            McLog.Info($"pause: renderer effects switched off for the UI camera: {string.Join(", ", effects.ConvertAll(f => f != null ? f.name : "?"))}");
            return effects;
        }
        GameObject uiShownRoot;
        RawImage uiShown;
        bool coverageLogged;
        readonly System.Collections.Generic.List<(Canvas canvas, RenderMode mode, Camera camera, float distance, int layer)> moved =
            new System.Collections.Generic.List<(Canvas, RenderMode, Camera, float, int)>();

        public bool On => on;

        public void Set(bool blur)
        {
            if (blur == on) return;
            if (blur && volume == null && !Create()) return;
            on = blur;
            drawn = false;
            // Test aids (2026-11-10, the plugin's laggy pause): leave out Mycelium's blur volume, or our UI blur.
            string[] args = System.Environment.GetCommandLineArgs();
            bool noVolume = System.Array.IndexOf(args, "-mcPauseNoVolume") >= 0, noUi = System.Array.IndexOf(args, "-mcPauseNoUi") >= 0;
            if (noVolume || noUi) McLog.Info($"pause: test aid: blur volume {(noVolume ? "left out" : "on")}, UI blur {(noUi ? "left out" : "on")}");
            volume.gameObject.SetActive(blur && !noVolume);
            int ui = blur ? (noUi ? 0 : UiToOurCamera()) : UiBack();
            McLog.Info(blur ? $"pause: Mycelium blurred like its own pause ({ProfileName}), {ui} of its UI canvases blurred separately" : $"pause: blur off, {ui} UI canvases back");
        }

        /// <summary>Every frame while blurred: draw Mycelium's UI, blur it, show it.</summary>
        // Timing while blurred (your report, 2026-11-10: the pause screen lagged in the BepInEx plugin).
        readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        double spentMs;
        int timedFrames;
        float nextTimeLog;

        public void Update()
        {
            if (!on || uiCamera == null || moved.Count == 0) return;
            watch.Restart();
            try { Draw(); }
            finally
            {
                spentMs += watch.Elapsed.TotalMilliseconds;
                timedFrames++;
                if (Time.realtimeSinceStartup >= nextTimeLog)
                {
                    if (nextTimeLog > 0f)
                        McLog.Info($"pause: blur took {spentMs / Mathf.Max(1, timedFrames):F2} ms a frame over {timedFrames} frames (whole frame {Time.unscaledDeltaTime * 1000f:F1} ms), {moved.Count} canvases, material {(blurMaterial != null ? "made" : "none")}");
                    nextTimeLog = Time.realtimeSinceStartup + 1f;
                    spentMs = 0;
                    timedFrames = 0;
                }
            }
        }

        // Drawn once per pause (and again if the window's size changes), not every frame: Mycelium's time
        // is stopped, so its UI doesn't change, and each draw switches its renderer effects off and on
        // (SSAO, Fog), which costs a lot at a big window: 150 ms a frame at 2560x1440, 5-7 fps on
        // Minecraft's pause screen, and Esc presses missed between frames (your report, 2026-11-10).
        bool drawn;

        void Draw()
        {
            int w = Mathf.Max(8, Screen.width), h = Mathf.Max(8, Screen.height);
            if (full == null || full.width != w || full.height != h) { MakeTextures(w, h); drawn = false; }
            if (drawn) return;
            drawn = true;
            uiCamera.targetTexture = full;
            // Mycelium's render effects off for this one draw (the main camera still gets them).
            var effects = RendererEffects();
            var wereOn = new System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature>();
            foreach (var f in effects) if (f != null && f.isActive) { wereOn.Add(f); f.SetActive(false); }
            try { uiCamera.Render(); }
            finally { foreach (var f in wereOn) f.SetActive(true); }
            Graphics.Blit(full, half);
            if (blurMaterial == null)
            {
                Shader blur = McShaders.Load("MyceliumCraftBlur");
                if (blur != null) blurMaterial = new Material(blur);
            }
            if (blurMaterial != null)
                for (int i = 0; i < BlurRounds; i++)
                {
                    blurMaterial.SetVector("_Direction", new Vector4(1f, 0f, 0f, 0f));
                    Graphics.Blit(half, quarter, blurMaterial);
                    blurMaterial.SetVector("_Direction", new Vector4(0f, 1f, 0f, 0f));
                    Graphics.Blit(quarter, half, blurMaterial);
                }
            if (!coverageLogged)
            {
                coverageLogged = true;
                UnityEngine.Rendering.AsyncGPUReadback.Request(half, 0, TextureFormat.RGBA32, r =>
                {
                    if (r.hasError) { McLog.Info("pause: UI picture readback failed"); return; }
                    var data = r.GetData<Color32>();
                    int covered = 0;
                    foreach (Color32 c in data) if (c.a > 8) covered++;
                    McLog.Info($"pause: blurred UI picture {r.width}x{r.height}, {100 * covered / Mathf.Max(1, data.Length)}% of it not transparent");
                });
            }
        }

        void MakeTextures(int w, int h)
        {
            ReleaseTextures();
            full = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "MyceliumCraft UI", filterMode = FilterMode.Bilinear };
            half = new RenderTexture(w / 2, h / 2, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            quarter = new RenderTexture(w / 2, h / 2, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            uiShown.texture = half;
        }

        void ReleaseTextures()
        {
            foreach (RenderTexture t in new[] { full, half, quarter })
                if (t != null) { t.Release(); Object.Destroy(t); }
            full = half = quarter = null;
        }

        /// <summary>Mycelium's overlay canvases (not ours) are drawn by our hidden UI camera instead.</summary>
        int UiToOurCamera()
        {
            // The UI camera draws only the UI layer, and the moved canvases go on it while blurred. Some
            // of Mycelium's canvases are on Default, the world's layer: drawing that made the UI camera
            // draw the whole world again every frame (5-7 fps on the pause screen in the BepInEx plugin,
            // your report 2026-11-10). Their own layers are put back after.
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0) uiLayer = 5;
            foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (c.gameObject.name.StartsWith("MyceliumCraft")) continue;
                moved.Add((c, c.renderMode, c.worldCamera, c.planeDistance, c.gameObject.layer));
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = uiCamera;
                c.planeDistance = 1f;
                c.gameObject.layer = uiLayer;
            }
            int layers = 1 << uiLayer;
            uiCamera.cullingMask = layers;
            McLog.Info("pause: canvases moved to the UI camera (on its UI layer while blurred): " + string.Join(", ", moved.ConvertAll(m => $"{m.canvas.gameObject.name} (was on {LayerMask.LayerToName(m.layer)})")));
            uiShownRoot.SetActive(moved.Count > 0);
            coverageLogged = false;
            return moved.Count;
        }

        int UiBack()
        {
            int n = 0;
            foreach (var (c, mode, camera, distance, layer) in moved)
            {
                if (c == null) continue;
                c.gameObject.layer = layer;
                c.renderMode = mode;
                c.worldCamera = camera;
                c.planeDistance = distance;
                n++;
            }
            moved.Clear();
            if (uiShownRoot != null) uiShownRoot.SetActive(false);
            ReleaseTextures();
            return n;
        }

        /// <summary>Test aid (-mcBlurCheck): hide the blurred UI for a moment, to compare.</summary>
        public void TestHideUi(bool hide)
        {
            if (uiShown != null) uiShown.enabled = !hide;
        }

        bool Create()
        {
            VolumeProfile profile = null;
            foreach (VolumeProfile p in Resources.FindObjectsOfTypeAll<VolumeProfile>())
                if (p.name == ProfileName) { profile = p; break; }
            if (profile == null)
            {
                McLog.Info($"pause: Mycelium's '{ProfileName}' isn't loaded; no blur");
                return false;
            }
            root = new GameObject("MyceliumCraft pause blur");
            Object.DontDestroyOnLoad(root);
            volume = root.AddComponent<Volume>();
            // As in Mycelium's PauseMenu prefab.
            volume.isGlobal = true;
            volume.priority = 0;
            volume.weight = 1;
            volume.sharedProfile = profile;
            root.SetActive(false);

            // The hidden UI camera: far below the world so it sees nothing but the canvases it draws.
            var camObject = new GameObject("MyceliumCraft UI camera");
            Object.DontDestroyOnLoad(camObject);
            camObject.transform.position = new Vector3(0f, -100000f, 0f);
            uiCamera = camObject.AddComponent<Camera>();
            uiCamera.enabled = false;           // drawn by hand, only while blurred
            uiCamera.clearFlags = CameraClearFlags.SolidColor;
            uiCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            uiCamera.nearClipPlane = 0.1f;
            uiCamera.farClipPlane = 10f;
            uiCamera.allowHDR = false;          // HDR draws through a buffer without transparency
            uiCamera.allowMSAA = false;
            uiCamera.cullingMask = 0;

            // Where the blurred UI is shown: a full-screen picture, just under the Minecraft overlay.
            uiShownRoot = new GameObject("MyceliumCraft paused UI");
            Object.DontDestroyOnLoad(uiShownRoot);
            var canvas = uiShownRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1;
            var imageObject = new GameObject("Blurred UI");
            imageObject.transform.SetParent(uiShownRoot.transform, false);
            uiShown = imageObject.AddComponent<RawImage>();
            uiShown.raycastTarget = false;
            Shader premultiplied = McShaders.Load("MyceliumCraftOverlay"); // UI drawn on clear is premultiplied
            if (premultiplied != null) uiShown.material = new Material(premultiplied);
            var rect = uiShown.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            uiShownRoot.SetActive(false);
            return true;
        }
    }
}
