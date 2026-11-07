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

    const float Emission = 2.4f;
    const float BloomIntensity = 0.8f;
    const float BloomThreshold = 1.2f;
    const float BloomScatter = 0.68f;
    const float RingMargin = 0.25f;
    const float BarMargin = 0.55f;

    static Material _team;
    static Material _neutral;

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

    static Texture2D SaveTexture(string name, Color32[] pixels, bool linear = false) {
      var path = $"{ResourceRoot}/{name}.png";
      var texture = new Texture2D(ArtPalette.Size, 1, TextureFormat.RGBA32, false);
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
      importer.sRGBTexture = linear == false;
      importer.alphaIsTransparency = false;
      importer.SaveAndReimport();
      return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>금속 반사에 쓰는 하늘 그라데이션. 하늘 큐브맵이 없으면 금속 면이 까맣게 나온다.</summary>
    static void BuildReflection() {
      const int Size = 32;
      var sky = new Color(0.82f, 0.88f, 0.95f);
      var horizon = new Color(0.62f, 0.62f, 0.6f);
      var ground = new Color(0.2f, 0.19f, 0.17f);
      var cube = new Cubemap(Size, TextureFormat.RGBA32, true);
      var faces = new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
      var pixels = new Color[Size * Size];
      foreach (var face in faces) {
        for (var y = 0; y < Size; y++) {
          var up = 1f - 2f * (y + 0.5f) / Size;
          var color = up > 0 ? Color.Lerp(horizon, sky, up) : Color.Lerp(horizon, ground, -up);
          for (var x = 0; x < Size; x++) {
            pixels[y * Size + x] = color;
          }
        }
        cube.SetPixels(pixels, face);
      }
      for (var i = 0; i < pixels.Length; i++) {
        pixels[i] = sky;
      }
      cube.SetPixels(pixels, CubemapFace.PositiveY);
      for (var i = 0; i < pixels.Length; i++) {
        pixels[i] = ground;
      }
      cube.SetPixels(pixels, CubemapFace.NegativeY);
      cube.Apply(true, false);
      var path = $"{ResourceRoot}/SkyReflection.cubemap";
      AssetDatabase.DeleteAsset(path);
      AssetDatabase.CreateAsset(cube, path);
    }

    /// <summary>컴포넌트를 하위 에셋으로 넣어야 다음 실행에도 남는다.</summary>
    static void BuildProfile(QualityTier tier, BloomDownscaleMode downscale, bool highQuality, int iterations) {
      var path = $"{ResourceRoot}/{GraphicsQuality.ProfileName(tier)}.asset";
      AssetDatabase.DeleteAsset(path);
      var profile = ScriptableObject.CreateInstance<VolumeProfile>();
      AssetDatabase.CreateAsset(profile, path);
      var bloom = profile.Add<Bloom>(true);
      bloom.intensity.Override(BloomIntensity);
      bloom.threshold.Override(BloomThreshold);
      bloom.scatter.Override(BloomScatter);
      bloom.downscale.Override(downscale);
      bloom.highQualityFiltering.Override(highQuality);
      bloom.maxIterations.Override(iterations);
      var tone = profile.Add<Tonemapping>(true);
      tone.mode.Override(TonemappingMode.Neutral);
      AssetDatabase.AddObjectToAsset(bloom, profile);
      AssetDatabase.AddObjectToAsset(tone, profile);
      EditorUtility.SetDirty(profile);
    }

    /// <summary>파티클용 부드러운 점 텍스처와 머티리얼 둘. 더하기는 불꽃, 알파는 연기다.</summary>
    static void BuildFxMaterials() {
      const int Size = 64;
      var path = $"{ResourceRoot}/FxDot.png";
      var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
      var pixels = new Color32[Size * Size];
      for (var y = 0; y < Size; y++) {
        for (var x = 0; x < Size; x++) {
          var distance = new Vector2(x + 0.5f - Size / 2f, y + 0.5f - Size / 2f).magnitude / (Size / 2f);
          var alpha = Mathf.Clamp01(1f - distance);
          pixels[y * Size + x] = new Color(1, 1, 1, alpha * alpha * (3f - 2f * alpha));
        }
      }
      texture.SetPixels32(pixels);
      texture.Apply();
      File.WriteAllBytes(path, texture.EncodeToPNG());
      Object.DestroyImmediate(texture);
      AssetDatabase.ImportAsset(path);
      var importer = (TextureImporter)AssetImporter.GetAtPath(path);
      importer.alphaIsTransparency = true;
      importer.textureCompression = TextureImporterCompression.Uncompressed;
      importer.SaveAndReimport();
      var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
      var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
      foreach (var (name, additive) in new[] { ("FxGlow", true), ("FxSmoke", false) }) {
        var material = new Material(shader) { name = name };
        material.SetTexture("_BaseMap", dot);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", additive ? 2f : 0f);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");
        SaveMaterial($"{ResourceRoot}/{name}.mat", material);
      }
    }

    static void BuildArtMaterials() {
      Directory.CreateDirectory(ResourceRoot);
      var surface = SaveTexture("PaletteSurface", ArtPalette.SurfacePixels(), true);
      var glowOn = SaveTexture("PaletteGlowOn", ArtPalette.EmissionPixels(true));
      var glowOff = SaveTexture("PaletteGlowOff", ArtPalette.EmissionPixels(false));
      var lit = Shader.Find("Universal Render Pipeline/Lit");

      // 모든 머티리얼이 같은 셰이더 변형을 쓴다. 그래야 SRP Batcher가 끊기지 않는다.
      Material Make(string name, Texture2D palette, Texture2D glow) {
        var material = new Material(lit) { name = name };
        material.SetTexture("_BaseMap", palette);
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_MetallicGlossMap", surface);
        material.SetFloat("_Smoothness", 1f);
        material.EnableKeyword("_METALLICSPECGLOSSMAP");
        material.SetTexture("_EmissionMap", glow);
        material.SetColor("_EmissionColor", Color.white * Emission);
        material.EnableKeyword("_EMISSION");
        // URP는 Emissive 플래그가 없으면 머티리얼 후처리에서 _EMISSION을 지운다. 실시간 GI는 쓰지 않아 비용이 없다.
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return SaveMaterial($"{ResourceRoot}/{name}.mat", material);
      }

      for (var i = 0; i < Palette.Count; i++) {
        var palette = SaveTexture($"PaletteP{i}", ArtPalette.BasePixels(Palette.Colors[i]));
        Make(ArtLook.MaterialName(i, false), palette, glowOff);
        var on = Make(ArtLook.MaterialName(i, true), palette, glowOn);
        if (i == 0) {
          _team = on;
        }
      }
      _neutral = Make(ArtLook.Neutral, SaveTexture("PaletteNeutral", ArtPalette.BasePixels(new Color32(0x80, 0x80, 0x80, 0xFF))), glowOn);

      var floor = new Material(lit) { name = "GalleryGround" };
      floor.SetColor("_BaseColor", new Color32(0x96, 0x94, 0x8A, 0xFF));
      floor.SetFloat("_Smoothness", 0.1f);
      SaveMaterial($"{ResourceRoot}/GalleryGround.mat", floor);

      BuildFxMaterials();
      BuildReflection();
      BuildProfile(QualityTier.Medium, BloomDownscaleMode.Quarter, false, 4);
      BuildProfile(QualityTier.High, BloomDownscaleMode.Half, true, 6);
      AssetDatabase.SaveAssets();
    }

    static Mesh SaveMesh(Mesh mesh, string name) {
      var path = $"{ArtRoot}/Meshes/{name}.asset";
      var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
      if (existing != null) {
        EditorUtility.CopySerialized(mesh, existing);
        return existing;
      }
      AssetDatabase.CreateAsset(mesh, path);
      return mesh;
    }

    static GameObject AddPart(Transform parent, string id, string name, Vector3 position, Material material, System.Action<MeshKit> build) {
      var kit = new MeshKit();
      build(kit);
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(parent, false);
      item.transform.localPosition = position;
      item.GetComponent<MeshFilter>().sharedMesh = SaveMesh(kit.ToMesh($"{id}_{name}"), $"{id}_{name}");
      item.GetComponent<MeshRenderer>().sharedMaterial = material;
      return item;
    }

    /// <summary>링과 바의 크기는 표시 배율을 곱한 모델에서 정한다. 시뮬레이션 반지름을 쓰지 않아서 모델이 링을 벗어나지 않는다.</summary>
    static ViewCatalog.Entry Describe(ArtRecipe recipe, GameObject root, GameObject prefab) {
      var renderers = root.GetComponentsInChildren<Renderer>(true);
      var bounds = renderers[0].bounds;
      foreach (var renderer in renderers) {
        bounds.Encapsulate(renderer.bounds);
      }
      // 포구: 포신이 있으면 포신 끝, 없으면 몸체 앞쪽 가슴 높이다.
      Transform muzzle = null;
      foreach (var name in new[] { "Barrel", "Guns", "Tubes" }) {
        muzzle = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        if (muzzle != null) {
          break;
        }
      }
      var muzzlePoint = new Vector3(0, 0.8f, bounds.max.z);
      var muzzlePart = "Model";
      if (muzzle != null) {
        var local = muzzle.GetComponent<MeshFilter>().sharedMesh.bounds;
        muzzlePoint = new Vector3(local.center.x, local.center.y, local.max.z);
        muzzlePart = muzzle.name;
      }
      return new ViewCatalog.Entry {
        Id = recipe.Id, Prefab = prefab, Unit = recipe.Kind == "유닛", DisplayScale = recipe.DisplayScale, Altitude = recipe.Altitude,
        MuzzlePart = muzzlePart, Muzzle = muzzlePoint, Smoke = recipe.Smoke,
        // 건물은 칸에 맞춘 바닥판이 기준이라 옮기지 않는다.
        Center = recipe.Kind == "유닛" ? new Vector2(bounds.center.x, bounds.center.z) : Vector2.zero,
        RingRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) * recipe.DisplayScale + RingMargin,
        BarHeight = recipe.Altitude + bounds.max.y * recipe.DisplayScale + BarMargin,
      };
    }

    [MenuItem("Tools/Factory Wars/Bake Art")]
    public static void BakeAll() {
      Directory.CreateDirectory($"{ArtRoot}/Meshes");
      Directory.CreateDirectory($"{ArtRoot}/Prefabs");
      BuildArtMaterials();
      IconBaker.BakeAll();
      var entries = new List<ViewCatalog.Entry>();
      foreach (var recipe in ArtRecipes.All()) {
        var root = new GameObject(recipe.Id);
        var material = recipe.Kind == "유닛" || recipe.Kind == "건물" ? _team : _neutral;
        AddPart(root.transform, recipe.Id, "Body", Vector3.zero, material, recipe.Body);
        var parts = new Dictionary<string, Transform>();
        foreach (var part in recipe.Moving) {
          var parent = part.Parent != null ? parts[part.Parent] : root.transform;
          parts[part.Name] = AddPart(parent, recipe.Id, part.Name, part.Pivot, material, part.Build).transform;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{ArtRoot}/Prefabs/{recipe.Id}.prefab");
        entries.Add(Describe(recipe, root, prefab));
        Object.DestroyImmediate(root);
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

    static ArtView Spawn(ViewCatalog catalog, Transform root, string id, Vector3 position, int color, float yaw = 0f, bool powered = true) {
      var view = ArtView.Create(catalog, id, root, color, powered);
      view.transform.position = position;
      view.SetYaw(yaw);
      return view;
    }

    static void AddForest(Transform root, Vector3 position, float remaining, string name) {
      var kit = new MeshKit();
      ForestKit.Forest(kit, 7f, 4f, 11, remaining);
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(root, false);
      item.transform.position = position;
      item.GetComponent<MeshFilter>().sharedMesh = SaveMesh(kit.ToMesh(name), name);
      item.GetComponent<MeshRenderer>().sharedMaterial = _neutral;
    }

    static readonly (string Id, float X)[] FreeRow = {
      ("ff_headquarters", 0), ("ff_engineer", 7), ("ff_ranger_squad", 11.5f), ("ff_skyguard_squad", 16),
      ("ff_eagle_tank", 21), ("ff_hawk_gunship", 28), ("ff_skyfortress_bomber", 39),
    };

    static readonly (string Id, float X)[] ContinentalRow = {
      ("cf_headquarters", 0), ("cf_engineer", 7), ("cf_militia", 11.5f), ("cf_flame_squad", 16),
      ("cf_dragonscale_tank", 21), ("cf_thunder_aa", 28), ("cf_longwall_mlrs", 35),
    };

    static readonly string[] BuildingKinds = {
      "collection_yard", "supply_depot", "barracks", "power_plant", "processor",
      "research_lab", "vehicle_factory", "ground_tower", "air_defense", "airfield", "heavy_works",
    };

    const string DataRoot = "Assets/QuantumUser/Resources/FactoryWars";
    static readonly Vector3 MapPreview = new Vector3(900, 0, 0);
    const float BuildingGap = 8f;
    const float FreeBuildingRow = -3f;
    const float ContinentalBuildingRow = -4f;
    const float OutageRow = -5f;

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
      camera.AddComponent<UniversalAdditionalCameraData>();
      camera.GetComponent<Camera>().allowHDR = true;
      camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
      camera.GetComponent<Camera>().backgroundColor = new Color(0.58f, 0.6f, 0.56f);

      GraphicsQuality.Apply(QualityTier.High);

      var root = new GameObject("Gallery").transform;
      void Row((string Id, float X)[] row, float index, int color) {
        foreach (var item in row) {
          Spawn(catalog, root, item.Id, Place(item.X, index), color, 180f);
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
      depleted.Model.transform.Find("Crystals").gameObject.SetActive(false);
      // 숲: 채집 전, 절반 채집
      AddForest(root, Place(20, -1), 1f, "gallery_forest_full");
      AddForest(root, Place(37, -1), 0.5f, "gallery_forest_half");
      // 다리는 항상 내려져 있다.
      Spawn(catalog, root, "bridge", Place(8, -2, 1f), 0, 180f);
      // 건물 줄. 왼쪽부터 BuildingKinds 순서다.
      for (var i = 0; i < BuildingKinds.Length; i++) {
        Spawn(catalog, root, $"ff_{BuildingKinds[i]}", Place(i * BuildingGap, FreeBuildingRow), 0, 180f);
        Spawn(catalog, root, $"cf_{BuildingKinds[i]}", Place(i * BuildingGap, ContinentalBuildingRow), 1, 180f);
      }
      // 정전 줄: 발전소, 가공소, 연구소
      var outageIds = new[] { "ff_power_plant", "ff_processor", "ff_research_lab", "cf_power_plant", "cf_processor", "cf_research_lab" };
      for (var i = 0; i < outageIds.Length; i++) {
        Spawn(catalog, root, outageIds[i], Place(i * BuildingGap, OutageRow), i < 3 ? 0 : 1, 180f, false);
      }
      // 정전: 같은 사령부를 전기 없이 한 채 더 놓는다.
      Spawn(catalog, root, "ff_headquarters", Place(-12, 0), 0, 180f, false);

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

    /// <summary>
    /// 내부 색 버퍼 형식은 대상 텍스처를 따른다. 8비트 대상이면 HDR이 꺼져 Bloom 임계값을 넘는 빛이 없어지므로, HDR 단계는 반정밀 대상에 그린다.
    /// </summary>
    static Color32[] Shot(Camera camera, string name, Vector3 position, Vector3 target, float fov, bool orthographic = false, float size = 10f) {
      const int Width = 1920;
      const int Height = 1080;
      camera.transform.position = position;
      camera.transform.LookAt(target);
      camera.orthographic = orthographic;
      camera.orthographicSize = size;
      camera.fieldOfView = fov;
      camera.nearClipPlane = 0.3f;
      camera.farClipPlane = 600f;
      var hdr = ((UniversalRenderPipelineAsset)QualitySettings.renderPipeline).supportsHDR;
      var surface = RenderTexture.GetTemporary(new RenderTextureDescriptor(Width, Height, hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, 24) { sRGB = hdr == false });
      camera.targetTexture = surface;
      camera.Render();
      RenderTexture.active = surface;
      var read = new Texture2D(Width, Height, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr);
      read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
      read.Apply();
      RenderTexture.active = null;
      camera.targetTexture = null;
      RenderTexture.ReleaseTemporary(surface);
      Color32[] pixels;
      if (hdr) {
        var values = read.GetPixels();
        pixels = new Color32[values.Length];
        for (var i = 0; i < values.Length; i++) {
          pixels[i] = values[i];
        }
      } else {
        pixels = read.GetPixels32();
      }
      Object.DestroyImmediate(read);
      var output = new Texture2D(Width, Height, TextureFormat.RGB24, false);
      output.SetPixels32(pixels);
      output.Apply();
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots"));
      Directory.CreateDirectory(folder);
      File.WriteAllBytes(Path.Combine(folder, name), output.EncodeToPNG());
      Object.DestroyImmediate(output);
      Debug.Log($"ArtBaker: 저장 {name}");
      return pixels;
    }

    static void ShowMarkers(bool shown) {
      var index = 0;
      foreach (var view in Object.FindObjectsByType<ArtView>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)) {
        view.SetSelected(shown && view.Entry.Unit);
        view.SetHealth(0.35f + 0.65f * MeshKit.Hash(3, index++));
      }
    }

    /// <summary>GPU가 있는 배치 모드에서 호출한다. 실패하면 로그에 이유가 남고 씬은 그대로 열어 볼 수 있다.</summary>
    public static void RenderGallery() {
      FactoryWarsSetup.ConfigureRenderPipeline();
      BakeAll();
      BuildGallery();
      Debug.Log($"ArtBaker: 그래픽 장치 {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}");
      var camera = Camera.main;
      GraphicsQuality.Apply(QualityTier.High);

      // 참고 이미지와 같은 위에서 비스듬한 3/4 시점: 방위 45도, 고각 약 35도.
      Vector3 ThreeQuarter(Vector3 target, float distance) {
        var pitch = 35f * Mathf.Deg2Rad;
        return target + (new Vector3(1, 0, -1).normalized * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * distance;
      }
      // 직교 투영은 그림자 캐스케이드가 거칠어 계단이 생긴다. 게임처럼 원근으로 찍고, 시야각을 좁혀 3/4 구도를 유지한다.
      void Tele(string name, Vector3 target, float distance, float size) {
        Shot(camera, name, ThreeQuarter(target, distance), target, 2f * Mathf.Atan(size / distance) * Mathf.Rad2Deg);
      }
      void Close(string name, float along, float row, float height, float size) {
        Tele(name, Place(along, row, height), 45f, size);
      }
      // 실제 게임 카메라와 같다: 피치 50도, 시야각 60도.
      Color32[] Game(string name, Vector3 focus, float height) {
        return Shot(camera, name, focus + new Vector3(0, height, -height / Mathf.Tan(50f * Mathf.Deg2Rad)), focus, 60f);
      }

      var middle = Place(20, 1.2f);
      Shot(camera, "01_front_oblique.png", middle + new Vector3(0, 40, -82), middle, 42f);
      var game = Place(17, 0.5f);
      ShowMarkers(true);
      Game("02a_game_view_height45.png", game, RtsCamera.DefaultHeight);
      Game("02b_game_view_height110.png", game, RtsCamera.MaxHeight);
      ShowMarkers(false);
      var pair = Place(20, 0.5f, 1f);
      Tele("03_three_quarter.png", pair, 90f, 15f);
      Close("04_close_free_units.png", 14f, 0, 1f, 6.4f);
      Close("05_close_free_air.png", 34f, 0, 3f, 9f);
      Close("06_close_continental_units.png", 14f, 1, 1f, 6.4f);
      Close("07_close_continental_heavy.png", 31f, 1, 1f, 7f);
      Close("08_close_hq_free.png", 0f, 0, 2.4f, 4.6f);
      Close("09_close_hq_continental.png", 0f, 1, 2.8f, 4.6f);
      Close("10_resources.png", 18.5f, -1, 2f, 13.5f);
      Close("11_bridges.png", 8.5f, -2, 1f, 9f);
      var colors = Place(20, 2.5f, 1f);
      Tele("12_team_colors.png", colors, 120f, 26f);

      // 후처리 확인: 같은 구도를 끄고 켜서 픽셀 차이를 잰다.
      var glow = Place(0, -0.5f, 1.5f);
      var cameraData = camera.GetUniversalAdditionalCameraData();
      cameraData.renderPostProcessing = false;
      var glowFov = 2f * Mathf.Atan(11f / 60f) * Mathf.Rad2Deg;
      var plain = Shot(camera, "13a_post_off.png", ThreeQuarter(glow, 60f), glow, glowFov);
      cameraData.renderPostProcessing = true;
      var post = Shot(camera, "13b_post_on.png", ThreeQuarter(glow, 60f), glow, glowFov);
      long total = 0;
      var changed = 0;
      for (var i = 0; i < plain.Length; i++) {
        var difference = Mathf.Abs(plain[i].r - post[i].r) + Mathf.Abs(plain[i].g - post[i].g) + Mathf.Abs(plain[i].b - post[i].b);
        total += difference;
        changed += difference > 24 ? 1 : 0;
      }
      Debug.Log($"ArtBaker: 후처리 차이 평균 {total / (float)plain.Length / 3f:0.00}/255, 눈에 띄게 바뀐 픽셀 {changed * 100f / plain.Length:0.0}%");

      Close("14_outage.png", -12f, 0, 2.4f, 4.6f);

      // 건물: 진영마다 네 장. 앞 세 채, 다음 세 채, 다음 두 채씩이다.
      void Buildings(string prefix, float row) {
        Tele($"{prefix}_1.png", Place(BuildingGap, row, 1.6f), 70f, 7.4f);
        Tele($"{prefix}_2.png", Place(BuildingGap * 4, row, 1.6f), 70f, 7.4f);
        Tele($"{prefix}_3.png", Place(BuildingGap * 7, row, 1.8f), 70f, 7.4f);
        Tele($"{prefix}_4.png", Place(BuildingGap * 9.5f, row, 2f), 70f, 6f);
      }
      Buildings("16_free_buildings", FreeBuildingRow);
      Buildings("17_continental_buildings", ContinentalBuildingRow);
      Game("18_buildings_game_view.png", Place(BuildingGap * 2.5f, FreeBuildingRow - 0.5f), RtsCamera.DefaultHeight);
      Tele("19_outage_buildings.png", Place(BuildingGap * 2.5f, OutageRow, 1.5f), 90f, 11.5f);

      // 지형: 실제 맵 데이터로 만든다. 갤러리에서 멀리 떨어뜨려 놓는다.
      var mapRoot = new GameObject("MapPreview").transform;
      mapRoot.position = MapPreview;
      var map2 = AssetDatabase.LoadAssetAtPath<Quantum.MapSpec>($"{DataRoot}/map_Map2P.asset");
      var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>($"{ResourceRoot}/ViewCatalog.asset");
      new WorldView(mapRoot, map2, catalog).Update(null, 0f);
      Vector3 OnMap(float x, float z) => MapPreview + new Vector3(x, 0, z);
      Shot(camera, "20_map2p_overview.png", OnMap(0, -150) + Vector3.up * 190f, OnMap(0, 0), 46f);
      Game("21_river_bridge.png", OnMap(-38, -40), 34f);
      Game("22_canyon.png", OnMap(-50, 30), 40f);
      Game("23_hills_rocks.png", OnMap(-10, -6), 40f);
      Object.DestroyImmediate(mapRoot.gameObject);

      // 품질 단계: 같은 게임 시점으로 비교한다.
      ShowMarkers(true);
      foreach (var tier in new[] { QualityTier.Low, QualityTier.Medium, QualityTier.High }) {
        GraphicsQuality.Apply(tier);
        Game($"15_quality_{tier.ToString().ToLowerInvariant()}.png", game, RtsCamera.DefaultHeight);
      }
      ShowMarkers(false);

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
