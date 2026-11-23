namespace FactoryWars.View {
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// Task 39 fix 2 (user review 13:14): every canal crossing on the grid map is a steel tied-arch bridge. The deck stays at street level
  /// (no ramps needed, grade 0 %), the canal is a deep cut (GridMapView.CanalDepth) so ships pass under it. Per bridge, one mesh:
  /// concrete abutments on both banks (outside the navigable channel - no piers in the water), two steel arches with hangers over the
  /// span, a deck girder, concrete parapets with a steel handrail along the whole bridge, and lamp posts. Read-only view of MapSpec.
  /// </summary>
  public class ArchBridgeView {
    const float Rise = 7f;
    readonly Transform _root;
    bool _built;
    public int Count { get; private set; }

    public ArchBridgeView(Transform root) {
      _root = root;
    }

    public void Update(Frame frame) {
      if (_built) return;
      _built = true;
      var map = FactoryWarsUtil.MapInfo(frame);
      if (map == null || map.Bridges == null || GridMapView.Uses(map) == false) return;
      var parent = new GameObject("ArchBridges").transform;
      parent.SetParent(_root, false);
      var n = map.CellGridSize;
      var size = map.CellGridCell.AsFloat;
      for (var i = 0; i < map.Bridges.Length; i++) {
        var shape = map.Bridges[i];
        var c = shape.Center.ToUnityVector3();
        var alongX = shape.HalfLength >= shape.HalfWidth;
        var half = (alongX ? shape.HalfLength : shape.HalfWidth).AsFloat;
        var width = (alongX ? shape.HalfWidth : shape.HalfLength).AsFloat * 2f;
        // Span over open water: the deck cells (G next to W) inside this bridge, measured along the road.
        float lo = float.MaxValue, hi = float.MinValue;
        for (var r = 0; r < n; r++) {
          for (var col = 0; col < n; col++) {
            if (GridMapView.Deck(map, r, col) == false) continue;
            var p = new Vector3(GridMapView.CellX(map, col), 0f, GridMapView.CellZ(map, r));
            if (shape.Distance(p.ToFPVector3()).AsFloat > 0.1f) continue;
            var along = alongX ? p.x - c.x : p.z - c.z;
            lo = Mathf.Min(lo, along - size / 2f);
            hi = Mathf.Max(hi, along + size / 2f);
          }
        }
        if (lo > hi) { lo = -half; hi = half; }
        var water = TerrainKit.WaterLevel;
        var kit = new MeshKit();
        // Deck girder under the slab over the channel.
        kit.Paint(Swatch.ConcreteDark);
        kit.Box(new Vector3((lo + hi) / 2f, -0.75f, 0f), new Vector3(hi - lo + 2f, 1.3f, width - 0.4f));
        // Abutments on both banks (the deck rests on them; nothing stands in the navigable water).
        kit.Paint(Swatch.Concrete);
        foreach (var end in new[] { lo, hi }) {
          var depth = -water + 0.2f;
          kit.Box(new Vector3(end + (end == lo ? -1.2f : 1.2f), -depth / 2f, 0f), new Vector3(2.6f, depth, width + 1.4f));
          kit.Paint(Swatch.ConcreteDark);
          kit.Box(new Vector3(end + (end == lo ? -1.2f : 1.2f), water + 0.6f, 0f), new Vector3(2.7f, 1.2f, width + 1.5f));
          kit.Paint(Swatch.Concrete);
        }
        // Parapets along the whole bridge, steel handrail on top.
        foreach (var side in new[] { -1f, 1f }) {
          var z = side * (width / 2f - 0.25f);
          kit.Paint(Swatch.Concrete);
          kit.Box(new Vector3(0f, 0.5f, z), new Vector3(half * 2f, 0.9f, 0.4f));
          kit.Paint(Swatch.MetalDark);
          kit.Box(new Vector3(0f, 1.05f, z), new Vector3(half * 2f, 0.1f, 0.18f));
        }
        // Two steel arches over the span (feet on the abutments) with hangers every ~3 m.
        var span = hi - lo + 2f;
        var mid = (lo + hi) / 2f;
        const int Segments = 14;
        foreach (var side in new[] { -1f, 1f }) {
          var z = side * (width / 2f + 0.35f);
          kit.Paint(Swatch.MetalDark);
          for (var s = 0; s < Segments; s++) {
            Vector3 At(int k) {
              var t = k / (float)Segments;
              return new Vector3(mid - span / 2f + span * t, 0.3f + Rise * 4f * t * (1f - t), z);
            }
            kit.Beam(At(s), At(s + 1), 0.55f);
            if (s > 0) {
              var top = At(s);
              kit.Paint(Swatch.MetalLight);
              kit.Beam(new Vector3(top.x, 0.3f, z), top, 0.12f);
              kit.Paint(Swatch.MetalDark);
            }
          }
          // Wind bracing between the two arches at the crown.
          if (side > 0) {
            for (var k = -2; k <= 2; k++) {
              var t = 0.5f + k * 0.08f;
              var x = mid - span / 2f + span * t;
              var y = 0.3f + Rise * 4f * t * (1f - t);
              kit.Beam(new Vector3(x, y, -z), new Vector3(x, y, z), 0.25f);
            }
          }
        }
        // Lamp posts at both ends and the middle of each side.
        foreach (var x in new[] { -half + 1.5f, 0f, half - 1.5f }) {
          foreach (var side in new[] { -1f, 1f }) {
            var z = side * (width / 2f - 0.25f);
            kit.Paint(Swatch.MetalDark);
            kit.Box(new Vector3(x, 2.6f, z), new Vector3(0.18f, 4.2f, 0.18f));
            kit.Box(new Vector3(x, 4.6f, z - side * 0.45f), new Vector3(0.14f, 0.14f, 0.9f));
            kit.Glow(Swatch.GlowOrange);
            kit.Box(new Vector3(x, 4.45f, z - side * 0.85f), new Vector3(0.36f, 0.16f, 0.36f));
          }
        }
        var go = new GameObject($"ArchBridge{i}", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(c.x, 0f, c.z);
        go.transform.rotation = Quaternion.Euler(0f, shape.Yaw.AsFloat + (alongX ? 0f : 90f), 0f);
        go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh($"arch_bridge_{i}");
        go.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
        go.isStatic = true;
        Count++;
      }
    }
  }
}
