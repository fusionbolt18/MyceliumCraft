namespace MyceliumCraft.Link
{
    /// <summary>The player model as last read from Minecraft (see MyceliumLink.TryReadModel).</summary>
    public sealed class ModelData
    {
        public bool Visible;
        /// <summary>Minecraft is in first person: draw only the body's shadow.</summary>
        public bool FirstPerson;
        public long Frame;
        public int Batches;
        public int Vertices;
        public readonly int[] Texture = new int[Proto.MMaxBatches];
        public readonly int[] First = new int[Proto.MMaxBatches];
        public readonly int[] Count = new int[Proto.MMaxBatches];
        /// <summary>Vertices * Proto.MVertexBytes bytes: f32 x, y, z, u, v; u8 r, g, b, a.</summary>
        public byte[] Raw = new byte[0];
    }
}
