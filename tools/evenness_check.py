"""How evenly camera values change frame to frame, from a camera trace (-mcCameraTrace).

usage: python -I tools/evenness_check.py work/build/camera_trace.csv [mode]

For each value: the frame-to-frame change d, and unevenness = mean |d(i) - d(i-1)| / mean |d|.
Smooth motion (even a curve) changes d only a little from frame to frame (close to 0); stuttering
motion (2, 0, 1 steps) gives about 1 or more.
"""
import csv
import math
import sys

rows = [r for r in csv.DictReader(open(sys.argv[1], encoding='utf-8')) if None not in r.values()]
if len(sys.argv) > 2:
    rows = [r for r in rows if r['mode'] == sys.argv[2]]
rows = rows[10:]
series = {
    'final view down': [float(r['downBeforeTilt']) + float(r['tilt']) for r in rows],
    'orbit Y': [float(r['y']) for r in rows],
    'camera height over target': [float(r['camY']) - float(r['targetY']) for r in rows],
    'camera distance to target': [math.dist((float(r['camX']), float(r['camZ'])), (float(r['targetX']), float(r['targetZ']))) for r in rows],
    'Minecraft pitch (input)': [float(r['pitch']) for r in rows],
}
for name, v in series.items():
    d = [b - a for a, b in zip(v, v[1:])]
    m = sum(abs(x) for x in d) / len(d)
    u = sum(abs(b - a) for a, b in zip(d, d[1:])) / (len(d) - 1) / m if m > 0 else 0
    print(f"{name:28s}: mean change {m:.4f}/frame, unevenness {u:.2f}")
