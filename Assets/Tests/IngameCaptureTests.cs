namespace FactoryWars.Tests {
  using System.Collections;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering.Universal;
  using UnityEngine.TestTools;

  /// <summary>실제 AI 대 AI 경기 화면을 저장한다. GPU가 있는 배치 모드에서 -testCategory Ingame으로 돌린다.</summary>
  public partial class LocalMatchTests {
    const float CaptureSpeed = 10f;
    const float CameraPitch = 50f;

    static string IngameFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "ingame"));

    static void AimCamera(Camera camera, Vector3 focus, float height) {
      camera.orthographic = false;
      camera.fieldOfView = 60f;
      camera.nearClipPlane = 0.3f;
      camera.farClipPlane = 900f;
      var back = height / Mathf.Tan(CameraPitch * Mathf.Deg2Rad);
      camera.transform.SetPositionAndRotation(focus + new Vector3(0, height, -back), Quaternion.Euler(CameraPitch, 0, 0));
    }

    /// <summary>내부 색 버퍼는 대상 형식을 따르므로 HDR 단계는 반정밀 대상에 그린다.</summary>
    static void Capture(Camera camera, string name) {
      const int Width = 1920;
      const int Height = 1080;
      var hdr = ((UniversalRenderPipelineAsset)QualitySettings.renderPipeline).supportsHDR;
      var surface = RenderTexture.GetTemporary(new RenderTextureDescriptor(Width, Height, hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, 24) { sRGB = hdr == false });
      var previous = camera.targetTexture;
      camera.targetTexture = surface;
      camera.Render();
      RenderTexture.active = surface;
      var read = new Texture2D(Width, Height, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr);
      read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
      read.Apply();
      RenderTexture.active = null;
      camera.targetTexture = previous;
      RenderTexture.ReleaseTemporary(surface);
      var output = new Texture2D(Width, Height, TextureFormat.RGB24, false);
      if (hdr) {
        output.SetPixels(read.GetPixels());
      } else {
        output.SetPixels32(read.GetPixels32());
      }
      output.Apply();
      var path = Path.Combine(IngameFolder(), name);
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllBytes(path, output.EncodeToPNG());
      Object.Destroy(read);
      Object.Destroy(output);
      Debug.Log($"[FW-CAPTURE] {name} frame {Verified.Number}");
    }

    static IEnumerator WaitGameSeconds(int seconds) {
      yield return WaitFor(() => Verified.Number >= seconds * Fps, 900f, $"경기 {seconds}초");
    }

    static Vector3 Center(Vector3[] points) {
      var sum = Vector3.zero;
      foreach (var point in points) {
        sum += point;
      }
      return sum / points.Length;
    }

    [UnityTest, Explicit, Category("Ingame"), Timeout(3000000)]
    public IEnumerator Ingame_TwoPlayer() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal }, seed: 7003, truce: -1);
      GraphicsQuality.Apply(QualityTier.Medium);
      Time.timeScale = CaptureSpeed;
      yield return WaitForBattle();
      var camera = Camera.main;
      var map = MapAsset("Map2P");

      Time.timeScale = 1f;
      AimCamera(camera, Vector3.zero, 165f);
      yield return new WaitForSecondsRealtime(0.2f);
      Capture(camera, "2p_river.png");
      Time.timeScale = CaptureSpeed;
      yield return WaitGameSeconds(180);
      var home = map.StartPositions[0].ToUnityVector3();
      AimCamera(camera, home + new Vector3(-6, 0, 2), 45f);
      Time.timeScale = 1f;
      yield return new WaitForSecondsRealtime(0.5f);
      Capture(camera, "2p_0300_gather.png");

      Time.timeScale = CaptureSpeed;
      yield return WaitGameSeconds(415);
      Time.timeScale = 1f;
      var bridge = map.Bridges[0].Center.ToUnityVector3();
      System.Func<bool> frameLit = () => Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Any(r => r.name == "Lamps" && r.enabled);
      AimCamera(camera, bridge, 22f);
      yield return new WaitForSecondsRealtime(0.1f);
      Capture(camera, "bridge_close.png");
      AimCamera(camera, bridge, 34f);
      yield return WaitFor(() => frameLit(), 5f, "경고등 켜짐");
      Capture(camera, "2p_0655_blink.png");
      yield return WaitFor(BarrierOpen, 60f, "휴전 종료");
      yield return new WaitForSecondsRealtime(0.8f);
      Capture(camera, "2p_0700_barrier.png");

      // 교전: 여러 유닛이 동시에 쏘는 순간을 찾는다.
      Time.timeScale = CaptureSpeed;
      var deadline = Time.realtimeSinceStartup + 300f;
      var firing = new Vector3[0];
      while (Time.realtimeSinceStartup < deadline && firing.Length < 5) {
        firing = Battle().Actors.Values.Where(a => a.IsBuilding == false && a.Recoil > 0f).Select(a => a.Position).ToArray();
        yield return null;
      }
      Assert.GreaterOrEqual(firing.Length, 1, "교전을 찾지 못함");
      Time.timeScale = 1f;
      var fight = Center(firing);
      AimCamera(camera, fight, 45f);
      yield return new WaitForSecondsRealtime(0.25f);
      Capture(camera, "2p_battle.png");
      GraphicsQuality.Apply(QualityTier.Low);
      yield return null;
      yield return new WaitForSecondsRealtime(0.25f);
      Capture(camera, "2p_battle_low.png");
      GraphicsQuality.Apply(QualityTier.Medium);

      // 겹침 확인: 교전 지점의 표시 크기 기준 겹침 비율을 기록한다.
      var actors = Battle().Actors.Values.Where(a => a.IsBuilding == false && a.IsAir == false).ToArray();
      var pairs = 0;
      var overlapped = 0;
      for (var i = 0; i < actors.Length; i++) {
        for (var j = i + 1; j < actors.Length; j++) {
          var distance = (actors[i].Position - actors[j].Position).magnitude;
          if (distance < 12f) {
            pairs++;
            overlapped += distance < (actors[i].Radius + actors[j].Radius) * 0.7f ? 1 : 0;
          }
        }
      }
      Debug.Log($"[FW-CAPTURE] 겹침 {overlapped}/{pairs} 쌍, 지상 유닛 {actors.Length}");
    }

    [UnityTest, Explicit, Category("Ingame"), Timeout(3000000)]
    public IEnumerator Ingame_FourPlayerOverview() {
      yield return StartMatch(Map4PScene, 4, bots: Enumerable.Repeat(BotDifficulty.Normal, 4).ToArray(), seed: 7004, truce: -1);
      GraphicsQuality.Apply(QualityTier.Medium);
      Time.timeScale = CaptureSpeed;
      yield return WaitForBattle();
      yield return WaitGameSeconds(480);
      Time.timeScale = 1f;
      var camera = Camera.main;
      AimCamera(camera, new Vector3(0, 0, 4), 250f);
      yield return new WaitForSecondsRealtime(0.25f);
      Capture(camera, "4p_river.png");
    }

    /// <summary>미니맵은 UI 전체가 아니라 그 텍스처를 저장한다.</summary>
    static void SaveMinimap(string name) {
      var image = Object.FindAnyObjectByType<FactoryWars.View.Minimap>().GetComponent<UnityEngine.UI.RawImage>();
      var texture = (Texture2D)image.texture;
      File.WriteAllBytes(Path.Combine(IngameFolder(), name), texture.EncodeToPNG());
    }

    IEnumerator ShootMap(Vector3[] shots, float[] heights, string[] names) {
      var camera = Camera.main;
      for (var i = 0; i < names.Length; i++) {
        AimCamera(camera, shots[i], heights[i]);
        yield return new WaitForSecondsRealtime(0.25f);
        Capture(camera, $"map17/{names[i]}.png");
      }
    }

    [UnityTest, Explicit, Category("Ingame"), Timeout(3000000)]
    public IEnumerator Ingame_Map17() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 7010, truce: -1);
      yield return WaitForHud();
      yield return WaitForBattle();
      GraphicsQuality.Apply(QualityTier.Medium);
      Time.timeScale = CaptureSpeed;
      yield return WaitGameSeconds(90);
      Time.timeScale = 1f;
      Directory.CreateDirectory(Path.Combine(IngameFolder(), "map17"));
      SaveMinimap("map17/2p_minimap.png");
      yield return ShootMap(
        new[] { new Vector3(0, 0, 8), new Vector3(0, 0, -4), new Vector3(-107, 0, 50), new Vector3(-44, 0, 22) },
        new[] { 225f, 60f, 45f, 45f },
        new[] { "2p_overview", "2p_center", "2p_canyon", "2p_backdoor" });
    }

    [UnityTest, Explicit, Category("Ingame"), Timeout(3000000)]
    public IEnumerator Ingame_Map17FourPlayer() {
      yield return StartMatch(Map4PScene, 4, bots: Enumerable.Repeat(BotDifficulty.Normal, 3).ToArray(), seed: 7011, truce: -1);
      yield return WaitForHud();
      yield return WaitForBattle();
      GraphicsQuality.Apply(QualityTier.Medium);
      Time.timeScale = CaptureSpeed;
      yield return WaitGameSeconds(90);
      Time.timeScale = 1f;
      Directory.CreateDirectory(Path.Combine(IngameFolder(), "map17"));
      SaveMinimap("map17/4p_minimap.png");
      yield return ShootMap(
        new[] { new Vector3(0, 0, 8), new Vector3(0, 0, -4), new Vector3(-147, 0, -14), new Vector3(-72, 0, -8) },
        new[] { 300f, 70f, 45f, 45f },
        new[] { "4p_overview", "4p_center", "4p_canyon", "4p_backdoor" });
    }

    [UnityTest, Explicit, Category("Ingame"), Timeout(3000000)]
    public IEnumerator Ingame_Hud() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 7005, truce: -1);
      yield return WaitForHud();
      yield return WaitForBattle();
      GraphicsQuality.Apply(QualityTier.Medium);
      yield return WaitFrames(5 * Fps);
      Controller().Input.Selection.SelectUnit(Engineers(0)[0]);
      Controller().Hud.Refresh(Controller().Frame);
      yield return null;
      var canvas = Controller().Hud.Root.GetComponentInParent<Canvas>();
      var camera = Camera.main;
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = camera;
      canvas.planeDistance = 2f;
      yield return null;
      Capture(camera, "hud.png");
    }
  }
}
