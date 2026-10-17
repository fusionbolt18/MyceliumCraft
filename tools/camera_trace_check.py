"""Measure camera smoothness from a per-frame trace (MyceliumCraft.exe -mcCameraTrace).

usage: python -I tools/camera_trace_check.py work/build/camera_trace.csv

Prints, for the final view angle (wantDown, what the tilt makes the camera look at), the camera's
height position (camY minus the target's height) and the orbit value Y:
  - how often a frame's change is very different from the change the frame before
    ("uneven steps": the motion stutters instead of flowing),
  - the largest single-frame jump,
  - how often a value doesn't move at all while the one before and after do (a hold-then-jump).
"""
import csv
import statistics
import sys

rows = list(csv.DictReader(open(sys.argv[1], encoding='utf-8')))
rows = [r for r in rows if None not in r.values() and r['pitch'] not in ('', 'NaN')]  # skip a line cut off when the game was closed
t = [float(r['time']) for r in rows]
series = {
    'view angle (wantDown, deg)': [float(r['wantDown']) for r in rows],
    'camera height over target': [float(r['camY']) - float(r['targetY']) for r in rows],
    'orbit Y': [float(r['y']) for r in rows],
    'minecraft pitch (input)': [float(r['pitch']) for r in rows],
}
dt = [b - a for a, b in zip(t, t[1:])]
print(f"{len(rows)} frames over {t[-1] - t[0]:.1f} s, median frame {1000 * statistics.median(dt):.1f} ms "
      f"({1 / statistics.median(dt):.0f} fps); Minecraft frames {rows[0]['mcFrame']}..{rows[-1]['mcFrame']}")
for name, v in series.items():
    d = [b - a for a, b in zip(v, v[1:])]
    moving = [x for x in d if abs(x) > 1e-6]
    if not moving:
        print(f"{name}: never moves")
        continue
    typical = statistics.median(abs(x) for x in moving)
    uneven = sum(1 for a, b in zip(d, d[1:]) if abs(a) > 1e-6 or abs(b) > 1e-6
                 if abs(abs(b) - abs(a)) > 2 * typical)
    holds = sum(1 for a, b, c in zip(d, d[1:], d[2:]) if abs(a) > 1e-6 and abs(b) <= 1e-6 and abs(c) > 1e-6)
    print(f"{name}: typical step {typical:.4f}, biggest {max(abs(x) for x in d):.4f}, "
          f"frames moving {len(moving)}/{len(d)}, uneven steps {uneven}, hold-then-jump {holds}")
