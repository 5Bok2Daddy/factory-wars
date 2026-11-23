# Map4P grid v5 (2026-11-23, Task 39 card 1): the v4 CSV (hand-edited) with a smaller central compound.
# Octagon about 147 m across -> 96 m (12 cells). Freed cells: ring road (2 cells) + concrete plaza (1 cell)
# + city lots with alleys that continue the outer streets. 180-degree symmetric by construction.
import csv, sys
N = 80
H0 = N / 2
src = [r for r in csv.reader(open('Map4P_grid_v4.csv'))]
g = [row[:] for row in src]

def oct_d(r, c):
    ax, az = abs(c + 0.5 - H0), abs(r + 0.5 - H0)
    return max(ax, az, (ax + az) / 1.4)

def old_region(r, c):
    ax, az = abs(c + 0.5 - H0), abs(r + 0.5 - H0)
    return max(ax, az) <= 11 and ax + az <= 15.4

def sym_hash(r, c):
    a, b = (r, c), (N - 1 - r, N - 1 - c)
    r0, c0 = min(a, b)
    return (r0 * 73 + c0 * 151) % 17

COMPOUND, RING, PLAZA = 6.0, 8.0, 9.0
for r in range(N):
    for c in range(N):
        if not old_region(r, c):
            continue
        ax, az = abs(c + 0.5 - H0), abs(r + 0.5 - H0)
        d = oct_d(r, c)
        if d <= COMPOUND:
            if (ax * ax + az * az) ** 0.5 <= 2.4:
                g[r][c] = 'p'                      # helipad / trader hub
            elif 3.5 <= max(ax, az) <= 4.6 and (int(ax) + int(az)) % 2 == 0 and min(ax, az) > 1.0:
                g[r][c] = 'Y'                      # container ring (gate axes stay clear)
            else:
                g[r][c] = 'C'
        elif d <= RING:
            g[r][c] = 'R'                          # ring road around the compound wall
        elif d <= PLAZA:
            g[r][c] = 's'                          # concrete plaza
        elif abs(ax - az) <= 2.0:
            g[r][c] = 'R'                          # diagonal mains reach the ring road
        else:
            g[r][c] = 'L'                          # placeholder lot, refined below

# Lots: continue outer streets (R/s/G) inward to the plaza along the radial axis; quay next to the canal; the rest city.
def outward(r, c):
    ax, az = c + 0.5 - H0, r + 0.5 - H0
    if abs(ax) >= abs(az):
        return (0, 1 if ax > 0 else -1)
    return (1 if az > 0 else -1, 0)

lots = [(r, c) for r in range(N) for c in range(N) if old_region(r, c) and oct_d(r, c) > PLAZA and g[r][c] == 'L']
lots.sort(key=lambda rc: -oct_d(*rc))
street = set()
for r, c in lots:
    dr, dc = outward(r, c)
    o = g[r + dr][c + dc]
    if o in 'WG':
        g[r][c] = 'w'                              # quay where the canal ends
    elif o == 'w':
        g[r][c] = 's'                              # waterfront plaza behind the quay
    elif o in 'Rs' and not old_region(r + dr, c + dc) and g[r + 2 * dr][c + 2 * dc] in 'RsG':
        g[r][c] = 's'                              # an outer street running radially into the plaza
        street.add((r, c))
    elif (r + dr, c + dc) in street:
        g[r][c] = 's'
        street.add((r, c))
    else:
        h = sym_hash(r, c)
        # Walkable lots only (parks and parking): the old ring road was open ground, so routes to the gates stay fair.
        g[r][c] = 'K' if h % 3 == 0 else 'L'

# Single H cells boxed in by roads stay; the dress pass groups them.
for r in range(N):
    for c in range(N):
        a, b = g[r][c], g[N - 1 - r][N - 1 - c]
        sim = lambda ch: '.' if ch in 'XYKLwM' else ch
        assert sim(a) == sim(b), (r, c, a, b)

with open('Map4P_grid_v5.csv', 'w', newline='') as fh:
    csv.writer(fh).writerows(g)
for i, row in enumerate(g):
    if 22 <= i <= 57:
        print('%2d ' % i + ''.join(row))
print('compound cells', sum(row.count('C') + row.count('Y') + row.count('p') for row in g[26:54]))
