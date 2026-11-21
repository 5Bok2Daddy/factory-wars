namespace FactoryWars.View {
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;

  public static class EnvironmentArt {
    public static bool Uses(MapSpec map) => map != null && map.SealedPerimeter && VisualProfile.Current.EnvironmentDetails;

    public static Color GroundColor(MapSpec map, float x, float z, Color previous) {
      var seed = VisualProfile.Current.EnvironmentSeed * 0.037f;
      var broad = Mathf.PerlinNoise(x * 0.018f + seed, z * 0.019f - seed);
      var fine = Mathf.PerlinNoise(x * 0.094f - seed, z * 0.082f + seed);
      var grain = broad * 0.70f + fine * 0.30f;
      var biome = TerrainKit.BiomeOf(map);
      var gravel = biome == TerrainKit.Biome.Urban ? new Color(0.43f, 0.43f, 0.41f)
        : biome == TerrainKit.Biome.Desert ? new Color(0.76f, 0.64f, 0.45f) : new Color(0.52f, 0.48f, 0.38f);
      var earth = biome == TerrainKit.Biome.Desert ? new Color(0.75f, 0.58f, 0.36f)
        : biome == TerrainKit.Biome.Urban ? new Color(0.40f, 0.39f, 0.36f) : new Color(0.43f, 0.36f, 0.27f);
      var p = Point(x, z);
      var field = biome == TerrainKit.Biome.Standard
        ? Color.Lerp(new Color(0.27f, 0.34f, 0.23f), new Color(0.45f, 0.52f, 0.31f), Mathf.Clamp01(grain * 1.4f - 0.17f))
        : previous;
      var ground = Color.Lerp(field, earth, Mathf.SmoothStep(0f, 0.54f, Mathf.InverseLerp(0.53f, 0.80f, grain)));
      foreach (var area in map.BaseAreas ?? System.Array.Empty<MapShape>()) {
        var delta = p - area.Center;
        if (FPMath.Abs(delta.X) > (FP)43 || FPMath.Abs(delta.Z) > (FP)43) continue;
        var fade = Mathf.SmoothStep(0f, 0.84f, Mathf.InverseLerp(4f, -8f, area.Distance(p).AsFloat));
        ground = Color.Lerp(ground, Color.Lerp(gravel * 0.88f, gravel * 1.08f, fine), fade);
      }
      foreach (var node in map.ResourceNodes ?? System.Array.Empty<ResourceNodePlacement>()) {
        var delta = p - node.Shape.Center;
        var reach = node.Shape.Reach + (FP)7;
        if (FPMath.Abs(delta.X) > reach || FPMath.Abs(delta.Z) > reach) continue;
        var fade = Mathf.SmoothStep(0f, 0.35f, Mathf.InverseLerp(7f, 0f, node.Shape.Distance(p).AsFloat));
        ground = Color.Lerp(ground, node.Kind == ResourceKind.Iron ? gravel * 0.84f : earth * 0.83f, fade);
      }
      ground.a = 1f;
      return ground;
    }

    public static float Elevation(MapSpec map, float x, float z) {
      if (!Uses(map)) return 0f;
      var p = Point(x, z);
      if (map.GroundAt(p) != 0) return 0f;
      var fade = 1f;
      foreach (var area in map.BaseAreas ?? System.Array.Empty<MapShape>()) {
        var offset = p - area.Center;
        var limit = area.HalfLength + (FP)8;
        if (FPMath.Abs(offset.X) > limit || FPMath.Abs(offset.Z) > limit) continue;
        var distance = Mathf.Max(FPMath.Abs(offset.X).AsFloat - area.HalfLength.AsFloat, FPMath.Abs(offset.Z).AsFloat - area.HalfWidth.AsFloat);
        fade = Mathf.Min(fade, Mathf.SmoothStep(0f, 1f, distance / 8f));
      }
      var seed = VisualProfile.Current.EnvironmentSeed * 0.021f;
      return (Mathf.PerlinNoise(x * 0.073f + seed, z * 0.070f - seed) - 0.5f) * 0.30f * fade;
    }

    public static float ScatterDensity(Vector3 point) {
      var seed = VisualProfile.Current.EnvironmentSeed * 0.065f;
      var cluster = Mathf.PerlinNoise(point.x * 0.031f + seed, point.z * 0.027f - seed);
      return Mathf.SmoothStep(0.12f, 0.91f, Mathf.InverseLerp(0.27f, 0.71f, cluster));
    }

    public static Color RoadColor(TerrainKit.Biome biome, Swatch surface, Vector3 position, Color previous) {
      var grain = Mathf.PerlinNoise(position.x * 0.29f + 11f, position.z * 0.23f + 19f);
      var color = surface switch {
        Swatch.Dirt => biome == TerrainKit.Biome.Desert ? new Color(0.60f, 0.46f, 0.30f) : new Color(0.45f, 0.36f, 0.27f),
        Swatch.DirtDark => biome == TerrainKit.Biome.Desert ? new Color(0.55f, 0.42f, 0.27f) : new Color(0.40f, 0.32f, 0.24f),
        Swatch.RockDark => biome == TerrainKit.Biome.Desert ? new Color(0.64f, 0.51f, 0.36f) : new Color(0.40f, 0.39f, 0.35f),
        Swatch.Asphalt => Color.Lerp(previous, new Color(0.30f, 0.31f, 0.32f), 0.18f),
        _ => previous,
      };
      if (biome == TerrainKit.Biome.Standard && (surface == Swatch.Dirt || surface == Swatch.Asphalt)) {
        var flank = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(129f, 145f, Mathf.Max(Mathf.Abs(position.x), Mathf.Abs(position.z))));
        color = Color.Lerp(new Color(0.26f, 0.27f, 0.27f), new Color(0.45f, 0.36f, 0.27f), flank);
      }
      return surface == Swatch.LaneWhite || surface == Swatch.LaneYellow ? color : color * Mathf.Lerp(0.94f, 1.065f, grain);
    }

    public static void Light(Light sun, TerrainKit.Biome biome) {
      if (sun != null) {
        sun.intensity *= biome == TerrainKit.Biome.Desert ? 0.94f : 1.06f;
        sun.shadowStrength *= 0.90f;
        sun.color = Color.Lerp(sun.color, new Color(1f, 0.96f, 0.90f), 0.15f);
      }
      RenderSettings.ambientEquatorColor = Color.Lerp(RenderSettings.ambientEquatorColor, new Color(0.59f, 0.59f, 0.55f), 0.16f);
    }

    static FPVector3 Point(float x, float z) => new FPVector3(FP.FromFloat_UNSAFE(x), FP._0, FP.FromFloat_UNSAFE(z));
  }
}
