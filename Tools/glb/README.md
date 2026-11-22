# GLB → Unity converter (no Blender needed)

`pip install trimesh numpy pygltflib fast-simplification pillow scipy matplotlib`

- `convert_glb.py in.glb out_dir name target_tris [palette_size] [waterline_m]` — merges all nodes, samples each face's
  texture colour into a small flat palette (removes printed text/markings), welds UV seams, decimates per part
  (long hull/deck parts gently, small details hard), optionally slices off everything below a waterline, writes
  OBJ + MTL + palette PNG with the pivot at the bottom (or waterline) centre.
- `preview_obj.py in.obj out.png [yaw] [pitch]` — quick flat-shaded preview without Unity.
- Used for `Assets/ThirdParty/SharedScoutDrone` (2,800 tris) and `Assets/ThirdParty/CfIronwallBattleship`
  (`... 14000 40 2.0` → 16,778 tris). Unity side: `ThirdPartyKit.Apply`.
