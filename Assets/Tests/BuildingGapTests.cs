namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>항목 19 5-b: 건물끼리 BuildingGap(2m)만큼 떨어져야 짓는다. 수치는 GameRulesSpec 데이터에만 있다.</summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest]
    public IEnumerator Build_KeepsGapBetweenBuildings() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var rules = FactoryWarsUtil.Rules(Verified);
      Assert.AreEqual(2f, rules.BuildingGap.AsFloat, 0.001f, "간격은 데이터 값 2m");
      var hq = Headquarters(0);
      var hqPosition = Verified.Get<Transform3D>(hq).Position;
      var hqRadius = Verified.FindAsset(Verified.Get<Building>(hq).Spec).FootprintRadius;
      var spec = FactionOf(0).Buildings.Select(b => Verified.FindAsset(b)).First(b => b.Role != BuildingRole.Headquarters);
      var touching = 0;
      var clear = 0;
      foreach (var direction in new[] { FPVector3.Right, FPVector3.Left, FPVector3.Forward, FPVector3.Back }) {
        // 간격 안(1m)은 늘 거절, 간격 밖(2.5m)은 다른 조건(땅·구역)이 맞으면 허용.
        var inside = hqPosition + direction * (hqRadius + spec.FootprintRadius + FP._1);
        var outside = hqPosition + direction * (hqRadius + spec.FootprintRadius + (FP._2 + FP._0_50));
        touching += ProductionSystem.CanPlace(Verified, spec, inside) ? 1 : 0;
        clear += ProductionSystem.CanPlace(Verified, spec, outside) ? 1 : 0;
      }
      Assert.AreEqual(0, touching, "사령부에서 간격 2m 안에는 지을 수 없음");
      Assert.Greater(clear, 0, "간격 밖 한 방향 이상은 지을 수 있음");
    }
  }
}
