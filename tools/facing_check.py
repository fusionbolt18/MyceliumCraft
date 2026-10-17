"""Measure how steady the spore's facing and the camera are, from a camera trace (-mcCameraTrace).

usage: python -I tools/facing_check.py work/build/camera_trace.csv [mode]

  - spore facing: frame-to-frame turn, and how often it reverses direction (a flicker between two
    facings shows as many reversals while the wanted facing is steady)
  - camera vs its target: frame-to-frame change of the camera's offset from the point it follows,
    minus the smooth part (a 9-frame moving average); jitter shows up as a large remainder
"""
import csv
import statistics
import sys

rows = [r for r in csv.DictReader(open(sys.argv[1], encoding='utf-8')) if None not in r.values()]
mode = sys.argv[2] if len(sys.argv) > 2 else None
if mode is not None:
    rows = [r for r in rows if r['mode'] == mode]
rows = rows[10:]  # skip the start
yaw = [float(r['sporeYaw']) for r in rows]
d = [((b - a + 180) % 360) - 180 for a, b in zip(yaw, yaw[1:])]
rev = sum(1 for a, b in zip(d, d[1:]) if abs(a) > 0.5 and abs(b) > 0.5 and (a > 0) != (b > 0))
print(f"{len(rows)} frames; spore facing: mean {statistics.mean(yaw):.1f}, spread (stdev) {statistics.pstdev(yaw):.2f} deg, "
      f"biggest turn {max(abs(x) for x in d):.2f} deg/frame, direction reversals {rev}")


def jitter(series, w=4):
    out = []
    for i in range(w, len(series) - w):
        avg = sum(series[i - w:i + w + 1]) / (2 * w + 1)
        out.append(abs(series[i] - avg))
    return out


for axis in ('X', 'Y', 'Z'):
    if axis == 'Y':
        off = [float(r['camY']) - float(r['targetY']) for r in rows]
    else:
        off = [float(r['cam' + axis]) - float(r['target' + axis]) for r in rows]
    j = jitter(off)
    print(f"camera offset {axis}: jitter mean {statistics.mean(j):.4f}, max {max(j):.4f} units")
sp = [(float(r['sporeX']), float(r['sporeZ'])) for r in rows]
tg = [(float(r['targetX']), float(r['targetZ'])) for r in rows]
lag = [((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5 for a, b in zip(sp, tg)]
print(f"target (CamTracker) vs spore, horizontal: mean {statistics.mean(lag):.3f}, max {max(lag):.3f} units; "
      f"jitter of that gap: mean {statistics.mean(jitter(lag)):.4f}")
