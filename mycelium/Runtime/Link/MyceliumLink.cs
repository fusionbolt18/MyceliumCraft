using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MyceliumCraft.Link
{
    /// <summary>Plain snapshot of Minecraft's McState block.</summary>
    public struct McState
    {
        public uint Flags;
        public double X, Y, Z;
        public float Yaw, Pitch;
        public ulong Frame;
        public uint Epoch;
        public uint TeleportAck;
        public uint CameraMode; // 0 first person, 1 third person behind, 2 third person in front

        public bool InWorld => (Flags & Proto.McInWorld) != 0;
    }

    /// <summary>A request for Minecraft to move its player (Minecraft coords). Applied when Seq changes.</summary>
    public struct Teleport
    {
        public uint Seq;
        public double X, Y, Z;
        public float Yaw;
    }

    /// <summary>
    /// The Mycelium end of the shared-memory link. Mycelium creates the mapping; Minecraft opens it.
    /// Plain .NET (no UnityEngine), so tools/FakeMycelium can use it outside Unity.
    /// </summary>
    public sealed class MyceliumLink : IDisposable
    {
        const uint PAGE_READWRITE = 0x04;
        const uint FILE_MAP_ALL_ACCESS = 0xF001F;
        const int ERROR_ALREADY_EXISTS = 183;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFileMappingW(IntPtr hFile, IntPtr attributes, uint protect, uint maxSizeHigh, uint maxSizeLow, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        static extern bool UnmapViewOfFile(IntPtr view);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentProcessId();

        IntPtr handle;
        IntPtr view;

        public bool AlreadyExisted { get; private set; }

        MyceliumLink() { }

        /// <summary>Create (or reopen, if Minecraft still holds it) the mapping. Throws on failure.</summary>
        public static MyceliumLink Create()
        {
            var link = new MyceliumLink();
            link.handle = CreateFileMappingW(new IntPtr(-1), IntPtr.Zero, PAGE_READWRITE, 0, Proto.MappingBytes, Proto.MappingName);
            int error = Marshal.GetLastWin32Error();
            if (link.handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateFileMappingW failed, Windows error " + error);
            }
            link.AlreadyExisted = error == ERROR_ALREADY_EXISTS;
            link.view = MapViewOfFile(link.handle, FILE_MAP_ALL_ACCESS, 0, 0, new UIntPtr((uint)Proto.MappingBytes));
            if (link.view == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                CloseHandle(link.handle);
                throw new InvalidOperationException("MapViewOfFile failed, Windows error " + error);
            }
            // Fill the header; magic goes last so Minecraft never sees a half-written header as valid.
            WriteU32(link.view, Proto.OffHeader + Proto.HVersion, Proto.Version);
            WriteU32(link.view, Proto.OffHeader + Proto.HMycPid, GetCurrentProcessId());
            Marshal.WriteInt64(link.view, Proto.OffHeader + Proto.HMycHeartbeat, (long)GetTickCount64());
            Thread.MemoryBarrier();
            WriteU32(link.view, Proto.OffHeader + Proto.HMagic, Proto.Magic);
            return link;
        }

        public static ulong TickCount() => GetTickCount64();

        /// <summary>Tell Minecraft we're alive. Call every frame.</summary>
        public void Beat()
        {
            Thread.MemoryBarrier();
            Marshal.WriteInt64(view, Proto.OffHeader + Proto.HMycHeartbeat, (long)GetTickCount64());
        }

        public uint MinecraftPid => ReadU32(view, Proto.OffHeader + Proto.HMcPid);

        /// <summary>True if a Minecraft has opened the mapping and its heartbeat is fresh.</summary>
        public bool MinecraftAlive
        {
            get
            {
                if (MinecraftPid == 0) return false;
                ulong beat = (ulong)Marshal.ReadInt64(view, Proto.OffHeader + Proto.HMcHeartbeat);
                Thread.MemoryBarrier();
                return GetTickCount64() - beat < Proto.HeartbeatTimeoutMs;
            }
        }

        /// <summary>Seqlock write of MycState.</summary>
        public void WriteMycState(uint flags, double x, double y, double z, float yaw, ulong frame, in Teleport tp, float playerHeightBlocks)
        {
            int b = Proto.OffMycState;
            uint seq = ReadU32(view, b + Proto.MySeq);
            WriteU32(view, b + Proto.MySeq, seq + 1); // odd: writing
            Thread.MemoryBarrier();
            WriteU32(view, b + Proto.MyFlags, flags);
            WriteF64(view, b + Proto.MyX, x);
            WriteF64(view, b + Proto.MyY, y);
            WriteF64(view, b + Proto.MyZ, z);
            WriteF32(view, b + Proto.MyYaw, yaw);
            Marshal.WriteInt64(view, b + Proto.MyFrame, (long)frame);
            WriteU32(view, b + Proto.MyTeleportSeq, tp.Seq);
            WriteF64(view, b + Proto.MyTpX, tp.X);
            WriteF64(view, b + Proto.MyTpY, tp.Y);
            WriteF64(view, b + Proto.MyTpZ, tp.Z);
            WriteF32(view, b + Proto.MyTpYaw, tp.Yaw);
            WriteF32(view, b + Proto.MyPlayerHeight, playerHeightBlocks);
            Thread.MemoryBarrier();
            WriteU32(view, b + Proto.MySeq, seq + 2); // even: done
        }

        /// <summary>Seqlock write of the ground grid: per cell GPieces (bottom, top) pairs in Minecraft Y.</summary>
        public void WriteGround(uint gridVersion, int n, double originX, double originZ, double cell, float[] pieces)
        {
            int floats = n * n * Proto.GFloatsPerCell;
            if (n > Proto.GMaxN || pieces.Length < floats) throw new ArgumentException("grid too big");
            int b = Proto.OffGround;
            uint seq = ReadU32(view, b + Proto.GSeq);
            WriteU32(view, b + Proto.GSeq, seq + 1);
            Thread.MemoryBarrier();
            WriteU32(view, b + Proto.GVersion, gridVersion);
            WriteU32(view, b + Proto.GN, (uint)n);
            WriteF64(view, b + Proto.GOriginX, originX);
            WriteF64(view, b + Proto.GOriginZ, originZ);
            WriteF64(view, b + Proto.GCell, cell);
            Marshal.Copy(pieces, 0, view + b + Proto.GHeights, floats);
            Thread.MemoryBarrier();
            WriteU32(view, b + Proto.GSeq, seq + 2);
        }

        /// <summary>Seqlock read of McState. False if Minecraft kept interrupting.</summary>
        public bool TryReadMcState(out McState s)
        {
            int b = Proto.OffMcState;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                uint seq1 = ReadU32(view, b + Proto.McSeq);
                Thread.MemoryBarrier();
                if ((seq1 & 1) != 0) { Thread.SpinWait(20); continue; }
                s.Flags = ReadU32(view, b + Proto.McFlagsOff);
                s.X = ReadF64(view, b + Proto.McX);
                s.Y = ReadF64(view, b + Proto.McY);
                s.Z = ReadF64(view, b + Proto.McZ);
                s.Yaw = ReadF32(view, b + Proto.McYaw);
                s.Pitch = ReadF32(view, b + Proto.McPitch);
                s.Frame = (ulong)Marshal.ReadInt64(view, b + Proto.McFrame);
                s.Epoch = ReadU32(view, b + Proto.McEpoch);
                s.TeleportAck = ReadU32(view, b + Proto.McTeleportAck);
                s.CameraMode = ReadU32(view, b + Proto.McCameraMode);
                Thread.MemoryBarrier();
                if (ReadU32(view, b + Proto.McSeq) == seq1) return true;
            }
            s = default;
            return false;
        }

        public void Dispose()
        {
            if (view != IntPtr.Zero) { UnmapViewOfFile(view); view = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }

        static uint ReadU32(IntPtr p, int off) => (uint)Marshal.ReadInt32(p, off);
        static void WriteU32(IntPtr p, int off, uint v) => Marshal.WriteInt32(p, off, (int)v);
        static double ReadF64(IntPtr p, int off) => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, off));
        static void WriteF64(IntPtr p, int off, double v) => Marshal.WriteInt64(p, off, BitConverter.DoubleToInt64Bits(v));
        static float ReadF32(IntPtr p, int off) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p, off));
        static void WriteF32(IntPtr p, int off, float v) => Marshal.WriteInt32(p, off, BitConverter.SingleToInt32Bits(v));
    }
}
