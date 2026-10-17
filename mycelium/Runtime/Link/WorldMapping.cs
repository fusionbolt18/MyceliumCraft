namespace MyceliumCraft.Link
{
    /// <summary>
    /// Fixed mapping between Mycelium (Unity) and Minecraft coordinates. See protocol/PROTOCOL.md.
    /// Minecraft +Z is south; Unity +Z is "north", so Z flips.
    /// </summary>
    public sealed class WorldMapping
    {
        /// <summary>Mycelium units per Minecraft block.</summary>
        public readonly double Scale;

        public WorldMapping(double scale) => Scale = scale;

        public void ToMc(double x, double y, double z, out double mx, out double my, out double mz)
        {
            mx = x / Scale;
            my = y / Scale;
            mz = -z / Scale;
        }

        public void ToMyc(double mx, double my, double mz, out double x, out double y, out double z)
        {
            x = mx * Scale;
            y = my * Scale;
            z = -mz * Scale;
        }

        /// <summary>Minecraft yaw 0 faces +Z (south) = Unity 180. Result is in [0, 360).</summary>
        public static float MycYaw(float mcYaw) => Wrap(mcYaw + 180f);

        public static float McYaw(float mycYaw) => Wrap(mycYaw + 180f);

        static float Wrap(float y)
        {
            y %= 360f;
            return y < 0 ? y + 360f : y;
        }
    }
}
