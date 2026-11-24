# Map4P grid v7 (2026-11-24, Task 46 phase B): v5 + the outer waterway on ALL FOUR sides (v6 was the west prototype).
# The v6 west edits are written in the west frame and applied once per quarter turn. One quarter turn maps a cell (r, c) to
# (79 - c, r), the same as FactoryWarsMaps.TurnShape (x, z) -> (-z, x): west -> south -> east -> north. Per side:
# - outer waterway: the edge E band (2 cells, 16 m) becomes water 'W' over the full length;
# - corner dock basins 24 x 24 m (3 x 3 cells) + a 16 m stub toward the next side;
# - neutral harbour: 24 m of water beside the edge expansion yard, a quay row 'w', the yard content 16 m inland ('M'),
#   with planted corners 'K' (about 37 % of the yard green), exactly as the v6 west yard.
# Run from design/maps: python gen_grid_v7.py
import csv
N = 80
g = [r for r in csv.reader(open('Map4P_grid_v5.csv'))]
assert len(g) == N and all(len(r) == N for r in g)


def west_edits():
    """The v6 west edits, in order, as (r, c, value)."""
    out = []
    for r in range(N):
        for c in (0, 1):
            out.append((r, c, 'W'))
    for r0 in (0, 77):
        for r in range(r0, r0 + 3):
            for c in range(3):
                out.append((r, c, 'W'))
    for r in (0, 1, 78, 79):
        out.append((r, 3, 'W'))
    for r in range(36, 44):
        out.append((r, 2, 'W'))
        out.append((r, 3, 'w'))
        for c in range(4, 12):
            out.append((r, c, 'M'))
    for r, c0, c1 in [(36, 4, 8), (37, 4, 8), (42, 4, 8), (43, 4, 8), (36, 8, 12), (43, 8, 12)]:
        for c in range(c0, c1):
            out.append((r, c, 'K'))
    return out


def turn(r, c, quarter_turns):
    for _ in range(quarter_turns):
        r, c = N - 1 - c, r
    return r, c


edits = west_edits()
# Yard rows first per side, then the water: a later side's water must win over an earlier side's yard edits at the corners.
for t in range(4):
    for r, c, v in edits:
        rr, cc = turn(r, c, t)
        if v in ('M', 'K', 'w'):
            g[rr][cc] = v
for t in range(4):
    for r, c, v in edits:
        rr, cc = turn(r, c, t)
        if v == 'W':
            g[rr][cc] = v

# N/S links: the central canal's north end (row 12) reaches the outer waterway through a 16 m channel beside the north yard
# (cols 44-45, rows 2-11) under a low culvert deck 'G'; the south one is its 180-degree mirror (the map generator gives every
# bridge beyond |z| 200 m a 3 m clearance: small vessels only).
v5 = [r for r in csv.reader(open('Map4P_grid_v5.csv'))]
for r in range(2, 12):
    for c in (44, 45):
        for rr, cc in ((r, c), (N - 1 - r, N - 1 - c)):
            # Culvert (like the canal under the central compound): a low deck over the whole channel keeps the ground
            # continuous, so every seat's routes stay equal; small vessels pass underneath (3 m clearance).
            g[rr][cc] = 'G'
# The 1-cell building strip left between the channel and the next street becomes a pocket park (no wall-less building cells).
for r in range(4, 11):
    for rr, cc in ((r, 46), (N - 1 - r, N - 1 - 46)):
        if g[rr][cc] == 'H':
            g[rr][cc] = 'K'
for r in range(2, 12):
    for c in (44, 45):
        assert g[r][c] == 'G' and g[N - 1 - r][N - 1 - c] == 'G'
assert g[12][44] == 'W' and g[67][35] == 'W', 'channel meets the canal'

with open('Map4P_grid_v7.csv', 'w', newline='') as f:
    csv.writer(f, lineterminator='\n').writerows(g)

# Self-checks: every side has its 16 m waterway, its harbour water + quay, and the edits are 90-degree symmetric.
def at(r, c):
    return g[r][c]
for t in range(4):
    for r in range(N):
        for c in (0, 1):
            rr, cc = turn(r, c, t)
            assert at(rr, cc) == 'W', (t, rr, cc)
    for r in range(36, 44):
        assert at(*turn(r, 2, t)) == 'W' and at(*turn(r, 3, t)) == 'w', (t, r)
print('wrote Map4P_grid_v7.csv')
