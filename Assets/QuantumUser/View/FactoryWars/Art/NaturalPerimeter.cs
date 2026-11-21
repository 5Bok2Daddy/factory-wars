namespace FactoryWars.View {
  using System.Collections.Generic;
  using System.Linq;
  using Quantum;
  using UnityEngine;

  public static class NaturalPerimeter {
    static readonly float[] Offsets = { 0.15f, 3.5f, 8.5f, 15f, 23f, 34f, 49f, 62f };

    public static void Build(MeshKit mesh, MapSpec map, TerrainKit.Biome biome) {
      var edges = MapPolishArt.Edges(MapPolishArt.PlayableRect(map)).ToArray();
      var stations = new List<(Vector2 Edge, Vector2 Out)>();
      for (var edge = 0; edge < edges.Length; edge++) {
        var (from, to, outward) = edges[edge];
        var next = edges[(edge + 1) % edges.Length].Outward;
        var steps = Mathf.CeilToInt(Vector2.Distance(from, to) / 5f);
        for (var i = 0; i < steps; i++) {
          stations.Add((Vector2.Lerp(from, to, i / (float)steps), outward));
        }
        for (var i = 0; i < 6; i++) {
          stations.Add((to, Vector2.Lerp(outward, next, i / 6f).normalized));
        }
      }

      var desert = biome == TerrainKit.Biome.Desert;
      var coast = biome == TerrainKit.Biome.Archipelago || biome == TerrainKit.Biome.Strait;
      var vertices = new Vector3[stations.Count, Offsets.Length];
      for (var i = 0; i < stations.Count; ++i) {
        var (edge, outward) = stations[i];
        var sample = edge + outward * 16f;
        var wave = Mathf.PerlinNoise(sample.x * 0.039f + 17f, sample.y * 0.039f + 39f);
        var rough = Mathf.PerlinNoise(sample.x * 0.093f + 8f, sample.y * 0.093f + 67f);
        var summit = 15f + wave * 15f + rough * 5f + (desert ? 6f : 0f);
        var low = coast ? TerrainKit.WaterLevel - 0.65f : -0.7f;
        for (var layer = 0; layer < Offsets.Length; ++layer) {
          var ratio = layer switch {
            0 => 0f, 1 => 0.15f, 2 => 0.46f, 3 => 0.81f, 4 => 1f, 5 => 0.92f, 6 => 0.66f, _ => 0.28f,
          };
          var position = edge + outward * (Offsets[layer] + (layer < 2 ? 0f : (wave - 0.5f) * 5.5f));
          vertices[i, layer] = new Vector3(position.x, Mathf.Lerp(low, summit, ratio), position.y);
        }
      }
      for (var i = 0; i < stations.Count; ++i) {
        var next = (i + 1) % stations.Count;
        for (var layer = 1; layer < Offsets.Length; ++layer) {
          mesh.Paint(Surface(biome, layer, MeshKit.Hash(i, layer)));
          mesh.Quad(vertices[i, layer - 1], vertices[next, layer - 1], vertices[next, layer], vertices[i, layer], Vector3.up);
        }
      }

      var rock = desert ? NewBundleArt.Prefab("CanyonRock") : coast ? MapPolishArt.Prefab("CoastalCliff") : MapPolishArt.Prefab("CoastalRock");
      var pine = coast || desert ? null : NewBundleArt.Prefab("WoodlandTallTree");
      var woodland = coast || desert ? null : NewBundleArt.Prefab("WoodlandTree");
      var dressingSeed = EnvironmentArt.Uses(map) ? VisualProfile.Current.EnvironmentSeed : 93;
      for (var i = 3; i < stations.Count; i += 2) {
        var variation = MeshKit.Hash(i, dressingSeed);
        if (EnvironmentArt.Uses(map) ? variation > 0.46f : i % 4 == 3) {
          var foot = vertices[i, 2] - Vector3.up * 2f;
          Dressing.AddBundle(rock, foot, 13f + variation * 8f, 211 + i, Fits);
        }
        if (pine != null) {
          Dressing.AddBundle(pine, vertices[i, 4] - Vector3.up * 0.5f, 5f + variation * 2f, 313 + i, Fits);
          Dressing.AddBundle(woodland, vertices[i, 3] - Vector3.up * 0.45f, 4f + variation * 2f, 451 + i, Fits);
        }
      }

      bool Fits(GroundFootprint footprint) {
        return (map.Roads == null || !map.Roads.Any(road => footprint.Overlaps(road, 1.3f)))
          && (map.Bridges == null || !map.Bridges.Any(bridge => footprint.Overlaps(bridge, 2f)));
      }
    }

    static Swatch Surface(TerrainKit.Biome biome, int layer, float variation) {
      return biome switch {
        TerrainKit.Biome.Desert => layer == 1 ? Swatch.Sand : layer is 3 or 5 ? Swatch.CanyonDark
          : variation < 0.35f ? Swatch.CanyonLight : Swatch.CanyonMid,
        TerrainKit.Biome.Archipelago or TerrainKit.Biome.Strait => layer == 1 ? Swatch.Stone : layer == 3 ? Swatch.RockDark
          : variation < 0.3f ? Swatch.RockFaceLight : Swatch.RockFace,
        _ => layer == 1 ? Swatch.Dirt : layer >= 4 && variation < 0.5f ? Swatch.ForestFloor
          : variation < 0.32f ? Swatch.RockFaceDark : Swatch.RockFace,
      };
    }

    public static void Tint(Mesh mesh, TerrainKit.Biome biome) {
      var points = mesh.vertices;
      var colors = new Color[points.Length];
      var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
      var (dark, rock, cap) = biome switch {
        TerrainKit.Biome.Desert => (new Color(0.34f, 0.19f, 0.13f), new Color(0.60f, 0.38f, 0.25f), new Color(0.73f, 0.53f, 0.32f)),
        TerrainKit.Biome.Archipelago or TerrainKit.Biome.Strait => (new Color(0.22f, 0.26f, 0.30f), new Color(0.43f, 0.46f, 0.48f), new Color(0.53f, 0.55f, 0.49f)),
        _ => (new Color(0.21f, 0.23f, 0.19f), new Color(0.42f, 0.43f, 0.36f), new Color(0.37f, 0.43f, 0.30f)),
      };
      for (var i = 0; i < colors.Length; i++) {
        var p = points[i];
        var variation = Mathf.PerlinNoise(p.x * 0.061f + p.y * 0.013f + 43f, p.z * 0.06f + p.y * 0.023f + 65f);
        var tint = Color.Lerp(dark, rock, Mathf.Clamp01(variation * 0.78f + Mathf.InverseLerp(0f, 20f, p.y) * 0.53f));
        tint = Color.Lerp(tint, cap, Mathf.SmoothStep(0f, 0.62f, Mathf.InverseLerp(17f, 30f, p.y)));
        tint.a = 0f;
        colors[i] = linear ? tint.linear : tint;
      }
      mesh.colors = colors;
    }
  }
}
