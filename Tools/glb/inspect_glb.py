import sys, json, trimesh
from pygltflib import GLTF2
for p in sys.argv[1:]:
    g = GLTF2().load(p)
    ex = g.asset.extras if g.asset else None
    print("==", p, "meshes", len(g.meshes), "mats", len(g.materials), "imgs", len(g.images))
    print("  extras:", json.dumps(ex)[:400] if ex else None)
    for m in g.materials[:30]:
        pbr = m.pbrMetallicRoughness
        print("  mat", m.name, pbr.baseColorFactor if pbr else None, (pbr.baseColorTexture.index if pbr and pbr.baseColorTexture else None))
    for im in g.images[:30]:
        print("  img", im.name, im.uri, im.mimeType)
    s = trimesh.load(p, force='scene')
    print("  bounds", s.bounds.tolist(), "extents", s.extents.tolist())
    print("  tris", sum(len(gm.faces) for gm in s.dump()))
