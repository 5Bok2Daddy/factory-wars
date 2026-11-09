namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text;
  using UnityEditor;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// 항목 19B·19C: Kenney 키트 도구. 임포트 설정, 공용 셰이더 머티리얼 변환, 아틀라스 군용 재도색, 키트별 접촉 시트(모델 격자 사진 + 번호 목록).
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

    static readonly HashSet<string> NewKits = new HashSet<string> {
      "furniture-kit", "prototype-kit", "mini-forest", "blaster-kit", "mini-arcade", "animated-characters-protagonists", "watercraft-pack",
    };

    /// <summary>스케일 1, 노멀은 파일 값, 애니메이션·카메라·조명 없음. 읽기/쓰기는 nature-kit만 켠다(실행 중 합치기). 나머지는 GPU 인스턴싱.</summary>
    [MenuItem("Tools/Factory Wars/Kenney/Configure Import")]
    public static void ConfigureImport() {
      foreach (var kit in Kits()) {
        if (NewKits.Contains(Path.GetFileName(kit)) == false) {
          continue;
        }
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
          var isAnimKit = kit.EndsWith("animated-characters-protagonists");
          var hasClips = isAnimKit && (path.EndsWith("idle.fbx") || path.EndsWith("run.fbx"));
          Set(importer.importAnimation, hasClips, v => importer.importAnimation = v);
          Set(importer.importCameras, false, v => importer.importCameras = v);
          Set(importer.importLights, false, v => importer.importLights = v);
          Set(importer.animationType, isAnimKit ? ModelImporterAnimationType.Legacy : ModelImporterAnimationType.None, v => importer.animationType = v);
          // nature-kit은 실행 중에 그룹별로 한 메쉬로 합치므로 읽기 가능해야 한다(그리기 호출 절약). 나머지는 끈다.
          Set(importer.isReadable, kit.EndsWith("nature-kit"), v => importer.isReadable = v);
          Set(importer.globalScale, 1f, v => importer.globalScale = v);
          Set(importer.importNormals, ModelImporterNormals.Import, v => importer.importNormals = v);
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
        if (NewKits.Contains(Path.GetFileName(kit)) == false) {
          continue;
        }
        foreach (var path in Models(kit)) {
          var importer = AssetImporter.GetAtPath(path) as ModelImporter;
          if (importer == null) {
            continue;
          }
          var changed = false;
          foreach (var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()) {
            var color = source.HasProperty("_Color") ? source.GetColor("_Color") : source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
            var texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            if (texture == null && kit.EndsWith("animated-characters-protagonists")) {
              texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{kit}/Textures/colormap.png");
            }
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
          var name = Path.GetFileName(kit);
          var sheetPath = Path.Combine(ShotFolder(), $"{name}_contact.png");
          if (File.Exists(sheetPath) && name != "car-kit") {
            continue;
          }
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
            var triangles = item.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)f.sharedMesh.GetIndexCount(i)) / 3);
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
          File.WriteAllBytes(sheetPath, sheet.EncodeToPNG());
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

    [System.Serializable]
    class MapFile {
      /// <summary>"original"(기본, 항목 27 K) 또는 "military"(19C 군용 재도색을 기본으로).</summary>
      public string Palette = "original";
      public KitLook[] Kits;
      public ColorLook[] Colors;
      public EntryFile[] Entries;
    }

    [System.Serializable]
    class KitLook {
      public string Kit;
      public float Desaturate = 0.4f;
      public float Brightness = 0.9f;
      public string Tint = "#FFFFFF";
      /// <summary>공용 셰이더의 높이 그라데이션·저주파 노이즈·접촉 AO 세기(항목 27: Kenney 원본은 거의 0).</summary>
      public float Gradient = 0.22f;
      public float Noise = 0.08f;
      public float Ao = 0.35f;
    }

    [System.Serializable]
    class ColorLook {
      public string Kit;
      public string From;
      public string To;
    }

    [System.Serializable]
    class EntryFile {
      public string Kind;
      public float Scale = 1f;
      public float JitterMin = 1f;
      public float JitterMax = 1f;
      public bool SnapYaw90;
      public bool Fit;
      public bool Shadows = true;
      public float LowKeep = 1f;
      public float MediumKeep = 1f;
      public string[] Models;
    }

    const string MapJson = Root + "/kenney-map.json";
    const string MapAsset = "Assets/QuantumUser/Resources/" + FactoryWars.View.KenneyAssetMap.ResourcePath + ".asset";

    static Color Hex(string value) => ColorUtility.TryParseHtmlString(value.StartsWith("#") ? value : "#" + value, out var color) ? color : Color.white;

    /// <summary>
    /// kenney-map.json → KenneyAssetMap 에셋과 공용 셰이더 머티리얼 값(키트별 채도·밝기·틴트, nature-kit 색 바꾸기).
    /// 모델마다 바운드를 재서 바닥 보정(Offset)과 크기(Size)를 저장한다. 기본 프로필 A·C가 이 맵을 쓰고 B는 절차 메쉬로 남긴다.
    /// </summary>
    [MenuItem("Tools/Factory Wars/Kenney/Build Map")]
    public static void BuildMap() {
      if (File.Exists(MapJson) == false) {
        Debug.Log("[FW-KENNEY] 매핑 파일 없음, 건너뜀");
        return;
      }
      var data = JsonUtility.FromJson<MapFile>(File.ReadAllText(MapJson));
      var swaps = new List<FactoryWars.View.KenneyAssetMap.MilitarySwap>();
      // 1. 머티리얼 값
      foreach (var path in Directory.Exists(MaterialRoot) ? Directory.GetFiles(MaterialRoot, "*.mat") : new string[0]) {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path.Replace('\\', '/'));
        if (material == null) {
          continue;
        }
        var kit = data.Kits.FirstOrDefault(k => material.name.StartsWith(k.Kit + "_"));
        if (kit == null) {
          continue;
        }
        // 항목 27: 기본은 Kenney 원본 색이다. 군용 아틀라스·색은 KenneyAssetMap.Military에 따로 남겨 프로필(B)이 런타임에 바꿔 끼운다.
        var original = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{kit.Kit}/Textures/colormap.png");
        var military = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{kit.Kit}/Textures/colormap_military.png");
        var atlas = material.GetTexture("_BaseMap") != null || kit.Kit == "animated-characters-protagonists";
        var hex = material.name.Substring(material.name.LastIndexOf('_') + 1);
        var swap = data.Colors.FirstOrDefault(c => c.Kit == kit.Kit && string.Equals(c.From, hex, System.StringComparison.OrdinalIgnoreCase));
        var militaryColor = swap != null ? Hex(swap.To) : Military(Hex(hex), out _);
        var useMilitary = data.Palette == "military";
        if (atlas) {
          material.SetTexture("_BaseMap", useMilitary && military != null ? military : original != null ? original : material.GetTexture("_BaseMap"));
        } else {
          material.SetColor("_BaseColor", useMilitary ? militaryColor : Hex(hex));
        }
        swaps.Add(new FactoryWars.View.KenneyAssetMap.MilitarySwap {
          Material = material, Texture = atlas ? military : null, HasColor = atlas == false, Color = militaryColor,
        });
        material.SetFloat("_Desaturate", kit.Desaturate);
        material.SetFloat("_Brightness", kit.Brightness);
        material.SetColor("_Tint", Hex(kit.Tint));
        material.SetFloat("_GradientStrength", kit.Gradient);
        material.SetFloat("_NoiseStrength", kit.Noise);
        material.SetFloat("_AoStrength", kit.Ao);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
      }
      // 2. 매핑 에셋
      var map = AssetDatabase.LoadAssetAtPath<FactoryWars.View.KenneyAssetMap>(MapAsset);
      if (map == null) {
        map = ScriptableObject.CreateInstance<FactoryWars.View.KenneyAssetMap>();
        Directory.CreateDirectory(Path.GetDirectoryName(MapAsset));
        AssetDatabase.CreateAsset(map, MapAsset);
      }
      map.Entries.Clear();
      map.Military = swaps;
      var missing = new List<string>();
      foreach (var file in data.Entries) {
        if (System.Enum.TryParse<FactoryWars.View.DressKind>(file.Kind, out var kind) == false) {
          missing.Add("종류 " + file.Kind);
          continue;
        }
        var entry = new FactoryWars.View.KenneyAssetMap.Entry {
          Kind = kind, Scale = file.Scale, ScaleJitter = new Vector2(file.JitterMin, file.JitterMax), SnapYaw90 = file.SnapYaw90,
          Fit = file.Fit, Shadows = file.Shadows, LowKeep = file.LowKeep, MediumKeep = file.MediumKeep,
        };
        foreach (var model in file.Models) {
          var parts = model.Split(':');
          var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{parts[0]}.fbx");
          if (prefab == null) {
            missing.Add(parts[0]);
            continue;
          }
          var probe = (GameObject)Object.Instantiate(prefab);
          var renderers = probe.GetComponentsInChildren<Renderer>();
          var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
          foreach (var r in renderers) {
            bounds.Encapsulate(r.bounds);
          }
          Object.DestroyImmediate(probe);
          entry.Variants.Add(new FactoryWars.View.KenneyAssetMap.Variant {
            Prefab = prefab,
            Weight = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : 1f,
            Offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z),
            Size = bounds.size,
          });
        }
        map.Entries.Add(entry);
      }
      map.WorkerIdle = AssetDatabase.LoadAllAssetsAtPath($"{Root}/animated-characters-protagonists/idle.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name.StartsWith("__preview__") == false);
      map.WorkerRun = AssetDatabase.LoadAllAssetsAtPath($"{Root}/animated-characters-protagonists/run.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name.StartsWith("__preview__") == false);
      EditorUtility.SetDirty(map);
      // 3. 프로필 연결: A(기본)·C는 Kenney, B는 절차 메쉬 비교용.
      foreach (var name in new[] { "VisualProfile", "VisualProfile_C" }) {
        var profile = AssetDatabase.LoadAssetAtPath<FactoryWars.View.VisualProfile>($"Assets/QuantumUser/Resources/FactoryWars/Visual/{name}.asset");
        if (profile != null) {
          profile.Dressing = map;
          EditorUtility.SetDirty(profile);
        }
      }
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-KENNEY] 매핑 {map.Entries.Count}종, 변종 {map.Entries.Sum(e => e.Variants.Count)}개" + (missing.Count > 0 ? ", 못 찾음: " + string.Join(", ", missing) : ""));
    }

    // 항목 19C 목표색. 벽·지붕·창문·금속·장식·초록·작업복.
    static readonly Color Wall = Hex("6E6A63");
    static readonly Color Roof = Hex("3D4043");
    static readonly Color Glass = Hex("222A30");
    static readonly Color Steel = Hex("5A626A");
    static readonly Color Rust = Hex("6B4A35");
    static readonly Color Khaki = Hex("7A7454");
    static readonly Color Olive = Hex("4E5A3A");
    static readonly Color WorkerOrange = Hex("C8662A");

    /// <summary>아틀라스 한 칸의 색을 범주로 나눠 군용 목표색으로 바꾼다. 원래 밝기 차이는 ±18%로 남긴다.</summary>
    public static Color Military(Color color, out string category, bool isCharacterKit = false) {
      Color.RGBToHSV(color, out var h, out var s, out var v);
      h *= 360f;
      Color target;
      if (s < 0.18f) {
        (target, category) = v > 0.72f ? (Wall, "벽") : v > 0.38f ? (Steel, "금속") : (Roof, "지붕");
      } else if (isCharacterKit && ((h >= 12f && h < 48f && s >= 0.35f) || (h >= 170f && h <= 260f && s >= 0.35f))) {
        (target, category) = (WorkerOrange, "작업복");
      } else if (h >= 170f && h <= 260f) {
        (target, category) = (Glass, "창문");
      } else if (h >= 70f && h < 170f) {
        (target, category) = (Olive, "초록");
      } else if (h >= 40f && h < 70f) {
        (target, category) = (Khaki, "노랑");
      } else if (h >= 15f && h < 40f && v < 0.75f) {
        (target, category) = (Color.Lerp(color, Rust, 0.6f), "갈색");
      } else {
        (target, category) = (Rust, "원색");
      }
      var shade = Mathf.Lerp(0.82f, 1.18f, v);
      return new Color(Mathf.Clamp01(target.r * shade), Mathf.Clamp01(target.g * shade), Mathf.Clamp01(target.b * shade), color.a);
    }

    /// <summary>
    /// 키트마다 colormap.png를 군용 팔레트로 다시 칠해 colormap_military.png로 굽는다(항목 19C).
    /// 바꾼 색 칸은 Game/Docs/kenney-palette.csv에 남긴다. nature-kit은 아틀라스가 없어 kenney-map.json의 색 바꾸기를 쓴다.
    /// </summary>
    [MenuItem("Tools/Factory Wars/Kenney/Recolor Atlases")]
    public static void RecolorAtlases() {
      var csv = new StringBuilder("kit,from,to,category,pixels\n");
      foreach (var kit in Kits()) {
        var source = $"{kit}/Textures/colormap.png";
        if (File.Exists(source) == false) {
          continue;
        }
        var isCharacterKit = kit.EndsWith("mini-characters") || kit.EndsWith("animated-characters-protagonists");
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes(source));
        var pixels = texture.GetPixels32();
        var origPixels = (Color32[])pixels.Clone();
        var counts = new Dictionary<Color32, (Color32 To, string Category, int Count)>();
        for (var i = 0; i < pixels.Length; i++) {
          var from = pixels[i];
          if (counts.TryGetValue(from, out var known) == false) {
            var to = (Color32)Military(from, out var category, isCharacterKit);
            known = (to, category, 0);
          }
          counts[from] = (known.To, known.Category, known.Count + 1);
          pixels[i] = known.To;
        }
        // 512x512 아틀라스에서 유색 칸 안의 흰 표식·문자·로고(garbage-truck 재활용 표식 등)를 주변 색으로 지운다(항목 19D).
        if (isCharacterKit == false && texture.width == 512 && texture.height == 512) {
          for (var cy = 0; cy < 512; cy += 32) {
            for (var cx = 0; cx < 512; cx += 32) {
              int wallCount = 0, colorCount = 0;
              Color32 dominantColor = default;
              for (var y = cy; y < cy + 32; y++) {
                for (var x = cx; x < cx + 32; x++) {
                  var idx = y * 512 + x;
                  Color.RGBToHSV(origPixels[idx], out _, out var s, out var v);
                  if (s < 0.22f && v > 0.65f) {
                    wallCount++;
                  } else if (s >= 0.28f) {
                    colorCount++;
                    dominantColor = pixels[idx];
                  }
                }
              }
              if (wallCount > 0 && colorCount >= wallCount * 2) {
                for (var y = cy; y < cy + 32; y++) {
                  for (var x = cx; x < cx + 32; x++) {
                    var idx = y * 512 + x;
                    Color.RGBToHSV(origPixels[idx], out _, out var s, out var v);
                    if (s < 0.25f && v > 0.55f) {
                      pixels[idx] = dominantColor;
                    }
                  }
                }
              }
            }
          }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        var target = $"{kit}/Textures/colormap_military.png";
        File.WriteAllBytes(target, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(target);
        var importer = (TextureImporter)AssetImporter.GetAtPath(target);
        var original = (TextureImporter)AssetImporter.GetAtPath(source);
        if (importer != null && original != null) {
          importer.filterMode = original.filterMode;
          importer.mipmapEnabled = original.mipmapEnabled;
          importer.textureCompression = TextureImporterCompression.Uncompressed;
          importer.SaveAndReimport();
        }
        foreach (var pair in counts.OrderByDescending(p => p.Value.Count)) {
          csv.AppendLine($"{Path.GetFileName(kit)},#{ColorUtility.ToHtmlStringRGB(pair.Key)},#{ColorUtility.ToHtmlStringRGB(pair.Value.To)},{pair.Value.Category},{pair.Value.Count}");
        }
      }
      var docs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs"));
      Directory.CreateDirectory(docs);
      File.WriteAllText(Path.Combine(docs, "kenney-palette.csv"), csv.ToString());
      Debug.Log("[FW-KENNEY] 아틀라스 군용 재도색 완료");
    }

    /// <summary>배치 모드 진입점: 임포트 설정 → 머티리얼 변환 → 군용 재도색 → 접촉 시트 → 매핑 에셋.</summary>
    public static void PrepareAll() {
      AssetDatabase.Refresh();
      ConfigureImport();
      ConvertMaterials();
      RecolorAtlases();
      BuildMap();
      ContactSheets();
      BuildMap();
      foreach (var kit in Kits()) {
        Debug.Log($"[FW-KENNEY] {Path.GetFileName(kit)} FBX {Models(kit).Length}");
      }
    }
  }
}
