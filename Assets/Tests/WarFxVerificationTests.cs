namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// Jean Moreno War FX(WFX_*) 통합 검증 + 원시 주황 구체/링 제거 + Synty 프리팹 + 5맵 바이옴/도로 침범 0건 자동 PlayMode 검증.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly (string Key, string ExpectedPrefix)[] RequiredWarFxKeys = {
      ("nuke", "WFX_Nuke"),
      ("nuke_extra", "WFX_ExplosiveSmokeGround Big"),
      ("explosion", "WFX_ExplosiveSmokeGround"),
      ("explosion_generic", "WFX_Explosion"),
      ("explosion_small", "WFX_Explosion Small"),
      ("bullet_impact", "WFX_BImpact"),
      ("muzzle", "WFX_MF 4P RIFLE1"),
      ("muzzle_heavy", "WFX_MF 4P RIFLE2"),
      ("flame", "WFX_FlameThrower"),
      ("fire_black", "WFX_Fire"),
      ("smoke_black", "WFX_SmokeGrenade"),
      ("smoke", "WFX_SmokeGrenade"),
      ("smoke_white", "WFX_SmokeGrenade"),
      ("welding_sparks", "WFX_BImpact Metal"),
    };

    static void AssertValidWarFxInstance(GameObject instance, string label) {
      Assert.IsNotNull(instance, $"War FX '{label}' 인스턴스 생성 실패(null)");
      var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
      Assert.Greater(systems.Length, 0, $"War FX '{label}'에 ParticleSystem이 없음");
      var renderers = instance.GetComponentsInChildren<ParticleSystemRenderer>(true);
      Assert.Greater(renderers.Length, 0, $"War FX '{label}'에 ParticleSystemRenderer가 없음");
      foreach (var r in renderers) {
        if (r.enabled == false) continue;
        var mat = r.sharedMaterial;
        Assert.IsNotNull(mat, $"War FX '{label}' 렌더러 '{r.name}' 머티리얼 누락");
        Assert.IsNotNull(mat.shader, $"War FX '{label}' 렌더러 '{r.name}' 셰이더 누락");
        Assert.IsTrue(mat.shader.isSupported, $"War FX '{label}' 셰이더 미지원: {mat.shader.name}");
        Assert.AreNotEqual("Hidden/InternalErrorShader", mat.shader.name, $"War FX '{label}' 핑크(에러) 셰이더 검출: {r.name}");
      }
      // 원시 주황 구체·실린더·충격파 링 메쉬 잔존 금지.
      foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true)) {
        var meshName = mf.sharedMesh != null ? mf.sharedMesh.name : "";
        Assert.IsFalse(meshName.Contains("Sphere") || meshName.Contains("Shockwave"), $"War FX '{label}'에 원시 구체/충격파 메쉬 잔존: {meshName}");
      }
    }

    [UnityTest]
    public IEnumerator WarFx_AllCombatEffectsInstantiateCleanly() {
      var errors = new List<string>();
      Application.LogCallback onLog = (cond, stack, type) => {
        if (type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Exception) {
          errors.Add($"[{type}] {cond}");
        }
      };
      Application.logMessageReceived += onLog;
      try {
        yield return StartMatch(
          buildings: new[] {
            (0, "ff_vehicle_factory", -18, 22),
            (0, "ff_power_plant", -28, 22),
            (1, "cf_barracks", 14, 22),
          },
          units: new[] {
            (0, "ff_breacher", 4, 22),
            (0, "ff_ranger_squad", 2, 20),
            (0, "ff_eagle_tank", 0, 24),
            (1, "cf_militia", 9, 22),
          },
          stock: Stock(20000, 20000, 20000, 1000));

        var view = Object.FindAnyObjectByType<BattleView>();
        Assert.IsNotNull(view, "BattleView 존재");
        yield return WaitFrames(4);
        var fx = view.Fx;
        Assert.IsNotNull(fx, "BattleFx 존재");

        var viewCatalog = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
        Assert.IsNotNull(viewCatalog, "ViewCatalog 로드");

        // 1) 모든 핵심 War FX 키가 VisualCatalog & ViewCatalog.WarFx에 정상 바인딩되어 있고 즉시 인스턴스화되는지 확인.
        foreach (var (key, expectedPrefix) in RequiredWarFxKeys) {
          Assert.IsTrue(VisualCatalog.TryFilled(VisualCatalog.FxId(key), out var vSlot) && vSlot.Prefab != null,
            $"VisualCatalog 슬롯 fx/{key} 바인딩 누락");
          Assert.IsTrue(vSlot.Prefab.name.StartsWith(expectedPrefix),
            $"VisualCatalog fx/{key} 프리팹 불일치: actual={vSlot.Prefab.name}, expectedPrefix={expectedPrefix}");
          Assert.IsTrue(viewCatalog.TryGetWarFx(key, out var vcEntry) && vcEntry.Prefab != null,
            $"ViewCatalog.WarFx 키 '{key}' 바인딩 누락");

          var inst = fx.SpawnWarFx(key, new Vector3(0f, 0.2f, 10f), Quaternion.identity, 1f, 0.1f);
          AssertValidWarFxInstance(inst, key);
        }

        // 2) 실전 전투 VFX API 호출: 미사일 핵폭발 · 전차 폭발 · 소총/전차 총구섬광 · 브리처 화염방사기 · 잔해 화염/연기 · 공장 굴뚝 연기.
        var beforeChildren = fx.transform.childCount;
        fx.MushroomExplosion(new Vector3(0f, 0f, 15f), 5.0f);
        fx.Explosion(new Vector3(5f, 0f, 15f), 2.0f);
        fx.Explosion(new Vector3(7f, 0f, 15f), 0.8f);
        fx.MuzzleFlash(new Vector3(2f, 1.2f, 18f), Vector3.right, 0.45f);
        fx.MuzzleFlash(new Vector3(0f, 1.6f, 24f), Vector3.right, 1.5f);
        fx.FlameJet(new Vector3(4f, 1.1f, 22f), Vector3.right, 5f);
        fx.Burning(new Vector3(-4f, 0.5f, 16f), true);
        fx.Burning(new Vector3(-6f, 0.5f, 16f), false);
        fx.ChimneySmoke(new Vector3(-18f, 6f, 22f));
        fx.SiloSteam(new Vector3(-10f, 0.5f, 12f));
        fx.Wreck(new Vector3(8f, 0f, 16f), 2.2f, 10f);
        yield return null;

        Assert.Greater(fx.transform.childCount, beforeChildren + 8, "실전 BattleFx 호출들이 War FX 자식 프리팹을 정상 생성");
        // 원시 주황 구체/충격파 링 메쉬 0건 확인.
        for (var i = 0; i < fx.transform.childCount; i++) {
          var child = fx.transform.GetChild(i);
          Assert.IsFalse(child.name.Contains("Shockwave") || child.name.Contains("OrangeSphere"),
            $"원시 충격파/구체 오브젝트 잔존 금지: {child.name}");
        }

        // 3) 라이브 교전(브리처 화염방사기 + 보병/전차 사격) 및 가동 공장 연기 2초간 진행 -> 콘솔 에러 0건 확인.
        var factory = OwnBuilding(0, "ff_vehicle_factory");
        yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitRef(0, "ff_scout_buggy"), Count = 1 });
        yield return WaitFrames(Fps * 2);

        Assert.AreEqual(0, errors.Count, "War FX 실행 중 콘솔 에러/예외 발생:\n" + string.Join("\n", errors));
      } finally {
        Application.logMessageReceived -= onLog;
      }
    }

    [UnityTest]
    public IEnumerator ArtAndMap_SyntyPrefabsBiomesAndRoadClearance() {
      // 1) 8대 산업 공장 + 미사일 격납고 + 포격함 + 스톰네스트 공중 항모: 100% 실제 Synty 복합 프리팹 연결(원시 흰 블록 0건).
      var requiredSyntySlots = new[] {
        "building/ff_barracks", "building/ff_vehicle_factory", "building/ff_heavy_works", "building/ff_airfield",
        "building/ff_processor", "building/ff_power_plant", "building/ff_supply_depot", "building/ff_research_lab",
        "building/cf_barracks", "building/cf_vehicle_factory", "building/cf_heavy_works", "building/cf_airfield",
        "building/cf_processor", "building/cf_power_plant", "building/cf_supply_depot", "building/cf_research_lab",
        "building/cf_missile_silo", "unit/cf_artillery_ship", "unit/ff_artillery_ship", "unit/ff_stormnest_carrier",
        "fx/factory_lv2", "fx/factory_lv3", "fx/build_scaffold",
        "fx/const_stage1", "fx/const_stage2", "fx/const_worker_0", "fx/const_worker_1", "fx/const_crate_0", "fx/const_trolley",
      };
      foreach (var id in requiredSyntySlots) {
        Assert.IsTrue(VisualCatalog.TryFilled(id, out var slot) && slot.Prefab != null, $"Synty 슬롯 '{id}' 연결 누락");
        var renderers = slot.Prefab.GetComponentsInChildren<Renderer>(true);
        Assert.Greater(renderers.Length, 0, $"Synty 프리팹 '{id}' 렌더러 없음");
        foreach (var r in renderers) {
          foreach (var m in r.sharedMaterials) {
            Assert.IsNotNull(m, $"Synty 프리팹 '{id}' 머티리얼 누락");
            Assert.AreNotEqual("Hidden/InternalErrorShader", m.shader != null ? m.shader.name : "", $"Synty 프리팹 '{id}' 핑크 셰이더");
          }
        }
      }

      // 2) 5종 맵 바이옴 차별화 검증(사막=초록 잔디 0%, 시가전=콘크리트 도심, 다도해=60%+ 바다, 해협=중앙 해협 3교량 + 수상 자원 0건).
      var desert = MapNamed("map_MapDesert");
      Assert.AreEqual(TerrainKit.Biome.Desert, TerrainKit.BiomeOf(desert));
      for (var x = -120f; x <= 120f; x += 30f) {
        for (var z = -120f; z <= 120f; z += 30f) {
          var c = TerrainKit.GroundTint(desert, x, z, 3);
          Assert.Greater(c.r, c.g, $"MapDesert ({x},{z})에 초록 잔디 색 검출: {c}");
        }
      }

      var urban = MapNamed("map_MapUrban");
      Assert.AreEqual(TerrainKit.Biome.Urban, TerrainKit.BiomeOf(urban));
      var uc = TerrainKit.GroundTint(urban, 0f, 40f, 3);
      Assert.Less(Mathf.Abs(uc.r - uc.g), 0.08f, $"MapUrban 바닥은 무채색 콘크리트여야 함: {uc}");

      var arch = MapNamed("map_MapArchipelago");
      Assert.AreEqual(TerrainKit.Biome.Archipelago, TerrainKit.BiomeOf(arch));
      var archPaths = TerrainKit.RiverPaths(arch);
      int waterSamples = 0, totalSamples = 0;
      for (var x = -150f; x <= 150f; x += 10f) {
        for (var z = -150f; z <= 150f; z += 10f) {
          totalSamples++;
          if (TerrainKit.RiverDistance(archPaths, new Vector2(x, z)) < 1f) waterSamples++;
        }
      }
      var oceanRatio = (float)waterSamples / totalSamples;
      Assert.GreaterOrEqual(oceanRatio, 0.58f, $"MapArchipelago 바다 비율 부족: {oceanRatio:P1}");

      var strait = MapNamed("map_MapStrait");
      Assert.AreEqual(TerrainKit.Biome.Strait, TerrainKit.BiomeOf(strait));
      var straitBridges = strait.Bridges.Count(b => Mathf.Abs(b.Center.X.AsFloat) < 5f && b.HalfLength.AsFloat > 20f);
      Assert.AreEqual(3, straitBridges, "MapStrait 중앙 해협 3대 교량 존재");
      foreach (var node in strait.ResourceNodes) {
        Assert.GreaterOrEqual(Mathf.Abs(node.Shape.Center.X.AsFloat), 28f,
          $"MapStrait 중앙 해협 물 위에 자원 노드 배치 금지: ({node.Shape.Center.X.AsFloat:F1}, {node.Shape.Center.Z.AsFloat:F1})");
      }

      // 3) Map4P 로드 후 아스팔트 도로 위에 CaptureSite 깃대/초소/컨테이너 침범 0건 확인.
      yield return StartMatch(Map4PScene, 2, truce: 0);
      yield return WaitFrames(4);
      var bView = Object.FindAnyObjectByType<BattleView>();
      Assert.IsNotNull(bView);
      var bfRoot = GameObject.Find("Battlefield");
      Assert.IsNotNull(bfRoot);
      foreach (Transform site in bfRoot.transform) {
        if (site.name != "CaptureSite") continue;
        var pole = site.Find("Pole");
        if (pole != null) {
          Assert.IsFalse(Dressing.IsOnAnyRoad(pole.position, 2.5f), $"CaptureSite 깃대가 아스팔트 도로 침범: {pole.position}");
        }
        var station = site.Find("Station");
        if (station != null && station.gameObject.activeSelf) {
          Assert.IsFalse(Dressing.IsOnAnyRoad(station.position, 2.5f), $"CaptureSite 중계소가 아스팔트 도로 침범: {station.position}");
        }
      }
    }

    [UnityTest]
    public IEnumerator ConstructionStages_AndAmbientWorkersLifecycle() {
      var errors = new List<string>();
      Application.LogCallback onLog = (cond, stack, type) => {
        if (type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Exception) {
          errors.Add($"[{type}] {cond}");
        }
      };
      Application.logMessageReceived += onLog;
      var prevTier = GraphicsQuality.Current;
      GraphicsQuality.Apply(QualityTier.High);
      try {
        yield return StartMatch(
          buildings: new[] {
            (0, "ff_headquarters", -14, 22),
            (0, "ff_processor", -14, 10),
            (0, "ff_power_plant", 14, 10),
            (0, "ff_supply_depot", 14, 22),
            (0, "ff_heavy_works", 0, 18),
          },
          stock: Stock(20000, 20000, 20000, 1000));
        yield return WaitFrames(4);

        var view = Object.FindAnyObjectByType<BattleView>();
        Assert.IsNotNull(view, "BattleView 존재");
        var heavy = OwnBuilding(0, "ff_heavy_works");
        var heavySpec = Verified.FindAsset(Verified.Get<Building>(heavy).Spec);
        Assert.Greater(heavySpec.BuildTimeSeconds.AsFloat, 0f, "중기갑 공장 건설 시간 > 0");

        void SetProgress(EntityRef bld, int remainPercent) {
          SetBuildingConstructionRemaining(bld, heavySpec.BuildTimeSeconds * remainPercent / 100);
        }

        // 1) Stage 1 (15% 부지 정리): 기초 패드·라바콘·바리케이드·저층 비계 표시, 본체 숨김.
        SetProgress(heavy, 85);
        yield return WaitFrames(3);
        var actor = view.Actors[heavy];
        Assert.AreEqual(1, actor.ConstructionStage, "Stage 1(0~33%) 진입");
        Assert.IsNotNull(actor.Scaffold, "Stage 1 건설 키트 생성");
        Assert.AreEqual("ConstructionStage1", actor.Scaffold.name);
        Assert.IsFalse(actor.View.Model.activeSelf, "Stage 1에서는 공장 본체 메쉬 숨김");

        // 2) Stage 2 (60% 골조 상승): 대형 비계·크레인 + 상승 중인 공장 본체 + 외곽 건설 작업자 2명 + War FX 용접 스파크.
        SetProgress(heavy, 40);
        yield return WaitFrames(15);
        Assert.AreEqual(2, actor.ConstructionStage, "Stage 2(33~99%) 진입");
        Assert.IsNotNull(actor.Scaffold, "Stage 2 건설 키트 생성");
        Assert.AreEqual("ConstructionStage2", actor.Scaffold.name);
        Assert.IsTrue(actor.View.Model.activeSelf, "Stage 2에서는 공장 본체가 점진적으로 상승");
        Assert.Less(actor.View.Model.transform.localPosition.y, -0.05f, "60% 시점 공장 본체는 아직 기초에서 올라오는 중");
        Assert.GreaterOrEqual(actor.ConstructionCrew.Count, 1, "Stage 2 외곽 안전모 건설 작업자 배치");
        var weldInst = view.Fx.WeldingSparks(actor.View.transform.position + Vector3.up * 2f, Vector3.forward);
        AssertValidWarFxInstance(weldInst, "welding_sparks");

        // 3) BaseAmbientWorkers 검증: 3~6명의 상주 안전모 작업자, 지면 안착, 콜라이더 0, 상자/핸드트럭 파지.
        Assert.IsNotNull(view.AmbientWorkers, "BaseAmbientWorkers 컴포넌트 존재");
        Assert.GreaterOrEqual(view.AmbientWorkers.ActiveWorkerCount, 3, "기지 상주 작업자 최소 3명");
        Assert.LessOrEqual(view.AmbientWorkers.ActiveWorkerCount, 16, "모바일 안전 상한 유지");
        var workerRoots = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "BaseWorker").ToList();
        Assert.GreaterOrEqual(workerRoots.Count, 3, "BaseWorker 3명 이상 생성");
        var carriedCount = 0;
        foreach (var wr in workerRoots) {
          Assert.AreEqual(0, wr.GetComponentsInChildren<Collider>(true).Length, "상주 작업자는 전투 유닛 충돌/NavMesh를 방해하지 않음(콜라이더 0)");
          var groundY = TerrainKit.SurfaceHeight(view.World.Map, wr.position.x, wr.position.z);
          Assert.LessOrEqual(Mathf.Abs(wr.position.y - groundY), 0.25f, $"상주 작업자 지면 안착 확인: y={wr.position.y:F2}, ground={groundY:F2}");
          if (wr.GetComponent<BaseAmbientWorkers.WorkerCarryRig>()?.PropObject != null) carriedCount++;
        }
        Assert.Greater(carriedCount, 0, "상주 작업자가 목재 상자/핸드트럭을 실제로 운반 중");

        // 4) Stage 3 (100% 완공): 비계·건설 작업자 제거 + 본체 y=0 안착.
        SetProgress(heavy, 0);
        yield return WaitFrames(4);
        Assert.AreEqual(3, actor.ConstructionStage, "Stage 3(100%) 완공 전환");
        Assert.IsNull(actor.Scaffold, "완공 시 비계·바리케이드 깔끔히 철거");
        Assert.AreEqual(0, actor.ConstructionCrew.Count, "완공 시 임시 건설 작업자 정리");
        Assert.IsTrue(actor.View.Model.activeSelf, "완공 본체 표시");
        // The finished body rises to its rest height over time (MoveTowards, height x 3 m/s), so wait for it instead of 4 frames.
        yield return WaitFor(() => Mathf.Abs(actor.View.Model.transform.localPosition.y - actor.BuildingModelY) <= 0.01f, 3f, "완공 본체 상승");
        Assert.AreEqual(actor.BuildingModelY, actor.View.Model.transform.localPosition.y, 0.01f, "완공 본체가 모델의 바닥 보정 높이에 안착");

        // 5) 건물 파괴 시 상주 작업자 대피/정리(에러 0건).
        var depot = OwnBuilding(0, "ff_supply_depot");
        SetBuildingHealth(depot, 0);
        yield return WaitFrames(Fps * 2);
        Assert.AreEqual(0, errors.Count, "3단계 건설 및 상주 작업자 테스트 중 콘솔 에러:\n" + string.Join("\n", errors));
      } finally {
        GraphicsQuality.Apply(prevTier);
        Application.logMessageReceived -= onLog;
      }
    }
  }
}
