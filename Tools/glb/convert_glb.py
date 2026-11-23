"""GLB -> decimated single-material OBJ+MTL+palette PNG for Unity.
Per-face colour is sampled from the source texture (removes printed text/markings),
quantized into a palette, each colour group decimated separately, UVs point at palette cells.
usage: python -I convert_glb.py in.glb out_dir name target_tris [palette_size] [waterline_m]"""
import sys, os, numpy as np, trimesh, fast_simplification
from PIL import Image

src, out_dir, name, target = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
pal_n = int(sys.argv[5]) if len(sys.argv) > 5 else 48
# optional waterline (m above the lowest point): faces fully below it are dropped and y=0 becomes the waterline
waterline = float(sys.argv[6]) if len(sys.argv) > 6 else None
scene = trimesh.load(src, force='scene')
V, F, C, N = [], [], [], []
base = 0
for node in scene.graph.nodes_geometry:
    tf, gname = scene.graph[node]
    g = scene.geometry[gname]
    if not isinstance(g, trimesh.Trimesh) or len(g.faces) == 0:
        continue
    verts = trimesh.transform_points(g.vertices, tf)
    cols = None
    vis = g.visual
    try:
        if isinstance(vis, trimesh.visual.texture.TextureVisuals) and vis.uv is not None:
            mat = vis.material
            img = getattr(mat, 'baseColorTexture', None) or getattr(mat, 'image', None)
            fac = np.array(getattr(mat, 'baseColorFactor', None) if getattr(mat, 'baseColorFactor', None) is not None else [255, 255, 255, 255], dtype=float)
            if fac.max() <= 1.0: fac = fac * 255
            if img is not None:
                im = np.asarray(img.convert('RGB'), dtype=float)
                h, w = im.shape[:2]
                uv = vis.uv[g.faces]  # (n,3,2)
                pts = np.concatenate([uv.mean(axis=1, keepdims=True), uv], axis=1)  # centroid + corners
                u = np.mod(pts[..., 0], 1.0); v = np.mod(pts[..., 1], 1.0)
                px = np.clip((u * (w - 1)).astype(int), 0, w - 1)
                py = np.clip(((1 - v) * (h - 1)).astype(int), 0, h - 1)
                samp = im[py, px]  # (n,4,3)
                cols = np.median(samp, axis=1) * fac[:3] / 255.0
            else:
                cols = np.tile(fac[:3], (len(g.faces), 1))
        else:
            fc = vis.face_colors if hasattr(vis, 'face_colors') else None
            cols = np.asarray(fc[:, :3], dtype=float) if fc is not None else np.full((len(g.faces), 3), 180.0)
    except Exception as e:
        print('colour fallback', gname, e)
        cols = np.full((len(g.faces), 3), 180.0)
    V.append(verts); F.append(g.faces + base); C.append(cols); N.append(np.full(len(g.faces), len(N)))
    base += len(verts)
V = np.vstack(V); F = np.vstack(F); C = np.vstack(C); N = np.concatenate(N)
print('source tris', len(F))
if waterline is not None:
    cut = V[:, 1].min() + waterline
    keep = V[F][:, :, 1].max(1) > cut
    F, C, N = F[keep], C[keep], N[keep]  # parts fully under water; crossing faces are sliced per part below
    print('waterline drop keeps', len(F))
# palette via simple k-means
rng = np.random.default_rng(7)
k = min(pal_n, len(np.unique(C.round(), axis=0)))
cent = C[rng.choice(len(C), k, replace=False)]
for _ in range(25):
    d = ((C[:, None, :] - cent[None]) ** 2).sum(-1)
    lab = d.argmin(1)
    for i in range(k):
        m = lab == i
        if m.any(): cent[i] = C[m].mean(0)
# Per-part decimation: global decimation tears open thin single-sided hull plates. Long structural parts
# (hull, deck: longer than half the model) are reduced gently, small detail parts take the rest of the cut.
length = (V.max(0) - V.min(0)).max()
parts = []
for nid in np.unique(N):
    m = N == nid
    sub = trimesh.Trimesh(V, F[m], process=False); sub.remove_unreferenced_vertices()
    sub.merge_vertices(merge_tex=True, merge_norm=True, digits_vertex=4)  # weld UV seams so the decimator sees one surface
    ext = (np.asarray(sub.vertices).max(0) - np.asarray(sub.vertices).min(0)).max()
    parts.append((nid, np.asarray(sub.vertices, np.float32), np.asarray(sub.faces, np.int32), ext > 0.5 * length, np.where(m)[0]))
big = sum(len(p[2]) for p in parts if p[3]); small = sum(len(p[2]) for p in parts if not p[3])
big_ratio = (float(os.environ.get('FW_BIG_RATIO', '0.7')) if len(F) > target else 1.0)  # hull/deck plates (welded) are reduced gently; FW_BIG_RATIO overrides
small_ratio = 1.0 if len(F) <= target else max(0.05, (target - big * (0.8 if waterline is not None else 1.0)) / max(1, small))
print('parts', len(parts), 'big', big, 'x', round(big_ratio, 2), 'small', small, 'x', round(small_ratio, 2))
outV, outF, outL = [], [], []
base = 0
src_c_all = V[F].mean(1)
for nid, sv, sf, is_big, idx in parts:
    r = big_ratio if is_big else small_ratio
    if r < 0.98 and len(sf) > 24:
        try:
            sv2, sf2 = fast_simplification.simplify(sv, sf, target_reduction=1 - r, agg=4 if is_big else 7)
            if len(sf2) > 0: sv, sf = sv2, sf2
        except Exception as e:
            print('simplify fail', nid, e)
    if waterline is not None:
        res = trimesh.intersections.slice_faces_plane(np.asarray(sv, float), np.asarray(sf), np.array([0.0, 1.0, 0.0]), np.array([0.0, cut, 0.0]))
        if res is None or len(res[1]) == 0:
            continue
        sv, sf = np.asarray(res[0], np.float32), np.asarray(res[1], np.int32)
    # palette label of the nearest source face centroid within this part
    src_c = src_c_all[idx]; new_c = sv[sf].mean(1); lab_part = lab[idx]
    L = np.empty(len(sf), int)
    for a in range(0, len(sf), 256):
        d = ((new_c[a:a+256, None, :] - src_c[None]) ** 2).sum(-1)
        L[a:a+256] = lab_part[d.argmin(1)]
    outV.append(sv); outF.append(sf + base); outL.append(L); base += len(sv)
V2 = np.vstack(outV).astype(float); F2 = np.vstack(outF); L2 = np.concatenate(outL)
# recentre: pivot = x/z centre, y = lowest point (waterline set later in Unity via slot pivot)
mn, mx = V2.min(0), V2.max(0)
V2 = V2 - np.array([(mn[0] + mx[0]) / 2, (cut if waterline is not None else mn[1]), (mn[2] + mx[2]) / 2])
# palette texture: cells of 8px in a 64x64 grid
os.makedirs(out_dir, exist_ok=True)
cells = int(np.ceil(np.sqrt(k))); size = 8 * cells
tex = np.zeros((size, size, 3), np.uint8)
uvs = {}
for i in range(k):
    cx, cy = i % cells, i // cells
    tex[cy * 8:(cy + 1) * 8, cx * 8:(cx + 1) * 8] = np.clip(cent[i], 0, 255)
    uvs[i] = ((cx + 0.5) / cells, 1 - (cy + 0.5) / cells)
Image.fromarray(tex).save(os.path.join(out_dir, f'{name}_palette.png'))
with open(os.path.join(out_dir, f'{name}.mtl'), 'w') as f:
    f.write(f'newmtl {name}\nKd 1 1 1\nmap_Kd {name}_palette.png\n')
with open(os.path.join(out_dir, f'{name}.obj'), 'w') as f:
    f.write(f'mtllib {name}.mtl\no {name}\n')
    for v in V2: f.write(f'v {v[0]:.4f} {v[1]:.4f} {v[2]:.4f}\n')
    for i in range(k): f.write(f'vt {uvs[i][0]:.5f} {uvs[i][1]:.5f}\n')
    f.write(f'usemtl {name}\ns off\n')
    for tri, l in zip(F2, L2):
        a, b, c = tri + 1; t = l + 1
        f.write(f'f {a}/{t} {b}/{t} {c}/{t}\n')
print('out tris', len(F2), 'verts', len(V2), 'palette', k, 'extents', (V2.max(0) - V2.min(0)).round(2).tolist())
