namespace Quantum {
  using System;
  using Photon.Deterministic;

  /// <summary>경기 시작 때 추가로 배치할 유닛. 튜토리얼과 테스트 상황 구성에 쓴다.</summary>
  [Serializable]
  public struct ScenarioUnit {
    public int Player;
    public AssetRef<UnitSpec> Unit;
    public FPVector3 Position;
  }

  /// <summary>경기 시작 때 완공 상태로 배치할 건물.</summary>
  [Serializable]
  public struct ScenarioBuilding {
    public int Player;
    public AssetRef<BuildingSpec> Building;
    public FPVector3 Position;
  }

  /// <summary>AI가 맡는 플레이어 자리.</summary>
  [Serializable]
  public struct BotSlot {
    public int Player;
    public AssetRef<FactionSpec> Faction;
    public int Team;
    public BotDifficulty Difficulty;
  }

  public partial class RuntimeConfig {
    public AssetRef<GameRulesSpec> Rules;
    public bool TeamMode;
    public bool JointVictory = true;
    public BotSlot[] Bots;
    public ScenarioUnit[] ScenarioUnits;
    public ScenarioBuilding[] ScenarioBuildings;
  }

  public partial class RuntimePlayer {
    public AssetRef<FactionSpec> Faction;
    public int Team;
  }
}
