namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>항목 35 맵 풀(지금은 표준 + 사막 보급전). 사막은 표준 지형을 그대로 쓰므로 대칭·연결은 표준과 같고, 데이터(광산·보급)만 다르다.</summary>
  public unsafe partial class LocalMatchTests {
    const string DesertScene = "Assets/QuantumUser/Scenes/MapDesert.unity";

    static MapSpec MapNamed(string name) => Resources.LoadAll<MapSpec>("FactoryWars").First(m => m.name == name);

    static int DropDelayFrames() => Verified.Global->NextDropFrame - Verified.Global->TruceEndFrame;

    [Test]
    public void Map_Desert_PresetData() {
      var standard = MapNamed("map_Map4P");
      var desert = MapNamed("map_MapDesert");
      Assert.AreEqual("사막 보급전", desert.Theme);
      Assert.AreEqual(0.5f, desert.SupplyIntervalMultiplier.AsFloat, 0.01f);
      Assert.AreEqual(2f, desert.SupplyRewardMultiplier.AsFloat, 0.01f);
      Assert.AreEqual(standard.ResourceNodes.Length, desert.ResourceNodes.Length, "같은 광산 자리");
      for (var i = 0; i < standard.ResourceNodes.Length; i++) {
        Assert.AreEqual(standard.ResourceNodes[i].Shape.Center, desert.ResourceNodes[i].Shape.Center, $"광산 {i} 위치");
        Assert.AreEqual(standard.ResourceNodes[i].Amount.AsFloat * 0.4f, desert.ResourceNodes[i].Amount.AsFloat, 1f, $"광산 {i} 양 40%");
      }
      Assert.AreEqual(standard.CrateSpots.Length, desert.CrateSpots.Length, "보급 자리 수");
      Assert.AreEqual(standard.CityBuildings.Length, desert.CityBuildings.Length, "도시 건물 수");
      Assert.AreEqual(standard.StartPositions.Length, desert.StartPositions.Length, "시작 자리(회전 대칭 그대로)");
      Assert.AreEqual(1f, standard.SupplyIntervalMultiplier.AsFloat, 0.01f, "표준은 배율 1");
    }

    [Test]
    public void Map_Catalog_CyclesAndFindsScenes() {
      Assert.GreaterOrEqual(MapCatalog.Maps.Length, 2);
      Assert.AreEqual("Map4P", MapCatalog.Maps[0].Id);
      Assert.AreEqual("MapDesert", MapCatalog.Next("Map4P").Id);
      Assert.AreEqual("Map4P", MapCatalog.Next("MapDesert").Id, "한 바퀴");
      var settings = new MatchSettings { MapId = "MapDesert" };
      Assert.AreEqual("MapDesert", settings.Scene);
      foreach (var entry in MapCatalog.Maps) {
        Assert.IsTrue(System.IO.File.Exists($"Assets/QuantumUser/Scenes/{entry.Id}.unity"), $"{entry.Id} 씬");
      }
    }

    /// <summary>사막 보급전: 첫 낙하까지 간격이 규칙의 절반이고, 보상 배율이 2다. 사람과 AI가 같은 규칙을 쓴다(시뮬레이션 데이터).</summary>
    [UnityTest]
    public IEnumerator Map_Desert_SupplyRunsFaster() {
      yield return StartMatch(DesertScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 5, truce: 20);
      yield return WaitFrames(Fps);
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      Assert.AreEqual(rules.DropIntervalSeconds.AsFloat * 0.5f, DropDelayFrames() / (float)Fps, 0.5f, "낙하 간격 절반");
      Assert.AreEqual(2f, BattlefieldSystem.SupplyReward(Verified).AsFloat, 0.01f);
      Assert.AreEqual(0.5f, BattlefieldSystem.SupplyInterval(Verified).AsFloat, 0.01f);
    }
  }
}
