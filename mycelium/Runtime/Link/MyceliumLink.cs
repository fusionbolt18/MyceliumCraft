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
        public float BodyYaw;   // where the body faces (Minecraft degrees)
        public long TickQpc;    // QueryPerformanceCounter at the latest tick
        public double PrevX, PrevY, PrevZ, CurX, CurY, CurZ; // feet at the previous / latest tick
        public float TickMs;    // milliseconds per tick; 0 = no tick data
        public float BodyYawO, BodyYawCur;
        public float FovScale, EyeScale; // Minecraft's FOV multiplier and eye height / standing (0: not sent)
        public float DamageTilt;         // Minecraft's "Damage Tilt" setting, 0 (off) .. 1
        public uint HandWeapon;          // the weapon whose carrot is in Minecraft's main hand (0 = none)
        public uint Swings;              // melee attacks Minecraft has started (counts up)

        public bool HasTicks => TickMs > 0f && TickQpc != 0;

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

        [DllImport("kernel32.dll")]
        static extern bool QueryPerformanceCounter(out long count);

        [DllImport("kernel32.dll")]
        static extern bool QueryPerformanceFrequency(out long frequency);

        static long qpcFrequency;

        /// <summary>
        /// Windows QueryPerformanceCounter, the clock Minecraft's side stamps its ticks with.
        /// Not System.Diagnostics.Stopwatch: under Unity's Mono it counts from a different start
        /// (measured: about 7.9 days apart), which made every tick time look far in the future.
        /// </summary>
        public static long Qpc()
        {
            QueryPerformanceCounter(out long c);
            return c;
        }

        public static long QpcFrequency
        {
            get
            {
                if (qpcFrequency == 0) QueryPerformanceFrequency(out qpcFrequency);
                return qpcFrequency;
            }
        }

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
        public void WriteMycState(uint flags, double x, double y, double z, float yaw, ulong frame, in Teleport tp, float playerHeightBlocks, float health = 0f, float maxHealth = 0f, int nutrients = -1, float emerge = -1f, float speed = 0f, int windowW = 0, int windowH = 0, float dashX = 0f, float dashZ = 0f)
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
            WriteF32(view, b + Proto.MyHealth, health);
            WriteF32(view, b + Proto.MyMaxHealth, maxHealth);
            Marshal.WriteInt32(view, b + Proto.MyNutrients, nutrients);
            WriteF32(view, b + Proto.MyEmerge, emerge);
            WriteF32(view, b + Proto.MySpeed, speed);
            Marshal.WriteInt32(view, b + Proto.MyWindowW, windowW);
            Marshal.WriteInt32(view, b + Proto.MyWindowH, windowH);
            WriteF32(view, b + Proto.MyDashX, dashX);
            WriteF32(view, b + Proto.MyDashZ, dashZ);
            Thread.MemoryBarrier();
            WriteU32(view, b + Proto.MySeq, seq + 2); // even: done
        }

        /// <summary>Seqlock write of the input bridge block (see MycInput in protocol/PROTOCOL.md).</summary>
        public void WriteInput(uint flags, uint reason, uint buttons, byte[] keys, double totalDX, double totalDY, double totalScroll, uint textCount, uint[] textRing, double cursorX, double cursorY)
        {
            int b = Proto.OffInput;
            uint seq = ReadU32(view, b + Proto.InSeq);
            WriteU32(view, b + Proto.InSeq, seq + 1);
            Thread.MemoryBarrier();
            WriteU32(view, b + Proto.InFlags, flags);
            WriteU32(view, b + Proto.InReason, reason);
            WriteU32(view, b + Proto.InButtons, buttons);
            Marshal.Copy(keys, 0, view + b + Proto.InKeys, Proto.InKeyBytes);
            WriteF64(view, b + Proto.InTotalDX, totalDX);
            WriteF64(view, b + Proto.InTotalDY, totalDY);
            WriteF64(view, b + Proto.InTotalScroll, totalScroll);
            WriteU32(view, b + Proto.InTextCount, textCount);
            for (int i = 0; i < Proto.InTextRing; i++) WriteU32(view, b + Proto.InText + 4 * i, textRing[i]);
            WriteF64(view, b + Proto.InCursorX, cursorX);
            WriteF64(view, b + Proto.InCursorY, cursorY);
            Thread.MemoryBarrier();
            WriteU32(view, b + Proto.InSeq, seq + 2);
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
                s.BodyYaw = ReadF32(view, b + Proto.McBodyYaw);
                s.TickQpc = Marshal.ReadInt64(view, b + Proto.McTickQpc);
                s.PrevX = ReadF64(view, b + Proto.McPrevX);
                s.PrevY = ReadF64(view, b + Proto.McPrevY);
                s.PrevZ = ReadF64(view, b + Proto.McPrevZ);
                s.CurX = ReadF64(view, b + Proto.McCurX);
                s.CurY = ReadF64(view, b + Proto.McCurY);
                s.CurZ = ReadF64(view, b + Proto.McCurZ);
                s.TickMs = ReadF32(view, b + Proto.McTickMs);
                s.BodyYawO = ReadF32(view, b + Proto.McBodyYawO);
                s.BodyYawCur = ReadF32(view, b + Proto.McBodyYawCur);
                s.FovScale = ReadF32(view, b + Proto.McFovScale);
                s.EyeScale = ReadF32(view, b + Proto.McEyeScale);
                s.DamageTilt = ReadF32(view, b + Proto.McDamageTilt);
                s.HandWeapon = ReadU32(view, b + Proto.McHandWeapon);
                s.Swings = ReadU32(view, b + Proto.McSwings);
                Thread.MemoryBarrier();
                if (ReadU32(view, b + Proto.McSeq) == seq1) return true;
            }
            s = default;
            return false;
        }

        /// <summary>
        /// Seqlock read of the Bindings block into kinds/codes (each at least Proto.BMax long).
        /// Returns the number of actions, 0 if Minecraft hasn't sent any, or -1 if the read kept being interrupted.
        /// </summary>
        public int TryReadBindings(uint[] kinds, uint[] codes)
        {
            int b = Proto.OffBindings;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                uint seq1 = ReadU32(view, b + Proto.BSeq);
                Thread.MemoryBarrier();
                if ((seq1 & 1) != 0) { Thread.SpinWait(20); continue; }
                int count = (int)Math.Min(ReadU32(view, b + Proto.BCount), (uint)Proto.BMax);
                for (int i = 0; i < count; i++)
                {
                    kinds[i] = ReadU32(view, b + Proto.BEntries + 8 * i);
                    codes[i] = ReadU32(view, b + Proto.BEntries + 8 * i + 4);
                }
                Thread.MemoryBarrier();
                if (ReadU32(view, b + Proto.BSeq) == seq1) return count;
            }
            return -1;
        }

        int overlayFront = 2; // our slot; Minecraft writes into 1 first, the middle starts at 0

        /// <summary>
        /// Take Minecraft's newest overlay picture, if there is one we haven't taken yet. The pixels
        /// (RGBA, premultiplied alpha) stay valid until the next call.
        /// </summary>
        public unsafe bool TryTakeOverlay(out OverlayFrame frame)
        {
            frame = default;
            int* state = (int*)(view + Proto.OffOverlayCtl + Proto.OcState);
            if ((Volatile.Read(ref *state) & Proto.OverlayDirty) == 0) return false;
            int old = Interlocked.Exchange(ref *state, overlayFront);
            overlayFront = old & 3;
            if (overlayFront >= Proto.OverlaySlots) return false;
            int hdr = Proto.OffOverlaySlotHdr + overlayFront * Proto.SlotHdrSize;
            frame.Width = (int)ReadU32(view, hdr + Proto.ShWidth);
            frame.Height = (int)ReadU32(view, hdr + Proto.ShHeight);
            frame.BottomUp = (ReadU32(view, hdr + Proto.ShFlags) & 1) != 0;
            frame.FrameId = Marshal.ReadInt64(view, hdr + Proto.ShFrameId);
            frame.Pixels = view + Proto.OffOverlayPixels + overlayFront * Proto.OverlaySlotBytes;
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > Proto.MaxOverlayW || frame.Height > Proto.MaxOverlayH) return false;
            return true;
        }

        /// <summary>Seqlock read of the player model into data. False if Minecraft kept interrupting.</summary>
        public bool TryReadModel(ModelData data)
        {
            int b = Proto.OffModel;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                uint seq1 = ReadU32(view, b + Proto.MSeq);
                Thread.MemoryBarrier();
                if ((seq1 & 1) != 0) { Thread.SpinWait(20); continue; }
                uint modelFlags = ReadU32(view, b + Proto.MFlags);
                data.Visible = (modelFlags & Proto.MVisible) != 0;
                data.FirstPerson = (modelFlags & Proto.MFirstPerson) != 0;
                int batches = (int)ReadU32(view, b + Proto.MBatches);
                int vertices = (int)ReadU32(view, b + Proto.MVertices);
                data.Frame = Marshal.ReadInt64(view, b + Proto.MFrame);
                if (batches < 0 || batches > Proto.MMaxBatches || vertices < 0 || vertices > Proto.MMaxVertices) return false;
                for (int i = 0; i < batches; i++)
                {
                    int e = b + Proto.MBatchTable + 16 * i;
                    data.Texture[i] = (int)ReadU32(view, e);
                    data.First[i] = (int)ReadU32(view, e + 4);
                    data.Count[i] = (int)ReadU32(view, e + 8);
                }
                int bytes = vertices * Proto.MVertexBytes;
                if (data.Raw.Length < bytes) data.Raw = new byte[bytes];
                Marshal.Copy(view + b + Proto.MVertexData, data.Raw, 0, bytes);
                Thread.MemoryBarrier();
                if (ReadU32(view, b + Proto.MSeq) != seq1) continue;
                data.Batches = batches;
                data.Vertices = vertices;
                for (int i = 0; i < batches; i++)
                    if (data.First[i] < 0 || data.Count[i] < 0 || data.First[i] + data.Count[i] > vertices) return false;
                return true;
            }
            return false;
        }

        /// <summary>Texture id (1-based) from Minecraft, if it has been sent: RGBA pixels, top row first.</summary>
        public bool TryReadTexture(int id, out uint version, out int width, out int height, out IntPtr pixels)
        {
            version = 0; width = height = 0; pixels = IntPtr.Zero;
            if (id < 1 || id > Proto.TMax) return false;
            int e = Proto.OffTextures + Proto.TTable + (id - 1) * Proto.TEntrySize;
            version = ReadU32(view, e);
            Thread.MemoryBarrier();
            if (version == 0) return false;
            width = (int)ReadU32(view, e + 4);
            height = (int)ReadU32(view, e + 8);
            long offset = Marshal.ReadInt64(view, e + 16);
            if (width <= 0 || height <= 0 || offset < 0 || offset + (long)width * height * 4 > Proto.TDataBytes) return false;
            pixels = view + Proto.OffTextures + Proto.TData + (int)offset;
            return true;
        }

        /// <summary>Seqlock write of the enemy table (count entries of ids/flags/values).</summary>
        /// <summary>Seqlock read of the Thrown table: ids, flags, and x, y, z (Minecraft) per item. Count, or -1.</summary>
        public int TryReadThrown(uint[] ids, uint[] flags, double[] xyz)
        {
            int b = Proto.OffThrown;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                uint seq1 = ReadU32(view, b + Proto.ThrSeq);
                Thread.MemoryBarrier();
                if ((seq1 & 1) != 0) { Thread.SpinWait(20); continue; }
                int count = (int)Math.Min(ReadU32(view, b + Proto.ThrCount), (uint)Math.Min(Proto.ThrMax, ids.Length));
                for (int i = 0; i < count; i++)
                {
                    int e = b + Proto.ThrList + i * Proto.ThrSize;
                    ids[i] = ReadU32(view, e);
                    flags[i] = ReadU32(view, e + 4);
                    xyz[i * 3] = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(view, e + 8));
                    xyz[i * 3 + 1] = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(view, e + 16));
                    xyz[i * 3 + 2] = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(view, e + 24));
                }
                Thread.MemoryBarrier();
                if (ReadU32(view, b + Proto.ThrSeq) == seq1) return count;
            }
            return -1;
        }

        /// <summary>One weapon for the Weapons table.</summary>
        public struct WeaponEntry
        {
            public uint Id, Type;
            public float Damage, PerSecond, Knockback, DamageMult, SpeedMod, Reach;
            public string Name, Attributes, Key;
        }

        /// <summary>Seqlock write of the Weapons table (your weapons, for Minecraft's carrots).</summary>
        public void WriteWeapons(uint version, int count, WeaponEntry[] list, uint selectSeq, uint selectId, uint heldId, float damageScale)
        {
            int b = Proto.OffWeapons;
            uint seq = ReadU32(view, b + Proto.WSeq);
            WriteU32(view, b + Proto.WSeq, seq + 1);
            Thread.MemoryBarrier();
            count = Math.Min(count, Proto.WMax);
            WriteU32(view, b + Proto.WCount, (uint)count);
            WriteU32(view, b + Proto.WVersion, version);
            WriteU32(view, b + Proto.WSelectSeq, selectSeq);
            WriteU32(view, b + Proto.WSelectId, selectId);
            WriteU32(view, b + Proto.WHeldId, heldId);
            WriteF32(view, b + Proto.WDamageScale, damageScale);
            for (int i = 0; i < count; i++)
            {
                int e = b + Proto.WList + i * Proto.WSize;
                WeaponEntry w = list[i];
                WriteU32(view, e + Proto.WeId, w.Id);
                WriteU32(view, e + Proto.WeType, w.Type);
                WriteF32(view, e + Proto.WeDamage, w.Damage);
                WriteF32(view, e + Proto.WePerSecond, w.PerSecond);
                WriteF32(view, e + Proto.WeKnockback, w.Knockback);
                WriteF32(view, e + Proto.WeDamageMult, w.DamageMult);
                WriteF32(view, e + Proto.WeSpeedMod, w.SpeedMod);
                WriteF32(view, e + Proto.WeReach, w.Reach);
                WriteUtf8(view, e + Proto.WeName, Proto.WeNameBytes, w.Name);
                WriteUtf8(view, e + Proto.WeAtts, Proto.WeAttsBytes, w.Attributes);
                WriteUtf8(view, e + Proto.WeKey, Proto.WeKeyBytes, w.Key ?? "");
            }
            Thread.MemoryBarrier();
            WriteU32(view, b + Proto.WSeq, seq + 2);
        }

        /// <summary>A string as UTF-8, cut to fit (whole characters) and zero-terminated.</summary>
        static void WriteUtf8(IntPtr p, int off, int bytes, string s)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(data.Length, bytes - 1);
            while (n > 0 && n < data.Length && (data[n] & 0xC0) == 0x80) n--; // don't cut a character in half
            if (n > 0) Marshal.Copy(data, 0, p + off, n);
            Marshal.WriteByte(p, off + n, 0);
        }

        public void WriteEnemies(int count, uint[] ids, uint[] flags, float[] values)
        {
            int b = Proto.OffEnemies;
            uint seq = ReadU32(view, b + Proto.ESeq);
            WriteU32(view, b + Proto.ESeq, seq + 1);
            Thread.MemoryBarrier();
            count = Math.Min(count, Proto.EMax);
            WriteU32(view, b + Proto.ECount, (uint)count);
            for (int i = 0; i < count; i++)
            {
                int e = b + Proto.EList + i * Proto.ESize;
                WriteU32(view, e + Proto.EnId, ids[i]);
                WriteU32(view, e + Proto.EnFlags, flags[i]);
                for (int k = 0; k < 8; k++) WriteF32(view, e + Proto.EnX + 4 * k, values[i * 8 + k]); // x, y, z, width, height, health, max, yaw
            }
            Thread.MemoryBarrier();
            WriteU32(view, b + Proto.ESeq, seq + 2);
        }

        /// <summary>Hits Minecraft has reported so far (the ring holds the last Proto.HRing).</summary>
        public uint HitCount => ReadU32(view, Proto.OffHits + Proto.HCount);

        /// <summary>Hit number n (0-based, total count), if still in the ring.</summary>
        public void ReadHit(uint n, out uint enemyId, out float damage, out uint flags)
        {
            int e = Proto.OffHits + Proto.HList + (int)(n % Proto.HRing) * Proto.HSize;
            Thread.MemoryBarrier();
            enemyId = ReadU32(view, e);
            damage = ReadF32(view, e + 4);
            flags = ReadU32(view, e + 8);
        }

        /// <summary>Seqlock read of the stand-ins' hit boxes: ids and 6 floats each (min x, y, z, max x, y, z). Count, or -1.</summary>
        public int TryReadHitBoxes(uint[] ids, float[] boxes)
        {
            int b = Proto.OffHitBoxes;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                uint seq1 = ReadU32(view, b + Proto.HbSeq);
                Thread.MemoryBarrier();
                if ((seq1 & 1) != 0) { Thread.SpinWait(20); continue; }
                int count = (int)Math.Min(ReadU32(view, b + Proto.HbCount), (uint)Math.Min(Proto.HbMax, ids.Length));
                for (int i = 0; i < count; i++)
                {
                    int e = b + Proto.HbList + i * Proto.HbSize;
                    ids[i] = ReadU32(view, e);
                    for (int k = 0; k < 6; k++) boxes[i * 6 + k] = ReadF32(view, e + 4 + 4 * k);
                }
                Thread.MemoryBarrier();
                if (ReadU32(view, b + Proto.HbSeq) == seq1) return count;
            }
            return -1;
        }

        /// <summary>Pictures Minecraft has published so far.</summary>
        public long OverlayFramesPublished => Marshal.ReadInt64(view, Proto.OffOverlayCtl + Proto.OcFramesPublished);

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
