namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text;
  using UnityEditor;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// 항목 19B: Kenney 키트 도구. 임포트 설정, 공용 셰이더 머티리얼 변환, 키트별 접촉 시트(모델 격자 사진 + 번호 목록).
  /// 배치 모드에서 GPU가 있어야 접촉 시트를 찍는다(-nographics 없이 실행).
  /// </summary>
  public static class KenneyTools {
    public const string Root = "Assets/ExternalAssets/Kenney";
    public const string MaterialRoot = Root + "/_Materials";
    const string ShaderName = "FactoryWars/KenneyLit";
    const int Cell = 256;
    const int Columns = 10;

    public static IEnumerable<string> Kits() => Directory.Exists(Root) ? Directory.GetDirectories(Root).Select(d => d.Replace('\\', '/')).Where(d => Path.GetFileName(d).StartsWith("_") == false) : Enumerable.Empty<string>();

    public static string[] Models(string kit) => Directory.GetFiles(kit, "*.fbx", SearchOption.AllDirectories).Select(f => f.Replace('\\', '/')).OrderBy(f => f).ToArray();

    /// <summary>모델은 읽기 가능(정적 배칭·해시 배치용)하고 애니메이션·카메라·조명은 가져오지 않는다.</summary>
    [MenuItem("Tools/Factory Wars/Kenney/Configure Import")]
    public static void ConfigureImport() {
      foreach (var kit in Kits()) {
        foreach (var path in Models(kit)) {
          var importer = AssetImporter.GetAtPath(path) as ModelImporter;
          if (importer == null) {
            continue;
          }
          var dirty = false;
          void Set<T>(T current, T wanted, System.Action<T> apply) {
            if (EqualityComparer<T>.Default.Equals(current, wanted) == false) {
              apply(wanted);
              dirty = true;
            }
          }
          Set(importer.importAnimation, false, v => importer.importAnimation = v);
          Set(importer.importCameras, false, v => importer.importCameras = v);
          Set(importer.importLights, false, v => importer.importLights = v);
          Set(importer.animationType, ModelImporterAnimationType.None, v => importer.animationType = v);
          Set(importer.isReadable, true, v => importer.isReadable = v);
          Set(importer.materialImportMode, ModelImporterMaterialImportMode.ImportStandard, v => importer.materialImportMode = v);
          if (dirty) {
            importer.SaveAndReimport();
          }
        }
      }
    }

    /// <summary>
    /// FBX의 내장 머티리얼(색·텍스처)을 공용 셰이더 머티리얼로 바꿔 끼운다. 같은 색·텍스처는 머티리얼 하나를 같이 쓴다.
    /// 결과는 _Materials 폴더의 에셋이고, 모델 임포터의 외부 머티리얼 연결로 저장한다.
    /// </summary>
    [MenuItem("Tools/Factory Wars/Kenney/Convert Materials")]
    public static int ConvertMaterials() {
      var shader = Shader.Find(ShaderName);
      if (shader == null) {
        Debug.LogError("[FW-KENNEY] 공용 셰이더를 찾지 못함: " + ShaderName);
        return 0;
      }
      Directory.CreateDirectory(MaterialRoot);
      var made = new Dictionary<string, Material>();
      foreach (var path in Directory.GetFiles(MaterialRoot, "*.mat")) {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path.Replace('\\', '/'));
        if (existing != null) {
          made[existing.name] = existing;
        }
      }
      foreach (var kit in Kits()) {
        foreach (var path in Models(kit)) {
          var importer = AssetImporter.GetAtPath(path) as ModelImporter;
          if (importer == null) {
            continue;
          }
          var changed = false;
          foreach (var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()) {
            var color = source.HasProperty("_Color") ? source.GetColor("_Color") : source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
            var texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            var key = $"{Path.GetFileName(kit)}_{(texture != null ? texture.name : "flat")}_{ColorUtility.ToHtmlStringRGB(color)}";
            if (made.TryGetValue(key, out var material) == false) {
              material = new Material(shader) { name = key, enableInstancing = true };
              material.SetColor("_BaseColor", color);
              if (texture != null) {
                material.SetTexture("_BaseMap", texture);
              }
              AssetDatabase.CreateAsset(material, $"{MaterialRoot}/{key}.mat");
              made[key] = material;
            }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source.name), material);
            changed = true;
          }
          if (changed) {
            importer.SaveAndReimport();
          }
        }
      }
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-KENNEY] 공용 셰이더 머티리얼 {made.Count}개");
      return made.Count;
    }

    static string ShotFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "kenney"));

    /// <summary>키트마다 모델을 한 칸씩 찍어 격자 사진과 번호·이름·크기 목록(csv)을 남긴다. 이름을 추측하지 않고 이 시트를 보고 고른다.</summary>
    [MenuItem("Tools/Factory Wars/Kenney/Contact Sheets")]
    public static void ContactSheets() {
      Directory.CreateDirectory(ShotFolder());
      var stage = new GameObject("KenneyStage");
      var cameraObject = new GameObject("KenneyCamera");
      var camera = cameraObject.AddComponent<Camera>();
      camera.clearFlags = CameraClearFlags.SolidColor;
      camera.backgroundColor = new Color(0.16f, 0.17f, 0.18f);
      camera.fieldOfView = 30f;
      var sun = new GameObject("KenneySun").AddComponent<Light>();
      sun.type = LightType.Directional;
      sun.color = new Color32(0xFF, 0xF4, 0xE0, 0xFF);
      sun.intensity = 1.2f;
      sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
      RenderSettings.ambientMode = AmbientMode.Trilight;
      RenderSettings.ambientSkyColor = new Color(0.52f, 0.6f, 0.72f);
      RenderSettings.ambientEquatorColor = new Color(0.44f, 0.44f, 0.42f);
      RenderSettings.ambientGroundColor = new Color(0.25f, 0.2f, 0.16f);
      var target = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32);
      camera.targetTexture = target;
      try {
        foreach (var kit in Kits()) {
          var models = Models(kit);
          var rows = Mathf.Max(1, (models.Length + Columns - 1) / Columns);
          var sheet = new Texture2D(Columns * Cell, rows * Cell, TextureFormat.RGB24, false);
          var list = new StringBuilder("index,file,size_x,size_y,size_z,triangles,materials\n");
          for (var i = 0; i < models.Length; i++) {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(models[i]);
            if (prefab == null) {
              continue;
            }
            var item = (GameObject)Object.Instantiate(prefab, stage.transform);
            var renderers = item.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) {
              Object.DestroyImmediate(item);
              continue;
            }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) {
              bounds.Encapsulate(r.bounds);
            }
            var triangles = item.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).Sum(f => (int)(f.sharedMesh.triangles.Length / 3));
            var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().Count();
            list.AppendLine($"{i},{Path.GetFileName(models[i])},{bounds.size.x:0.##},{bounds.size.y:0.##},{bounds.size.z:0.##},{triangles},{materials}");
            var radius = Mathf.Max(0.01f, bounds.extents.magnitude);
            var distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            var direction = Quaternion.Euler(35f, -40f, 0f) * Vector3.forward;
            camera.transform.SetPositionAndRotation(bounds.center - direction * distance, Quaternion.LookRotation(direction));
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            camera.farClipPlane = distance + radius * 2f;
            camera.Render();
            RenderTexture.active = target;
            sheet.ReadPixels(new Rect(0, 0, Cell, Cell), (i % Columns) * Cell, (rows - 1 - i / Columns) * Cell);
            RenderTexture.active = null;
            Object.DestroyImmediate(item);
          }
          sheet.Apply();
          var name = Path.GetFileName(kit);
          File.WriteAllBytes(Path.Combine(ShotFolder(), $"{name}_contact.png"), sheet.EncodeToPNG());
          File.WriteAllText(Path.Combine(ShotFolder(), $"{name}_list.csv"), list.ToString());
          Object.DestroyImmediate(sheet);
          Debug.Log($"[FW-KENNEY] {name}: 모델 {models.Length}개 시트 저장");
        }
      } finally {
        camera.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(stage);
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(sun.gameObject);
      }
    }

    /// <summary>배치 모드 진입점: 임포트 설정 → 머티리얼 변환 → 접촉 시트.</summary>
    public static void PrepareAll() {
      AssetDatabase.Refresh();
      ConfigureImport();
      ConvertMaterials();
      ContactSheets();
      foreach (var kit in Kits()) {
        Debug.Log($"[FW-KENNEY] {Path.GetFileName(kit)} FBX {Models(kit).Length}");
      }
    }
  }
}
