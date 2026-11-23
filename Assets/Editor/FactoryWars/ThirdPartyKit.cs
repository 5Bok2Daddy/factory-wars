namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 사용자가 준 CC BY 4.0 모델(드론·전투함)을 슬롯에 연결한다(2026-11-22, PROMPT-ASSETS-DRONE-SHIPS).
  /// 원본 GLB(temp-asset/, git 제외)를 Game/Tools/glb/convert_glb.py로 줄여 만든 OBJ + 팔레트 PNG(Assets/ThirdParty/&lt;Name&gt;/)를
  /// 공유 머티리얼 1개짜리 프리팹으로 감싸 VisualCatalog 슬롯 첫 칸에 넣는다. 예전 모델은 둘째 칸에 남겨 대체용으로 쓴다.
  /// 배치: -executeMethod FactoryWars.EditorTools.ThirdPartyKit.Apply
  /// </summary>
  public static class ThirdPartyKit {
    public sealed class Model {
      public string Folder;
      public string Name;
      /// <summary>화면 길이(가장 긴 수평 변, m).</summary>
      public float Span;
      /// <summary>모델 앞(+Z)을 맞추는 Y 회전.</summary>
      public float Yaw;
      public string[] Slots;
      public string Notes;
    }

    public const string Root = "Assets/ThirdParty";

    public static readonly Model[] Models = {
      new Model {
        Folder = "SharedScoutDrone", Name = "shared_scout_drone", Span = 2.6f, Yaw = -90f,
        Slots = new[] { "unit/shared_scout_drone", "unit/ff_nest_drone" },
        Notes = "CC BY 4.0 fixed-wing drone (see Assets/ThirdParty/CREDITS.md), decimated + flat palette",
      },
      new Model {
        Folder = "CfIronwallBattleship", Name = "cf_ironwall_battleship", Span = 20f, Yaw = 180f,
        Slots = new[] { "unit/cf_artillery_ship" },
        Notes = "CC BY 4.0 gun warship (see Assets/ThirdParty/CREDITS.md), decimated + flat palette, markings removed",
      },
      // Task 39 fix 3 (2026-11-23): PMC models from temp-assets/bridge (CC BY 4.0), real scale (infantry 1.8 m).
      new Model {
        Folder = "PmcStrikeHeli", Name = "pmc_strike_heli", Span = 16f, Yaw = 0f,
        Slots = new[] { "unit/pmc_attack_heli", "camp/display_heli" },
        Notes = "CC BY 4.0 attack helicopter (see Assets/ThirdParty/CREDITS.md), decimated + flat palette, renamed; 16 m real scale",
      },
      new Model {
        Folder = "PmcScoutHeli", Name = "pmc_scout_heli", Span = 8.5f, Yaw = 0f,
        Slots = new[] { "unit/pmc_scout_heli", "camp/display_scoutheli" },
        Notes = "CC BY 4.0 light helicopter (see Assets/ThirdParty/CREDITS.md), decimated + flat palette, renamed; 8.5 m real scale",
      },
      new Model {
        Folder = "MercHaulerTruck", Name = "merc_hauler_truck", Span = 7.5f, Yaw = 0f,
        Slots = new[] { "unit/merc_cargo_truck", "unit/logistics_truck" },
        Notes = "CC BY 4.0 military cargo truck (see Assets/ThirdParty/CREDITS.md), decimated + flat palette, renamed; 7.5 m",
      },
      new Model {
        Folder = "PmcLoiterDrone", Name = "pmc_loiter_drone", Span = 2.5f, Yaw = 90f,
        Slots = new[] { "unit/pmc_attack_drone", "camp/display_drone" },
        Notes = "CC BY 4.0 loitering drone (see Assets/ThirdParty/CREDITS.md), decimated + flat palette, renamed; 2.5 m",
      },
    };

    public static string PrefabPath(Model model) => $"{Root}/{model.Folder}/{model.Name}.prefab";

    static void ImportSettings(Model model) {
      var texPath = $"{Root}/{model.Folder}/{model.Name}_palette.png";
      if (AssetImporter.GetAtPath(texPath) is TextureImporter tex) {
        tex.filterMode = FilterMode.Point;
        tex.mipmapEnabled = false;
        tex.textureCompression = TextureImporterCompression.Uncompressed;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SaveAndReimport();
      }
      var objPath = $"{Root}/{model.Folder}/{model.Name}.obj";
      if (AssetImporter.GetAtPath(objPath) is ModelImporter mesh) {
        mesh.materialImportMode = ModelImporterMaterialImportMode.None;
        mesh.importNormals = ModelImporterNormals.Calculate;
        mesh.normalSmoothingAngle = 30f;
        mesh.importAnimation = false;
        mesh.isReadable = true;
        mesh.SaveAndReimport();
      }
    }

    static Material SharedMaterial(Model model) {
      var path = $"{Root}/{model.Folder}/{model.Name}.mat";
      var material = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (material == null) {
        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        AssetDatabase.CreateAsset(material, path);
      }
      var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{model.Folder}/{model.Name}_palette.png");
      material.SetTexture("_BaseMap", tex);
      material.mainTexture = tex;
      // 팔레트가 밝은 회색이라 게임 빛에서 하얗게 뜬다. 함정 회색 쪽으로 살짝 낮춘다.
      material.SetColor("_BaseColor", new Color(0.78f, 0.8f, 0.84f));
      material.SetFloat("_Smoothness", 0.25f);
      material.SetFloat("_Metallic", 0.1f);
      material.enableInstancing = true;
      EditorUtility.SetDirty(material);
      return material;
    }

    public static GameObject BuildPrefab(Model model) {
      ImportSettings(model);
      var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{model.Folder}/{model.Name}.obj");
      if (source == null) {
        Debug.LogWarning($"[FW-3P] model missing: {model.Name}");
        return null;
      }
      var material = SharedMaterial(model);
      var root = new GameObject(model.Name);
      var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
      instance.name = "Model";
      instance.transform.localRotation = Quaternion.Euler(0f, model.Yaw, 0f);
      foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) {
        renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
      }
      var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(model));
      Object.DestroyImmediate(root);
      return prefab;
    }

    static (Vector3 Pivot, Vector3 Size) Measure(GameObject prefab) {
      var copy = Object.Instantiate(prefab);
      try {
        var renderers = copy.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return (new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z), bounds.size);
      } finally {
        Object.DestroyImmediate(copy);
      }
    }

    public static void Wire(VisualCatalog catalog, Model model, GameObject prefab) {
      var (pivot, size) = Measure(prefab);
      foreach (var id in model.Slots) {
        var slot = catalog.Slots.FirstOrDefault(s => s != null && s.Id == id);
        if (slot == null) {
          slot = new VisualSlot { Id = id, Category = VisualCategory.Unit, OwnerRing = true };
          catalog.Slots.Add(slot);
        }
        // 예전 모델은 둘째 칸(대체용). 유닛은 첫 칸만 쓴다.
        var old = slot.Prefabs?.Where(p => p != null && p != prefab).Take(1).ToList() ?? new List<GameObject>();
        slot.Prefabs = new List<GameObject> { prefab };
        slot.Prefabs.AddRange(old);
        slot.Category = VisualCategory.Unit;
        slot.PivotOffset = pivot;
        slot.Size = size;
        slot.AutoFit = false;
        slot.YawOffset = 0f;
        slot.Scale = model.Span / Mathf.Max(0.01f, Mathf.Max(size.x, size.z));
        slot.TeamColor = TeamColorMode.None;
        slot.TeamTextures = new Texture2D[0];
        slot.SpinParts = new string[0];
        slot.TurretPart = null;
        slot.BarrelPart = null;
        slot.MuzzlePart = null;
        slot.MuzzleOffset = new Vector3(0f, size.y * 0.7f, size.z * 0.45f);
        slot.Animator = null;
        slot.RealWorld = RealWorldCheck.Modified;
        slot.OwnerRing = true;
        slot.Notes = model.Notes;
        Debug.Log($"[FW-3P] {id} <- {model.Name} size={size} scale={slot.Scale:0.####} span={model.Span}m");
      }
    }

    [MenuItem("Tools/Factory Wars/Assets/Apply Third-Party Drone + Ships")]
    public static void Apply() {
      AssetDatabase.Refresh();
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      foreach (var model in Models) {
        var prefab = BuildPrefab(model);
        if (prefab != null) {
          Wire(catalog, model, prefab);
        }
      }
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
    }
  }
}
