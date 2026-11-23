namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// Task 46: models for the outer waterway, assembled from owned Synty pack prefabs (unchanged: no paint, no scaling).
  /// - building/sea_barrier: a steel-caisson boom kitbashed from POLYGON Battle Royale port walls in a row across 16 m, dock poles at
  ///   both ends.
  /// - building/sea_watchtower: the pack guard tower.
  /// - naval/*: dock, dock poles, boat tie, concrete slab, port wall, moored boat, crane for harbours and dock basins (WaterwayView).
  /// </summary>
  public static class WaterwayKit {
    const string BR = "Assets/ExternalAssets/Synty/PolygonBattleRoyale/Prefabs/";
    const string PK = "Assets/ExternalAssets/Synty/PolygonConstruction/Prefabs/";
    const string Out = "Assets/FactoryWarsArt/Waterway";
    public const float BoomLength = 16f;

    static GameObject Load(string path) {
      var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
      if (prefab == null) Debug.LogWarning($"[FW-T46] missing {path}");
      return prefab;
    }

    static GameObject Place(Transform parent, GameObject prefab, Vector3 position, float yaw) {
      var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
      item.transform.SetParent(parent, false);
      item.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
      var bounds = BlockKit.WorldBounds(item);
      // Centre the piece on the requested point, bottom at the requested height.
      item.transform.position += position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) + parent.position;
      return item;
    }

    static GameObject BuildBoom() {
      var wall = Load(BR + "Environments/SM_Env_Port_Wall_01.prefab");
      var pole = Load(BR + "Environments/SM_Env_Dock_Poles_01.prefab");
      if (wall == null) return null;
      var root = new GameObject("sea_barrier_kit");
      var probe = (GameObject)PrefabUtility.InstantiatePrefab(wall);
      var size = BlockKit.WorldBounds(probe).size;
      Object.DestroyImmediate(probe);
      // Lay the wall's long side along X; walls sit with their top about 1.2 m above the water line (y = -0.1).
      var turned = size.z > size.x;
      var length = Mathf.Max(0.5f, turned ? size.z : size.x);
      var count = Mathf.Max(1, Mathf.RoundToInt(BoomLength / length));
      var start = -length * (count - 1) / 2f;
      var sink = Mathf.Max(0f, size.y - 1.2f);
      for (var i = 0; i < count; i++) Place(root.transform, wall, new Vector3(start + i * length, -sink, 0f), turned ? 90f : 0f);
      if (pole != null) {
        Place(root.transform, pole, new Vector3(-BoomLength / 2f - 0.6f, -0.5f, 0f), 0f);
        Place(root.transform, pole, new Vector3(BoomLength / 2f + 0.6f, -0.5f, 0f), 180f);
      }
      if (AssetDatabase.IsValidFolder("Assets/FactoryWarsArt") == false) AssetDatabase.CreateFolder("Assets", "FactoryWarsArt");
      if (AssetDatabase.IsValidFolder(Out) == false) AssetDatabase.CreateFolder("Assets/FactoryWarsArt", "Waterway");
      var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/sea_barrier_kit.prefab");
      Object.DestroyImmediate(root);
      Debug.Log($"[FW-T46] boom: {count} x port wall ({length:0.0} m, sink {sink:0.0} m)");
      return prefab;
    }

    static void SetSlot(VisualCatalog catalog, string id, GameObject prefab, VisualCategory category, string notes) {
      if (prefab == null) return;
      var slot = catalog.Slots.FirstOrDefault(s => s != null && s.Id == id);
      if (slot == null) {
        slot = new VisualSlot { Id = id };
        catalog.Slots.Add(slot);
      }
      slot.Category = category;
      slot.Prefabs = new List<GameObject> { prefab };
      slot.Scale = 1f;
      slot.AutoFit = false;
      slot.PivotOffset = Vector3.zero;
      slot.Size = BlockKit.WorldBounds(prefab).size;
      slot.TeamColor = TeamColorMode.None;
      slot.RealWorld = RealWorldCheck.Clear;
      slot.OwnerRing = category == VisualCategory.Building;
      slot.Notes = notes;
    }

    [MenuItem("Tools/Factory Wars/Task 46/Build waterway models")]
    public static void Build() {
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      SetSlot(catalog, VisualCatalog.BuildingId("sea_barrier"), BuildBoom(), VisualCategory.Building, "Task 46 sea barrier boom: POLYGON Battle Royale port walls + dock poles (kitbash, unchanged pieces)");
      SetSlot(catalog, VisualCatalog.BuildingId("sea_watchtower"), Load(BR + "Props/SM_Prop_GuardTower_01.prefab"), VisualCategory.Building, "Task 46 watchtower: POLYGON Battle Royale guard tower");
      foreach (var (id, path) in new[] {
        ("naval/dock", BR + "Environments/SM_Env_Dock_01.prefab"),
        ("naval/dock_poles", BR + "Environments/SM_Env_Dock_Poles_01.prefab"),
        ("naval/boat_tie", BR + "Environments/SM_Env_Port_Boat_Tie_01.prefab"),
        ("naval/slab", BR + "Environments/SM_Env_Port_Concrete_Slab_01.prefab"),
        ("naval/port_wall", BR + "Environments/SM_Env_Port_Wall_01.prefab"),
        ("naval/port_corner", BR + "Environments/SM_Env_Port_Wall_Corner_01.prefab"),
        ("naval/boat", BR + "Vehicles/SM_Veh_Boat_01.prefab"),
        ("naval/crane", PK + "Buildings/SM_Bld_Crane_01.prefab"),
      }) {
        SetSlot(catalog, id, Load(path), VisualCategory.Prop, "Task 46 harbour / dock dressing (pack prefab, unchanged)");
      }
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      Debug.Log("[FW-T46] waterway models registered");
    }
  }
}
