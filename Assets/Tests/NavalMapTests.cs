namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
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

    static unsafe EntityRef SpawnUnitFromYard(Frame f, int player, AssetRef<UnitSpec> spec, FPVector3 pos, EntityRef yardEntity) {
      var yard = f.Unsafe.GetPointer<Building>(yardEntity);
      return ProductionSystem.SpawnUnit(f, player, spec, pos, 0, yard);
    }

    static unsafe EntityRef SpawnUnitAtWater(Frame f, int player, AssetRef<UnitSpec> spec, FPVector3 pos) {
      return ProductionSystem.SpawnUnitAt(f, player, spec, pos, null);
    }

    static unsafe void SetEntityPosition(Frame f, EntityRef entity, FPVector3 pos) {
      f.Unsafe.GetPointer<Transform3D>(entity)->Position = pos;
    }

    static unsafe int GetPlayerCratesPicked(Frame f, int player) {
      return f.Global->Players.GetPointer(player)->CratesPicked;
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

    [UnityTest]
    public IEnumerator Naval_ShipyardPlacementOnlyNearWater() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var shipyardRef = BuildingOf(0, "_shipyard");
      var spec = Verified.FindAsset(shipyardRef);
      Assert.IsNotNull(spec, "조선소 스펙이 존재함");

      // 1. 물에서 먼 본진 마른 땅에는 건설 불가
      var dryLand = map.StartPositions[0] + new FPVector3(10, 0, 10);
      Assert.IsFalse(ProductionSystem.CanPlace(Verified, spec, dryLand), "물에서 먼 마른 땅에는 조선소 설치 불가");

      // 2. 운하 변의 유효 지상 위치 탐색 (최소 1곳 이상 설치 가능해야 함)
      var canal = CanalPoint(map);
      var foundSite = false;
      for (var dx = -20; dx <= 20; dx += 2) {
        for (var dz = -20; dz <= 20; dz += 2) {
          var testPos = canal + new FPVector3(dx, 0, dz);
          if (ProductionSystem.CanPlace(Verified, spec, testPos)) {
            foundSite = true;
            break;
          }
        }
        if (foundSite) break;
      }
      Assert.IsTrue(foundSite, "운하 변에 유효한 조선소 설치 위치가 존재함");
    }

    [UnityTest]
    public IEnumerator Naval_ShipyardSpawnsUnitOnWaterNavMesh() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var rules = FactoryWarsUtil.Rules(Verified);
      var waterNavMesh = Verified.Map.GetNavMesh(rules.WaterNavMeshName);
      var canal = CanalPoint(map);
      var shipyardRef = BuildingOf(0, "_shipyard");
      var shipyardSpec = Verified.FindAsset(shipyardRef);

      var foundPos = FPVector3.Zero;
      var foundSite = false;
      for (var dx = -20; dx <= 20; dx += 2) {
        for (var dz = -20; dz <= 20; dz += 2) {
          var testPos = canal + new FPVector3(dx, 0, dz);
          if (ProductionSystem.CanPlace(Verified, shipyardSpec, testPos)) {
            foundPos = testPos;
            foundSite = true;
            break;
          }
        }
        if (foundSite) break;
      }
      Assert.IsTrue(foundSite, "조선소 부지 탐색 성공");

      var yardEntity = ProductionSystem.CreateFinishedBuilding(Verified, 0, shipyardRef, foundPos);

      // 정찰선 스폰
      var scoutBoatSpec = FactionOf(0).Units.First(u => Verified.FindAsset(u).name.EndsWith("_scout_boat"));
      var boatEntity = SpawnUnitFromYard(Verified, 0, scoutBoatSpec, foundPos, yardEntity);
      Assert.AreNotEqual(EntityRef.None, boatEntity, "정찰선 스폰 성공");

      var boatPos = Verified.Get<Transform3D>(boatEntity).Position;
      var all = NavMeshRegionMask.Default;
      Assert.IsTrue(waterNavMesh.Contains(boatPos, all, true), "배는 수상 NavMesh 위에 스폰됨");

      var groundNav = Verified.Map.GetNavMesh(rules.NavMeshName);
      Assert.IsFalse(groundNav.Contains(boatPos, all, true), "배 스폰 위치는 지상 NavMesh가 아님");

      var boatUnit = Verified.Get<Unit>(boatEntity);
      Assert.IsTrue(Verified.FindAsset(boatUnit.Spec).IsNaval, "유닛 IsNaval 플래그 활성화");
    }

    [UnityTest]
    public IEnumerator Naval_ScoutBoatRevealsHighGroundStealth() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 42, 24), (1, "cf_scout_boat", 31, 24) }, truce: 60);
      var hidden = UnitsNamed(0, "ff_eagle_tank").First(e => Verified.Get<Transform3D>(e).Position.X > 30);
      var scout = UnitsNamed(1, "cf_scout_boat").First();

      // 11m 거리 (12m 이내): 정찰선이 고지대 은폐 유닛을 감지함
      Assert.IsTrue(FactoryWarsUtil.IsVisibleTo(Verified, 1, hidden), "정찰선은 12m 안에서 고지대 은폐 유닛을 봄");

      // 12m 밖으로 이동 (거리 22m)
      Verified.Set(scout, Transform3D.Create(new FPVector3(20, 0, 24)));
      Assert.IsFalse(FactoryWarsUtil.IsVisibleTo(Verified, 1, hidden), "12m 밖에서는 고지대 은폐 유닛이 안 보임");
    }

    [UnityTest]
    public IEnumerator Naval_ArtilleryShipCombatAndMinRange() {
      // 0번: 포격함 (0, 40), 1번: 5m 거리 적(5, 40), 1번: 15m 거리 적(15, 40)
      yield return StartMatch(units: new[] {
        (0, "ff_artillery_ship", 0, 40),
        (1, "cf_scout_boat", 5, 40),
        (1, "cf_scout_boat", 15, 40)
      }, truce: 0);

      var artShip = UnitsNamed(0, "ff_artillery_ship").First();
      var artSpec = Verified.FindAsset(Verified.Get<Unit>(artShip).Spec);
      Assert.AreEqual(24, artSpec.Range.AsInt, "포격함 최대 사거리 24");
      Assert.AreEqual(8, artSpec.MinRange.AsInt, "포격함 최소 사거리 8");

      var closeTarget = UnitsNamed(1, "cf_scout_boat").First(e => Verified.Get<Transform3D>(e).Position.X < 10);
      var midTarget = UnitsNamed(1, "cf_scout_boat").First(e => Verified.Get<Transform3D>(e).Position.X > 10);

      // 몇 프레임 진행하여 자동 조준 확인
      yield return WaitFrames(CommandFrames);

      var targeted = Verified.Get<Unit>(artShip).TargetEntity;
      Assert.AreEqual(midTarget, targeted, "최소 사거리(8m) 이내인 5m 표적을 건너뛰고 15m 표적을 조준함");
    }

    [UnityTest]
    public IEnumerator Naval_BoatCollectsSupplyCrateOnWater() {
      yield return StartMatch(Map4PScene, 2, truce: 0);

      // 외곽 수로 위 보급상자 투하 지점 확인
      var waterCratePos = WaterPoint(-169, -20);

      // 수로 위에 상자 생성
      var crateEntity = Verified.Create();
      Verified.Add(crateEntity, Transform3D.Create(waterCratePos));
      Verified.Add(crateEntity, new SupplyCrate { Seat = -1, Reward = new ResourceCost { Iron = 150, Wood = 75 } });

      // 상자 바로 옆에 정찰선 스폰
      var scoutSpec = FactionOf(0).Units.First(u => Verified.FindAsset(u).name.EndsWith("_scout_boat"));
      var boatEntity = SpawnUnitAtWater(Verified, 0, scoutSpec, waterCratePos + new FPVector3(1, 0, 0));

      var initialIron = Funds(0);
      var initialCrates = GetPlayerCratesPicked(Verified, 0);

      // 상자 수거 판정 (매 틱 또는 프레임 실행)
      yield return WaitFrames(Fps / 2);

      Assert.AreEqual(initialCrates + 1, GetPlayerCratesPicked(Verified, 0), "배가 수로 위 상자를 수거함");
      Assert.AreEqual(initialIron + 150, Funds(0), "상자 수거 보상 자금 지급 확인");
    }

    [UnityTest]
    public IEnumerator Naval_CaptureCompleteScreenshot() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var canal = CanalPoint(map);
      var shipyardRef = BuildingOf(0, "_shipyard");
      var shipyardSpec = Verified.FindAsset(shipyardRef);
      var foundPos = FPVector3.Zero;
      for (var dx = -20; dx <= 20; dx += 2) {
        for (var dz = -20; dz <= 20; dz += 2) {
          var testPos = canal + new FPVector3(dx, 0, dz);
          if (ProductionSystem.CanPlace(Verified, shipyardSpec, testPos)) {
            foundPos = testPos;
            break;
          }
        }
        if (foundPos != FPVector3.Zero) break;
      }
      var yardEntity = ProductionSystem.CreateFinishedBuilding(Verified, 0, shipyardRef, foundPos != FPVector3.Zero ? foundPos : canal + new FPVector3(0, 0, 4));
      var scoutRef = FactionOf(0).Units.First(u => Verified.FindAsset(u).name.EndsWith("_scout_boat"));
      var artRef = FactionOf(0).Units.First(u => Verified.FindAsset(u).name.EndsWith("_artillery_ship"));
      SpawnUnitAtWater(Verified, 0, scoutRef, canal + new FPVector3(-6, 0, 0));
      SpawnUnitAtWater(Verified, 0, artRef, canal + new FPVector3(6, 0, 0));
      yield return null;
      if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) {
        var camera = Camera.main;
        if (camera != null) {
          camera.transform.position = canal.ToUnityVector3() + new Vector3(0, 35, -30);
          camera.transform.LookAt(canal.ToUnityVector3());
          var shot = Shoot(camera, 1920, 1080);
          var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "lookdev"));
          Directory.CreateDirectory(folder);
          File.WriteAllBytes(Path.Combine(folder, "24_naval_complete.png"), shot.EncodeToPNG());
          Object.Destroy(shot);
        }
      }
    }
  }
}
