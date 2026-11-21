namespace FactoryWars.View {
  using System.Collections.Generic;
  using System.Linq;
  using Quantum;
  using UnityEngine;

  public static class MapPolishArt {
    static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

    public static GameObject Prefab(string name) {
      if (!Prefabs.TryGetValue(name, out var prefab)) {
        Prefabs[name] = prefab = Resources.Load<GameObject>("FactoryWarsPolish/" + name);
      }
      return prefab;
    }

    public static GameObject Place(string name, Transform parent, Vector3 point, Quaternion rotation, Vector3 size) {
      var prefab = Prefab(name);
      if (prefab == null) return null;
      var bounds = NewBundleArt.BoundsOf(prefab);
      var scale = ScaleTo(bounds, size);
      var model = Object.Instantiate(prefab, parent, false);
      model.name = name;
      model.transform.localRotation = rotation;
      model.transform.localScale = scale;
      var baseCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
      model.transform.localPosition = point - rotation * Vector3.Scale(baseCenter, scale);
      foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
      return model;
    }

    static Vector3 ScaleTo(Bounds bounds, Vector3 size) => new Vector3(
      size.x / Mathf.Max(0.01f, bounds.size.x), size.y / Mathf.Max(0.01f, bounds.size.y), size.z / Mathf.Max(0.01f, bounds.size.z));

    static void Dress(string key, Vector3 point, float yaw, float width, System.Predicate<GroundFootprint> fits, float span = 0f) {
      var prefab = Prefab(key);
      if (prefab == null) return;
      var bounds = NewBundleArt.BoundsOf(prefab);
      var scale = Vector3.one * (width / Mathf.Max(0.01f, bounds.size.x, bounds.size.z));
      if (span > 0f) {
        if (bounds.size.x >= bounds.size.z) scale.x = span / bounds.size.x;
        else {
          scale.z = span / bounds.size.z;
          yaw += 90f;
        }
      }
      var rotation = Quaternion.Euler(0f, yaw, 0f);
      if (key is "Fence" or "Guardrail" or "Dock") {
        Dressing.AddPrefab(prefab, point, rotation, scale, fits);
      } else {
        var profile = VisualProfile.Current;
        Dressing.AddPrefab(prefab, point, rotation, scale, fits, MeshKit.Hash(KenneyAssetMap.Seed(point, 417), 5),
          profile.UrbanDensityLow, profile.UrbanDensityMedium);
      }
    }

    public static Rect PlayableRect(MapSpec map) => Rect.MinMaxRect(map.BoundsMin.X.AsFloat, map.BoundsMin.Y.AsFloat,
      map.BoundsMax.X.AsFloat, map.BoundsMax.Y.AsFloat);

    public static IEnumerable<(Vector2 From, Vector2 To, Vector2 Outward)> Edges(Rect rect) {
      yield return (rect.min, new Vector2(rect.xMax, rect.yMin), Vector2.down);
      yield return (new Vector2(rect.xMax, rect.yMin), rect.max, Vector2.right);
      yield return (rect.max, new Vector2(rect.xMin, rect.yMax), Vector2.up);
      yield return (new Vector2(rect.xMin, rect.yMax), rect.min, Vector2.left);
    }

    static Vector3 GroundPoint(Vector2 point, float height = 0f) => new Vector3(point.x, height, point.y);

    static bool ClearOfCrossings(MapSpec map, GroundFootprint footprint, float gap) =>
      (map.Roads == null || !map.Roads.Any(shape => footprint.Overlaps(shape, gap)))
      && (map.Bridges == null || !map.Bridges.Any(shape => footprint.Overlaps(shape, gap)))
      && (map.WaterGates == null || !map.WaterGates.Any(shape => footprint.Overlaps(shape, gap)));

    public static void Perimeter(MapSpec map) {
      if (TerrainKit.Channel(map) <= 0f) return;
      var playable = PlayableRect(map);
      var river = TerrainKit.RiverPaths(map, false);
      bool Fits(GroundFootprint footprint) => ClearOfCrossings(map, footprint, 1.2f)
        && TerrainKit.RiverDistance(river, footprint.Center) > 7f;
      const float panel = 4f;
      foreach (var (from, to, outward) in Edges(playable)) {
        var along = (to - from).normalized;
        var yaw = -Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
        var length = Vector2.Distance(from, to);
        for (var n = 0; (n + 1) * panel < length - 1f; n++) {
          var point = from + along * ((n + 0.5f) * panel);
          Dress("Fence", GroundPoint(point - outward * 2.5f), yaw, 4f, Fits, panel + 0.03f);
          Dress("Guardrail", GroundPoint(point - outward * 0.9f), yaw, 3f, Fits, panel + 0.03f);
          if (n % 6 < 2) Dress("Jersey", GroundPoint(point - outward * 4.25f), yaw, 3f, Fits, panel - 0.04f);
          if (n % 12 == 3) {
            Dress("Light", GroundPoint(point - outward * 4.1f), yaw, 1.4f, Fits);
            Dress("Crates", GroundPoint(point - outward * 5.8f + along * 2f), yaw, 2.3f, Fits);
          }
          if (n == 1 || n % 18 == 11) {
            Dress("Tower", GroundPoint(point - outward * 6.8f), yaw, 3.8f, Fits);
            Dress("Sandbags", GroundPoint(point - outward * 9.2f), yaw, 3.0f, Fits);
          }
        }
      }
    }

    public static void CoastDecor(MapSpec map, TerrainKit.Biome biome) {
      var land = PlayableRect(map);
      var paths = TerrainKit.RiverPaths(map, false);
      bool Fits(GroundFootprint footprint) => ClearOfCrossings(map, footprint, 1.5f);
      if (biome == TerrainKit.Biome.Archipelago && !map.SealedPerimeter) {
        var spot = 0;
        foreach (var (from, to, outward) in Edges(land)) {
          var length = Vector2.Distance(from, to);
          var along = (to - from).normalized;
          var yaw = -Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
          for (var distance = 2f; distance < length; distance += 7f, spot++) {
            var point = from + along * distance;
            if (TerrainKit.RiverDistance(paths, point) < 6f) continue;
            if (spot % 11 == 4) {
              Dress("Dock", GroundPoint(point + outward * 4.9f, TerrainKit.WaterLevel + 0.25f), yaw + 90f, 12f, Fits, 13f);
            } else if (spot % 11 != 3 && spot % 11 != 5) {
              var size = 4.5f + MeshKit.Hash(spot, 19) * 5.5f;
              var rock = point + outward * (1.3f + MeshKit.Hash(spot, 17) * 3.8f);
              Dress(spot % 3 == 0 ? "CoastalCliff" : "CoastalRock", GroundPoint(rock, -1.1f), yaw + spot * 39f, size, Fits);
              if (spot % 3 == 1) Dressing.AddBundle(NewBundleArt.Prefab("MeadowShrub"), GroundPoint(point - outward * 1.8f), 1.3f, spot, Fits);
            }
          }
        }
      }
      foreach (var path in paths) {
        if (biome == TerrainKit.Biome.Archipelago && (path.Count < 2 || path[0].Half < 18f)) continue;
        for (var i = 2; i + 2 < path.Count; i += 3) {
          var p = path[i];
          for (var side = -1; side <= 1; side += 2) {
            var point = p.Position + p.Normal * (side * (p.Half - 0.25f));
            if (!land.Contains(point) || TerrainKit.RiverDistance(paths, point) < -3f) continue;
            var yaw = -Mathf.Atan2(p.Normal.y, p.Normal.x) * Mathf.Rad2Deg;
            if (biome == TerrainKit.Biome.Strait && i % 21 == 5) {
              Dress("Dock", GroundPoint(point - p.Normal * (side * 2.4f), TerrainKit.WaterLevel + 0.32f), yaw, 10f, Fits, 10.5f);
              continue;
            }
            var size = 3.1f + MeshKit.Hash(i, side) * 4.9f;
            var rock = point + p.Normal * (side * (MeshKit.Hash(i, side + 8) - 0.62f) * 4f);
            Dress(i % 2 == 0 ? "CoastalCliff" : "CoastalRock", GroundPoint(rock, -1.45f), yaw + i * 39f, size, Fits);
            if (biome == TerrainKit.Biome.Archipelago && i % 3 == 2 && i % 2 == 0) {
              Dressing.AddBundle(NewBundleArt.Prefab("MeadowShrub"), GroundPoint(point + p.Normal * (side * 2.1f)), 1.35f, 317 + i * side, Fits);
            }
          }
        }
      }
    }
  }
}
