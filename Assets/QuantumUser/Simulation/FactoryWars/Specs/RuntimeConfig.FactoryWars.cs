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
    /// <summary>true면 규칙 대신 이 휴전 시간을 쓴다. 대전 설정 화면과 방 설정에서 고른다.</summary>
    public bool OverrideTruce;
    public int TruceSeconds;
    /// <summary>true면 맵 대신 이 값으로 시작한다. 튜토리얼과 테스트 상황 구성에 쓴다.</summary>
    public bool OverrideStartingResources;
    public ResourceCost StartingResources;
    public ScenarioUnit[] ScenarioUnits;
    public ScenarioBuilding[] ScenarioBuildings;
  }

  public partial class RuntimePlayer {
    public AssetRef<FactionSpec> Faction;
    public int Team;
  }
}
