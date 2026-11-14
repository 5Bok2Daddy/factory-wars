namespace FactoryWars.Tests {
  using System.Collections;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 34 전/후 캡처(GPU 필요, -testCategory CaptureFog): 안개 끔(전) / 켬(후), 같은 장면·같은 카메라.
  /// 보급 경고 35초·10초·착지 장면도 같은 방식으로 남긴다. 결과: screenshots/fog-sniper-sortie/*.png (해시가 서로 달라야 한다).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static string FogFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "fog-sniper-sortie"));

    static void ShootFog(string name) {
      var camera = Camera.main;
      var surface = new RenderTexture(1920, 1080, 24);
      camera.targetTexture = surface;
      camera.Render();
      RenderTexture.active = surface;
      var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
      image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
      image.Apply();
      RenderTexture.active = null;
      var path = Path.Combine(FogFolder(), name + ".png");
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllBytes(path, image.EncodeToPNG());
      Object.Destroy(image);
      camera.targetTexture = null;
      surface.Release();
    }

    [UnityTest, Explicit, Category("CaptureFog"), Timeout(1800000)]
    public IEnumerator CaptureFog_BeforeAfter() {
      Directory.CreateDirectory(FogFolder());
      foreach (var fogOn in new[] { false, true }) {
        TearDown();
        yield return null;
        if (fogOn) {
          UseFog();
        }
        yield return StartMatch(Map4PScene, 2, units: new[] { (0, "ff_ranger_squad", 0, 6), (0, "ff_eagle_tank", 6, 10), (1, "cf_militia", 0, -30), (1, "cf_dragonscale_tank", 6, -34) }, truce: -1);
        yield return WaitForHud();
        yield return WaitForBattle();
        Time.timeScale = 1f;
        var camera = Controller().Camera;
        camera.MoveTo(new Vector2(0f, -4f));
        camera.Height = 90f;
        camera.Apply();
        yield return WaitFrames(Fps);
        yield return null;
        ShootFog(fogOn ? "fog_after" : "fog_before");
        if (fogOn) {
          // 이동하면서 걷힌 안개와 남는 탐색 흔적.
          yield return Send(0, new MoveUnitsCommand { Units = UnitsNamed(0, "ff_ranger_squad").ToArray(), Target = V(0, -14) });
          yield return WaitFrames(Fps * 8);
          ShootFog("fog_after_moved");
        }
      }
      Assert.AreNotEqual(Sha(Path.Combine(FogFolder(), "fog_before.png")), Sha(Path.Combine(FogFolder(), "fog_after.png")), "전/후 캡처가 같음");
    }

    /// <summary>보급 경고 단계별 캡처: 35초 전(구역 원 이벤트)·10초 전·착지(신호탄). 장면은 같은 위치에서 안개를 켜고 찍는다.</summary>
    [UnityTest, Explicit, Category("CaptureFog"), Timeout(1800000)]
    public IEnumerator CaptureFog_DropStages() {
      Directory.CreateDirectory(FogFolder());
      UseFog();
      FastDrops(60f);
      var landed = FPVector3.Zero;
      var zone = FPVector3.Zero;
      var closing = FPVector3.Zero;
      yield return StartMatch(Map4PScene, 2, units: new[] { (0, "ff_ranger_squad", 0, 6) }, truce: 0);
      yield return WaitForHud();
      yield return WaitForBattle();
      Time.timeScale = 1f;
      QuantumEvent.Subscribe<EventSupplyDropStage>(_listener, e => {
        if (e.Phase == DropPhase.Zone && zone == FPVector3.Zero) {
          zone = e.Position;
        } else if (e.Phase == DropPhase.Closing && closing == FPVector3.Zero) {
          closing = e.Position;
        } else if (e.Phase == DropPhase.Landed) {
          landed = e.Position;
        }
      });
      var camera = Controller().Camera;
      yield return WaitFor(() => zone != FPVector3.Zero, 120f, "구역 공개(35초 전)");
      camera.MoveTo(new Vector2(zone.X.AsFloat, zone.Z.AsFloat));
      camera.Height = 70f;
      camera.Apply();
      yield return WaitFrames(Fps / 2);
      ShootFog("drop_35s_zone");
      yield return WaitFor(() => closing != FPVector3.Zero, 60f, "좁아짐(10초 전)");
      camera.MoveTo(new Vector2(closing.X.AsFloat, closing.Z.AsFloat));
      yield return WaitFrames(Fps / 2);
      ShootFog("drop_10s_closing");
      yield return WaitFor(() => landed != FPVector3.Zero, 60f, "착지");
      camera.MoveTo(new Vector2(landed.X.AsFloat, landed.Z.AsFloat));
      yield return WaitFrames(Fps);
      yield return null;
      ShootFog("drop_landed_flare");
      var hashes = new[] { "drop_35s_zone", "drop_10s_closing", "drop_landed_flare" }.Select(n => Sha(Path.Combine(FogFolder(), n + ".png"))).ToArray();
      Assert.AreEqual(3, hashes.Distinct().Count(), "세 장이 서로 다르다");
    }
  }
}
