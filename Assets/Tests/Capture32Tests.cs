namespace FactoryWars.Tests {
  using System.Collections;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 32 화면 캡처(GPU 필요, -testCategory Capture32). screenshots/ui32/32_after_*.png와,
  /// 움직임 확인용 연속 프레임 screenshots/ui32/frames_*/NN.png(0.1초 간격 20장, 녹화 대신).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static string Ui32Folder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "ui32"));

    static void Shoot32(string name) {
      var camera = Camera.main;
      var surface = new RenderTexture(1920, 1080, 24);
      camera.targetTexture = surface;
      camera.Render();
      RenderTexture.active = surface;
      var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
      image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
      image.Apply();
      RenderTexture.active = null;
      var path = Path.Combine(Ui32Folder(), name + ".png");
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllBytes(path, image.EncodeToPNG());
      Object.Destroy(image);
      camera.targetTexture = null;
      surface.Release();
    }

    IEnumerator Frames32(string folder, int count) {
      for (var i = 0; i < count; i++) {
        Shoot32($"{folder}/{i:00}");
        var until = Time.realtimeSinceStartup + 0.1f;
        while (Time.realtimeSinceStartup < until) {
          yield return null;
        }
      }
    }

    [UnityTest, Explicit, Category("Capture32"), Timeout(900000)]
    public IEnumerator Capture32_CoverTanksHelicopters() {
      Directory.CreateDirectory(Ui32Folder());
      // 1. 엄폐 교전: 레인저 둘이 모래주머니 뒤로 들어가 북쪽 민병대와 싸운다.
      yield return StartMatch(buildings: new[] { (0, "ff_sandbags", 0, 30), (0, "ff_bunker", 14, 30) },
        units: new[] { (0, "ff_ranger_squad", 0, 24), (0, "ff_ranger_squad", 3, 24), (1, "cf_militia", 0, 40), (1, "cf_militia", 4, 40) });
      yield return WaitForHud();
      yield return WaitForBattle();
      Controller().Camera.MoveTo(new Vector2(2, 32));
      yield return Send(0, new MoveUnitsCommand { Units = UnitsNamed(0, "ff_ranger_squad").ToArray(), Target = V(0, 34), AttackMove = true });
      yield return WaitFor(() => StateOf(0).CoverEntries > 0, 60f, "엄폐 들어감");
      Time.timeScale = 1f;
      yield return null;
      Shoot32("32_after_cover_battle");
      yield return Frames32("frames_cover", 20);

      // 2. 전차 이동(가감속 기울기)과 헬기(선회 기울기).
      TearDown();
      yield return null;
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -10, 20), (0, "ff_bulwark_tank", -14, 24), (0, "ff_hawk_gunship", -10, 30) });
      yield return WaitForHud();
      yield return WaitForBattle();
      Time.timeScale = 1f;
      Controller().Camera.MoveTo(new Vector2(0, 25));
      var tanks = UnitsNamed(0, "ff_eagle_tank").Concat(UnitsNamed(0, "ff_bulwark_tank")).ToArray();
      var heli = UnitsNamed(0, "ff_hawk_gunship").ToArray();
      yield return Send(0, new MoveUnitsCommand { Units = tanks, Target = V(20, 20) });
      yield return Send(0, new MoveUnitsCommand { Units = heli, Target = V(20, 40) });
      yield return Frames32("frames_tank_heli", 20);
      Shoot32("32_after_tank_heli");

      // 3. 도시 전장(4인 맵 가운데).
      TearDown();
      yield return null;
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 7005, truce: 0);
      yield return WaitForHud();
      yield return WaitForBattle();
      Time.timeScale = 1f;
      Controller().Camera.MoveTo(Vector2.zero);
      yield return WaitFrames(2 * Fps);
      Shoot32("32_after_city");
      Assert.IsTrue(File.Exists(Path.Combine(Ui32Folder(), "32_after_city.png")));
    }
  }
}
