namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>중앙 암시장 주둔지: 20m 정사각 철조망(90도 정렬, 출입구 2), 막사·감시탑 보초·순찰·드론, 반경 22m 비움, 도크린 창이 열리면 옆 패널 숨김.</summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest]
    public IEnumerator Trader_OutpostRectangularGuardsDronesAndClearCenter() {
      yield return StartMatch(Map4PScene, 2, truce: -1);
      yield return WaitForHud();
      yield return WaitFrames(Fps);
      var view = Battle().Trader;
      Assert.IsNotNull(view, "주둔지 화면");
      Assert.IsTrue(view.Built, "지어짐");
      if (Map4P().MercenaryCamp) {
        // Task 39 card 4: on the PMC camp map the camp perimeter/garrison replace the outpost fence, gates and guards
        // (MercCampView); only the surveillance drones stay on the trader (CampViewTests cover the rest).
        Assert.AreEqual(3, view.DroneTotal, "드론 3");
        yield break;
      }
      if (PackPresent) {
        Assert.AreEqual(28, view.FenceCount, "네 변 8칸씩, 출입구 4칸 비움");
        Assert.IsTrue(view.FenceYaws.All(y => Mathf.Abs(Mathf.DeltaAngle(y, Mathf.Round(y / 90f) * 90f)) < 0.01f), "울타리 90도 정렬");
        Assert.AreEqual(2, view.GateCount, "차단기 출입구 2");
        Assert.IsTrue(view.HasHq, "거래 막사");
      }
      Assert.AreEqual(2, view.TowerSentries, "탑 위 보초 2");
      Assert.AreEqual(2, view.Patrols, "순찰 2");
      Assert.AreEqual(3, view.DroneTotal, "드론 3");
      var heights = view.DroneHeights;
      Assert.GreaterOrEqual(heights.x, 4.5f, "드론 높이 하한");
      Assert.LessOrEqual(heights.y, 7.5f, "드론 높이 상한");
      // 반경 22m 비움: 도시 건물·길가 장식·건물 터가 하나도 없다.
      var center = view.Center;
      foreach (var pair in Verified.GetComponentIterator<CityBuilding>()) {
        var at = Verified.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
        Assert.GreaterOrEqual(new Vector2(at.x - center.x, at.z - center.z).magnitude, Dressing.TraderClearRadius - 4f, $"도시 건물이 가운데에: {at}");
      }
      Assert.IsFalse(UrbanDecor.Spots.Any(s => new Vector2(s.Position.x - center.x, s.Position.z - center.z).magnitude < Dressing.TraderClearRadius && s.Role != "road"), "가운데 장식 없음");
      Assert.IsFalse(TerrainKit.Lots.Any(l => (l.Center - new Vector2(center.x, center.z)).magnitude < Dressing.TraderClearRadius), "가운데 건물 터 없음");
      Assert.IsTrue(Dressing.InTraderClear(center + new Vector3(15f, 0f, 0f)), "비움 반경 판정");
      Assert.GreaterOrEqual(view.DroneMinSpacing, 100f, "드론 120도 간격");
      // 들어오는 길은 순환로 바깥 가장자리 안쪽 1.5m에서 끝나 울타리·감시탑까지 들어오지 않는다.
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var limit = TerrainKit.RingOuter - 1.5f - 0.01f;
      foreach (var road in map.Roads) {
        var dir = Quaternion.Euler(0f, road.Yaw.AsFloat, 0f) * Vector3.right;
        foreach (var (pc, ph) in TerrainKit.RoadPieces(map, road)) {
          for (var t = -1f; t <= 1f; t += 0.05f) {
            var p = pc + dir * (ph * t) - center;
            Assert.IsFalse(Mathf.Abs(p.x) < limit && Mathf.Abs(p.z) < limit, $"길 조각이 주둔지 안: {p}");
          }
        }
      }
    }

    [UnityTest]
    public IEnumerator Urban_RoadsidesDressedAlongRoads() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음");
      }
      yield return StartMatch(Map4PScene, 2, truce: -1);
      yield return WaitForHud();
      var spots = UrbanDecor.Spots;
            Debug.Log("[ROADSIDE] " + string.Join(", ", spots.GroupBy(s => s.Role + ":" + s.Kind).Select(g => g.Key + "=" + g.Count())));
      foreach (var role in new[] { "roadside", "cover", "wreck", "verge" }) {
        Assert.Greater(spots.Count(s => s.Role == role), 4, $"길가 {role}");
      }
      foreach (var kind in new[] { DressKind.RoadLight, DressKind.RoadPole, DressKind.RoadBin, DressKind.RoadSign, DressKind.RoadCover, DressKind.RoadWreck }) {
        Assert.Greater(spots.Count(s => s.Kind == kind), 2, $"길가 {kind}");
      }
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      // 길가 소품은 아스팔트(길 폭) 위에 없다.
      foreach (var spot in spots.Where(s => s.Role == "roadside" || s.Role == "cover" || s.Role == "wreck")) {
        var fp = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(spot.Position.x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(spot.Position.z));
        Assert.IsFalse(map.Roads.Any(r => r.Distance(fp).AsFloat < 0.4f), $"길 위 소품: {spot.Kind} {spot.Position}");
      }
    }

    [UnityTest]
    public IEnumerator Hud_DoctrineModalHidesSidePanels() {
      yield return StartMatch(Map4PScene, 2, truce: -1);
      yield return WaitForHud();
      yield return WaitFrames(Fps / 2);
      Controller().Input.Selection.SelectBuilding(TraderEntity());
      yield return WaitFrames(3);
      Controller().Hud.Refresh(Controller().Frame);
      Assert.IsTrue(Controller().Hud.DoctrineOpen, "도크린 창 열림");
      Assert.IsNull(GameObject.Find("SelectionCard"), "도크린 창이 열려 있는 동안 선택 카드 숨김");
      yield return Send(0, new ChooseDoctrineCommand { Doctrine = 1 });
      yield return WaitFor(() => DoctrineLockedOf(0), 10f, "선택 반영");
      yield return WaitFrames(3);
      Controller().Hud.Refresh(Controller().Frame);
      Assert.IsFalse(Controller().Hud.DoctrineOpen, "고른 뒤 닫힘");
      Assert.IsNotNull(GameObject.Find("SelectionCard"), "고른 뒤 거래 카드 표시");
    }
  }
}
