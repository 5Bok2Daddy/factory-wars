# Map4P grid v6 (2026-11-24, Task 46 phase A): v5 + the WEST outer waterway prototype.
# - Outer waterway: the empty west E band (cols 0-1, 16 m) becomes water 'W' over the full height.
# - Corner dock basins NW/SW: 24x24 m (rows 0-2 / 77-79, cols 0-2) + a 16 m stub toward the future north/south waterway (col 3).
#   (2,2) and (77,2) were the thin edge sidewalk strip ('B' outside the base flood), not base high ground: no base cell is lost.
# - W neutral harbour: the west expansion yard (rows 36-43) gets 24 m of water (cols 0-2), a quay row (col 3, 'w'),
#   and the yard content moves 16 m inland: yard cells 'M' now cols 4-11 (the city cells there are replaced).
# Phase B mirrors this to N/E/S. Run from design/maps: python gen_grid_v6.py
import csv
N = 80
g = [r for r in csv.reader(open('Map4P_grid_v5.csv'))]
assert len(g) == N and all(len(r) == N for r in g)

for r in range(N):
    for c in (0, 1):
        g[r][c] = 'W'
for r0 in (0, 77):
    for r in range(r0, r0 + 3):
        for c in range(3):
            g[r][c] = 'W'
for r in (0, 1, 78, 79):
    g[r][3] = 'W'
for r in range(36, 44):
    g[r][2] = 'W'
    g[r][3] = 'w'
    for c in range(4, 12):
        g[r][c] = 'M'

with open('Map4P_grid_v6.csv', 'w', newline='') as f:
    csv.writer(f, lineterminator='\n').writerows(g)
print('wrote Map4P_grid_v6.csv')
