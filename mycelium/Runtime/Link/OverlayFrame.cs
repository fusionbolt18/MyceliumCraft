using System;

namespace MyceliumCraft.Link
{
    /// <summary>One overlay picture from Minecraft, in shared memory (see MyceliumLink.TryTakeOverlay).</summary>
    public struct OverlayFrame
    {
        public int Width, Height;
        /// <summary>Rows run bottom to top (what Unity textures expect too).</summary>
        public bool BottomUp;
        public long FrameId;
        /// <summary>Width * Height RGBA pixels, premultiplied alpha.</summary>
        public IntPtr Pixels;
    }
}
