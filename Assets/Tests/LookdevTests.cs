namespace FactoryWars.Tests {
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering.Universal;
  using UnityEngine.TestTools;
  using UnityEngine.UI;
  using Object = UnityEngine.Object;

  /// <summary>
  /// 그래픽 룩 개발(항목 19A). 같은 시드·프레임의 장면을 여러 화면 프로필로 찍어 비교 시트를 만든다.
  /// GPU가 있는 배치 모드에서 -testCategory Lookdev로 돌린다. 결과는 screenshots/lookdev와 Game-logs/lookdev다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    public const int LookdevSeed = 7020;
    const int CellWidth = 640;
    const int CellHeight = 360;
    const int LabelHeight = 64;

    static string LookdevFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "lookdev"));

    static string LookdevLog() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "lookdev"));

    static void LookdevNote(string file, string line) {
      Directory.CreateDirectory(LookdevLog());
      File.AppendAllLines(Path.Combine(LookdevLog(), file), new[] { line });
      Debug.Log($"[FW-LOOKDEV] {line}");
    }

    static string L(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>AI 대 AI 2인 경기를 정해진 시드로 seconds까지 돌리고 멈춘다. 같은 시드·프레임이면 같은 장면이다.</summary>
    IEnumerator FixedScene(int seconds, bool human = false) {
      var bots = human ? new[] { BotDifficulty.Normal } : new[] { BotDifficulty.Normal, BotDifficulty.Normal };
      yield return StartMatch(Map2PScene, 2, bots: bots, seed: LookdevSeed, truce: -1);
      if (human) {
        yield return WaitForHud();
      }
      yield return WaitForBattle();
      Time.timeScale = 12f;
      yield return WaitFor(() => Verified.Number >= seconds * Fps, 900f, $"고정 장면 {seconds}초");
      QuantumRunner.Default.IsSessionUpdateDisabled = true;
      Time.timeScale = 1f;
      yield return null;
      yield return null;
    }

    /// <summary>고정 시점. A 본진, B 다리와 강, C 중앙 교전, D 맵 전체.</summary>
    static (string Name, Vector3 Focus, float Height)[] ViewPoints(MapSpec map) {
      var profile = VisualProfile.Current;
      var home = map.StartPositions[0].ToUnityVector3();
      var bridge = map.Bridges[0].Center.ToUnityVector3();
      var fight = BattleCenter();
      var whole = (map.BoundsMax.X.AsFloat - map.BoundsMin.X.AsFloat) * 0.62f / Mathf.Tan(profile.FieldOfView * 0.5f * Mathf.Deg2Rad);
      return new[] {
        ("A", home + new Vector3(8, 0, 6), profile.DefaultHeight),
        ("B", bridge + new Vector3(4, 0, 2), profile.DefaultHeight * 0.8f),
        ("C", fight, profile.DefaultHeight),
        ("D", new Vector3(0, 0, 6), whole),
      };
    }

    /// <summary>두 진영 전투 유닛이 가장 가까이 붙은 곳. 없으면 맵 중앙이다.</summary>
    static Vector3 BattleCenter() {
      var a = new List<Vector3>();
      var b = new List<Vector3>();
      foreach (var pair in Verified.GetComponentIterator<Unit>()) {
        if (Verified.FindAsset(pair.Component.Spec).Attack == AttackType.None) {
          continue;
        }
        (pair.Component.Owner == 0 ? a : b).Add(Verified.Get<Transform3D>(pair.Entity).Position.ToUnityVector3());
      }
      var best = float.MaxValue;
      var center = Vector3.zero;
      foreach (var p in a) {
        foreach (var q in b) {
          var d = (p - q).sqrMagnitude;
          if (d < best) {
            best = d;
            center = (p + q) / 2;
          }
        }
      }
      center.y = 0;
      return center;
    }

    static void Aim(Camera camera, Vector3 focus, float height) {
      var profile = VisualProfile.Current;
      camera.orthographic = false;
      camera.fieldOfView = profile.FieldOfView;
      camera.nearClipPlane = 0.3f;
      camera.farClipPlane = 1500f;
      var back = height / Mathf.Tan(profile.Pitch * Mathf.Deg2Rad);
      camera.transform.SetPositionAndRotation(focus + new Vector3(0, height, -back), Quaternion.Euler(profile.Pitch, 0, 0));
    }

    /// <summary>HDR 단계는 반정밀 대상에 그린다. 결과는 sRGB 8비트 텍스처다.</summary>
    static Texture2D Shoot(Camera camera, int width, int height) {
      var hdr = ((UniversalRenderPipelineAsset)QualitySettings.renderPipeline).supportsHDR;
      var surface = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, 24));
      var previous = camera.targetTexture;
      camera.targetTexture = surface;
      camera.Render();
      RenderTexture.active = surface;
      var read = new Texture2D(width, height, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr);
      read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
      read.Apply();
      RenderTexture.active = null;
      camera.targetTexture = previous;
      RenderTexture.ReleaseTemporary(surface);
      var output = new Texture2D(width, height, TextureFormat.RGB24, false);
      if (hdr) {
        output.SetPixels(read.GetPixels());
      } else {
        output.SetPixels32(read.GetPixels32());
      }
      output.Apply();
      Object.Destroy(read);
      return output;
    }

    /// <summary>카메라에 보이는 렌더러 수·삼각형 수·그림자 캐스터 수. 그리기 호출은 이것으로 추정한다.</summary>
    static (int Renderers, int Triangles, int Casters) Visible(Camera camera, int width, int height) {
      var surface = RenderTexture.GetTemporary(width, height, 24);
      var previous = camera.targetTexture;
      camera.targetTexture = surface;
      var planes = GeometryUtility.CalculateFrustumPlanes(camera);
      camera.targetTexture = previous;
      RenderTexture.ReleaseTemporary(surface);
      int renderers = 0, triangles = 0, casters = 0;
      foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) {
        if (renderer.enabled == false || renderer.gameObject.activeInHierarchy == false || GeometryUtility.TestPlanesAABB(planes, renderer.bounds) == false) {
          continue;
        }
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) {
          continue;
        }
        renderers += filter.sharedMesh.subMeshCount;
        triangles += (int)(filter.sharedMesh.GetIndexCount(0) / 3);
        casters += renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off ? 1 : 0;
      }
      foreach (var renderer in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None)) {
        renderers += renderer.isVisible ? 1 : 0;
      }
      return (renderers, triangles, casters);
    }

    /// <summary>그리기 호출 추정: 보이는 렌더러 + 그림자 캐스터 × 분할 수 + 후처리 패스.</summary>
    static int EstimateDrawCalls((int Renderers, int Triangles, int Casters) seen) {
      var asset = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
      var post = GraphicsQuality.PostProcessing ? 4 + (VisualProfile.Current.Ssao && GraphicsQuality.Current != QualityTier.Low ? 3 : 0) : 0;
      return seen.Renderers + seen.Casters * Mathf.Max(1, asset.shadowCascadeCount) + post;
    }

    /// <summary>전차 한 대의 화면 가로 폭(픽셀).</summary>
    static float TankPixels(Camera camera, int width, int height) {
      var surface = RenderTexture.GetTemporary(width, height, 24);
      var previous = camera.targetTexture;
      camera.targetTexture = surface;
      var best = 0f;
      foreach (var actor in Battle().Actors.Values) {
        if (actor.View == null || actor.View.Entry.Id.Contains("tank") == false || actor.View.gameObject.activeInHierarchy == false) {
          continue;
        }
        var bounds = new Bounds(actor.View.transform.position, Vector3.zero);
        foreach (var renderer in actor.View.Model.GetComponentsInChildren<Renderer>()) {
          bounds.Encapsulate(renderer.bounds);
        }
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var i = 0; i < 8; i++) {
          var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
          var screen = camera.WorldToScreenPoint(corner);
          min = Mathf.Min(min, screen.x);
          max = Mathf.Max(max, screen.x);
        }
        best = Mathf.Max(best, max - min);
      }
      camera.targetTexture = previous;
      RenderTexture.ReleaseTemporary(surface);
      return best;
    }

    /// <summary>화면 가운데 줄에서 보이는 땅의 가로 폭.</summary>
    static float GroundWidth(Camera camera, int width, int height) {
      var surface = RenderTexture.GetTemporary(width, height, 24);
      var previous = camera.targetTexture;
      camera.targetTexture = surface;
      Vector3 Hit(float x) {
        var ray = camera.ScreenPointToRay(new Vector3(x, height * 0.5f, 0));
        return ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
      }
      var result = (Hit(0) - Hit(width)).magnitude;
      camera.targetTexture = previous;
      RenderTexture.ReleaseTemporary(surface);
      return result;
    }

    /// <summary>여러 장을 격자로 붙이고 칸마다 이름과 수치를 적어 PNG로 저장한다. UI를 오프스크린으로 그린다.</summary>
    public static void SaveSheet(string name, string title, IList<(Texture2D Image, string Label)> cells, int columns) {
      var rows = Mathf.CeilToInt(cells.Count / (float)columns);
      var width = columns * CellWidth;
      var height = rows * (CellHeight + LabelHeight) + LabelHeight;
      var root = new GameObject("LookdevSheet");
      var camera = new GameObject("SheetCamera").AddComponent<Camera>();
      camera.transform.SetParent(root.transform, false);
      camera.clearFlags = CameraClearFlags.SolidColor;
      camera.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
      camera.cullingMask = 1 << 5;
      camera.orthographic = true;
      var canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
      canvas.transform.SetParent(root.transform, false);
      canvas.gameObject.layer = 5;
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = camera;
      canvas.planeDistance = 1f;
      var surface = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
      camera.targetTexture = surface;
      var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      Text Label(string text, Vector2 position, Vector2 size, int fontSize) {
        var item = new GameObject("Label", typeof(Text)).GetComponent<Text>();
        item.transform.SetParent(canvas.transform, false);
        item.gameObject.layer = 5;
        item.font = font;
        item.fontSize = fontSize;
        item.color = Color.white;
        item.text = text;
        item.alignment = TextAnchor.MiddleLeft;
        var rect = item.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return item;
      }
      Label(title, new Vector2(16, 0), new Vector2(width - 32, LabelHeight), 30);
      for (var i = 0; i < cells.Count; i++) {
        var x = (i % columns) * CellWidth;
        var y = -LabelHeight - (i / columns) * (CellHeight + LabelHeight);
        var image = new GameObject("Cell", typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(canvas.transform, false);
        image.gameObject.layer = 5;
        image.texture = cells[i].Image;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x + 4, y - 4);
        rect.sizeDelta = new Vector2(CellWidth - 8, CellHeight - 8);
        Label(cells[i].Label, new Vector2(x + 8, y - CellHeight), new Vector2(CellWidth - 16, LabelHeight), 18);
      }
      var scaler = canvas.GetComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
      Canvas.ForceUpdateCanvases();
      camera.Render();
      RenderTexture.active = surface;
      var output = new Texture2D(width, height, TextureFormat.RGB24, false);
      output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
      output.Apply();
      RenderTexture.active = null;
      Directory.CreateDirectory(LookdevFolder());
      File.WriteAllBytes(Path.Combine(LookdevFolder(), name + ".png"), output.EncodeToPNG());
      Object.Destroy(output);
      camera.targetTexture = null;
      surface.Release();
      Object.Destroy(root);
      Debug.Log($"[FW-LOOKDEV] sheet {name}");
    }

    /// <summary>
    /// 항목 19 0단계 수치: 기본 카메라의 땅 폭, 전차 픽셀 폭, 품질별 그리기 호출·삼각형. 이름표 tag로 before/after를 가른다.
    /// </summary>
    IEnumerator MeasureLook(string tag) {
      yield return FixedScene(450);
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var camera = Camera.main;
      var home = map.StartPositions[0].ToUnityVector3();
      var cells = new List<(Texture2D, string)>();
      foreach (var tier in new[] { QualityTier.Low, QualityTier.Medium, QualityTier.High }) {
        GraphicsQuality.Apply(tier);
        yield return null;
        foreach (var view in ViewPoints(map)) {
          Aim(camera, view.Focus, view.Height);
          var seen = Visible(camera, 1920, 1080);
          LookdevNote("measure.csv", $"{tag},{tier},{view.Name},{seen.Renderers},{seen.Triangles},{seen.Casters},{EstimateDrawCalls(seen)}");
          if (view.Name == "A") {
            cells.Add((Shoot(camera, CellWidth * 2, CellHeight * 2), $"{tag} {tier} A  draw~{EstimateDrawCalls(seen)} tris {seen.Triangles}"));
          }
        }
      }
      GraphicsQuality.Apply(QualityTier.Medium);
      Aim(camera, home + new Vector3(8, 0, 6), VisualProfile.Current.DefaultHeight);
      LookdevNote("measure.csv", $"{tag},ground_width_m,{L(GroundWidth(camera, 1920, 1080))}");
      var tank = Battle().Actors.Values.FirstOrDefault(actor => actor.View != null && actor.View.Entry.Id.Contains("tank"));
      Assert.IsNotNull(tank, "전차가 있음: " + string.Join(",", Battle().Actors.Values.Where(a => a.View != null).Select(a => a.View.Entry.Id).Distinct()));
      Aim(camera, tank.View.transform.position, VisualProfile.Current.DefaultHeight);
      LookdevNote("measure.csv", $"{tag},tank_px,{L(TankPixels(camera, 1920, 1080))},{tank.View.Entry.Id}");
      SaveSheet($"measure_{tag}", $"{tag}: 품질별 본진 시점", cells, 3);
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(3000000)]
    public IEnumerator Lookdev_Measure() {
      yield return MeasureLook(Environment.GetEnvironmentVariable("FW_LOOK_TAG") ?? "now");
    }
  }
}
