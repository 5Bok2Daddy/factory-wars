namespace FactoryWars.Tests {
  using System.Collections;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>항목 21 화면. screenshots/ingame/city21에 저장한다. GPU 배치 모드에서 -testCategory Ingame으로 돌린다.</summary>
  public unsafe partial class LocalMatchTests {
    static int FirstCity(MapSpec spec, System.Func<CityBuildingPlacement, bool> match, Vector3 near) {
      var best = -1;
      var bestDistance = float.MaxValue;
      for (var i = 0; i < spec.CityBuildings.Length; i++) {
        var distance = (spec.CityBuildings[i].Shape.Center.ToUnityVector3() - near).sqrMagnitude;
        if (match(spec.CityBuildings[i]) && distance < bestDistance) {
          best = i;
          bestDistance = distance;
        }
      }
      return best;
    }

    [UnityTest, Explicit, Category("Ingame"), Timeout(3000000)]
    public IEnumerator Ingame_City21Overview() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 7030, truce: -1);
      yield return WaitForHud();
      yield return WaitForBattle();
      GraphicsQuality.Apply(QualityTier.Medium);
      Time.timeScale = CaptureSpeed;
      yield return WaitGameSeconds(120);
      Time.timeScale = 1f;
      Directory.CreateDirectory(Path.Combine(IngameFolder(), "city21"));
      SaveMinimap("city21/2p_minimap.png");
      var camera = Camera.main;
      AimCamera(camera, new Vector3(0, 0, 8), 225f);
      yield return new WaitForSecondsRealtime(0.25f);
      Capture(camera, "city21/2p_overview.png");
      AimCamera(camera, new Vector3(0, 0, -4), 60f);
      yield return new WaitForSecondsRealtime(0.25f);
      Capture(camera, "city21/2p_center.png");
      // 경계: 최대로 줌 아웃한 게임 카메라를 맵 모서리로 보낸다.
      var rts = camera.GetComponent<RtsCamera>();
      if (rts != null) {
        rts.Height = 999f;
        rts.Zoom(1f);
        rts.MoveTo(new Vector2(-200, -200));
        yield return new WaitForSecondsRealtime(0.25f);
        Capture(camera, "city21/2p_boundary.png");
      }
    }

    [UnityTest, Explicit, Category("Ingame"), Timeout(3000000)]
    public IEnumerator Ingame_City21FourPlayer() {
      yield return StartMatch(Map4PScene, 4, bots: Enumerable.Repeat(BotDifficulty.Normal, 3).ToArray(), seed: 7031, truce: -1);
      yield return WaitForHud();
      yield return WaitForBattle();
      GraphicsQuality.Apply(QualityTier.Medium);
      yield return WaitFrames(Fps);
      var camera = Camera.main;
      AimCamera(camera, new Vector3(0, 0, 8), 300f);
      yield return new WaitForSecondsRealtime(0.25f);
      Capture(camera, "city21/4p_overview.png");
      SaveMinimap("city21/4p_minimap.png");
    }

    /// <summary>진지 교전과 쓰러짐 전·중·후. 실제 맵에 유닛을 놓고 연출을 확인한다.</summary>
    [UnityTest, Explicit, Category("Ingame"), Timeout(3000000)]
    public IEnumerator Ingame_City21Collapse() {
      var spec0 = Resources.Load<MapSpec>("FactoryWars/map_Map2P");
      var garrison = FirstCity(spec0, c => c.Garrison, Vector3.zero);
      var tall = FirstCity(spec0, c => c.Topples, Vector3.zero);
      var g = spec0.CityBuildings[garrison].Shape.Center.ToUnityVector3();
      var t = spec0.CityBuildings[tall].Shape.Center.ToUnityVector3();
      var gz = spec0.CityBuildings[garrison].Shape.Approach(spec0.CityBuildings[garrison].Shape.Center + new FPVector3(0, 0, -20), FP._2).ToUnityVector3();
      var units = new[] {
        (0, "ff_ranger_squad", (int)gz.x, (int)gz.z), (0, "ff_ranger_squad", (int)gz.x + 2, (int)gz.z),
        (1, "cf_militia", (int)g.x, (int)g.z - 14), (1, "cf_militia", (int)g.x + 3, (int)g.z - 15), (1, "cf_flame_squad", (int)g.x - 3, (int)g.z - 14),
      }.Concat(Enumerable.Range(0, 6).Select(i => (0, "cf_longwall_mlrs", (int)t.x - 10 + i * 4, (int)t.z - 22))).ToArray();
      yield return StartMatch(Map2PScene, 2, units: units, bots: new[] { BotDifficulty.Easy }, truce: 0);
      yield return WaitForBattle();
      GraphicsQuality.Apply(QualityTier.Medium);
      Directory.CreateDirectory(Path.Combine(IngameFolder(), "city21"));
      var camera = Camera.main;
      var rangers = UnitsNamed(0, "ff_ranger_squad").ToArray();
      yield return Send(0, new EnterGarrisonCommand { Units = rangers, Building = CityAt(garrison) });
      yield return WaitFor(() => rangers.Any(r => Verified.Exists(r) && GarrisonOf(r) != EntityRef.None), 30f, "진입");
      yield return WaitFrames(3 * Fps);
      AimCamera(camera, g + new Vector3(0, 0, -6), 34f);
      yield return new WaitForSecondsRealtime(0.25f);
      Capture(camera, "city21/garrison_fight.png");

      AimCamera(camera, t + new Vector3(0, 0, -4), 42f);
      yield return new WaitForSecondsRealtime(0.25f);
      Capture(camera, "city21/topple_before.png");
      var mlrs = UnitsNamed(0, "cf_longwall_mlrs").ToArray();
      yield return Send(0, new AttackTargetCommand { Units = mlrs, Target = CityAt(tall) });
      Time.timeScale = 6f;
      yield return WaitFor(() => CitySystem.IsCollapsed(Verified, tall), 120f, "쓰러짐");
      Time.timeScale = 1f;
      yield return new WaitForSecondsRealtime(0.6f);
      Capture(camera, "city21/topple_during.png");
      yield return new WaitForSecondsRealtime(2.5f);
      Capture(camera, "city21/topple_after.png");
      Assert.Greater(Battle().City.RubbleCount, 0);
    }
  }
}
