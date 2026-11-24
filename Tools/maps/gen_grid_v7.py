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

# B2 (2026-11-24 user request): fill the empty X lots between edge yards and PMC camp with ~60 % buildings ('H') and ~40 % real ruins ('X').
# Keep every 's' alley and 'R' road untouched. Choose organic block clusters (no checkerboard) on the west side, then mirror 180 deg to east.
# Reserve (39, 17) on west and its mirror (40, 62) on east as 'X' for the PolygonConstruction half-built site.
B2_BUILD_BLOCKS = [
    # West top strip (rows 33-37): leave corner (33,12..13) as X ruins so NW->W / SE->E outpost routes stay <= 5% fair
    (34, 12, 3, 2), (33, 15, 2, 3), (35, 17, 3, 3), (33, 21, 3, 2), (35, 25, 3, 2),
    # West mid strip (rows 39-40): buildings flanking the central construction site at (39, 17)
    (39, 12, 2, 2), (39, 15, 2, 2), (39, 19, 2, 1), (39, 21, 2, 2), (39, 25, 2, 2),
    # West bottom strip (rows 42-46): vertical mirror of top strip (leaves corner 46,12..13 as X ruins)
    (43, 12, 3, 2), (45, 15, 2, 3), (42, 17, 3, 3), (44, 21, 3, 2), (42, 25, 3, 2),
]
w_to_h = e_to_h = 0
for r0, c0, h_len, w_len in B2_BUILD_BLOCKS:
    for dr in range(h_len):
        for dc in range(w_len):
            r, c = r0 + dr, c0 + dc
            mr, mc = N - 1 - r, N - 1 - c
            if (r, c) == (39, 17) or (mr, mc) == (40, 62):
                continue
            if g[r][c] == 'X' and g[mr][mc] in ('X', 'Y', 'K', 'L'):
                g[r][c] = 'H'
                g[mr][mc] = 'H'
                w_to_h += 1
                e_to_h += 1
assert g[39][17] == 'X' and g[40][62] == 'X', 'construction sites stay X'
w_x_left = sum(1 for r in range(33, 47) for c in range(12, 27) if g[r][c] in ('X', 'Y'))
e_x_left = sum(1 for r in range(33, 47) for c in range(53, 68) if g[r][c] in ('X', 'Y'))
assert w_to_h == e_to_h, f'180-deg symmetry mismatch: west {w_to_h} != east {e_to_h}'
print(f'B2 X->H: west {w_to_h}H/{w_x_left}XY ({w_to_h/(w_to_h+w_x_left)*100:.1f}% H), east {e_to_h}H/{e_x_left}XY ({e_to_h/(e_to_h+e_x_left)*100:.1f}% H)')

# Harness fix (2026-11-24): break up the remaining bare-looking Y container strips on the eastern upper side (rows 28, 30-31, cols 53-67)
# and their exact 180-degree west mirror (rows 48-49, 51, cols 12-26) with buildings ('H'), parking ('L'), and parks ('K').
EAST_Y_DRESS = [
    (30, 56, 'H'), (30, 57, 'H'), (31, 56, 'H'), (31, 57, 'H'),
    (30, 60, 'H'), (30, 61, 'H'), (31, 60, 'H'), (31, 61, 'H'),
    (30, 62, 'K'), (31, 62, 'L'), (30, 63, 'H'), (31, 63, 'H'),
    (30, 66, 'L'), (31, 66, 'L'), (30, 67, 'K'), (31, 67, 'L'),
    (28, 56, 'H'), (28, 57, 'K'), (28, 60, 'H'), (28, 61, 'H'), (28, 62, 'L'), (28, 66, 'L'), (28, 67, 'K'),
]
y_dressed = 0
for r, c, ch in EAST_Y_DRESS:
    mr, mc = N - 1 - r, N - 1 - c
    if g[r][c] == 'Y' and g[mr][mc] == 'Y':
        g[r][c] = ch
        g[mr][mc] = ch
        y_dressed += 1
print(f'Eastern/Western Y strip dressed (180-sym pairs): {y_dressed}')

# Fairness polish (2026-11-24): the N/S culvert + park (col 46) opened a direct diagonal entry from NE->North & SW->South (241.1 m),
# whereas NW->North & SE->South detoured around 3 H cells on cols 34-35 (254.4 m = 5.5 % spread > 5 %). Turn those 3 mirror pairs
# into pocket parks 'K' so all 4 seats have equal outpost approach (<= 2 % spread) while keeping 180-degree symmetry.
for r, c in [(4, 35), (6, 34), (6, 35), (7, 34)]:
    for rr, cc in ((r, c), (N - 1 - r, N - 1 - c)):
        if g[rr][cc] == 'H':
            g[rr][cc] = 'K'

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
