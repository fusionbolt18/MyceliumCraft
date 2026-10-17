"""Check ground models against positions the spore really walked.

usage (from the repo folder):
  python -I tools/survey_check.py work/build/survey.csv work/build/MyceliumCraft.log [more logs ...]

survey.csv comes from: work/build/MyceliumCraft.exe -mcScene "New Tutorial" -mcSurvey
Prints, per model, at how many walked spore positions Minecraft's player (0.6 x 1.8 blocks, stepping
up 0.6; times --player-scale if given) would fit, and what blocks it where it does not. See docs/DESIGN.md section 5.
"""
import re
import sys
from collections import Counter

args = sys.argv[1:]
PLAYER_SCALE = 1.0  # Minecraft's player scale attribute (1 = normal size)
FILL_BELOW_ONE_SIDED = 0.0  # --fill-below: gaps under a surface with no underside smaller than this (units) are filled
if '--fill-below' in args:
    i = args.index('--fill-below')
    FILL_BELOW_ONE_SIDED = float(args[i + 1])
    del args[i:i + 2]
if '--player-scale' in args:
    i = args.index('--player-scale')
    PLAYER_SCALE = float(args[i + 1])
    del args[i:i + 2]
survey_path, logs = args[0], args[1:]
cols = {}
cell = None
x0 = z0 = None
with open(survey_path, encoding='utf-8') as f:
    for line in f:
        if line.startswith('#'):
            cell = float(re.search(r'cell=([0-9.]+)', line).group(1))
            continue
        parts = line.rstrip('\n').split(',')
        x, z = float(parts[0]), float(parts[1])
        if x0 is None:
            x0, z0 = x, z
        surf = []
        for p in parts[2:]:
            y, name, front, anyc = p.split('|')
            surf.append((float(y), name, None if front == '-' else float(front), None if anyc == '-' else float(anyc)))
        cols[(round((x - x0) / cell), round((z - z0) / cell))] = surf


def column(x, z):
    return cols.get((round((x - x0) / cell), round((z - z0) / cell)))


pos = []
for log in logs:
    for line in open(log, encoding='utf-8'):
        m = re.search(r'spore=\(([-0-9.]+), ([-0-9.]+), ([-0-9.]+)\)', line)
        if m and 'status' in line:
            pos.append(tuple(float(v) for v in m.groups()))
pos = sorted(set(pos))
print(f"{len(cols)} columns, cell {cell}; {len(pos)} distinct walked spore positions")

PLAYER_H = 1.8 * 1.4  # Minecraft player height in Mycelium units (scale 1.4)
stats = Counter()
under = []
for (x, y, z) in pos:
    c = column(x, z)
    if not c:
        stats['outside survey'] += 1
        continue
    feet = y - 0.02
    over = [s for s in c if s[0] > feet + 0.3]
    if not over:
        stats['open sky'] += 1
        continue
    stats['something overhead'] += 1
    under.append((x, y, z, c))

print(dict(stats))
names = Counter(s[1] for (_, _, _, c) in under for s in c)
print("colliders in columns with something overhead:", names.most_common(10))
# For walked positions under something: is the ceiling seen by an up-ray (front faces / any faces)?
ceil = Counter()
for (x, y, z, c) in under:
    feet = y - 0.02
    below = [s for s in c if s[0] <= feet + 0.3]
    floor = below[0] if below else None
    if floor is None:
        ceil['no floor below feet'] += 1
        continue
    f_front, f_any = floor[2], floor[3]
    gap_front = None if f_front is None else f_front - floor[0]
    gap_any = None if f_any is None else f_any - floor[0]
    key = ('front ' + ('none' if gap_front is None else ('>=player' if gap_front >= PLAYER_H else '<player'))
           + ' / any ' + ('none' if gap_any is None else ('>=player' if gap_any >= PLAYER_H else '<player')))
    ceil[key] += 1
print("ceiling above the walked floor:", dict(ceil))
print("examples:")
for (x, y, z, c) in under[:: max(1, len(under) // 12)]:
    print(f"  spore ({x:.1f}, {y:.1f}, {z:.1f}) column: " + ' ; '.join(f"{s[0]:.1f} {s[1]} up={s[2]}/{s[3]}" for s in c))

# Headroom: from the walked feet to the lowest thing overhead (a surface above, or a ceiling found from below).
head = []
for (x, y, z, c) in under:
    feet = y - 0.02
    overhead = [s[0] for s in c if s[0] > feet + 0.3]
    below = [s for s in c if s[0] <= feet + 0.3]
    cands = overhead[:]
    if below and below[0][3] is not None:
        cands.append(below[0][3])
    head.append((min(cands) - feet, x, y, z))
head.sort()
print("headroom over walked positions (Mycelium units), lowest 10:", [round(h[0], 2) for h in head[:10]])
buckets = Counter('<2.1 (MC crouch 1.5 blk)' if h[0] < 2.1 else '<2.52 (MC stand 1.8 blk)' if h[0] < 2.52 else '<4' if h[0] < 4 else '>=4' for h in head)
print("headroom buckets:", dict(buckets))
for h in head[:5]:
    print(f"  tight: headroom {h[0]:.2f} at spore ({h[1]:.1f}, {h[2]:.1f}, {h[3]:.1f}):", ' ; '.join(f"{s[0]:.1f} {s[1]} up={s[2]}/{s[3]}" for s in column(h[1], h[3])))

# ---- Model comparison: would Minecraft's player (0.6 x 1.8 blocks) fit at each walked position? ----
S = 1.4
HALF_W = 0.3 * S * PLAYER_SCALE   # half width, Mycelium units
HEIGHT = 1.8 * S * PLAYER_SCALE
SLAB = 0.5 * S            # thickness given to a surface with no underside found


def solids_layered(c):
    """Solid intervals [bottom, top] of a column: the new model."""
    out = []
    n = len(c)
    if n == 0:
        return out
    out.append((-1e9, c[-1][0]))
    for k in range(n - 1):
        top = c[k][0]
        below_surface = c[k + 1][0]
        under = c[k + 1][3]            # ceiling found going up from the next surface down (any faces)
        if under is not None and under <= top + 0.05:
            bottom = under
        elif top - below_surface < FILL_BELOW_ONE_SIDED:
            bottom = below_surface  # too small to be a real passage: hidden ground under a one-sided surface
        else:
            bottom = top - SLAB
        bottom = max(bottom, below_surface)
        out.append((bottom, top))
    return out


def solids_top(c):
    return [(-1e9, c[0][0])] if c else []


def fits(x, feet, z, model):
    lift = 0.6 * S  # Minecraft steps up 0.6 blocks without jumping
    lo, hi = feet + lift, feet + HEIGHT
    for dx in (-HALF_W, 0, HALF_W):
        for dz in (-HALF_W, 0, HALF_W):
            c = column(x + dx, z + dz)
            if c is None:
                continue
            for (b, t) in model(c):
                if b < hi and t > lo:
                    return False, (x + dx, z + dz, b, t)
    return True, None


for name, model in (("top surface only", solids_top), ("layered (new)", solids_layered)):
    bad = []
    for (x, y, z) in pos:
        ok, why = fits(x, y - 0.02, z, model)
        if not ok:
            bad.append(((x, y, z), why))
    print(f"{name}: player fits at {len(pos) - len(bad)}/{len(pos)} walked positions")
    for (p, why) in bad[:6]:
        print(f"    blocked at spore ({p[0]:.1f}, {p[1]:.1f}, {p[2]:.1f}) by solid {why[2]:.2f}..{why[3]:.2f} in column ({why[0]:.1f}, {why[1]:.1f}):",
              ' ; '.join(f"{s[0]:.1f} {s[1]} up={s[3]}" for s in column(why[0], why[1])))

# What blocks the layered model, and how much headroom the spore had there.
blockers = Counter()
for (x, y, z) in pos:
    ok, why = fits(x, y - 0.02, z, solids_layered)
    if not ok:
        col = column(why[0], why[1])
        name = next((s[1] for s in col if abs(s[0] - why[3]) < 0.01), '?')
        blockers[(name, round(why[2] - (y - 0.02), 1))] += 1
print("layered blockers (collider, solid bottom above feet):", blockers.most_common())


def solids_layered4(c):
    """The layered model as built in the game: at most 4 pieces, smallest gaps closed first."""
    p = solids_layered(c)
    while len(p) > 4:
        best = min(range(len(p) - 1), key=lambda k: p[k + 1][0] - p[k][1])
        p[best] = (p[best][0], p[best + 1][1])
        del p[best + 1]
    return p


def solids_top4(c):
    """What the previous build did: only the top 4 surfaces were seen."""
    return solids_layered(c[:4]) if len(c) > 4 else solids_layered(c)


for name, model in (("layered, top 4 surfaces only (previous build)", solids_top4), ("layered, 4 pieces after merging (new)", solids_layered4)):
    bad = sum(1 for (x, y, z) in pos if not fits(x, y - 0.02, z, model)[0])
    print(f"{name}: player fits at {len(pos) - bad}/{len(pos)} walked positions")


# Gaps under one-sided surfaces (no underside found) across the whole survey: their sizes, and how
# many the spore really walked in. Small ones the spore never used are likely hidden ground.
gaps = Counter()
for col in cols.values():
    for k in range(len(col) - 1):
        if col[k + 1][3] is None or col[k + 1][3] > col[k][0] + 0.05:
            g = col[k][0] - col[k + 1][0]
            gaps[min(int(g), 12)] += 1
print("gaps under one-sided surfaces, by size in whole units (12 = 12+):", sorted(gaps.items()))
walked_gaps = []
for (x, y, z) in pos:
    col = column(x, z)
    if not col:
        continue
    feet = y - 0.02
    for k in range(len(col) - 1):
        if col[k + 1][0] <= feet + 0.3 < col[k][0] and (col[k + 1][3] is None or col[k + 1][3] > col[k][0] + 0.05):
            walked_gaps.append(round(col[k][0] - col[k + 1][0], 2))
print("one-sided gaps the spore walked in (units):", sorted(walked_gaps)[:12], "... n =", len(walked_gaps))

# Minecraft's own positions from the logs (mc=(...), converted to Mycelium units): where the
# Minecraft player actually stood. Any that don't fit the model are places Minecraft got into that
# the model says are closed (with the fill on: the traps it closes).
mc_pos = set()
for log in logs:
    for line in open(log, encoding='utf-8'):
        m = re.search(r'phase=Driving mc=\(([-0-9.]+), ([-0-9.]+), ([-0-9.]+)\)', line)
        if m:
            mx, my, mz = (float(v) for v in m.groups())
            mc_pos.add((mx * S, my * S, -mz * S))
closed = [p for p in sorted(mc_pos) if not fits(p[0], p[1], p[2], solids_layered4)[0]]
print(f"Minecraft positions the model says are closed: {len(closed)}/{len(mc_pos)}",
      [f"({p[0]:.1f}, {p[1]:.1f}, {p[2]:.1f})" for p in closed[:8]])
