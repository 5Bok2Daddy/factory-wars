namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>대전 화면 연결, 지형 생성, 아이콘, 터치 선택.</summary>
  public partial class LocalMatchTests {
    static BattleView Battle() => Object.FindAnyObjectByType<BattleView>();


    static MapSpec MapAsset(string id) => Resources.Load<MapSpec>($"FactoryWars/map_{id}");

    static BattleView.Actor ActorOf(EntityRef entity) => Battle().Actors[entity];

    IEnumerator WaitForBattle() {
      yield return WaitFor(() => Battle() != null && Battle().World != null, 30f, "대전 화면 생성");
      yield return null;
      yield return null;
    }

    [UnityTest]
    public IEnumerator Battle_EveryEntityIsDrawnWithItsPrefab() {
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal }, seed: 5, truce: -1);
      yield return WaitForBattle();
      yield return WaitFrames(Fps);
      var view = Battle();
      Assert.AreEqual(Verified.ComponentCount<Unit>() + Verified.ComponentCount<Building>(), view.Actors.Count, "유닛과 건물 전부");
      foreach (var actor in view.Actors.Values) {
        Assert.IsNotNull(actor.View.Model, actor.View.Entry.Id);
        foreach (var renderer in actor.View.Model.GetComponentsInChildren<Renderer>(true)) {
          StringAssert.StartsWith("Art", renderer.sharedMaterial.name, actor.View.Entry.Id);
        }
      }
      Assert.Greater(view.World.ForestCount, 0, "숲");
      Assert.Greater(view.World.MineCount, 0, "철광산");
      Assert.AreEqual(MapAsset("Map4P").ResourceNodes.Length, view.World.ForestCount + view.World.MineCount, "자원 전부");
      var terrain = GameObject.Find("Terrain");
      Assert.IsFalse(terrain.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled), "임시 도형은 꺼짐");
    }

    [UnityTest]
    public IEnumerator Battle_UsesPlayerColorMaterial() {
      yield return StartMatch();
      yield return WaitForBattle();
      var color = PlayerOf(0).ColorIndex;
      var actor = ActorOf(Headquarters(0));
      Assert.AreEqual(ArtLook.MaterialName(color, true), actor.View.Model.GetComponentInChildren<Renderer>().sharedMaterial.name);
    }

    [UnityTest]
    public IEnumerator Battle_DisplayScaleLeavesSimulationPositionAlone() {
      yield return StartMatch();
      yield return WaitForBattle();
      var engineer = Engineers(0)[0];
      yield return WaitFrames(Fps);
      var actor = ActorOf(engineer);
      var position = Verified.Get<Transform3D>(engineer).Position.ToUnityVector3();
      Assert.AreEqual(position.x, actor.View.transform.position.x, 0.3f);
      Assert.AreEqual(position.z, actor.View.transform.position.z, 0.3f);
      Assert.AreEqual(Vector3.one, actor.View.transform.localScale, "위치 노드는 배율 없음");
    }

    [UnityTest]
    public IEnumerator Battle_OutageSwapsBuildingsButNotUnits() {
      yield return StartMatch(buildings: new[] { (0, "ff_ground_tower", -42, 12) }, stock: Stock(1000, 1000, 0, 0));
      yield return WaitForBattle();
      yield return WaitFor(() => (bool)PlayerOf(0).Outage, 30f, "정전");
      yield return null;
      var color = PlayerOf(0).ColorIndex;
      Assert.AreEqual(ArtLook.MaterialName(color, false), ActorOf(Headquarters(0)).View.Model.GetComponentInChildren<Renderer>().sharedMaterial.name);
      Assert.AreEqual(ArtLook.MaterialName(color, true), ActorOf(Engineers(0)[0]).View.Model.GetComponentInChildren<Renderer>().sharedMaterial.name, "유닛의 발광은 그대로");
    }

    [UnityTest]
    public IEnumerator Battle_ConstructionRisesFromTheGround() {
      yield return StartMatch();
      yield return WaitForBattle();
      var position = V(-20, 10);
      yield return Build(0, "_power_plant", position);
      var site = BuildingAt(position);
      yield return WaitFor(() => Battle().Actors.ContainsKey(site), 10f, "부지 표시");
      yield return null;
      var actor = ActorOf(site);
      Assert.Less(actor.View.Model.transform.localPosition.y, -0.01f, "땅 밑에서 시작");
      Assert.IsTrue(actor.Scaffold.activeSelf, "비계");
      yield return WaitFor(() => Finished(site), 60f, "완공");
      yield return null;
      yield return null;
      Assert.AreEqual(0f, actor.View.Model.transform.localPosition.y, 0.001f);
      Assert.IsFalse(actor.Scaffold.activeSelf);
    }

    [UnityTest]
    public IEnumerator Battle_FiringShowsRecoilEffectsAndDestructionExplodes() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -4, ArenaZ), (1, "cf_militia", 4, ArenaZ) });
      yield return WaitForBattle();
      var tank = ArenaUnit(0, "ff_eagle_tank");
      var militia = ArenaUnit(1, "cf_militia");
      var before = Battle().Fx.Emitted;
      var recoiled = false;
      yield return WaitFor(() => {
        var actor = Battle().Actors.TryGetValue(tank, out var found) ? found : null;
        recoiled |= actor != null && actor.Recoil > 0f;
        return recoiled && Battle().Fx.Emitted > before;
      }, 30f, "사격 반동과 포구 화염");
      yield return WaitFor(() => Verified.Exists(militia) == false, 40f, "격파");
      yield return null;
      yield return null;
      Assert.IsFalse(Battle().Actors.ContainsKey(militia), "사라진 유닛의 모델 제거");
    }

    [UnityTest]
    public IEnumerator Battle_FxAmountFollowsQuality() {
      yield return StartMatch();
      var before = GraphicsQuality.Current;
      GraphicsQuality.Apply(QualityTier.Low);
      var low = GraphicsQuality.FxScale;
      GraphicsQuality.Apply(QualityTier.Medium);
      var medium = GraphicsQuality.FxScale;
      GraphicsQuality.Apply(QualityTier.High);
      var high = GraphicsQuality.FxScale;
      GraphicsQuality.Apply(before);
      Assert.Less(low, medium);
      Assert.Less(medium, high);
    }

    [UnityTest]
    public IEnumerator Battle_SelectionShowsRingAndBar() {
      yield return StartMatch();
      yield return WaitForHud();
      yield return WaitForBattle();
      var engineer = Engineers(0)[0];
      Controller().Input.Selection.SelectUnit(engineer);
      yield return null;
      yield return null;
      var actor = ActorOf(engineer);
      Assert.IsTrue(actor.View.Ring.activeSelf);
      Assert.IsTrue(actor.View.Bar.activeSelf);
      Controller().Input.Selection.Clear();
      yield return null;
      yield return null;
      Assert.IsFalse(actor.View.Ring.activeSelf);
    }

    [Test]
    public void Mine_CrystalsFollowRemaining() {
      var item = new GameObject("Crystals");
      WorldView.ShowCrystals(item, 10f);
      Assert.IsTrue(item.activeSelf);
      WorldView.ShowCrystals(item, 0f);
      Assert.IsFalse(item.activeSelf, "고갈되면 결정만 사라짐");
      Object.DestroyImmediate(item);
    }

    // 터치 선택

    [Test]
    public void Pick_RadiusFollowsDisplaySize() {
      Assert.GreaterOrEqual(MatchInput.PickRadius("cf_militia"), MatchInput.UnitPickRadius, "손가락 최소 크기");
      Assert.Greater(MatchInput.PickRadius("ff_eagle_tank"), MatchInput.UnitPickRadius);
      Assert.Greater(MatchInput.PickRadius("ff_skyfortress_bomber"), 6f, "날개 끝까지");
      Assert.Greater(MatchInput.AirTapOffset("ff_hawk_gunship"), 1f, "공중 표시 위치");
      Assert.AreEqual(0f, MatchInput.AirTapOffset("ff_eagle_tank"));
    }

    [UnityTest]
    public IEnumerator Pick_BomberWingAndHawkOnScreen() {
      yield return StartMatch(units: new[] { (0, "ff_skyfortress_bomber", 0, ArenaZ), (0, "ff_hawk_gunship", -30, ArenaZ) });
      var frame = Verified;
      var bomber = UnitsNamed(0, "ff_skyfortress_bomber")[0];
      var hawk = UnitsNamed(0, "ff_hawk_gunship")[0];
      var shift = MatchInput.AirTapOffset("ff_skyfortress_bomber");
      Assert.AreEqual(bomber, MatchInput.PickUnit(frame, new Vector3(5.5f, 0, ArenaZ + shift)), "날개 끝을 눌러도 선택");
      var hawkPosition = frame.Get<Transform3D>(hawk).Position.ToUnityVector3();
      Assert.AreEqual(hawk, MatchInput.PickUnit(frame, new Vector3(hawkPosition.x, 0, hawkPosition.z + MatchInput.AirTapOffset("ff_hawk_gunship"))), "화면의 기체 위치");
    }

    [UnityTest]
    public IEnumerator Pick_SmallUnitWinsInsideBigRing() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 0, ArenaZ), (0, "ff_ranger_squad", 2, ArenaZ) });
      var frame = Verified;
      var ranger = ArenaUnit(0, "ff_ranger_squad");
      var position = frame.Get<Transform3D>(ranger).Position.ToUnityVector3();
      Assert.AreEqual(ranger, MatchInput.PickUnit(frame, position), "겹쳐도 누른 유닛이 잡힘");
    }

    // 지형

    [Test]
    public void Terrain_GroundIsCutAlongTheRiver() {
      var map = MapAsset("Map4P");
      var kit = new MeshKit();
      TerrainKit.Ground(kit, map, 3);
      var mesh = kit.ToMesh("test");
      var bridge = map.Bridges[0];
      var center = new Vector2(bridge.Center.X.AsFloat, bridge.Center.Z.AsFloat);
      Assert.IsFalse(mesh.vertices.Any(v => (new Vector2(v.x, v.z) - center).magnitude < 1.5f), "강 자리에는 바닥이 없음");
      Assert.Less(kit.TriangleCount, 50000, "삼각형 예산");
    }

    [Test]
    public void Terrain_SameMapSameShape() {
      Vector3[] Build() {
        var kit = new MeshKit();
        TerrainKit.Features(kit, MapAsset("Map4P"));
        return kit.ToMesh("test").vertices;
      }
      Assert.AreEqual(Build(), Build());
    }

    [Test]
    public void Terrain_FeaturesStayInsideTheirBlockedShape() {
      var map = MapAsset("Map4P");
      for (var i = 0; i < map.Terrain.Length; i++) {
        var feature = map.Terrain[i];
        if (feature.Kind != TerrainKind.Hill) {
          continue;
        }
        var kit = new MeshKit();
        TerrainKit.Hill(kit, feature.Shape, i);
        var center = new Vector3(feature.Shape.Center.X.AsFloat, 0, feature.Shape.Center.Z.AsFloat);
        var reach = feature.Shape.Reach.AsFloat + 0.5f;
        var bounds = kit.ToMesh("test").bounds;
        Assert.LessOrEqual(Mathf.Abs(bounds.center.x - center.x) + bounds.extents.x, reach + 0.5f, i.ToString());
        Assert.Greater(bounds.size.y, 2f, "층층 높이");
      }
    }

    [Test]
    public void Terrain_DifferentSeedsChangeHills() {
      var map = MapAsset("Map4P");
      var shape = map.Terrain.First(t => t.Kind == TerrainKind.Hill).Shape;
      var a = new MeshKit();
      var b = new MeshKit();
      TerrainKit.Hill(a, shape, 1);
      TerrainKit.Hill(b, shape, 2);
      Assert.AreNotEqual(a.ToMesh("a").vertices, b.ToMesh("b").vertices);
    }

    // 아이콘

    static readonly string[] IconNames = {
      "res_iron", "res_wood", "res_steel", "res_power", "res_population", "mark_free_federation", "mark_continental_federation",
      "ff_engineer", "ff_ranger_squad", "ff_skyguard_squad", "ff_eagle_tank", "ff_hawk_gunship", "ff_skyfortress_bomber",
      "cf_engineer", "cf_militia", "cf_flame_squad", "cf_dragonscale_tank", "cf_thunder_aa", "cf_longwall_mlrs",
    };

    [Test]
    public void Icons_AllSpritesExistWithSvgSource() {
      foreach (var kind in new[] { "headquarters" }.Concat(BuildingKinds)) {
        Assert.IsNotNull(Ui.IconSprite($"building_{kind}"), kind);
        Assert.IsTrue(File.Exists($"Assets/Art/Icons/src/building_{kind}.svg"), kind);
      }
      foreach (var name in IconNames) {
        Assert.IsNotNull(Ui.IconSprite(name), name);
        Assert.IsTrue(File.Exists($"Assets/Art/Icons/src/{name}.svg"), name);
      }
    }

    [UnityTest]
    public IEnumerator Hud_ShowsResourceIconsAndPanelIcons() {
      yield return StartMatch();
      yield return WaitForHud();
      var hud = Controller().Hud;
      Assert.AreEqual(5, hud.ResourceIcons, "자원 5종");
      Controller().Input.Selection.SelectUnit(Engineers(0)[0]);
      hud.Refresh(Controller().Frame);
      foreach (var entry in hud.Entries) {
        Assert.IsNotNull(Ui.IconSprite(entry.Icon), entry.Label);
      }
    }

    [UnityTest]
    public IEnumerator Minimap_ShowsFactionMarkAtHeadquarters() {
      yield return StartMatch();
      yield return WaitForHud();
      Controller().Hud.Refresh(Controller().Frame);
      Assert.AreEqual(2, Object.FindAnyObjectByType<Minimap>().MarkCount, "플레이어마다");
    }
  }
}
