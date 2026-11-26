namespace FactoryWars.EditorTools {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// Task 44 — Priority GLB Batch 1 importer &amp; VisualCatalog slot wirer.
  /// Reads normalized OBJ/MTL/PNG assets produced by Tools/import_glb_batch.py,
  /// converts materials to URP Lit with a non-solid-black floor, builds Prefabs,
  /// and wires target slots in VisualCatalog.asset.
  /// </summary>
  public static class GlbBatchImportTools {
    const string ImportRoot = "Assets/ThirdParty/GlbImported";
    const string PrefabRoot = ImportRoot + "/Prefabs";
    const string MatRoot = ImportRoot + "/Materials";

    readonly struct Entry {
      public readonly string Key;
      public readonly string SourceGlb;
      public readonly string[] Slots;
      public readonly float YawOffset;

      public Entry(string key, string sourceGlb, float yawOffset, params string[] slots) {
        Key = key;
        SourceGlb = sourceGlb;
        YawOffset = yawOffset;
        Slots = slots;
      }
    }

    static readonly Entry[] Batch1 = {
      new Entry("apache_1", "apache_1.glb", 0f, "unit/ff_hawk_gunship", "unit/pmc_attack_heli"),
      new Entry("mh6_little_bird", "mh-6_little_bird_low_poly.glb", 0f, "unit/pmc_scout_heli", "unit/cf_gale_gunship"),
      new Entry("zala_lancet", "zala_lancet.glb", 0f, "unit/pmc_strike_drone", "unit/ff_loiter_munition", "unit/cf_loiter_munition"),
      new Entry("military_truck", "military_truck.glb", 0f, "unit/logistics_truck", "unit/merc_cargo_truck"),
      new Entry("armored_troop_carrier", "armored_troop_carrier.glb", 0f, "unit/cf_apc_transport", "unit/ff_striker_ifv"),
      new Entry("large_low_poly_building", "large_low_poly_building.glb", 0f, "dress/CityTower"),
      new Entry("tokyo_otemachi_bldg", "building_no_6_form_tokyo_otemachi_building_pack.glb", 0f, "dress/CityMedium"),
      new Entry("old_factory", "old_factory_free.glb", 0f, "building/ff_heavy_works", "building/cf_heavy_works"),
    };

    [MenuItem("Tools/Factory Wars/Assets/Apply Task 44 GLB Batch 1")]
    public static void ApplyBatch1() {
      Directory.CreateDirectory(PrefabRoot);
      Directory.CreateDirectory(MatRoot);
      AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

      var urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
      if (urpLit == null) throw new InvalidOperationException("URP Lit shader not found.");

      var catalog = AssetDatabase.LoadAssetAtPath<VisualCatalog>(VisualCatalogTools.AssetPath);
      if (catalog == null) throw new InvalidOperationException($"Missing VisualCatalog at {VisualCatalogTools.AssetPath}");

      var wiredSlots = 0;
      var builtPrefabs = 0;

      foreach (var item in Batch1) {
        var objPath = $"{ImportRoot}/{item.Key}/{item.Key}.obj";
        var importer = AssetImporter.GetAtPath(objPath) as ModelImporter;
        if (importer == null) {
          Debug.LogError($"[FW-GLB] Missing OBJ at {objPath}. Run Tools/import_glb_batch.py first.");
          continue;
        }

        var changed = false;
        if (importer.importCameras) { importer.importCameras = false; changed = true; }
        if (importer.importLights) { importer.importLights = false; changed = true; }
        if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription) {
          importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
          changed = true;
        }
        if (changed) importer.SaveAndReimport();

        var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
        if (sourceModel == null) {
          Debug.LogError($"[FW-GLB] Failed to load model at {objPath}");
          continue;
        }

        // Convert / build URP materials for this model so no pink or solid-black materials occur
        var folder = $"{ImportRoot}/{item.Key}";
        var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
          .Select(AssetDatabase.GUIDToAssetPath)
          .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
          .Where(t => t != null)
          .ToList();

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceModel);
        try {
          PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
          foreach (var col in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);

          var matCache = new Dictionary<Material, Material>();
          var matIndex = 0;
          foreach (var mr in instance.GetComponentsInChildren<MeshRenderer>(true)) {
            var mats = mr.sharedMaterials;
            var outMats = new Material[mats.Length];
            for (var i = 0; i < mats.Length; i++) {
              var srcMat = mats[i];
              if (srcMat != null && matCache.TryGetValue(srcMat, out var cached)) {
                outMats[i] = cached;
                continue;
              }
              var matPath = $"{MatRoot}/{item.Key}_mat{matIndex++}.mat";
              var urpMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
              if (urpMat == null) {
                urpMat = new Material(urpLit);
                AssetDatabase.CreateAsset(urpMat, matPath);
              } else {
                urpMat.shader = urpLit;
              }

              Texture baseTex = null;
              var baseColor = new Color(0.58f, 0.60f, 0.56f, 1f);
              if (srcMat != null) {
                if (srcMat.HasProperty("_BaseMap")) baseTex = srcMat.GetTexture("_BaseMap");
                if (baseTex == null && srcMat.HasProperty("_MainTex")) baseTex = srcMat.GetTexture("_MainTex");
                if (srcMat.HasProperty("_BaseColor")) baseColor = srcMat.GetColor("_BaseColor");
                else if (srcMat.HasProperty("_Color")) baseColor = srcMat.GetColor("_Color");
              }
              if (baseTex == null && textures.Count == 1) {
                baseTex = textures[0];
              }

              // Enforce AGENTS.md: never allow pure solid-black albedo on vehicles/structures
              var lum = 0.2126f * baseColor.r + 0.7152f * baseColor.g + 0.0722f * baseColor.b;
              if (baseTex == null && lum < 0.18f) {
                baseColor = new Color(Mathf.Max(baseColor.r, 0.24f), Mathf.Max(baseColor.g, 0.26f), Mathf.Max(baseColor.b, 0.25f), 1f);
              } else if (baseTex != null && lum < 0.35f) {
                baseColor = Color.white;
              }

              urpMat.SetColor("_BaseColor", baseColor);
              if (baseTex != null) urpMat.SetTexture("_BaseMap", baseTex);
              urpMat.SetFloat("_Smoothness", 0.28f);
              urpMat.SetFloat("_Metallic", 0.15f);
              urpMat.enableInstancing = true;
              EditorUtility.SetDirty(urpMat);

              if (srcMat != null) matCache[srcMat] = urpMat;
              outMats[i] = urpMat;
            }
            mr.sharedMaterials = outMats;
          }

          var prefabPath = $"{PrefabRoot}/{item.Key}.prefab";
          var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
          builtPrefabs++;

          var bounds = VisualCatalog.LocalBounds(prefab);
          var pivotOffset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
          var size = bounds.size;

          foreach (var slotId in item.Slots) {
            var slot = catalog.Get(slotId);
            if (slot == null) {
              Debug.LogWarning($"[FW-GLB] Slot not found in VisualCatalog: {slotId}");
              continue;
            }
            if (slot.Prefabs == null) slot.Prefabs = new List<GameObject>();
            slot.Prefabs.Clear();
            slot.Prefabs.Add(prefab);
            slot.PivotOffset = pivotOffset;
            slot.Size = size;
            slot.YawOffset = item.YawOffset;
            slot.AutoFit = true;
            slot.RealWorld = RealWorldCheck.Modified;
            slot.Notes = $"Task 44 GLB Batch 1 ({item.SourceGlb})";
            wiredSlots++;
            Debug.Log($"[FW-GLB] Wired {slotId} -> {prefabPath} (size={size})");
          }
        } finally {
          UnityEngine.Object.DestroyImmediate(instance);
        }
      }

      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-GLB] Batch 1 complete: prefabs={builtPrefabs}, wiredSlots={wiredSlots}");
    }

    [MenuItem("Tools/Factory Wars/Assets/Apply Task 44 GLB Batch 1 + Bake Map4P T45")]
    public static void ApplyBatch1AndBakeMap4P() {
      ApplyBatch1();
      FactoryWarsSetup.BakeMap4P();
      Debug.Log("[FW-GLB+T45] Completed Task 44 GLB Batch 1 wiring and Map4P T45 bake.");
    }
  }
}
