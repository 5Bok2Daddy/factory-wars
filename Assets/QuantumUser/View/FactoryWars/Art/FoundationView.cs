namespace FactoryWars.View {
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>A terrain-fitted foundation owned by the building view.</summary>
  public sealed class FoundationView : MonoBehaviour {
    Mesh _mesh;

    public static void Attach(Transform parent, MapSpec map, BuildingSpec spec, Vector3 position, float yaw) {
      var (margin, round, trim) = Preset(spec.Role);
      var footprint = spec.FootprintRadius.AsFloat;
      var half = footprint + margin * Mathf.Max(0f, VisualProfile.Current.FoundationMarginScale);
      var rotation = Quaternion.Euler(0f, yaw, 0f);
      var detailed = EnvironmentArt.Uses(map);
      var apron = detailed ? 0.28f : 0f;
      var area = new GroundFootprint(position, new Vector3(half + apron, 0f, half + apron), rotation);
      foreach (var road in map.Roads ?? System.Array.Empty<MapShape>()) {
        if (!area.Overlaps(road, 0f)) continue;
        half = footprint;
        apron = 0f;
        break;
      }

      var root = new GameObject("ConcreteFoundation", typeof(MeshFilter), typeof(MeshRenderer), typeof(FoundationView));
      root.transform.SetParent(parent, false);
      root.transform.localRotation = rotation;
      var kit = new MeshKit();
      var outline = Outline(half, round);
      var outer = apron > 0f ? Outline(half + apron, round) : outline;
      var layer = 0.045f + MeshKit.Hash(KenneyAssetMap.Seed(position, 7001), 23) * 0.025f;

      Vector3 Vertex(Vector2 point, float sink = 0f) {
        var offset = rotation * new Vector3(point.x, 0f, point.y);
        var elevation = TerrainKit.SurfaceHeight(map, position.x + offset.x, position.z + offset.z);
        return new Vector3(point.x, elevation - position.y + layer - sink, point.y);
      }

      var center = Vertex(Vector2.zero);
      for (var i = 0; i < outline.Length; ++i) {
        var a = outline[i];
        var b = outline[(i + 1) % outline.Length];
        var innerA = a * 0.93f;
        var innerB = b * 0.93f;
        kit.Paint(!detailed && i % 3 == 1 ? Swatch.Stone : Swatch.Concrete);
        kit.Tri(center, Vertex(innerA), Vertex(innerB), Vector3.up);
        kit.Paint(detailed && trim == Swatch.Hazard && i % 3 != 1 ? Swatch.ConcreteDark : trim);
        kit.Quad(Vertex(innerA), Vertex(a), Vertex(b), Vertex(innerB), Vector3.up);
        kit.Paint(Swatch.ConcreteDark);
        kit.Quad(Vertex(a), Vertex(a, 0.14f), Vertex(b, 0.14f), Vertex(b), new Vector3(a.x + b.x, 0f, a.y + b.y));
        if (apron > 0f) {
          kit.Paint(Swatch.Dirt);
          kit.Quad(Vertex(a, layer - 0.03f), Vertex(b, layer - 0.03f),
            Vertex(outer[(i + 1) % outer.Length], layer - 0.012f), Vertex(outer[i], layer - 0.012f), Vector3.up);
        }
      }
      if (detailed && spec.Role is BuildingRole.SupplyDepot or BuildingRole.VehicleFactory or BuildingRole.MlrsBattery) {
        var edge = half * 0.85f;
        kit.Paint(spec.Role == BuildingRole.MlrsBattery ? Swatch.Hazard : Swatch.LaneWhite);
        for (var i = -2; i <= 2; i++) {
          var a = i * 0.42f;
          kit.Quad(Vertex(new Vector2(a, edge - 0.25f), -0.009f), Vertex(new Vector2(a + 0.26f, edge - 0.25f), -0.009f),
            Vertex(new Vector2(a + 0.26f, edge), -0.009f), Vertex(new Vector2(a, edge), -0.009f), Vector3.up);
        }
      }
      var mesh = kit.ToMesh("terrain_fitted_foundation");
      root.GetComponent<FoundationView>()._mesh = mesh;
      root.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = root.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = detailed ? WorldView.SharedGroundMaterial : ArtLook.NeutralMaterial;
      renderer.shadowCastingMode = ShadowCastingMode.Off;
      if (detailed) {
        var uvs = mesh.uv;
        var points = mesh.vertices;
        var colors = new Color[points.Length];
        var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        for (var i = 0; i < colors.Length; i++) {
          var colorKey = (Swatch)Mathf.Clamp(Mathf.FloorToInt(uvs[i].x * ArtPalette.Size), 0, ArtPalette.CellCount - 1);
          var at = position + rotation * points[i];
          var color = ArtPalette.ColorOf(colorKey);
          if (colorKey == Swatch.Dirt) {
            var ground = TerrainKit.GroundTint(map, at.x, at.z, 3);
            var edge = round ? new Vector2(points[i].x, points[i].z).magnitude : Mathf.Max(Mathf.Abs(points[i].x), Mathf.Abs(points[i].z));
            var between = Mathf.InverseLerp(half - 0.22f, half + apron - 0.18f, edge);
            color = Color.Lerp(Color.Lerp(ground, color, 0.29f), ground, between);
          } else if (colorKey == Swatch.Concrete) {
            color *= 0.98f + 0.06f * Mathf.PerlinNoise(at.x * 0.24f, at.z * 0.26f);
          }
          color.a = 0f;
          colors[i] = linear ? color.linear : color;
        }
        mesh.colors = colors;
      }
    }

    static Vector2[] Outline(float half, bool round) {
      if (round) {
        var ring = new Vector2[16];
        for (var i = 0; i < ring.Length; ++i) {
          var angle = i * 2f * Mathf.PI / ring.Length;
          ring[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * half;
        }
        return ring;
      }
      var bevel = Mathf.Min(0.42f, half * 0.2f);
      var inset = half - bevel;
      return new[] {
        new Vector2(-half, -inset), new Vector2(-inset, -half), new Vector2(0f, -half),
        new Vector2(inset, -half), new Vector2(half, -inset), new Vector2(half, 0f),
        new Vector2(half, inset), new Vector2(inset, half), new Vector2(0f, half),
        new Vector2(-inset, half), new Vector2(-half, inset), new Vector2(-half, 0f),
      };
    }

    static (float Margin, bool Round, Swatch Trim) Preset(BuildingRole role) => role switch {
      BuildingRole.Headquarters => (0.7f, false, Swatch.ConcreteDark),
      BuildingRole.VehicleFactory or BuildingRole.HeavyWorks or BuildingRole.Processor => (0.63f, false, Swatch.Hazard),
      BuildingRole.SupplyDepot or BuildingRole.CollectionYard => (0.55f, false, Swatch.ConcreteDark),
      BuildingRole.LumberMill or BuildingRole.IronExtractor => (0.48f, false, Swatch.Dirt),
      BuildingRole.GroundTower or BuildingRole.AirDefense or BuildingRole.PowerPlant => (0.42f, true, Swatch.ConcreteDark),
      _ => (0.35f, false, Swatch.ConcreteDark),
    };

    public static LineRenderer CreatePreview(Transform parent) {
      var line = new GameObject("FoundationFootprintPreview").AddComponent<LineRenderer>();
      line.transform.SetParent(parent, false);
      line.loop = true;
      line.positionCount = 8;
      line.startWidth = line.endWidth = 0.16f;
      line.alignment = LineAlignment.TransformZ;
      line.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
      line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
      line.gameObject.AddComponent<FoundationView>();
      return line;
    }

    public static void UpdatePreview(LineRenderer line, MapSpec map, Vector3 center, float half, bool valid) {
      var color = valid ? new Color(0.18f, 0.96f, 0.42f) : new Color(0.98f, 0.22f, 0.18f);
      line.startColor = line.endColor = color;
      for (var i = 0; i < 8; i++) {
        var x = i == 0 || i == 6 || i == 7 ? -half : i == 2 || i == 3 || i == 4 ? half : 0f;
        var z = i < 3 ? -half : i == 3 || i == 7 ? 0f : half;
        var point = center + new Vector3(x, 0f, z);
        point.y = TerrainKit.SurfaceHeight(map, point.x, point.z) + 0.14f;
        line.SetPosition(i, point);
      }
      line.enabled = true;
    }

    void OnDestroy() {
      if (_mesh != null) Destroy(_mesh);
      if (TryGetComponent<LineRenderer>(out var line)) Destroy(line.sharedMaterial);
    }
  }
}
