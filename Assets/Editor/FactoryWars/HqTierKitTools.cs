namespace FactoryWars.EditorTools {
  using System;
  using System.IO;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 승인된 사령부(HQ) 1→2→3단계 컨셉 시안 적용 도구.
  /// 1) 1단계 본관(ff_headquarters_kit / cf_headquarters_kit)을 단순 창고에서 Synty 군사 막사 + 위장 텐트 + 방벽 + 보급품으로 교체.
  /// 2) 2·3단계 증설 파츠(작전통제실 윙·발전기·관제탑·강화 방벽·헬리패드·투광등·지휘 APC·지휘 헬기)를 Resources/FactoryWarsPolish/에 배치.
  /// </summary>
  public static class HqTierKitTools {
    const string PolishDir = SyntyAssign.OutRoot + "/Polish/Resources/FactoryWarsPolish";
    const string PrefabDir = SyntyAssign.OutRoot + "/Prefabs";
    const string PM = SyntyAssign.PackRoot + "/PolygonMilitary/Prefabs/";
    const string PCN = SyntyAssign.PackRoot + "/PolygonConstruction/Prefabs/";
    const string GLB = "Assets/ThirdParty/GlbImported/Prefabs/";

    [MenuItem("Tools/Factory Wars/Assets/Apply Approved HQ Tier 1-3 Kit")]
    public static void ApplyApprovedHqTierKit() {
      Directory.CreateDirectory(PolishDir);
      Directory.CreateDirectory(PrefabDir);
      AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

      // 1. Copy Tier 2 & Tier 3 modular upgrade parts into Resources/FactoryWarsPolish/
      var parts = new (string Key, string Source)[] {
        ("HqControlRoom1", PM + "Props/Military/SM_Prop_Drone_Control_Room_01.prefab"),
        ("HqControlRoom2", PM + "Props/Military/SM_Prop_Drone_Control_Room_02.prefab"),
        ("HqGenerator",    PM + "Props/SM_Prop_Generator_Large_01.prefab"),
        ("HqControlTower", PM + "Buildings/SM_Bld_ControlTower_01.prefab"),
        ("HqBlastWall",    PM + "Props/SM_Prop_Barrier_Tall_Group_01.prefab"),
        ("HqHelipad",      PM + "Environment/SM_Env_Helipad_01.prefab"),
        ("HqFloodlight",   PCN + "Props/SM_Prop_Floodlights_01.prefab"),
        ("HqCmdApc",       GLB + "armored_troop_carrier.prefab"),
        ("HqCmdHeli",      GLB + "mh6_little_bird.prefab"),
      };
      var copied = 0;
      foreach (var (key, src) in parts) {
        var dst = $"{PolishDir}/{key}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null) {
          Debug.LogWarning($"[FW-HQ] Missing source prefab: {src}");
          continue;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null) {
          AssetDatabase.DeleteAsset(dst);
        }
        if (AssetDatabase.CopyAsset(src, dst)) copied++;
      }

      // 2. Rebuild Tier 1 Field HQ core (ff_headquarters_kit & cf_headquarters_kit)
      var catalog = AssetDatabase.LoadAssetAtPath<VisualCatalog>(VisualCatalogTools.AssetPath);
      if (catalog == null) throw new InvalidOperationException($"Missing VisualCatalog at {VisualCatalogTools.AssetPath}");

      foreach (var faction in new[] { "ff", "cf" }) {
        var kitPath = $"{PrefabDir}/{faction}_headquarters_kit.prefab";
        var root = new GameObject($"{faction}_headquarters_kit");
        try {
          // Core low-profile military barracks (replaces monotonous Warehouse_01 + baked ControlTower)
          AddPart(root.transform, PM + "Buildings/SM_Bld_Barracks_01.prefab", Vector3.zero, Quaternion.identity, Vector3.one * 0.92f);
          // Right-side camo net command canopy
          AddPart(root.transform, PM + "Buildings/SM_Bld_CamoNet_Tent_01.prefab", new Vector3(4.3f, 0f, -1.5f), Quaternion.Euler(0f, 15f, 0f), Vector3.one * 0.72f);
          // Front sandbag defensive barrier
          AddPart(root.transform, PM + "Props/Military/SM_Prop_Barrier_Base_Row_01.prefab", new Vector3(0f, 0f, -4.3f), Quaternion.identity, Vector3.one * 0.95f);
          // Left-side supply crate stack
          AddPart(root.transform, PM + "Props/Military/SM_Prop_Crate_Stack_01.prefab", new Vector3(-4.2f, 0f, -1.3f), Quaternion.Euler(0f, -12f, 0f), Vector3.one * 0.95f);

          foreach (var col in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);

          var prefab = PrefabUtility.SaveAsPrefabAsset(root, kitPath);
          var bounds = VisualCatalog.LocalBounds(prefab);
          var slot = catalog.Get($"building/{faction}_headquarters");
          if (slot != null) {
            slot.Prefabs.Clear();
            slot.Prefabs.Add(prefab);
            slot.PivotOffset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            slot.Size = bounds.size;
            slot.AutoFit = true;
            slot.Notes = "Approved Tier 1 Field HQ (Barracks + Camo Tent + Sandbags + Crates; Tier 2/3 attached dynamically)";
            Debug.Log($"[FW-HQ] Rebuilt {slot.Id} -> {kitPath} (size={bounds.size})");
          }
        } finally {
          UnityEngine.Object.DestroyImmediate(root);
        }
      }

      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-HQ] Approved HQ Tier 1-3 Kit applied: copiedParts={copied}");
    }

    static void AddPart(Transform parent, string assetPath, Vector3 localPos, Quaternion localRot, Vector3 localScale) {
      var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
      if (src == null) {
        Debug.LogWarning($"[FW-HQ] AddPart missing: {assetPath}");
        return;
      }
      var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
      PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
      var bounds = VisualCatalog.LocalBounds(inst);
      var baseCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
      inst.transform.localRotation = localRot;
      inst.transform.localScale = localScale;
      inst.transform.localPosition = localPos - localRot * Vector3.Scale(baseCenter, localScale);
    }
  }
}
