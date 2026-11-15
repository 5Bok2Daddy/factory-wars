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
    }
  }
}
