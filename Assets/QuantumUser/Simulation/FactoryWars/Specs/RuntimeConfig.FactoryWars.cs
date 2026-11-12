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
    public int ColorIndex;
    public BotDifficulty Difficulty;
  }

  public partial class RuntimeConfig {
    public AssetRef<GameRulesSpec> Rules;
    public bool TeamMode;
    /// <summary>팀전에서 팀을 본진 자리로 정한다(대각선이 한 팀). 온라인 방은 서버가 플레이어 번호를 정해서 이것을 켠다.</summary>
    public bool TeamsBySeat;
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
    /// <summary>0보다 크면 맵 대신 이 기지 단계로 시작한다(항목 31 시험·튜토리얼).</summary>
    public int StartingTier;
  }

  public partial class RuntimePlayer {
    public AssetRef<FactionSpec> Faction;
    public int Team;
    /// <summary>고른 색. 화면 전용이지만 모든 기기에서 같아야 해서 플레이어 데이터로 보낸다.</summary>
    public int ColorIndex;
  }
}
