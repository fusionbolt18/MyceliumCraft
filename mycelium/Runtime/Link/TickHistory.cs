using System;

namespace MyceliumCraft.Link
{
    /// <summary>
    /// The last few Minecraft physics ticks and when they happened (QueryPerformanceCounter), to
    /// know where Minecraft's player is at any recent moment, as Minecraft's renderer shows it.
    ///
    /// Minecraft draws its player between its two latest ticks: at tick time T_k + p * length it
    /// shows lerp(pos_(k-1), pos_k, p). So the drawn position passes through pos_k at T_k + length,
    /// and is a straight line between those points. Each tick is stored as that point; Sample()
    /// interpolates between stored points, and runs a little past the newest one in a straight line.
    /// Plain .NET, no UnityEngine.
    /// </summary>
    public sealed class TickHistory
    {
        const int Size = 16;
        readonly long[] t = new long[Size];
        readonly double[] x = new double[Size], y = new double[Size], z = new double[Size];
        readonly float[] yaw = new float[Size];
        int count, newest = -1;
        long lastTickQpc;
        double tickLength; // in QPC units

        public int Count => count;

        public void Clear()
        {
            count = 0;
            newest = -1;
            lastTickQpc = 0;
        }

        /// <summary>Take in Minecraft's latest state (called whenever McState is read). Adds a point per new tick.</summary>
        public void Observe(in McState mc, long qpcFrequency)
        {
            if (!mc.HasTicks) return;
            tickLength = mc.TickMs / 1000.0 * qpcFrequency;
            // Each Minecraft frame re-reports the same tick with a slightly different time estimate;
            // a new tick is one more than half a tick later than the last one taken.
            if (count > 0 && Math.Abs(mc.TickQpc - lastTickQpc) < tickLength / 2) return;
            if (count == 0) Add(mc.TickQpc, mc.PrevX, mc.PrevY, mc.PrevZ, mc.BodyYawO); // the previous tick, drawn at T_k
            Add(mc.TickQpc + (long)tickLength, mc.CurX, mc.CurY, mc.CurZ, mc.BodyYawCur);
            lastTickQpc = mc.TickQpc;
        }

        void Add(long time, double px, double py, double pz, float pyaw)
        {
            newest = (newest + 1) % Size;
            t[newest] = time;
            x[newest] = px;
            y[newest] = py;
            z[newest] = pz;
            yaw[newest] = pyaw;
            if (count < Size) count++;
        }

        /// <summary>
        /// Minecraft's drawn position at this time. Returns how far past the newest point the time is,
        /// in ticks (negative = inside the history), or NaN if there's no history.
        /// </summary>
        public double Sample(long time, out double px, out double py, out double pz, out float pyaw)
        {
            px = py = pz = 0;
            pyaw = 0;
            if (count == 0) return double.NaN;
            int n = newest;
            if (count == 1 || time >= t[n])
            {
                // Past the newest point: continue the last segment for up to half a tick, then hold.
                int p = count > 1 ? (n + Size - 1) % Size : n;
                double span = t[n] - t[p];
                double over = Math.Min(time - t[n], tickLength / 2);
                double f = span > 0 ? over / span : 0;
                px = x[n] + (x[n] - x[p]) * f;
                py = y[n] + (y[n] - y[p]) * f;
                pz = z[n] + (z[n] - z[p]) * f;
                pyaw = yaw[n];
                return tickLength > 0 ? (time - t[n]) / tickLength : 0;
            }
            // Walk back to the segment containing the time.
            int b = n;
            for (int i = 1; i < count; i++)
            {
                int a = (b + Size - 1) % Size;
                if (time >= t[a])
                {
                    double f = t[b] > t[a] ? (double)(time - t[a]) / (t[b] - t[a]) : 1;
                    px = x[a] + (x[b] - x[a]) * f;
                    py = y[a] + (y[b] - y[a]) * f;
                    pz = z[a] + (z[b] - z[a]) * f;
                    float d = ((yaw[b] - yaw[a]) % 360f + 540f) % 360f - 180f;
                    pyaw = yaw[a] + d * (float)f;
                    return tickLength > 0 ? (time - t[n]) / tickLength : 0;
                }
                b = a;
            }
            // Older than the whole history: the oldest point.
            px = x[b];
            py = y[b];
            pz = z[b];
            pyaw = yaw[b];
            return tickLength > 0 ? (time - t[n]) / tickLength : 0;
        }
    }
}
