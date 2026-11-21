namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;

  public static class NewBundleArt {
    static readonly Dictionary<string, GameObject> Assets = new Dictionary<string, GameObject>();
    static readonly Dictionary<GameObject, Bounds> BoundsByAsset = new Dictionary<GameObject, Bounds>();

    public static GameObject Prefab(string key) {
      if (Assets.TryGetValue(key, out var model) == false) {
        Assets[key] = model = Resources.Load<GameObject>("FactoryWarsBundle/" + key);
      }
      return model;
    }

    public static Bounds BoundsOf(GameObject prefab) {
      if (BoundsByAsset.TryGetValue(prefab, out var bounds) == false) {
        BoundsByAsset[prefab] = bounds = VisualCatalog.LocalBounds(prefab);
      }
      return bounds;
    }

    public static bool HasBridge => Prefab("BridgeBase") != null && Prefab("BridgePier") != null && Prefab("BridgeRail") != null;

    public static bool UsesBridges(TerrainKit.Biome biome) => HasBridge &&
      (biome == TerrainKit.Biome.Standard || biome == TerrainKit.Biome.Strait || biome == TerrainKit.Biome.Archipelago);

    public static void AddBridge(Transform parent, float span, float width, bool longStrait) {
      var sections = Mathf.Clamp(Mathf.CeilToInt(span / 13f), 1, 6);
      var length = span / sections;
      for (var i = 0; i < sections; ++i) {
        var z = -span * 0.5f + length * (i + 0.5f);
        Fit("BridgeBase", parent, new Vector3(0f, 0.1f, z), new Vector3(width, 0.85f, length), top: true);
        for (var side = -1; side <= 1; side += 2) {
          Fit("BridgeRail", parent, new Vector3(side * (width * 0.5f - 0.21f), 0.19f, z),
            new Vector3(0.40f, longStrait ? 0.72f : 0.64f, length - 0.12f), top: false);
        }
      }
      for (var i = 1; i < Mathf.Max(sections, 2); ++i) {
        var z = -span * 0.5f + span * i / Mathf.Max(sections, 2);
        Fit("BridgePier", parent, new Vector3(0f, -0.63f, z),
          new Vector3(width * 0.77f, longStrait ? 5.2f : 2.8f, 1.55f), top: true);
      }
    }

    static void Fit(string key, Transform parent, Vector3 position, Vector3 size, bool top) {
      var prefab = Prefab(key);
      var bounds = BoundsOf(prefab);
      var rotation = bounds.size.x > bounds.size.z ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
      var localSize = rotation == Quaternion.identity ? size : new Vector3(size.z, size.y, size.x);
      var scale = new Vector3(localSize.x / Mathf.Max(0.01f, bounds.size.x),
        localSize.y / Mathf.Max(0.01f, bounds.size.y), localSize.z / Mathf.Max(0.01f, bounds.size.z));
      var model = Object.Instantiate(prefab, parent, false);
      model.name = "Bundle" + key;
      model.transform.localRotation = rotation;
      model.transform.localScale = scale;
      var pivot = new Vector3(bounds.center.x, top ? bounds.max.y : bounds.min.y, bounds.center.z);
      model.transform.localPosition = position - rotation * Vector3.Scale(pivot, scale);
    }

    public static bool HasWoodland(MapSpec map) =>
      (map.name == "map_Map4P" || map.name == "map_test") && Prefab("WoodlandTree") != null;

    public static bool CanyonFeature(MapSpec map, MapShape shape, float height, int seed) {
      var prefab = Prefab("CanyonRock");
      if (prefab == null || map == null) return false;
      var length = (shape.IsCircle ? shape.Radius : shape.HalfLength).AsFloat * 2f;
      var width = (shape.IsCircle ? shape.Radius : shape.HalfWidth).AsFloat * 2f;
      if (Mathf.Min(length, width) < 2f) return false;
      var bounds = BoundsOf(prefab);
      var yaw = shape.IsCircle ? MeshKit.Hash(seed, 151) * 360f : shape.Yaw.AsFloat + (length > width ? 90f : 0f);
      var rotation = Quaternion.Euler(0f, yaw, 0f);
      var scale = new Vector3(Mathf.Min(length, width) / bounds.size.x, height / bounds.size.y, Mathf.Max(length, width) / bounds.size.z);
      var center = shape.Center.ToUnityVector3();
      bool Fits(GroundFootprint footprint) {
        foreach (var road in map.Roads ?? System.Array.Empty<MapShape>()) if (footprint.Overlaps(road, 0.8f)) return false;
        foreach (var bridge in map.Bridges ?? System.Array.Empty<MapShape>()) if (footprint.Overlaps(bridge, 0.8f)) return false;
        foreach (var city in map.CityBuildings ?? System.Array.Empty<CityBuildingPlacement>()) if (footprint.Overlaps(city.Shape, 0.5f)) return false;
        foreach (var node in map.ResourceNodes ?? System.Array.Empty<ResourceNodePlacement>()) if (footprint.Overlaps(node.Shape, 0.4f)) return false;
        return true;
      }
      return Dressing.AddPrefab(prefab, center, rotation, scale * 0.92f, Fits)
        || Dressing.AddPrefab(prefab, center, rotation, scale * 0.68f, Fits)
        || Dressing.AddPrefab(prefab, center, rotation, scale * 0.47f, Fits);
    }

    public static void AddWoodland(MapSpec map, MapShape shape, int seed, float remaining, float clearHalfExtent, bool scarred) {
      var layout = Matrix4x4.TRS(shape.Center.ToUnityVector3(), Quaternion.Euler(0f, shape.Yaw.AsFloat, 0f), Vector3.one);
      var halfX = (shape.IsCircle ? shape.Radius : shape.HalfLength).AsFloat;
      var halfZ = (shape.IsCircle ? shape.Radius : shape.HalfWidth).AsFloat;
      var occupied = new List<GroundFootprint>();
      const int spots = 12;
      for (var i = 0; i < spots; ++i) {
        var angle = i * (Mathf.PI * 2f / spots);
        var offset = new Vector3(Mathf.Cos(angle) * halfX, 0f, Mathf.Sin(angle) * halfZ) * 0.70f;
        if (Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.z)) < clearHalfExtent + 1f) continue;
        var spot = layout.MultiplyPoint3x4(offset);
        spot.y = TerrainKit.SurfaceHeight(map, spot.x, spot.z);
        var noise = MeshKit.Hash(seed, 20 + i);
        var cut = scarred || noise >= remaining;
        var key = cut ? "WoodlandStump" : i % 3 == 0 ? "WoodlandTallTree" : "WoodlandTree";
        if (i % 4 == 3 && cut == false) key = i == 3 ? "MeadowRock" : "MeadowShrub";
        var tree = key == "WoodlandTree" || key == "WoodlandTallTree";
        var size = Mathf.Min(tree ? 3.9f : 1.5f, Mathf.Min(halfX, halfZ) * 0.42f);
        bool Fits(GroundFootprint footprint) {
          if (UrbanDecor.Fits(map, footprint) == false || occupied.Exists(other => footprint.Overlaps(other, 0.12f))) return false;
          occupied.Add(footprint);
          return true;
        }
        Dressing.AddBundle(Prefab(key), spot, size, seed + i * 17, Fits);
      }
    }
  }
}
