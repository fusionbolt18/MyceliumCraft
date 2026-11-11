using System.Collections.Generic;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Minecraft's F1 (hide the HUD) hides Mycelium's interface too (your request, 2026-11-11). While
    /// McState says Minecraft's HUD is hidden and it's normal play, every root canvas of Mycelium's
    /// (its HUD, notifications, crosshair, the pop-ups over curios and spores) is switched off, except
    /// the screens that aren't HUD: its menus and the loading, black, start-up, death and title cards,
    /// and our own. Canvases that appear meanwhile (a pop-up) are caught every frame, and again just
    /// before Unity draws the canvases (Canvas.willRenderCanvases), so one made later in the frame
    /// isn't seen either (your report, 2026-11-11: pop-ups showed for a split second). When F1 is
    /// pressed again, or Mycelium takes the controls (a menu, the shop: Minecraft still shows its
    /// screens with F1 on), the ones switched off here are switched back on.
    ///
    /// Renderers under those canvases aren't drawn either (forceRenderingOff): the happiness faces in
    /// a spore's pop-up are SpriteRenderers (HappinessSlider in the tooltip), which a canvas being off
    /// doesn't hide (your report, 2026-11-11).
    /// </summary>
    sealed class UiHider
    {
        static readonly string[] Keep = { "PauseMenuCanvas", "LoadingCanvas", "BlackCanvas", "StartupCanvas", "DeathCanavs", "DeathCanvas", "TitleCanvas" };

        readonly List<Canvas> off = new List<Canvas>();
        readonly List<Renderer> notDrawn = new List<Renderer>();
        readonly List<Renderer> under = new List<Renderer>();
        bool hiding, hooked;

        public void Update(bool hide)
        {
            if (!hide)
            {
                if (!hiding) return;
                hiding = false;
                int back = 0;
                foreach (Canvas c in off) if (c != null && !c.enabled) { c.enabled = true; back++; }
                off.Clear();
                foreach (Renderer r in notDrawn) if (r != null) r.forceRenderingOff = false;
                notDrawn.Clear();
                McLog.Info($"ui: Minecraft's HUD is back (or a menu is open): {back} of Mycelium's canvases shown again");
                return;
            }
            if (!hooked)
            {
                hooked = true;
                Canvas.willRenderCanvases += BeforeCanvasesDrawn;
            }
            Scan();
        }

        /// <summary>Just before Unity draws the canvases: catch a pop-up made since this frame's Update.</summary>
        void BeforeCanvasesDrawn()
        {
            if (hiding) Scan();
        }

        void Scan()
        {
            int added = 0;
            foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
            {
                if (!c.isRootCanvas || !c.enabled) continue;
                string name = c.gameObject.name;
                if (name.StartsWith("MyceliumCraft") || System.Array.IndexOf(Keep, name) >= 0) continue;
                c.enabled = false;
                off.Add(c);
                added++;
            }
            // Sprites and the like under the hidden canvases (a pop-up's happiness faces); also ones added since.
            int sprites = 0;
            foreach (Canvas c in off)
            {
                if (c == null) continue;
                c.GetComponentsInChildren(true, under);
                foreach (Renderer r in under)
                {
                    if (r.forceRenderingOff) continue;
                    r.forceRenderingOff = true;
                    notDrawn.Add(r);
                    sprites++;
                }
            }
            if (sprites > 0 && hiding) McLog.Info($"ui: {sprites} more sprite(s) under Mycelium's hidden canvases not drawn");
            if (!hiding)
            {
                hiding = true;
                McLog.Info($"ui: Minecraft's HUD is hidden (F1): {added} of Mycelium's canvases hidden too ({string.Join(", ", off.ConvertAll(c => c.gameObject.name))}), and {sprites} sprite(s) under them");
            }
            else if (added > 0) McLog.Info($"ui: {added} more of Mycelium's canvases hidden while F1 is on");
        }

        public void SceneLoaded()
        {
            off.Clear();
            notDrawn.Clear();
            hiding = false;
        }
    }
}
