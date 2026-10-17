namespace MyceliumCraft.Link
{
    /// <summary>
    /// Phase 0 coordinate mapping: Minecraft movement is applied to Mycelium as a difference from an
    /// anchor (both positions recorded at the same moment). See protocol/PROTOCOL.md.
    /// Height is left to Mycelium's own physics for now.
    /// </summary>
    public sealed class AnchorMapping
    {
        /// <summary>Mycelium units per Minecraft block.</summary>
        public double Scale = 1.0;

        public bool HasAnchor { get; private set; }
        public uint Epoch { get; private set; }

        double mcX, mcZ;
        double mycX, mycY, mycZ;

        public void SetAnchor(double mcX, double mcZ, double mycX, double mycY, double mycZ, uint epoch)
        {
            this.mcX = mcX;
            this.mcZ = mcZ;
            this.mycX = mycX;
            this.mycY = mycY;
            this.mycZ = mycZ;
            Epoch = epoch;
            HasAnchor = true;
        }

        public void Clear() => HasAnchor = false;

        /// <summary>Where the spore should be (X, Z) for this Minecraft position. Minecraft +Z is south; Unity +Z is north.</summary>
        public void Target(double mcX, double mcZ, out double x, out double z)
        {
            x = mycX + Scale * (mcX - this.mcX);
            z = mycZ - Scale * (mcZ - this.mcZ);
        }

        /// <summary>Minecraft yaw 0 faces +Z (south) = Unity 180. Result is in [0, 360).</summary>
        public static float Yaw(float mcYaw)
        {
            float y = (mcYaw + 180f) % 360f;
            return y < 0 ? y + 360f : y;
        }
    }
}
