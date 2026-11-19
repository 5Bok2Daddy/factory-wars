namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>전투 가독성 캡처: 붉은 공격선·조준 고리, 예광·포탄 궤적, 포구 섬광, 폭발 그을음, 피해 입은 유닛만 체력바.</summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest, Explicit, Category("CaptureUrban")]
    public IEnumerator CaptureCombat_LinesTracersScorch() {
      var units = new[] {
        (0, "ff_eagle_tank", -8, 10), (0, "ff_eagle_tank", -2, 10), (0, "ff_ranger_squad", 4, 10), (0, "ff_ranger_squad", 8, 10), (0, "ff_skyguard_squad", 12, 10),
        (1, "cf_dragonscale_tank", -6, 26), (1, "cf_militia", 0, 26), (1, "cf_militia", 6, 26), (1, "cf_flame_squad", 10, 26), (1, "cf_gale_gunship", 2, 30),
      };
      yield return StartMatch(units: units);
      yield return WaitForHud();
      Time.timeScale = 1f;
      var camera = Controller().Camera;
      camera.MoveTo(new Vector2(2f, 18f));
      camera.Height = 28f;
      camera.Apply();
      foreach (var unit in UnitsNamed(0, "ff_eagle_tank").Concat(UnitsNamed(0, "ff_ranger_squad"))) {
        Controller().Input.Selection.SelectUnit(unit);
      }
      Controller().Input.Selection.Clear();
      var mine = UnitsNamed(0, "ff_eagle_tank").Concat(UnitsNamed(0, "ff_ranger_squad")).Where(e => At(e).Z.AsFloat > 0f).ToList();
      foreach (var unit in mine) {
        Controller().Input.Selection.Units.Add(unit);
      }
      yield return Send(0, new Quantum.FactoryWars.MoveUnitsCommand { Units = mine.ToArray(), Target = V(2, 26), AttackMove = true });
      yield return Send(1, new Quantum.FactoryWars.MoveUnitsCommand { Units = UnitsNamed(1, "cf_militia").Concat(UnitsNamed(1, "cf_dragonscale_tank")).Where(e => At(e).Z.AsFloat > 20f).ToArray(), Target = V(2, 10), AttackMove = true });
      yield return WaitFor(() => Battle().AttackLines > 0, 30f, "공격선");
      // 몇 초 교전해서 폭발·그을음·피해 체력바가 생기게 한다.
      for (var i = 0; i < 12; i++) {
        yield return new WaitForSeconds(0.25f);
        if (i == 11 || i == 6) {
          ShootUrban(i == 6 ? "combat_mid" : "combat_lines");
        }
      }
      Assert.Greater(Battle().AttackLines, 0, "붉은 공격선");
      Assert.Greater(Battle().RifleRigs, 0, "분대원이 소총을 든다");
      // 가까이: 소총 조준 자세·총구 섬광·가는 예광탄.
      // 교전 중인 쌍: 내 소총 분대와 적 민병 중 서로 가장 가까운 둘의 가운데를 비춘다.
      var rangers = UnitsNamed(0, "ff_ranger_squad").Select(e => At(e).ToUnityVector3()).ToList();
      var foes = UnitsNamed(1, "cf_militia").Concat(UnitsNamed(1, "cf_flame_squad")).Select(e => At(e).ToUnityVector3()).ToList();
      var middle = rangers.Count > 0 ? rangers[0] : Vector3.zero;
      var bestGap = float.MaxValue;
      foreach (var a in rangers) {
        foreach (var b in foes) {
          if ((a - b).sqrMagnitude < bestGap) {
            bestGap = (a - b).sqrMagnitude;
            middle = (a + b) * 0.5f;
          }
        }
      }
      camera.MoveTo(new Vector2(middle.x, middle.z));
      camera.Height = 15f;
      camera.Apply();
      var aimed = false;
      for (var i = 0; i < 16; i++) {
        yield return new WaitForSeconds(0.12f);
        aimed |= Battle().AimingRigs > 0;
        if (i == 9 || i == 15) {
          ShootUrban(i == 9 ? "combat_closeup" : "combat_closeup2");
        }
      }
      Assert.IsTrue(aimed, "조준 자세");
      // 전후 MVP 카드 및 타임라인 그래프 결과 모달 캡처.
      if (FactoryWars.View.MatchFlow.Current != null) {
        FactoryWars.View.MatchFlow.Current.ShowResult();
        yield return null;
        yield return ShootHud("post_match_mvp_timeline");
        var statusDir = System.IO.Path.Combine(Application.dataPath, "..", "Docs", "status_images");
        System.IO.Directory.CreateDirectory(statusDir);
        var src = System.IO.Path.Combine(UrbanFolder(), "post_match_mvp_timeline.png");
        if (System.IO.File.Exists(src)) {
          System.IO.File.Copy(src, System.IO.Path.Combine(statusDir, "46_post_match_mvp_timeline.png"), true);
        }
      }
    }

    [UnityTest]
    public IEnumerator Formation_AutoAlignSpreadAndAntiClumping() {
      var units = new[] {
        (0, "ff_eagle_tank", 0, 10), (0, "ff_eagle_tank", 0, 10),
        (0, "ff_ranger_squad", 0, 10), (0, "ff_ranger_squad", 0, 10), (0, "ff_ranger_squad", 0, 10),
        (0, "ff_sniper", 0, 10),
      };
      yield return StartMatch(units: units, stock: Stock(5000, 5000, 2000, 500));
      var mine = UnitsNamed(0, "ff_eagle_tank").Concat(UnitsNamed(0, "ff_ranger_squad")).Concat(UnitsNamed(0, "ff_sniper")).ToArray();
      // 1) 동일 좌표에 스폰된 유닛들이 결정론적 소프트 분리로 서로 떨어져 뭉침이 해소된다.
      yield return WaitFrames(Fps * 2);
      var minPair = float.MaxValue;
      for (var i = 0; i < mine.Length; i++) {
        for (var j = i + 1; j < mine.Length; j++) {
          var d = (At(mine[i]).ToUnityVector3() - At(mine[j]).ToUnityVector3()).magnitude;
          if (d < minPair) {
            minPair = d;
          }
        }
      }
      Assert.Greater(minPair, 0.9f, "소프트 분리로 유닛 겹침 해소");

      // 2) 다중 선택 이동 시 각 유닛이 서로 다른 목표 슬롯(전차 앞열 · 소총수 중간 · 저격수 뒷열)을 받는다.
      yield return Send(0, new Quantum.FactoryWars.MoveUnitsCommand { Units = mine, Target = V(0, 26), AttackMove = true });
      yield return WaitFrames(CommandFrames + 2);
      var tankZ = UnitsNamed(0, "ff_eagle_tank").Average(e => Verified.Get<Unit>(e).TargetPosition.Z.AsFloat);
      var rangerZ = UnitsNamed(0, "ff_ranger_squad").Average(e => Verified.Get<Unit>(e).TargetPosition.Z.AsFloat);
      var sniperZ = UnitsNamed(0, "ff_sniper").Average(e => Verified.Get<Unit>(e).TargetPosition.Z.AsFloat);
      Assert.Greater(tankZ, rangerZ + 0.5f, "전차가 소총수보다 앞열");
      Assert.Greater(rangerZ, sniperZ + 0.5f, "소총수가 저격수보다 앞열");

      // 3) 산개(Spread) 명령 시 중심으로부터 방사형으로 퍼진다.
      yield return Send(0, new Quantum.FactoryWars.MoveUnitsCommand { Units = mine, FormationMode = Quantum.FactoryWars.MoveUnitsCommand.ModeSpread });
      yield return WaitFrames(CommandFrames + 2);
      var distinctTargets = mine.Select(e => Verified.Get<Unit>(e).TargetPosition).Distinct().Count();
      Assert.GreaterOrEqual(distinctTargets, mine.Length - 1, "산개 시 각기 다른 외곽 목표 지정");
    }

    [UnityTest, Explicit, Category("CaptureUrban")]
    public IEnumerator CaptureIndividualFormationAndThumbHud() {
      var units = new[] {
        (0, "ff_eagle_tank", -2, 8), (0, "ff_eagle_tank", 2, 8),
        (0, "ff_ranger_squad", -4, 6), (0, "ff_ranger_squad", -2, 6), (0, "ff_ranger_squad", 0, 6), (0, "ff_ranger_squad", 2, 6), (0, "ff_ranger_squad", 4, 6),
        (0, "ff_sniper", -1, 4), (0, "ff_sniper", 1, 4),
        (1, "cf_militia", -3, 24), (1, "cf_militia", 0, 24), (1, "cf_militia", 3, 24), (1, "cf_flame_squad", 1, 26),
      };
      yield return StartMatch(units: units, stock: Stock(5000, 5000, 2000, 500));
      yield return WaitForHud();
      yield return Send(0, new Quantum.FactoryWars.ChooseDoctrineCommand { Doctrine = 1 });
      Time.timeScale = 1f;
      var mine = UnitsNamed(0, "ff_eagle_tank").Concat(UnitsNamed(0, "ff_ranger_squad")).Concat(UnitsNamed(0, "ff_sniper")).ToArray();
      Controller().Input.Selection.Clear();
      foreach (var u in mine) {
        Controller().Input.Selection.Units.Add(u);
      }
      // 전술 자동 정렬(대형 정렬) 후 전진 교전.
      yield return Send(0, new Quantum.FactoryWars.MoveUnitsCommand { Units = mine, Target = V(0, 14), AttackMove = true });
      var cam = Controller().Camera;
      cam.MoveTo(new Vector2(0f, 14f));
      cam.Height = 20f;
      cam.Apply();
      for (var i = 0; i < 18; i++) {
        yield return new WaitForSeconds(0.15f);
      }
      var statusDir = System.IO.Path.Combine(Application.dataPath, "..", "Docs", "status_images");
      System.IO.Directory.CreateDirectory(statusDir);
      ShootUrban("individual_units_formation");
      var src1 = System.IO.Path.Combine(UrbanFolder(), "individual_units_formation.png");
      if (System.IO.File.Exists(src1)) {
        System.IO.File.Copy(src1, System.IO.Path.Combine(statusDir, "55_individual_units_formation.png"), true);
      }
      Controller().Hud.Refresh(Controller().Frame);
      yield return null;
      yield return ShootHud("mobile_thumb_hud");
      var src2 = System.IO.Path.Combine(UrbanFolder(), "mobile_thumb_hud.png");
      if (System.IO.File.Exists(src2)) {
        System.IO.File.Copy(src2, System.IO.Path.Combine(statusDir, "56_mobile_thumb_hud.png"), true);
      }
    }
  }
}
