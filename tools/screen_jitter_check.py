"""How much the spore shakes on screen, from a camera trace (-mcCameraTrace).

usage: python -I tools/screen_jitter_check.py work/build/camera_trace.csv [mode] [skip_frames]

For every frame: the angle between the camera's final view direction (viewYaw, and the final
downward angle = downBeforeTilt + tilt) and the direction from the camera to the spore's middle.
A steady follow camera keeps that angle steady; frame-to-frame wobble in it is what you see as
jitter. Prints the wobble (each frame's angle minus a 9-frame moving average), split into
horizontal and vertical, plus how evenly the spore itself moves (step lengths).
"""
import csv
import math
import statistics
import sys

rows = [r for r in csv.DictReader(open(sys.argv[1], encoding='utf-8')) if None not in r.values()]
if len(sys.argv) > 2:
    rows = [r for r in rows if r['mode'] == sys.argv[2]]
rows = rows[int(sys.argv[3]) if len(sys.argv) > 3 else 10:]

h, v, steps = [], [], []
prev = None
for r in rows:
    cx, cy, cz = float(r['camX']), float(r['camY']), float(r['camZ'])
    sx, sy, sz = float(r['sporeX']), float(r['sporeY']) + 0.85, float(r['sporeZ'])
    yaw = float(r['viewYaw'])
    down = float(r['downBeforeTilt']) + float(r['tilt'])
    dx, dy, dz = sx - cx, sy - cy, sz - cz
    to_yaw = math.degrees(math.atan2(dx, dz))
    to_down = -math.degrees(math.atan2(dy, math.hypot(dx, dz)))
    h.append(((to_yaw - yaw + 180) % 360) - 180)
    v.append(to_down - down)
    if prev:
        steps.append(math.dist(prev, (float(r['sporeX']), float(r['sporeY']), float(r['sporeZ']))))
    prev = (float(r['sporeX']), float(r['sporeY']), float(r['sporeZ']))


def wobble(series, w=4):
    return [abs(series[i] - sum(series[i - w:i + w + 1]) / (2 * w + 1)) for i in range(w, len(series) - w)]


wh, wv = wobble(h), wobble(v)
print(f"{len(rows)} frames; spore on screen, wobble in degrees: horizontal mean {statistics.mean(wh):.3f} max {max(wh):.3f}; "
      f"vertical mean {statistics.mean(wv):.3f} max {max(wv):.3f}")
moving = [s for s in steps if s > 1e-4]
if moving:
    print(f"spore steps: {len(moving)}/{len(steps)} frames moving, mean {statistics.mean(moving):.4f} units, "
          f"unevenness (stdev/mean) {statistics.pstdev(moving) / statistics.mean(moving):.2f}")
