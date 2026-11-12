namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
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
  /// 항목 30-1 에셋 교체 구조와 0-2 표시 이름. 슬롯이 비어 있으면 지금 모델을 쓰고, 채우면 코드 수정 없이 바뀌는지 본다.
  /// 채운 슬롯이 있으면 판정 크기·예산·실존 무기 확인 칸도 검사한다(사용자가 팩을 넣은 뒤 바로 돌리는 시험).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static IEnumerable<T> AllAssets<T>() where T : AssetObject => Resources.LoadAll<T>("FactoryWars");

    /// <summary>건설·생산 패널과 선택 표시에 내부 이름(ff_shipyard 같은)이 보이면 안 된다.</summary>
    [Test]
    public void Names_EveryBuildingUnitModuleHasDisplayName() {
      var names = AllAssets<BuildingSpec>().Select(a => a.name).Concat(AllAssets<UnitSpec>().Select(a => a.name)).Concat(AllAssets<ModuleSpec>().Select(a => a.name)).ToArray();
      Assert.Greater(names.Length, 40, "데이터 에셋을 찾음");
      foreach (var name in names) {
        var shown = Texts.NameOf(name);
        Assert.AreNotEqual(name, shown, $"{name}의 표시 이름 없음");
        Assert.IsFalse(shown.Contains("_"), $"{name} → {shown}: 내부 이름 같음");
      }
      Assert.AreEqual("조선소", Texts.NameOf("ff_shipyard"));
    }

    [Test]
    public void Visual_CatalogHasSlotForEveryElement() {
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      Assert.IsNotNull(catalog, "Resources/FactoryWars/Visual/VisualCatalog.asset (Tools/Factory Wars/Assets/1. Build Visual Slots)");
      var view = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
      foreach (var entry in view.Entries) {
        Assert.IsNotNull(catalog.Get(VisualCatalog.ViewSlotId(entry.Id, entry.Unit)), $"슬롯 없음: {entry.Id}");
      }
      foreach (DressKind kind in System.Enum.GetValues(typeof(DressKind))) {
        Assert.IsNotNull(catalog.Get(VisualCatalog.DressId(kind)), $"소품·도시·지형 슬롯 없음: {kind}");
      }
      foreach (var fx in new[] { "muzzle", "impact", "explosion", "flame", "smoke" }) {
        Assert.IsNotNull(catalog.Get(VisualCatalog.FxId(fx)), $"효과 슬롯 없음: {fx}");
      }
      // 모든 유닛·건물 데이터가 화면 표에 있다(새 데이터를 넣고 그림을 빠뜨리지 않게).
      foreach (var unit in AllAssets<UnitSpec>()) {
        Assert.IsNotNull(catalog.Get(VisualCatalog.UnitId(unit.name)), $"유닛 슬롯 없음: {unit.name}");
      }
      foreach (var building in AllAssets<BuildingSpec>()) {
        Assert.IsNotNull(catalog.Get(VisualCatalog.BuildingId(building.name)), $"건물 슬롯 없음: {building.name}");
      }
      var filled = catalog.Slots.Count(s => s.IsFilled);
      Debug.Log($"[FW-Assets] 슬롯 {catalog.Slots.Count}개, 채움 {filled}개");
    }

    /// <summary>임시 슬롯에 모델을 넣으면 코드 수정 없이 그 모델이 나오고, 판정 크기·고리·체력바·팀 색·애니메이션이 따라간다.</summary>
    [UnityTest]
    public IEnumerator Visual_FilledSlotReplacesModelAndFollowsFootprint() {
      var saved = VisualCatalog.Current;
      var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
      prefab.name = "SwapTank";
      prefab.transform.localScale = new Vector3(2f, 1f, 4f);
      var holder = new GameObject("Prefab");
      prefab.transform.SetParent(holder.transform, false);
      prefab.transform.localPosition = new Vector3(0, 0.5f, 0);
      holder.AddComponent<Animator>();
      holder.SetActive(false);
      var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
      rock.SetActive(false);
      var catalog = ScriptableObject.CreateInstance<VisualCatalog>();
      catalog.Slots.Add(new VisualSlot { Id = VisualCatalog.UnitId("ff_eagle_tank"), Category = VisualCategory.Unit, Prefabs = new List<GameObject> { holder }, AutoFit = true, TeamColor = TeamColorMode.Tint, RealWorld = RealWorldCheck.Clear });
      catalog.Slots.Add(new VisualSlot { Id = VisualCatalog.DressId(DressKind.RockLarge), Category = VisualCategory.Terrain, Prefabs = new List<GameObject> { rock } });
      VisualCatalog.Current = catalog;
      try {
        var view = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
        Assert.IsTrue(view.TryGet("ff_eagle_tank", out var original));
        var parent = new GameObject("Stage").transform;
        var art = ArtView.Create(view, "ff_eagle_tank", parent, 3);
        Assert.IsNotNull(art.Slot, "슬롯이 쓰였음");
        Assert.AreEqual(holder, art.Entry.Prefab);
        Assert.IsNotNull(art.Model.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.sharedMesh != null && f.sharedMesh.name.Contains("Cube")), "교체 모델이 나옴");
        // 판정 크기: 모델 바닥 가장 긴 변이 선택 고리 지름의 0.9배.
        var bounds = new Bounds(art.Model.transform.position, Vector3.zero);
        foreach (var r in art.Model.GetComponentsInChildren<Renderer>()) {
          bounds.Encapsulate(r.bounds);
        }
        var footprint = Mathf.Max(bounds.size.x, bounds.size.z);
        var ring = original.RingRadius * 2f * ArtView.ScaleOf(original);
        Assert.That(footprint, Is.InRange(ring * 0.6f, ring * 1.1f), $"모델 {footprint:0.00}m가 판정 지름 {ring:0.00}m에 맞음");
        Assert.AreEqual(original.RingRadius, art.Entry.RingRadius, "선택 고리는 판정 크기 그대로");
        Assert.Greater(art.Bar.transform.position.y, bounds.max.y, "체력바가 모델 위");
        Assert.LessOrEqual(Mathf.Abs(bounds.min.y - art.transform.position.y), 0.05f, "바닥 보정: 땅에 붙음");
        // 팀 색(곱하기): 색 번호가 바뀌면 원래 머티리얼에서 다시 칠한다.
        var renderer = art.Model.GetComponentInChildren<Renderer>();
        ArtLook.Apply(art.Model, 1);
        var first = renderer.sharedMaterial;
        ArtLook.Apply(art.Model, 1);
        Assert.AreSame(first, renderer.sharedMaterial, "같은 색이면 같은 머티리얼(배칭 유지)");
        ArtLook.Apply(art.Model, 4);
        Assert.AreNotSame(first, renderer.sharedMaterial, "다른 색이면 다른 머티리얼");
        // 애니메이션: 컨트롤러 없는 Animator도 예외 없이 상태를 받는다.
        Assert.IsNotNull(art.Animation, "Animator가 있으면 연결");
        art.Animation.Set(UnitAnimState.Move, 3f);
        Assert.AreEqual(UnitAnimState.Move, art.Animation.State);
        art.Animation.Fire();
        Assert.AreEqual(1, art.Animation.AttackCount);
        Assert.AreEqual(0f, art.Animation.Die(), "사망 파라미터가 없으면 바로 지움");
        Assert.AreEqual(UnitAnimState.Move, BattleView.AnimState(3f, false, WorkerTask.Idle));
        Assert.AreEqual(UnitAnimState.Build, BattleView.AnimState(0f, false, WorkerTask.Build));
        Assert.AreEqual(UnitAnimState.Gather, BattleView.AnimState(0f, false, WorkerTask.Gather));
        Assert.AreEqual(UnitAnimState.Attack, BattleView.AnimState(0f, true, WorkerTask.Idle));

        // 소품 슬롯: Kenney 대신 이 프리팹. 비어 있는 종류는 Kenney 그대로.
        var map = VisualProfile.Current.Dressing;
        if (map != null) {
          var entry = map.Get(DressKind.RockLarge);
          Assert.AreEqual(rock, entry.Variants[0].Prefab, "소품 슬롯이 Kenney를 대체");
          var tree = map.Get(DressKind.Conifer);
          Assert.IsTrue(tree == null || tree.Variants.All(v => v.Prefab != rock), "빈 슬롯은 그대로");
        }

        // 슬롯을 끄면 원래 모델.
        catalog.Enabled = false;
        var plain = ArtView.Create(view, "ff_eagle_tank", parent, 3);
        Assert.IsNull(plain.Slot);
        Assert.AreEqual(original.Prefab, plain.Entry.Prefab);
        Object.Destroy(parent.gameObject);
      } finally {
        VisualCatalog.Current = saved;
        saved?.Invalidate();
        Object.Destroy(holder);
        Object.Destroy(rock);
      }
      yield return null;
    }

    /// <summary>
    /// 채운 슬롯 검사(팩을 넣은 뒤): 크기·삼각형·그리기 호출 예산, 실존 무기 확인. 빈 슬롯은 지금 모델의 예산만 기록한다.
    /// 결과는 Game-logs/assets/swap-check.csv.
    /// </summary>
    [Test]
    public void Visual_FilledSlotsWithinBudgetAndChecked() {
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      Assert.IsNotNull(catalog);
      var view = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
      const int UnitTriangles = 4000;
      const int BuildingTriangles = 12000;
      const int UnitDrawCalls = 6;
      const int BuildingDrawCalls = 12;
      var rows = new List<string> { "Id,Filled,Triangles,DrawCalls,FootprintRatio,RealWorld" };
      var problems = new List<string>();
      foreach (var entry in view.Entries) {
        var id = VisualCatalog.ViewSlotId(entry.Id, entry.Unit);
        var slot = catalog.Get(id);
        var filled = slot != null && slot.IsFilled;
        var prefab = filled ? slot.Prefab : entry.Prefab;
        if (prefab == null) {
          continue;
        }
        var triangles = prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => f.sharedMesh.triangles.Length / 3)
          + prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).Sum(s => s.sharedMesh.triangles.Length / 3);
        var draws = prefab.GetComponentsInChildren<Renderer>(true).Sum(r => r.sharedMaterials.Length);
        var ratio = 1f;
        if (filled) {
          var copy = entry;
          VisualCatalog.Override(id, ref copy, out _);
          var size = VisualCatalog.Measure(slot.Prefab, slot).Size * copy.DisplayScale;
          ratio = Mathf.Max(size.x, size.z) / Mathf.Max(0.01f, entry.RingRadius * 2f);
          if (ratio < 0.5f || ratio > 1.6f) {
            problems.Add($"{id}: 모델이 판정 크기의 {ratio:0.00}배");
          }
          if (slot.RealWorld == RealWorldCheck.Rejected) {
            problems.Add($"{id}: 실존 무기와 닮아 사용 불가로 표시됨");
          }
          if (slot.RealWorld == RealWorldCheck.Unchecked && (slot.Category == VisualCategory.Unit || slot.Category == VisualCategory.Building)) {
            problems.Add($"{id}: 실존 무기 닮음 확인 안 함(RealWorld 칸)");
          }
          if (triangles > (entry.Unit ? UnitTriangles : BuildingTriangles)) {
            problems.Add($"{id}: 삼각형 {triangles} (예산 {(entry.Unit ? UnitTriangles : BuildingTriangles)})");
          }
          if (draws > (entry.Unit ? UnitDrawCalls : BuildingDrawCalls)) {
            problems.Add($"{id}: 그리기 호출 {draws} (예산 {(entry.Unit ? UnitDrawCalls : BuildingDrawCalls)})");
          }
        }
        rows.Add($"{id},{(filled ? 1 : 0)},{triangles},{draws},{ratio:0.00},{(slot != null ? slot.RealWorld.ToString() : "")}");
      }
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets"));
      Directory.CreateDirectory(folder);
      File.WriteAllLines(Path.Combine(folder, "swap-check.csv"), rows);
      Assert.IsEmpty(problems, string.Join("\n", problems));
    }

    /// <summary>해군 경계: 지상 유닛은 물로, 배는 땅으로 못 간다(명령을 줘도 각자 길찾기 면 위에 남는다).</summary>
    [UnityTest]
    public IEnumerator Naval_GroundStaysOnLandShipsStayOnWater() {
      var map = Resources.LoadAll<MapSpec>("FactoryWars").First(m => m.name == "map_Map4P");
      Assert.Greater(map.Channel.Length, 0);
      var channel = map.Channel[0].Center;
      var start = map.StartPositions[0];
      yield return StartMatch(Map4PScene, 2, units: new[] {
        (0, "ff_eagle_tank", start.X.AsInt + 8, start.Z.AsInt + 8),
        (0, "ff_scout_boat", channel.X.AsInt, channel.Z.AsInt),
      }, truce: 0);
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      var ground = Verified.Map.GetNavMesh(rules.NavMeshName);
      var water = Verified.Map.GetNavMesh(rules.WaterNavMeshName);
      var tank = FindOwnUnit(0, "ff_eagle_tank");
      var boat = FindOwnUnit(0, "ff_scout_boat");
      Assert.AreNotEqual(EntityRef.None, boat, "정찰선 배치");
      Assert.IsTrue(water.Contains(Verified.Get<Transform3D>(boat).Position, NavMeshRegionMask.Default, true), "배는 물 위에서 시작");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = channel });
      yield return Send(0, new MoveUnitsCommand { Units = new[] { boat }, Target = start });
      yield return WaitFrames(25 * Fps);
      var tankPos = Verified.Get<Transform3D>(tank).Position;
      var boatPos = Verified.Get<Transform3D>(boat).Position;
      Assert.IsTrue(ground.Contains(tankPos, NavMeshRegionMask.Default, true), $"전차는 땅 위 {tankPos}");
      Assert.IsTrue(water.Contains(boatPos, NavMeshRegionMask.Default, true), $"배는 물 위 {boatPos}");
      Assert.IsFalse(ground.Contains(boatPos, NavMeshRegionMask.Default, true), "배가 땅으로 올라오지 않음");
    }

    static EntityRef FindOwnUnit(int owner, string name) {
      var units = Verified.Filter<Unit>();
      while (units.NextUnsafe(out var entity, out var unit)) {
        if (unit->Owner == owner && Verified.FindAsset(unit->Spec).name == name) {
          return entity;
        }
      }
      return EntityRef.None;
    }
  }
}
