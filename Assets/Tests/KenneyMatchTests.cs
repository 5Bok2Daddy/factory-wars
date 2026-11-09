namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text;
  using FactoryWars.View;
  using NUnit.Framework;
  using UnityEngine;
  using UnityEngine.Rendering;
  using UnityEngine.SceneManagement;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;
  using Object = UnityEngine.Object;

  /// <summary>
  /// 항목 27 K-0: Kenney 참고 이미지(city-builder.png)의 색을 수치로 맞춘다.
  /// 받아 둔 Kenney 모델로 비교 장면(잔디·보도·도로 띠, 3~4층 상업 건물, 침엽수)을 만들어 참고와 같은 좁은 시야각·내려다보는 각도로 918×515에 찍고,
  /// 대상(배경·보도·그림자 진 보도·도로·잔디·지붕·나무 잎) 평균색과 목표색의 색차(ΔE, CIE76)를 잰다.
  /// 환경광·햇빛·그림자·바닥 단색·도로색을 고쳐 가며 반복하고, 맞춘 값을 Game-logs/lookdev/kenney_match.csv로 남긴다(VisualPresets "K"로 옮긴다).
  /// 결과 시트: screenshots/lookdev/27_kenney_match.png(참고 | 렌더 | 대상별 목표·렌더 색 비교).
  /// </summary>
  public class KenneyMatchTests {
    const int Width = 918;
    const int Height = 515;

    // 목표색(참고 이미지 색 군집 분석값, NEXT-TASK-27 K-0 표). 범위는 가운데 값을 쓴다.
    static readonly (string Name, Color32 Target)[] Targets = {
      ("배경", new Color32(0xB2, 0xBA, 0xCC, 0xFF)),
      ("보도(빛)", new Color32(0xB6, 0xBE, 0xD8, 0xFF)),
      ("보도(그림자)", new Color32(0x8E, 0x96, 0xB2, 0xFF)),
      ("도로", new Color32(0x40, 0x4A, 0x50, 0xFF)),
      ("잔디(빛)", new Color32(0x7B, 0xE5, 0xBA, 0xFF)),
      ("지붕 윗면", new Color32(0xDE, 0xE3, 0xF9, 0xFF)),
      ("나무 잎", new Color32(0x4C, 0x96, 0x88, 0xFF)),
    };

    static Vector3 Lab(Color32 c) {
      float Linear(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
      var r = Linear(c.r / 255f);
      var g = Linear(c.g / 255f);
      var b = Linear(c.b / 255f);
      var x = (r * 0.4124f + g * 0.3576f + b * 0.1805f) / 0.95047f;
      var y = r * 0.2126f + g * 0.7152f + b * 0.0722f;
      var z = (r * 0.0193f + g * 0.1192f + b * 0.9505f) / 1.08883f;
      float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
      return new Vector3(116f * F(y) - 16f, 500f * (F(x) - F(y)), 200f * (F(y) - F(z)));
    }

    public static float DeltaE(Color32 a, Color32 b) => (Lab(a) - Lab(b)).magnitude;

    static Color Linear(Color32 c) => ((Color)c).linear;

    /// <summary>sRGB 대상에 찍어 화면에 보이는 그대로의 8비트 색을 읽는다.</summary>
    static Texture2D Render(Camera camera) {
      var target = RenderTexture.GetTemporary(new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24) { sRGB = true });
      camera.targetTexture = target;
      camera.Render();
      RenderTexture.active = target;
      var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
      image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
      image.Apply();
      RenderTexture.active = null;
      camera.targetTexture = null;
      RenderTexture.ReleaseTemporary(target);
      return image;
    }

    static Color32 Sample(Texture2D image, Vector2 pixel) {
      var sum = Vector3.zero;
      var n = 0;
      for (var dy = -3; dy <= 3; dy++) {
        for (var dx = -3; dx <= 3; dx++) {
          var x = Mathf.Clamp(Mathf.RoundToInt(pixel.x) + dx, 0, Width - 1);
          var y = Mathf.Clamp(Mathf.RoundToInt(pixel.y) + dy, 0, Height - 1);
          var c = image.GetPixel(x, y);
          sum += new Vector3(c.r, c.g, c.b);
          n++;
        }
      }
      sum /= n;
      return new Color(sum.x, sum.y, sum.z, 1f);
    }

    static Mesh GroundStrips() {
      // 잔디(x < -14) | 보도(-14~14) | 도로(14~26). z는 -30~30. 서브메쉬마다 머티리얼 색으로 칠한다.
      var vertices = new List<Vector3>();
      var mesh = new Mesh { name = "kenney_ref_ground", subMeshCount = 3 };
      var lists = new List<int>[3];
      void Quad(int sub, float x0, float x1) {
        var i = vertices.Count;
        vertices.Add(new Vector3(x0, 0, -30));
        vertices.Add(new Vector3(x0, 0, 30));
        vertices.Add(new Vector3(x1, 0, 30));
        vertices.Add(new Vector3(x1, 0, -30));
        lists[sub] = new List<int> { i, i + 1, i + 2, i, i + 2, i + 3 };
      }
      Quad(0, -30, -14);
      Quad(1, -14, 14);
      Quad(2, 14, 26);
      mesh.SetVertices(vertices);
      for (var sub = 0; sub < 3; sub++) {
        mesh.SetTriangles(lists[sub], sub);
      }
      mesh.RecalculateNormals();
      return mesh;
    }

    static Material Strip(Color color) {
      var material = new Material(WorldView.SharedGroundMaterial);
      material.SetFloat("_UseVertexColor", 0f);
      material.SetColor("_BaseColor", color);
      return material;
    }

    /// <summary>지붕 윗면: 바운드 위쪽 여러 점 중 밝기 중앙값인 곳(옥상 소품·안테나를 피한다).</summary>
    static Vector2 RoofPoint(Texture2D image, Camera camera, Bounds bounds) {
      var candidates = new List<(float Luma, Vector2 Pixel)>();
      for (var gx = -1; gx <= 1; gx++) {
        for (var gz = -1; gz <= 1; gz++) {
          var world = new Vector3(bounds.center.x + gx * bounds.extents.x * 0.55f, bounds.max.y - 0.4f, bounds.center.z + gz * bounds.extents.z * 0.55f);
          var pixel = Vector2.Scale(camera.WorldToViewportPoint(world), new Vector2(Width, Height));
          var c = Sample(image, pixel);
          candidates.Add((((Color)c).grayscale, pixel));
        }
      }
      candidates.Sort((x, y) => y.Luma.CompareTo(x.Luma));
      return candidates[2].Pixel;
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(3000000)]
    public IEnumerator Lookdev_KenneyColorMatch() {
      var basis = VisualProfile.Current;
      Assert.IsNotNull(basis.Dressing, "Kenney 맵이 있는 프로필");
      var profile = Object.Instantiate(basis);
      profile.Tone = ToneMode.None;
      profile.Saturation = 0f;
      profile.Contrast = 0f;
      profile.Vignette = 0f;
      profile.BloomIntensity = 0f;
      VisualProfile.Current = profile;

      var scene = SceneManager.CreateScene("KenneyRef");
      SceneManager.SetActiveScene(scene);
      var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
      var camera = cameraObject.AddComponent<Camera>();
      var sun = new GameObject("Sun").AddComponent<Light>();
      sun.type = LightType.Directional;
      sun.shadows = LightShadows.Soft;
      RenderSettings.sun = sun;
      GraphicsQuality.Apply(QualityTier.High);

      var ground = new GameObject("Ground", typeof(MeshFilter), typeof(MeshRenderer));
      ground.GetComponent<MeshFilter>().sharedMesh = GroundStrips();
      // 첫 값은 게임 프로필(K) 그대로다. 맞춘 값을 VisualPresets.Kenney()로 옮겨 두었으므로 0회차가 곧 게임 값의 검증이다.
      var root = new GameObject("Props").transform;
      var building = DressingSpawn.Fitted(DressKind.CitySmall, root, 10f, 10f, 12f, 4001);
      Assert.IsNotNull(building, "상업 건물 모델");
      var trees = new List<GameObject>();
      for (var t = 0; t < 5; t++) {
        var tree = DressingSpawn.Place(DressKind.Conifer, root, new Vector3(-22f + (t % 3) * 3f, 0f, 6f + t * 2.5f), t * 40f, 77 + t);
        if (tree != null) {
          trees.Add(tree);
        }
      }
      // 참고 이미지 구도: 좁은 시야각, 약 37도 내려다봄, 대각선 방향.
      camera.fieldOfView = 22f;
      camera.aspect = Width / (float)Height;
      var look = Quaternion.Euler(37f, 45f, 0f);
      camera.transform.SetPositionAndRotation(new Vector3(-2f, 0f, 2f) - look * Vector3.forward * 120f, look);
      camera.nearClipPlane = 1f;
      camera.farClipPlane = 400f;

      var log = new StringBuilder();
      var rendered = new StringBuilder();
      var best = float.MaxValue;
      Texture2D finalImage = null;
      Color32[] finalColors = null;
      for (var iteration = 0; iteration < 10; iteration++) {
        WorldView.ApplyLighting(camera);
        GraphicsQuality.Apply(QualityTier.High);
        ground.GetComponent<MeshRenderer>().sharedMaterials = new[] { Strip(profile.GroundGrass), Strip(profile.GroundPlaza), Strip(profile.RoadColor.a > 0f ? profile.RoadColor : new Color32(0x3C, 0x3D, 0x40, 0xFF)) };
        yield return null;
        var image = Render(camera);
        // 표본 지점: 배경은 왼쪽 위 모서리, 나머지는 월드 위치를 화면으로 옮긴다.
        var light = sun.transform.forward;
        var flat = new Vector3(light.x, 0f, light.z).normalized;
        var bounds = building.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        // 렌더 해상도(918×515) 기준 화면 좌표. 배치 모드 기본 화면 크기와 다르므로 뷰포트로 옮긴다.
        camera.aspect = Width / (float)Height;
        Vector2 Screen(Vector3 world) => Vector2.Scale(camera.WorldToViewportPoint(world), new Vector2(Width, Height));
        var points = new Vector2[] {
          new Vector2(12, Height - 12),
          Screen(bounds.center - flat * (bounds.extents.magnitude * 0.9f + 3f) - new Vector3(0, bounds.center.y, 0)),
          Screen(new Vector3(bounds.center.x, 0f, bounds.center.z) + flat * (Mathf.Max(bounds.extents.x, bounds.extents.z) + 2.5f)),
          Screen(new Vector3(20f, 0f, -12f)),
          Screen(new Vector3(-24f, 0f, -14f)),
          RoofPoint(image, camera, bounds),
          trees.Count > 0 ? Screen(trees[0].GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }).center) : Vector2.zero,
        };
        var colors = points.Select(p => Sample(image, p)).ToArray();
        var errors = Targets.Select((t, i) => DeltaE(colors[i], t.Target)).ToArray();
        var mean = errors.Average();
        log.AppendLine($"{iteration}," + string.Join(",", errors.Select(e => e.ToString("0.0", CultureInfo.InvariantCulture))) + $",{mean.ToString("0.00", CultureInfo.InvariantCulture)}");
        rendered.AppendLine($"{iteration}," + string.Join(",", colors.Select(c => "#" + ColorUtility.ToHtmlStringRGB(c))) + "," + Values(profile).Replace("\n", ";"));
        if (mean < best) {
          best = mean;
          if (finalImage != null) {
            Object.Destroy(finalImage);
          }
          finalImage = image;
          finalColors = colors;
          File.WriteAllText(Path.Combine(LocalMatchTests.LookdevLogPath(), "kenney_match_values.txt"), Values(profile));
        } else {
          Object.Destroy(image);
        }
        if (errors.Max() <= 8f && mean <= 5f) {
          break;
        }
        // 빛은 그대로 두고(첫 추정이 빛·그림자 비율을 이미 거의 맞춘다), 고칠 수 있는 바닥 단색만 목표/렌더 비율로 고친다.
        // 환경광 색조는 목표의 그림자/빛 비율에 맞춰 조금씩(푸른 그림자). 지붕·잎은 모델 원래 색이라 기록만 한다.
        Color Fix(Color albedo, Color32 rendered, Color32 target) {
          var r = Linear(rendered);
          var t = Linear(target);
          var a = albedo.linear;
          float Step(float x, float want, float got) => Mathf.Clamp01(x * Mathf.Pow(want / Mathf.Max(0.002f, got), 0.85f));
          return new Color(Step(a.r, t.r, r.r), Step(a.g, t.g, r.g), Step(a.b, t.b, r.b), 1f).gamma;
        }
        profile.GroundPlaza = Fix(profile.GroundPlaza, colors[1], Targets[1].Target);
        profile.GroundPlazaDark = profile.GroundPlaza * 0.94f;
        profile.RoadColor = Fix(profile.RoadColor.a > 0f ? profile.RoadColor : new Color32(0x3C, 0x3D, 0x40, 0xFF), colors[3], Targets[3].Target);
        profile.GroundGrass = Fix(profile.GroundGrass, colors[4], Targets[4].Target);
        profile.GroundGrassLight = Color.Lerp(profile.GroundGrass, Color.white, 0.04f);
        profile.GroundGrassDark = profile.GroundGrass * 0.94f;
        // 그림자/빛 = A/(S+A). 채널마다 A' = A·[(1-g)/g]·[w/(1-w)] (g 지금 비율, w 목표 비율), 0.7 감쇠. 환경광은 HDR이라 1을 넘어도 된다.
        Vector3 Ratio(Color32 shadow, Color32 lit) => new Vector3(Linear(shadow).r / Mathf.Max(0.002f, Linear(lit).r), Linear(shadow).g / Mathf.Max(0.002f, Linear(lit).g), Linear(shadow).b / Mathf.Max(0.002f, Linear(lit).b));
        var want = Ratio(Targets[2].Target, Targets[1].Target);
        var got = Ratio(colors[2], colors[1]);
        float Gain(float w, float g) {
          g = Mathf.Clamp(g, 0.05f, 0.97f);
          w = Mathf.Clamp(w, 0.05f, 0.97f);
          return Mathf.Pow((1f - g) / g * (w / (1f - w)), 0.7f);
        }
        var gain = new Vector3(Gain(want.x, got.x), Gain(want.y, got.y), Gain(want.z, got.z));
        // 노출: 지붕·잎은 모델 원래 색(못 바꾼다)이라 빛 전체 세기로만 맞춘다. 두 곳 밝기 비의 기하평균, 0.5 감쇠, 한 번에 -20%~+25%.
        float Luma(Color32 c) => Linear(c).r * 0.2126f + Linear(c).g * 0.7152f + Linear(c).b * 0.0722f;
        var exposure = Mathf.Clamp(Mathf.Pow(Luma(Targets[5].Target) / Mathf.Max(0.002f, Luma(colors[5])) * (Luma(Targets[6].Target) / Mathf.Max(0.002f, Luma(colors[6]))), 0.25f), 0.8f, 1.25f);
        Color Ambient(Color c) => new Color(c.linear.r * gain.x * exposure, c.linear.g * gain.y * exposure, c.linear.b * gain.z * exposure).gamma;
        profile.AmbientSky = Ambient(profile.AmbientSky);
        profile.AmbientEquator = Ambient(profile.AmbientEquator);
        profile.AmbientGround = Ambient(profile.AmbientGround);
        profile.SunIntensity *= exposure;
        profile.BackgroundColor = Targets[0].Target;
      }
      Debug.Log("[FW-KENNEY-MATCH] 반복별 ΔE(배경,보도빛,보도그림자,도로,잔디,지붕,잎,평균)\n" + log);
      File.WriteAllText(Path.Combine(LocalMatchTests.LookdevLogPath(), "kenney_match.csv"), "iteration,background,walk_lit,walk_shadow,road,grass,roof,leaves,mean\n" + log);
      File.WriteAllText(Path.Combine(LocalMatchTests.LookdevLogPath(), "kenney_match_rendered.csv"), "iteration,background,walk_lit,walk_shadow,road,grass,roof,leaves,values\n" + rendered);

      // 시트: 참고 | 렌더 | 대상별 목표(왼쪽 반)·렌더(오른쪽 반) 색 비교.
      var swatches = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
      var rowHeight = Height / Targets.Length;
      for (var i = 0; i < Targets.Length; i++) {
        for (var y = i * rowHeight; y < (i + 1) * rowHeight - 4; y++) {
          for (var x = 0; x < Width; x++) {
            swatches.SetPixel(x, Height - 1 - y, x < Width / 2 ? (Color)Targets[i].Target : (Color)finalColors[i]);
          }
        }
      }
      swatches.Apply();
      var reference = new Texture2D(2, 2);
      reference.LoadImage(File.ReadAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "concept-art", "kenney-ref", "city-builder.png"))));
      var labels = Targets.Select((t, i) => $"{t.Name} ΔE {DeltaE(finalColors[i], t.Target):0.0}").ToArray();
      LocalMatchTests.SaveSheet("27_kenney_match", $"27 K-0 색 맞추기: 참고 | 비교 장면 렌더 | 대상별 목표(왼쪽)·렌더(오른쪽) — 평균 ΔE {best:0.0} / " + string.Join(", ", labels),
        new List<(Texture2D, string)> { (reference, "참고: Kenney City Builder"), (finalImage, "비교 장면(받아 둔 Kenney 모델, 맞춘 값)"), (swatches, "위부터 " + string.Join(" · ", Targets.Select(t => t.Name))) }, 3);
      VisualProfile.Current = basis;
      yield return SceneManager.UnloadSceneAsync(scene);
    }

    static string Values(VisualProfile p) {
      string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
      string Floats(Color c) => string.Join("/", new[] { c.r, c.g, c.b }.Select(v => v.ToString("0.000", CultureInfo.InvariantCulture)));
      return string.Join("\n",
        $"SunIntensity={p.SunIntensity.ToString("0.000", CultureInfo.InvariantCulture)}", $"SunColor={Hex(p.SunColor)}", $"ShadowStrength={p.ShadowStrength.ToString("0.00", CultureInfo.InvariantCulture)}",
        $"AmbientSky={Hex(p.AmbientSky)}", $"AmbientEquator={Hex(p.AmbientEquator)}", $"AmbientGround={Hex(p.AmbientGround)}",
        $"AmbientSkyF={Floats(p.AmbientSky)}", $"AmbientEquatorF={Floats(p.AmbientEquator)}", $"AmbientGroundF={Floats(p.AmbientGround)}",
        $"GroundGrass={Hex(p.GroundGrass)}", $"GroundPlaza={Hex(p.GroundPlaza)}", $"RoadColor={Hex(p.RoadColor)}", $"BackgroundColor={Hex(p.BackgroundColor)}");
    }
  }
}
