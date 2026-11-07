namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEditor.SceneManagement;
  using UnityEngine;
  using UnityEngine.Rendering;
  using UnityEngine.Rendering.Universal;

  /// <summary>
  /// 파츠 표에서 프리팹을 굽고, 확인용 갤러리 씬을 만들고, 카메라로 PNG를 저장한다.
  /// 렌더링은 -nographics 없이 배치 모드로 돌린다: -executeMethod FactoryWars.EditorTools.ArtBaker.RenderGallery
  /// </summary>
  public static class ArtBaker {
    const string ArtRoot = "Assets/Art/Generated";
    const string ResourceRoot = "Assets/QuantumUser/Resources/FactoryWars/Art";
    const string GalleryScene = "Assets/Art/Gallery/Gallery.unity";

    public class Stats {
      public string Id;
      public string Kind;
      public int Renderers;
      public int Triangles;
      public int Draws;
      public int MaxRenderers;
      public int MaxTriangles;
    }

    static Material _base;
    static Material _glowOn;

    static Material SaveMaterial(string path, Material fresh) {
      var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (existing != null) {
        existing.CopyPropertiesFromMaterial(fresh);
        existing.shaderKeywords = fresh.shaderKeywords;
        existing.globalIlluminationFlags = fresh.globalIlluminationFlags;
        EditorUtility.SetDirty(existing);
        return existing;
      }
      AssetDatabase.CreateAsset(fresh, path);
      return fresh;
    }

    static Texture2D BuildPalette(string name, float scale) {
      var path = $"{ResourceRoot}/{name}.png";
      var pixels = ArtPalette.Pixels();
      var texture = new Texture2D(ArtPalette.Size, 1, TextureFormat.RGBA32, false);
      for (var i = 0; i < pixels.Length; i++) {
        var c = pixels[i];
        pixels[i] = new Color32((byte)(c.r * scale), (byte)(c.g * scale), (byte)(c.b * scale), 255);
      }
      texture.SetPixels32(pixels);
      texture.Apply();
      File.WriteAllBytes(path, texture.EncodeToPNG());
      Object.DestroyImmediate(texture);
      AssetDatabase.ImportAsset(path);
      var importer = (TextureImporter)AssetImporter.GetAtPath(path);
      importer.filterMode = FilterMode.Point;
      importer.mipmapEnabled = false;
      importer.wrapMode = TextureWrapMode.Clamp;
      importer.textureCompression = TextureImporterCompression.Uncompressed;
      importer.npotScale = TextureImporterNPOTScale.None;
      importer.SaveAndReimport();
      return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void BuildArtMaterials() {
      Directory.CreateDirectory(ResourceRoot);
      var palette = BuildPalette("Palette", 1f);
      var dim = BuildPalette("PaletteDim", 0.3f);
      var lit = Shader.Find("Universal Render Pipeline/Lit");

      var baseMaterial = new Material(lit) { name = "ArtBase" };
      baseMaterial.SetTexture("_BaseMap", palette);
      baseMaterial.SetColor("_BaseColor", Color.white);
      baseMaterial.SetFloat("_Metallic", 0.25f);
      baseMaterial.SetFloat("_Smoothness", 0.32f);
      _base = SaveMaterial($"{ResourceRoot}/ArtBase.mat", baseMaterial);

      var on = new Material(lit) { name = ArtLook.GlowOn };
      on.SetTexture("_BaseMap", palette);
      on.SetTexture("_EmissionMap", palette);
      on.SetColor("_EmissionColor", Color.white * 2.2f);
      on.EnableKeyword("_EMISSION");
      on.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
      _glowOn = SaveMaterial($"{ResourceRoot}/{ArtLook.GlowOn}.mat", on);

      var floor = new Material(lit) { name = "GalleryGround" };
      floor.SetColor("_BaseColor", new Color32(0x96, 0x94, 0x8A, 0xFF));
      floor.SetFloat("_Smoothness", 0.1f);
      SaveMaterial($"{ResourceRoot}/GalleryGround.mat", floor);

      var off = new Material(lit) { name = ArtLook.GlowOff };
      off.SetTexture("_BaseMap", dim);
      off.SetFloat("_Smoothness", 0.2f);
      SaveMaterial($"{ResourceRoot}/{ArtLook.GlowOff}.mat", off);
      AssetDatabase.SaveAssets();
    }

    static Material SlotMaterial(ArtSlot slot) {
      switch (slot) {
        case ArtSlot.Team: return AssetDatabase.LoadAssetAtPath<Material>("Assets/QuantumUser/Resources/FactoryWars/Materials/Player0.mat");
        case ArtSlot.Glow: return _glowOn;
        default: return _base;
      }
    }

    static GameObject AddPart(Transform parent, string id, string name, Vector3 position, System.Action<MeshKit> build) {
      var kit = new MeshKit();
      build(kit);
      var slots = new List<ArtSlot>();
      var mesh = kit.ToMesh($"{id}_{name}", slots);
      var path = $"{ArtRoot}/Meshes/{id}_{name}.asset";
      var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
      if (existing != null) {
        EditorUtility.CopySerialized(mesh, existing);
        mesh = existing;
      } else {
        AssetDatabase.CreateAsset(mesh, path);
      }
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(parent, false);
      item.transform.localPosition = position;
      item.GetComponent<MeshFilter>().sharedMesh = mesh;
      item.GetComponent<MeshRenderer>().sharedMaterials = slots.Select(SlotMaterial).ToArray();
      return item;
    }

    [MenuItem("Tools/Factory Wars/Bake Art")]
    public static void BakeAll() {
      Directory.CreateDirectory($"{ArtRoot}/Meshes");
      Directory.CreateDirectory($"{ArtRoot}/Prefabs");
      BuildArtMaterials();
      var entries = new List<ViewCatalog.Entry>();
      foreach (var recipe in ArtRecipes.All()) {
        var root = new GameObject(recipe.Id);
        AddPart(root.transform, recipe.Id, "Body", Vector3.zero, recipe.Body);
        var parts = new Dictionary<string, Transform>();
        foreach (var part in recipe.Moving) {
          var parent = part.Parent != null ? parts[part.Parent] : root.transform;
          parts[part.Name] = AddPart(parent, recipe.Id, part.Name, part.Pivot, part.Build).transform;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{ArtRoot}/Prefabs/{recipe.Id}.prefab");
        Object.DestroyImmediate(root);
        entries.Add(new ViewCatalog.Entry { Id = recipe.Id, Prefab = prefab });
      }

      var catalogPath = $"{ResourceRoot}/ViewCatalog.asset";
      var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(catalogPath);
      if (catalog == null) {
        catalog = ScriptableObject.CreateInstance<ViewCatalog>();
        AssetDatabase.CreateAsset(catalog, catalogPath);
      }
      catalog.Entries = entries.ToArray();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      Debug.Log($"ArtBaker: 프리팹 {entries.Count}개");
    }

    static GameObject Spawn(ViewCatalog catalog, Transform root, string id, Vector3 position, int color, float yaw = 0f) {
      var instance = (GameObject)PrefabUtility.InstantiatePrefab(catalog.Find(id), root);
      instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
      ArtLook.Apply(instance, color);
      return instance;
    }

    static void AddForest(Transform root, Vector3 position, float remaining, string name) {
      var kit = new MeshKit();
      ForestKit.Forest(kit, 7f, 4f, 11, remaining);
      var slots = new List<ArtSlot>();
      var mesh = kit.ToMesh(name, slots);
      var path = $"{ArtRoot}/Meshes/{name}.asset";
      var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
      if (existing != null) {
        EditorUtility.CopySerialized(mesh, existing);
        mesh = existing;
      } else {
        AssetDatabase.CreateAsset(mesh, path);
      }
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(root, false);
      item.transform.position = position;
      item.GetComponent<MeshFilter>().sharedMesh = mesh;
      item.GetComponent<MeshRenderer>().sharedMaterials = slots.Select(SlotMaterial).ToArray();
    }

    static readonly (string Id, float X, float Y)[] FreeRow = {
      ("ff_headquarters", 0, 0), ("ff_engineer", 7, 0), ("ff_ranger_squad", 11, 0), ("ff_skyguard_squad", 15, 0),
      ("ff_eagle_tank", 20, 0), ("ff_hawk_gunship", 26, 2.2f), ("ff_skyfortress_bomber", 35, 2.4f),
    };

    static readonly (string Id, float X, float Y)[] ContinentalRow = {
      ("cf_headquarters", 0, 0), ("cf_engineer", 7, 0), ("cf_militia", 11, 0), ("cf_flame_squad", 15, 0),
      ("cf_dragonscale_tank", 20, 0), ("cf_thunder_aa", 26, 0), ("cf_longwall_mlrs", 33, 0),
    };

    // 줄은 3/4 시점 카메라의 오른쪽 방향으로 놓는다. 그래야 화면에서 가로로 보인다.
    static readonly Vector3 Along = new Vector3(1, 0, 1).normalized;
    static readonly Vector3 Back = new Vector3(-1, 0, 1).normalized;
    const float RowGap = 22f;

    static Vector3 Place(float along, float row, float height = 0f) => Along * along + Back * (row * RowGap) + Vector3.up * height;

    [MenuItem("Tools/Factory Wars/Build Gallery")]
    public static void BuildGallery() {
      Directory.CreateDirectory(Path.GetDirectoryName(GalleryScene));
      var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
      var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>($"{ResourceRoot}/ViewCatalog.asset");

      var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
      ground.name = "Ground";
      ground.transform.position = new Vector3(-20, 0, 40);
      ground.transform.localScale = new Vector3(40, 1, 40);
      Object.DestroyImmediate(ground.GetComponent<Collider>());
      ground.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{ResourceRoot}/GalleryGround.mat");

      var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
      sun.type = LightType.Directional;
      sun.color = new Color(1f, 0.96f, 0.88f);
      sun.intensity = 0.98f;
      sun.shadows = LightShadows.Soft;
      sun.transform.rotation = Quaternion.Euler(52, 20, 0);
      RenderSettings.ambientMode = AmbientMode.Trilight;
      RenderSettings.ambientSkyColor = new Color(0.56f, 0.59f, 0.64f);
      RenderSettings.ambientEquatorColor = new Color(0.44f, 0.44f, 0.42f);
      RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.24f);

      var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
      camera.tag = "MainCamera";
      var cameraData = camera.AddComponent<UniversalAdditionalCameraData>();
      cameraData.renderPostProcessing = true;
      camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
      camera.GetComponent<Camera>().backgroundColor = new Color(0.58f, 0.6f, 0.56f);

      var volumeObject = new GameObject("Volume", typeof(Volume));
      var volume = volumeObject.GetComponent<Volume>();
      volume.isGlobal = true;
      AssetDatabase.DeleteAsset("Assets/Art/Gallery/GalleryVolume.asset");
      var profile = ScriptableObject.CreateInstance<VolumeProfile>();
      AssetDatabase.CreateAsset(profile, "Assets/Art/Gallery/GalleryVolume.asset");
      var bloom = profile.Add<Bloom>(true);
      bloom.intensity.Override(0.55f);
      bloom.threshold.Override(1.05f);
      var tone = profile.Add<Tonemapping>(true);
      tone.mode.Override(TonemappingMode.Neutral);
      AssetDatabase.SaveAssets();
      volume.sharedProfile = profile;

      var root = new GameObject("Gallery").transform;
      void Row((string Id, float X, float Y)[] row, float index, int color) {
        foreach (var item in row) {
          Spawn(catalog, root, item.Id, Place(item.X, index, item.Y), color, 180f);
        }
      }
      Row(FreeRow, 0, 0);
      Row(ContinentalRow, 1, 1);
      Row(FreeRow, 2, 2);
      Row(ContinentalRow, 3, 3);
      Row(FreeRow, 4, 5);
      Row(ContinentalRow, 5, 6);

      // 철광산: 채집 전, 고갈
      Spawn(catalog, root, "iron_mine", Place(0, -1), 0);
      var depleted = Spawn(catalog, root, "iron_mine", Place(8, -1), 0);
      depleted.transform.Find("Crystals").gameObject.SetActive(false);
      // 숲: 채집 전, 절반 채집
      AddForest(root, Place(20, -1), 1f, "gallery_forest_full");
      AddForest(root, Place(37, -1), 0.5f, "gallery_forest_half");
      // 다리: 세움, 내림
      var raised = Spawn(catalog, root, "bridge", Place(0, -2, 0.5f), 0, 180f);
      raised.transform.Find("Deck").localRotation = Quaternion.Euler(-78, 0, 0);
      Spawn(catalog, root, "bridge", Place(17, -2, 0.5f), 0, 180f);

      EditorSceneManager.SaveScene(scene, GalleryScene);
      Debug.Log("ArtBaker: 갤러리 씬 저장");
    }

    public static List<Stats> Measure() {
      var result = new List<Stats>();
      var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>($"{ResourceRoot}/ViewCatalog.asset");
      foreach (var recipe in ArtRecipes.All()) {
        var meshes = catalog.Find(recipe.Id).GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
        result.Add(new Stats {
          Id = recipe.Id, Kind = recipe.Kind, Renderers = meshes.Length, MaxRenderers = recipe.MaxRenderers, MaxTriangles = recipe.MaxTriangles,
          Triangles = meshes.Sum(m => m.triangles.Length / 3), Draws = meshes.Sum(m => m.subMeshCount),
        });
      }
      foreach (var remaining in new[] { 1f, 0.5f }) {
        var kit = new MeshKit();
        ForestKit.Forest(kit, 7f, 4f, 11, remaining);
        result.Add(new Stats { Id = $"forest_{remaining:0.0}", Kind = "자원", Renderers = 1, Draws = 1, Triangles = kit.TriangleCount, MaxRenderers = 1, MaxTriangles = 6000 });
      }
      return result;
    }

    static void Shot(Camera camera, string name, Vector3 position, Vector3 target, float fov, bool orthographic = false, float size = 10f) {
      const int Width = 1920;
      const int Height = 1080;
      camera.transform.position = position;
      camera.transform.LookAt(target);
      camera.orthographic = orthographic;
      camera.orthographicSize = size;
      camera.fieldOfView = fov;
      camera.nearClipPlane = 0.3f;
      camera.farClipPlane = 600f;
      var target2 = RenderTexture.GetTemporary(new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 4 });
      camera.targetTexture = target2;
      camera.Render();
      RenderTexture.active = target2;
      var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
      texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
      texture.Apply();
      RenderTexture.active = null;
      camera.targetTexture = null;
      RenderTexture.ReleaseTemporary(target2);
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots"));
      Directory.CreateDirectory(folder);
      File.WriteAllBytes(Path.Combine(folder, name), texture.EncodeToPNG());
      Object.DestroyImmediate(texture);
      Debug.Log($"ArtBaker: 저장 {name}");
    }

    /// <summary>GPU가 있는 배치 모드에서 호출한다. 실패하면 로그에 이유가 남고 씬은 그대로 열어 볼 수 있다.</summary>
    public static void RenderGallery() {
      BakeAll();
      BuildGallery();
      var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
      var shadow = pipeline.shadowDistance;
      pipeline.shadowDistance = 150f;
      var depthBias = pipeline.shadowDepthBias;
      var normalBias = pipeline.shadowNormalBias;
      pipeline.shadowDepthBias = 2f;
      pipeline.shadowNormalBias = 1.5f;
      var resolution = pipeline.mainLightShadowmapResolution;
      pipeline.mainLightShadowmapResolution = 4096;
      var cascades = pipeline.shadowCascadeCount;
      pipeline.shadowCascadeCount = 4;
      Debug.Log($"ArtBaker: 그래픽 장치 {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}");
      var camera = Camera.main;

      // 참고 이미지와 같은 위에서 비스듬한 3/4 시점: 방위 45도, 고각 약 35도, 직교 투영.
      Vector3 ThreeQuarter(Vector3 target, float distance) {
        var pitch = 35f * Mathf.Deg2Rad;
        return target + (new Vector3(1, 0, -1).normalized * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * distance;
      }
      void Close(string name, float along, float row, float height, float size) {
        var target = Place(along, row, height);
        Shot(camera, name, ThreeQuarter(target, 45f), target, 30f, true, size);
      }

      var middle = Place(18, 1.2f);
      Shot(camera, "01_front_oblique.png", middle + new Vector3(0, 40, -82), middle, 42f);
      var game = Place(14, 0.5f);
      Shot(camera, "02a_game_view_height70.png", game + new Vector3(0, 70, -58.7f), game, 60f);
      var near = Place(14, 0);
      Shot(camera, "02b_game_view_height45.png", near + new Vector3(0, 45, -37.8f), near, 60f);
      var pair = Place(18, 0.5f, 1f);
      Shot(camera, "03_three_quarter.png", ThreeQuarter(pair, 90f), pair, 30f, true, 13f);
      Close("04_close_free_units.png", 13.5f, 0, 1f, 5.2f);
      Close("05_close_free_air.png", 31f, 0, 2f, 6.6f);
      Close("06_close_continental_units.png", 13.5f, 1, 1f, 5.2f);
      Close("07_close_continental_heavy.png", 29.5f, 1, 1f, 5.2f);
      Close("08_close_hq_free.png", 0f, 0, 2.4f, 4.6f);
      Close("09_close_hq_continental.png", 0f, 1, 2.8f, 4.6f);
      Close("10_resources.png", 18.5f, -1, 2f, 13.5f);
      Close("11_bridges.png", 8.5f, -2, 1f, 9f);
      var colors = Place(20, 2.5f, 1f);
      Shot(camera, "12_team_colors.png", ThreeQuarter(colors, 120f), colors, 30f, true, 24f);

      pipeline.shadowDistance = shadow;
      pipeline.shadowDepthBias = depthBias;
      pipeline.shadowNormalBias = normalBias;
      pipeline.mainLightShadowmapResolution = resolution;
      pipeline.shadowCascadeCount = cascades;
      var text = new StringBuilder("id,kind,renderers,draws,triangles,maxRenderers,maxTriangles\n");
      foreach (var stat in Measure()) {
        text.AppendLine($"{stat.Id},{stat.Kind},{stat.Renderers},{stat.Draws},{stat.Triangles},{stat.MaxRenderers},{stat.MaxTriangles}");
      }
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots"));
      File.WriteAllText(Path.Combine(folder, "stats.csv"), text.ToString());
      Debug.Log("ArtBaker: 렌더링 완료");
    }
  }
}
