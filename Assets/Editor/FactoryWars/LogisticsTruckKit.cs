namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 2026-11-24 (AGY fix A): the Task 41 logistics truck uses the owned Synty PolygonMilitary canopy truck
  /// (SM_Veh_Truck_01_Canopy), never the rocket truck and never the CC BY hauler (that one stays the PMC cargo truck).
  /// A project-side kit prefab wraps the pack prefab (the pack original is not edited) and adds a hidden "Cargo" group of pack
  /// crates at the open tail; the battle view shows it only while the truck carries a load. Destroyed state =
  /// SM_Veh_Truck_01_Canopy_Destroyed through VisualSlot.WreckPrefab.
  /// Batch: -executeMethod FactoryWars.EditorTools.LogisticsTruckKit.Apply
  /// </summary>
  public static class LogisticsTruckKit {
    public const string SlotId = "unit/logistics_truck";
    const string PM = "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/";
    // The pack's canopy is an attachment: chassis SM_Veh_Truck_01 + SM_Veh_Truck_01_Canopy on its bed (same for the wreck).
    public const string ChassisPath = PM + "Vehicles/SM_Veh_Truck_01.prefab";
    public const string TruckPath = PM + "Vehicles/SM_Veh_Truck_01_Canopy.prefab";
    public const string WreckChassisPath = PM + "Vehicles/Destroyed/SM_Veh_Truck_01_Destroyed.prefab";
    public const string WreckPath = PM + "Vehicles/Destroyed/SM_Veh_Truck_01_Canopy_Destroyed.prefab";
    public const string KitPath = "Assets/FactoryWarsArt/Logistics/logistics_truck_kit.prefab";
    public const string WreckKitPath = "Assets/FactoryWarsArt/Logistics/logistics_truck_wreck_kit.prefab";
    /// <summary>Real length of the truck on screen (m), as the earlier hauler slot.</summary>
    public const float Span = 7.5f;

    static readonly (string Prefab, float X, float Z, float Yaw)[] Crates = {
      (PM + "Props/Military/SM_Prop_Crate_Ammo_01.prefab", -0.45f, 0.10f, 0f),
      (PM + "Props/Military/SM_Prop_Crate_Ammo_02.prefab", 0.45f, 0.05f, 8f),
      (PM + "Props/Military/SM_Prop_Crate_Cube_01.prefab", 0.0f, 0.55f, -6f),
    };

    static Bounds BoundsOf(GameObject root) {
      var renderers = root.GetComponentsInChildren<Renderer>(false);
      var b = renderers[0].bounds;
      foreach (var r in renderers) b.Encapsulate(r.bounds);
      return b;
    }

    static void EnsureFolder(string path) {
      var parts = path.Split('/');
      var cur = parts[0];
      for (var i = 1; i < parts.Length; i++) {
        var next = cur + "/" + parts[i];
        if (AssetDatabase.IsValidFolder(next) == false) AssetDatabase.CreateFolder(cur, parts[i]);
        cur = next;
      }
    }

    /// <summary>
    /// Canopy offset on the chassis. If the pack models the canopy in truck space (its bottom already at bed height) it stays at the
    /// origin; if it is modelled on the ground it is lifted onto the bed and centred over the rear axle.
    /// </summary>
    static Vector3 CanopyOffset(Bounds chassis, Bounds canopy) {
      if (canopy.min.y > chassis.min.y + chassis.size.y * 0.25f) return Vector3.zero;
      var bedY = chassis.min.y + chassis.size.y * 0.45f;
      var rearZ = chassis.min.z + canopy.size.z * 0.5f + 0.1f;
      return new Vector3(chassis.center.x - canopy.center.x, bedY - canopy.min.y, rearZ - canopy.center.z);
    }

    static GameObject Pair(string name, string chassisPath, string canopyPath, out Bounds chassisBounds, out Vector3 offset, out Bounds canopyOnTruck) {
      var root = new GameObject(name);
      var chassis = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(chassisPath), root.transform);
      chassis.name = "Part0_" + chassis.name;
      var canopy = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(canopyPath), root.transform);
      canopy.name = "Part1_" + canopy.name;
      chassisBounds = BoundsOf(chassis);
      var canopyBounds = BoundsOf(canopy);
      offset = CanopyOffset(chassisBounds, canopyBounds);
      canopy.transform.localPosition = offset;
      canopyOnTruck = new Bounds(canopyBounds.center + offset, canopyBounds.size);
      Debug.Log($"[FW-Truck] {name}: chassis {chassisBounds.min}..{chassisBounds.max}, canopy {canopyBounds.min}..{canopyBounds.max}, canopy offset {offset}");
      return root;
    }

    public static GameObject BuildKit() {
      if (AssetDatabase.LoadAssetAtPath<GameObject>(ChassisPath) == null || AssetDatabase.LoadAssetAtPath<GameObject>(TruckPath) == null) {
        Debug.LogError($"[FW-Truck] missing {ChassisPath} or {TruckPath}");
        return null;
      }
      EnsureFolder("Assets/FactoryWarsArt/Logistics");
      var root = Pair("logistics_truck_kit", ChassisPath, TruckPath, out var b, out _, out var bed);
      try {
        // Cargo at the open tail (-Z is the rear of Synty vehicles), standing on the bed.
        var cargo = new GameObject("Cargo");
        cargo.transform.SetParent(root.transform, false);
        // Bed = the canopy's bottom; crates stand at the open tail under the canopy edge.
        var bedY = bed.min.y;
        var tailZ = bed.min.z + 0.5f;
        var i = 0;
        foreach (var (path, x, z, yaw) in Crates) {
          var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
          if (src == null) {
            Debug.LogWarning($"[FW-Truck] missing crate {path}");
            continue;
          }
          var crate = (GameObject)PrefabUtility.InstantiatePrefab(src, cargo.transform);
          crate.name = $"Crate{i++}_{src.name}";
          crate.transform.localPosition = new Vector3(b.center.x + x * b.size.x * 0.5f, bedY, tailZ + z);
          crate.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }
        cargo.SetActive(false);
        return PrefabUtility.SaveAsPrefabAsset(root, KitPath);
      } finally {
        Object.DestroyImmediate(root);
      }
    }

    public static GameObject BuildWreckKit() {
      if (AssetDatabase.LoadAssetAtPath<GameObject>(WreckChassisPath) == null || AssetDatabase.LoadAssetAtPath<GameObject>(WreckPath) == null) return null;
      var root = Pair("logistics_truck_wreck_kit", WreckChassisPath, WreckPath, out _, out _, out _);
      try {
        return PrefabUtility.SaveAsPrefabAsset(root, WreckKitPath);
      } finally {
        Object.DestroyImmediate(root);
      }
    }

    [MenuItem("Tools/Factory Wars/Assets/Rebuild Logistics Truck")]
    public static void Apply() {
      AssetDatabase.Refresh();
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      var kit = BuildKit();
      if (kit == null) {
        EditorApplication.Exit(2);
        return;
      }
      var slot = catalog.Slots.FirstOrDefault(s => s != null && s.Id == SlotId);
      if (slot == null) {
        slot = new VisualSlot { Id = SlotId };
        catalog.Slots.Add(slot);
      }
      var copy = (GameObject)PrefabUtility.InstantiatePrefab(kit);
      Vector3 size, pivot;
      try {
        var b = BoundsOf(copy);
        size = b.size;
        pivot = new Vector3(-b.center.x, -b.min.y, -b.center.z);
      } finally {
        Object.DestroyImmediate(copy);
      }
      slot.Category = VisualCategory.Unit;
      slot.Prefabs = new List<GameObject> { kit };
      slot.PivotOffset = pivot;
      slot.Size = size;
      slot.AutoFit = false;
      slot.YawOffset = 0f;
      slot.Scale = Span / Mathf.Max(0.01f, Mathf.Max(size.x, size.z));
      slot.TeamColor = TeamColorMode.None;
      slot.TeamTextures = new Texture2D[0];
      slot.SpinParts = new string[0];
      slot.TurretPart = null;
      slot.BarrelPart = null;
      slot.MuzzlePart = null;
      slot.MuzzleOffset = new Vector3(0f, size.y * 0.7f, size.z * 0.45f);
      slot.Animator = null;
      slot.DeathSeconds = 0f;
      slot.RealWorld = RealWorldCheck.Clear;
      slot.OwnerRing = true;
      slot.WreckPrefab = BuildWreckKit();
      slot.Notes = "Synty PolygonMilitary SM_Veh_Truck_01 + SM_Veh_Truck_01_Canopy (project kit + Cargo crates); wreck SM_Veh_Truck_01_Destroyed + SM_Veh_Truck_01_Canopy_Destroyed";
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-Truck] {SlotId} <- {KitPath} size={size} scale={slot.Scale:0.###} wreck={(slot.WreckPrefab != null)}");
    }
  }
}
