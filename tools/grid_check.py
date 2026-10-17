"""Check Minecraft player positions from a log against a grid dumped by the game (-mcDumpGrid).

usage: python -I tools/grid_check.py work/build/grid_v1.csv <log> [--player-scale 0.674] [--from T] [--to T] [--spore]

--spore checks the spore's positions instead (where Mycelium says the spore really stood): those
should all be open, or Minecraft can't follow the spore there.

For every logged Minecraft position (mc=(...), Minecraft coordinates) it asks: would Minecraft's
player box (0.6 x 1.8 blocks times the scale, stepping up 0.6) overlap a solid piece of the grid?
"Closed" positions are places Minecraft stood that the grid now says are solid, e.g. a trap the
grid closes. Uses the same pieces Minecraft's collision is built from (GroundGrid.forEachBox).
"""
import re
import sys

args = sys.argv[1:]
opts = {'--player-scale': 1.0, '--from': -1e9, '--to': 1e9}
for k in list(opts):
    if k in args:
        i = args.index(k)
        opts[k] = float(args[i + 1])
        del args[i:i + 2]
SPORE = '--spore' in args
if SPORE:
    args.remove('--spore')
grid_path, log_path = args
head = open(grid_path, encoding='utf-8').readline()
meta = dict(m.groups() for m in re.finditer(r'(\w+)=([-0-9.eE+]+)', head))
n, ox, oz, cell = int(meta['n']), float(meta['originX']), float(meta['originZ']), float(meta['cell'])
pieces = {}
for line in open(grid_path, encoding='utf-8'):
    if line.startswith('#'):
        continue
    v = line.strip().split(',')
    i, j, f = int(v[0]), int(v[1]), [float(x) for x in v[2:]]
    pieces[(i, j)] = [(f[2 * k], f[2 * k + 1]) for k in range(len(f) // 2) if f[2 * k + 1] < 1e29]

sc = opts['--player-scale']
half_w, height, step = 0.3 * sc, 1.8 * sc, 0.6


EPS = 0.01  # touching is not overlapping (Minecraft stands flush against walls; the log rounds to 0.01)


def closed(x, y, z):
    i0, i1 = int((x - half_w + EPS - ox) // cell), int((x + half_w - EPS - ox) // cell)
    j0, j1 = int((z - half_w + EPS - oz) // cell), int((z + half_w - EPS - oz) // cell)
    for i in range(i0, i1 + 1):
        for j in range(j0, j1 + 1):
            for (b, t) in pieces.get((i, j), []):
                if t - b <= 1e-4:
                    continue  # zero thickness: Minecraft gets no box from it (GroundGrid.forEachBox)
                if b < y + height - EPS and t > y + step + EPS:
                    return (i, j, b, t)
    return None


SCALE = 1.4  # Mycelium units per block, for --spore
rows = []
for line in open(log_path, encoding='utf-8'):
    if SPORE:
        # Where the spore really stood (Mycelium coords, rb position = feet + 0.02), as Minecraft coords.
        m = re.search(r'\+ *([0-9.]+)s\].*status:.*spore=\(([-0-9.]+), ([-0-9.]+), ([-0-9.]+)\)', line)
    else:
        m = re.search(r'\+ *([0-9.]+)s\].*phase=Driving mc=\(([-0-9.]+), ([-0-9.]+), ([-0-9.]+)\)', line)
    if not m:
        continue
    t, x, y, z = (float(g) for g in m.groups())
    if SPORE:
        x, y, z = x / SCALE, (y - 0.02) / SCALE, -z / SCALE
    if not (opts['--from'] <= t <= opts['--to']):
        continue
    if not (ox <= x < ox + n * cell and oz <= z < oz + n * cell):
        continue
    rows.append((t, x, y, z, closed(x, y, z)))
bad = [r for r in rows if r[4]]
print(f"{grid_path} (fillBelow={meta.get('fillBelow')}): {'spore' if SPORE else 'Minecraft'} positions closed by the grid: {len(bad)}/{len(rows)}")
for r in bad[:6]:
    print(f"  t={r[0]:.1f} mc=({r[1]:.2f}, {r[2]:.2f}, {r[3]:.2f}) overlaps piece {r[4][2]:.2f}..{r[4][3]:.2f} in cell {r[4][0]},{r[4][1]}")
