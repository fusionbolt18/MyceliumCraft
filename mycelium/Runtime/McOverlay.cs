using System;
using System.Diagnostics;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.UI;

namespace MyceliumCraft
{
    /// <summary>
    /// The overlay, Mycelium side: Minecraft's picture (its hand, hotbar, hearts and open screens,
    /// on a transparent background; MycOverlay.java) drawn over the whole of Mycelium's screen, on
    /// top of Mycelium's own UI. It's a full-screen RawImage on its own canvas, with a shader that
    /// blends premultiplied alpha (Resources/MyceliumCraftOverlay.shader). The picture is stretched
    /// to Mycelium's window, so it looks right when both windows have the same shape.
    /// </summary>
    sealed class McOverlay
    {
        const float StaleSeconds = 0.5f; // hide Minecraft's picture if no new one arrived for this long

        GameObject root;
        RawImage image;
        Texture2D texture;
        bool failed;
        float lastPictureTime = -1f;
        bool visible;

        // Once-a-second log
        float nextLog;
        int pictures;
        double uploadMs;
        long lastFrameId;
        int coveragePercent = -1;
        bool wantCoverage = true;
        bool sizeLogged;

        // Blip watch (your report, 2026-11-10: for a split second Minecraft's things vanish): gaps
        // between pictures and slow Mycelium frames, logged only when they happen.
        float lastGapLog, lastSlowLog;

        /// <summary>For the -mcOverlayCheck test aid: hide the picture even when it would show.</summary>
        public bool ForceHidden;
        public bool Visible => visible;
        public int Width => texture != null ? texture.width : 0;
        public int Height => texture != null ? texture.height : 0;

        /// <summary>Call every frame. show: it's normal play and the overlay is wanted.</summary>
        public void Update(MyceliumLink link, bool show)
        {
            if (failed) return;
            if (root == null && !Create()) return;

            if (link.TryTakeOverlay(out OverlayFrame frame))
            {
                var watch = Stopwatch.StartNew();
                if (texture == null || texture.width != frame.Width || texture.height != frame.Height)
                {
                    if (texture != null) UnityEngine.Object.Destroy(texture);
                    // sRGB texture: Minecraft's colors are ordinary screen colors.
                    texture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false, false)
                    {
                        name = "MyceliumCraft overlay",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    image.texture = texture;
                    sizeLogged = false;
                }
                if (wantCoverage)
                {
                    wantCoverage = false;
                    coveragePercent = Coverage(frame);
                }
                if (lastPictureTime >= 0f && visible)
                {
                    float gap = Time.realtimeSinceStartup - lastPictureTime;
                    if (gap > 0.1f && Time.realtimeSinceStartup - lastGapLog > 0.5f)
                    {
                        lastGapLog = Time.realtimeSinceStartup;
                        McLog.Info($"overlay blip: no new picture from Minecraft for {gap * 1000f:F0} ms (the last one stayed up), now #{frame.FrameId}");
                    }
                }
                texture.LoadRawTextureData(frame.Pixels, frame.Width * frame.Height * 4);
                texture.Apply(false, false);
                // Unity textures run bottom to top too; a top-down picture is flipped.
                image.uvRect = frame.BottomUp ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, 1f, 1f, -1f);
                uploadMs += watch.Elapsed.TotalMilliseconds;
                pictures++;
                lastFrameId = frame.FrameId;
                lastPictureTime = Time.realtimeSinceStartup;
                if (!sizeLogged)
                {
                    sizeLogged = true;
                    string shape = Mathf.Abs((float)frame.Width / frame.Height - (float)Screen.width / Screen.height) > 0.02f
                        ? " (not the same shape as Mycelium's window: Minecraft's picture is stretched; give both windows the same size)"
                        : "";
                    McLog.Info($"overlay: Minecraft's picture is {frame.Width}x{frame.Height}, Mycelium's window {Screen.width}x{Screen.height}{shape}; color space {QualitySettings.activeColorSpace}");
                }
            }

            if (visible && Time.unscaledDeltaTime > 0.1f && Time.realtimeSinceStartup - lastSlowLog > 0.5f)
            {
                lastSlowLog = Time.realtimeSinceStartup;
                McLog.Info($"overlay blip: Mycelium's frame took {Time.unscaledDeltaTime * 1000f:F0} ms");
            }

            bool fresh = lastPictureTime >= 0f && Time.realtimeSinceStartup - lastPictureTime <= StaleSeconds;
            bool now = show && fresh && texture != null && !ForceHidden;
            if (now != visible)
            {
                visible = now;
                root.SetActive(now);
                McLog.Info($"overlay: {(now ? "showing Minecraft's hand, HUD and screens" : "hidden")} ({(show ? "" : "not normal play; ")}{(fresh ? "" : "no new picture from Minecraft; ")}{(ForceHidden ? "test hides it; " : "")}frames published {link.OverlayFramesPublished})");
            }

            if (Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 1f;
                if (pictures > 0 || visible)
                {
                    McLog.Info($"overlay: {pictures} pictures in the last second, {(pictures > 0 ? uploadMs / pictures : 0):F2} ms each to upload, latest #{lastFrameId} {Width}x{Height}, {coveragePercent}% of it not transparent, visible={visible}");
                }
                pictures = 0;
                uploadMs = 0;
                wantCoverage = true;
            }
        }

        bool Create()
        {
            Shader shader = McShaders.Load("MyceliumCraftOverlay");
            if (shader == null || !shader.isSupported)
            {
                failed = true;
                McLog.Info("overlay: OFF, its shader (Resources/MyceliumCraftOverlay) is missing or not supported");
                return false;
            }
            root = new GameObject("MyceliumCraft overlay");
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // above all of Mycelium's own UI
            var imageObject = new GameObject("Minecraft picture");
            imageObject.transform.SetParent(root.transform, false);
            image = imageObject.AddComponent<RawImage>();
            image.material = new Material(shader);
            image.raycastTarget = false; // never blocks clicks on Mycelium's UI
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            root.SetActive(false);
            McLog.Info("overlay: ready");
            return true;
        }

        /// <summary>Percent of pixels with any alpha, from every 7th pixel: is the background really transparent?</summary>
        static unsafe int Coverage(in OverlayFrame frame)
        {
            byte* p = (byte*)frame.Pixels;
            long count = (long)frame.Width * frame.Height;
            long seen = 0, covered = 0;
            for (long i = 0; i < count; i += 7)
            {
                seen++;
                if (p[i * 4 + 3] != 0) covered++;
            }
            return seen > 0 ? (int)(100 * covered / seen) : 0;
        }
    }
}
