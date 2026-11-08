namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 24 맵: 외곽 수로와 수상 NavMesh. 지상 NavMesh에는 물이 없고 수상 NavMesh에는 땅이 없다.
  /// 본진 운하는 휴전 중 수문으로 막혔다가 울타리와 함께 열린다. 수로 너비는 18m 이상이다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static FPVector3 WaterPoint(float x, float z) => new FPVector3(FP.FromFloat_UNSAFE(x), FP._0, FP.FromFloat_UNSAFE(z));

    /// <summary>자리 0(남서) 운하 안쪽의 한 점: 땅 경계 안에서 본진 쪽 운하 조각 가운데.</summary>
    static FPVector3 CanalPoint(MapSpec map) {
      var h = map.BoundsMax.X.AsFloat;
      var piece = map.River.Where(p => p.Center.X.AsFloat < 0 && p.Center.Z.AsFloat < 0 && Mathf.Abs(p.Center.X.AsFloat) < h - 20 && Mathf.Abs(p.Center.Z.AsFloat) < h - 20)
        .OrderBy(p => p.Center.X.AsFloat + p.Center.Z.AsFloat).First();
      return piece.Center;
    }

    Navigation.FindPathResult.Code WaterPath(FPVector3 from, FPVector3 to) {
      var rules = FactoryWarsUtil.Rules(Verified);
      var water = Verified.Map.GetNavMesh(rules.WaterNavMeshName);
      var mask = *Verified.NavMeshRegionMask;
      return Verified.Navigation.FindPath((FrameThreadSafe)Verified, from, to, water, in mask).Result;
    }

    [UnityTest]
    public IEnumerator Naval_ChannelWaterNavMeshAndGates() {
      yield return StartMatch(Map4PScene, 4, truce: 420);
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var rules = FactoryWarsUtil.Rules(Verified);
      Assert.Greater(map.Channel.Length, 0, "외곽 수로가 있음");
      Assert.GreaterOrEqual(map.ChannelWidth.AsFloat, 18f, "수로 너비 18m 이상");
      Assert.AreEqual(8, map.WaterGates.Length, "수문은 자리마다 2개");
      var water = Verified.Map.GetNavMesh(rules.WaterNavMeshName);
      var ground = Verified.Map.GetNavMesh(rules.NavMeshName);
      Assert.IsNotNull(water, "수상 NavMesh");
      var h = map.BoundsMax.X.AsFloat;
      var channel = WaterPoint(-(h + map.ChannelWidth.AsFloat / 2), 0);
      var canal = CanalPoint(map);
      var all = NavMeshRegionMask.Default;
      Assert.IsTrue(water.Contains(channel, all, true), "수로 가운데는 물 위");
      Assert.IsTrue(water.Contains(canal, all, true), "본진 운하는 물 위");
      Assert.IsFalse(water.Contains(map.StartPositions[0], all, true), "본진 땅은 물이 아님");
      Assert.IsFalse(ground.Contains(channel, all, true), "지상 NavMesh에 수로가 없음");
      for (var seat = 0; seat < 4; seat++) {
        Assert.IsTrue(Verified.Map.RegionMap.ContainsKey($"{rules.WaterGateRegion}{seat}"), $"수문 영역 {seat}");
      }
      Assert.AreNotEqual(Navigation.FindPathResult.Code.Success, WaterPath(canal, channel), "휴전 중에는 수문이 막혀 운하에서 수로로 못 나감");
      // 울타리를 여는 순간(휴전 끝)과 같은 규칙으로 연다.
      TruceSystem.SetBarrier(Verified, true);
      Assert.AreEqual(Navigation.FindPathResult.Code.Success, WaterPath(canal, channel), "수문이 열리면 운하에서 수로로 나감");
      // 네 자리 운하 모두 수로와 이어진다(90도 대칭).
      for (var seat = 1; seat < 4; seat++) {
        var turn = Quaternion.Euler(0, -90f * seat, 0);
        var p = turn * canal.ToUnityVector3();
        Assert.AreEqual(Navigation.FindPathResult.Code.Success, WaterPath(WaterPoint(p.x, p.z), channel), $"자리 {seat} 운하 → 수로");
      }
    }
  }
}
