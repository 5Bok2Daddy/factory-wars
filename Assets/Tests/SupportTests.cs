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

  /// <summary>항목 32 새 건물: 의무대·정비소 치료, 레이더 시야·경고, 포병 진지 최소 사거리, 헬기장 생산, 단계 배치.</summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest]
    public IEnumerator Building_MedicTentHealsInfantryOnly() {
      // 민병대가 레인저·전차를 조금 깎은 뒤 정리되면, 의무대 옆 레인저만 회복한다.
      yield return StartMatch(buildings: new[] { (0, "ff_medic_tent", 0, 30), (0, "ff_power_plant", -42, 22) },
        units: new[] { (0, "ff_ranger_squad", 4, 36), (0, "ff_eagle_tank", -4, 36), (1, "cf_militia", 4, 41), (1, "cf_militia", -4, 41) });
      var ranger = UnitsNamed(0, "ff_ranger_squad").OrderBy(u => (Verified.Get<Transform3D>(u).Position - V(4, 36)).SqrMagnitude).First();
      var tank = UnitsNamed(0, "ff_eagle_tank").OrderBy(u => (Verified.Get<Transform3D>(u).Position - V(-4, 36)).SqrMagnitude).First();
      var rangerMax = Verified.FindAsset(Verified.Get<Unit>(ranger).Spec).MaxHealth;
      yield return WaitFor(() => Verified.Exists(ranger) && Verified.Get<Unit>(ranger).Health < rangerMax, 30f, "레인저가 맞음");
      yield return WaitFor(() => UnitsNamed(1, "cf_militia").All(u => (Verified.Get<Transform3D>(u).Position - V(0, 41)).Magnitude > 20), 60f, "민병대 정리");
      var rangerHurt = Verified.Get<Unit>(ranger).Health;
      var tankHurt = Verified.Get<Unit>(tank).Health;
      yield return WaitFrames(4 * Fps);
      Assert.Greater(Verified.Get<Unit>(ranger).Health.AsFloat, rangerHurt.AsFloat, "보병 회복");
      Assert.AreEqual(tankHurt.AsFloat, Verified.Get<Unit>(tank).Health.AsFloat, 0.01f, "차량은 의무대가 고치지 않는다");
    }

    [UnityTest]
    public IEnumerator Building_RadarRevealsHighGroundAndWarns() {
      // 시험 맵 고지대(42,24)의 적은 원래 안 보인다. 레이더(20,20, 반경 40) 안이면 보이고 경고가 온다.
      // 적 민병대는 레이더 밖(-40,44)에서 시작해 고지대로 걸어 들어온다(경고는 들어올 때).
      yield return StartMatch(buildings: new[] { (0, "ff_radar", 20, 20), (0, "ff_power_plant", -42, 22) }, units: new[] { (1, "cf_militia", -40, 44) });
      var contacts = 0;
      QuantumEvent.Subscribe<EventRadarContact>(_listener, e => contacts += e.Player == 0 ? 1 : 0);
      var hidden = UnitsNamed(1, "cf_militia").OrderBy(u => (Verified.Get<Transform3D>(u).Position - V(-40, 44)).SqrMagnitude).First();
      yield return Send(1, new MoveUnitsCommand { Units = new[] { hidden }, Target = V(42, 24) });
      yield return WaitFor(() => (Verified.Get<Transform3D>(hidden).Position - V(42, 24)).Magnitude < 3, 60f, "고지대 도착");
      Assert.IsTrue(SupportSystem.InRadar(Verified, 0, Verified.Get<Transform3D>(hidden).Position), "레이더 반경 안");
      Assert.IsTrue(FactoryWarsUtil.IsVisibleTo(Verified, 0, hidden), "고지대라도 레이더 안이면 보임");
      Assert.IsFalse(SupportSystem.InRadar(Verified, 0, V(-45, -45)), "반경 밖");
      Assert.IsFalse(SupportSystem.InRadar(Verified, 1, Verified.Get<Transform3D>(hidden).Position), "상대에게는 레이더 없음");
      yield return WaitFor(() => contacts > 0, 60f, "접근 경고");
    }

    [UnityTest]
    public IEnumerator Building_ArtilleryRespectsMinimumRange() {
      yield return StartMatch(buildings: new[] { (0, "ff_artillery", 0, 30), (0, "ff_power_plant", -42, 22) }, units: new[] { (1, "cf_dragonscale_tank", 4, 34) });
      var spec = Verified.FindAsset(Verified.Get<Building>(OwnBuilding(0, "ff_artillery")).Spec);
      Assert.Greater(spec.MinRange.AsFloat, 5.7f, "시험 전제: 가까운 전차는 최소 사거리 안");
      var near = UnitsNamed(1, "cf_dragonscale_tank").OrderBy(u => (Verified.Get<Transform3D>(u).Position - V(4, 34)).SqrMagnitude).First();
      var health = Verified.Get<Unit>(near).Health;
      yield return Send(1, new HoldUnitsCommand { Units = new[] { near } });
      yield return WaitFrames(8 * Fps);
      Assert.AreEqual(health.AsFloat, Verified.Get<Unit>(near).Health.AsFloat, 0.01f, "최소 사거리 안은 못 쏜다");
    }

    [UnityTest]
    public IEnumerator Building_HelipadProducesHelicopters() {
      yield return StartMatch(buildings: new[] { (0, "ff_helipad", 0, 30), (0, "ff_power_plant", -42, 22) });
      var spec = Verified.FindAsset(Verified.Get<Building>(OwnBuilding(0, "ff_helipad")).Spec);
      Assert.Greater(spec.Producible.Length, 0);
      Assert.IsTrue(spec.Producible.All(u => Verified.FindAsset(u).IsAir && Verified.FindAsset(u).Size == UnitSize.Medium), "헬기(중형 공중)만");
      Assert.AreEqual(2, spec.RequiredTier);
    }

    [Test]
    public void Building_NewBuildingsPlacedInTiers() {
      foreach (var faction in Resources.LoadAll<FactionSpec>("FactoryWars")) {
        var specs = faction.Buildings.Select(b => Resources.LoadAll<BuildingSpec>("FactoryWars").First(s => s.Guid == b.Id)).ToList();
        int TierOf(BuildingRole role) => specs.First(s => s.Role == role).RequiredTier;
        Assert.AreEqual(1, TierOf(BuildingRole.Sandbags));
        Assert.AreEqual(1, TierOf(BuildingRole.Bunker));
        Assert.AreEqual(2, TierOf(BuildingRole.MedicTent));
        Assert.AreEqual(2, TierOf(BuildingRole.RepairBay));
        Assert.AreEqual(2, TierOf(BuildingRole.Radar));
        Assert.AreEqual(2, TierOf(BuildingRole.Helipad));
        Assert.AreEqual(3, TierOf(BuildingRole.Artillery));
        Assert.Greater(specs.First(s => s.Role == BuildingRole.Sandbags).CoverSpots, 0);
        Assert.Greater(specs.First(s => s.Role == BuildingRole.Artillery).MinRange.AsFloat, 0f);
      }
    }
  }
}
