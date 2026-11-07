namespace Quantum {
  using System;
  using Photon.Deterministic;

  /// <summary>공격 유형 하나의 대상별 피해 배율.</summary>
  [Serializable]
  public struct DamageMatchup {
    public AttackType Attack;
    public FP VsSmall;
    public FP VsMedium;
    public FP VsLarge;
    public FP VsAir;
    public FP VsBuilding;
  }

  public partial class GameRulesSpec : AssetObject {
    public string NavMeshName;
    /// <summary>경기 시작부터 다리가 열릴 때까지의 휴전 시간. 공격할 수 없고 본진 밖으로 나갈 수 없다.</summary>
    public int TruceSeconds;
    /// <summary>다리를 묶은 NavMesh 영역 이름.</summary>
    public string BridgeRegion;
    public int MinDiplomacyPlayers;
    public FP ProposalSeconds;
    public FP ProposalCost;
    public FP ProposalRefund;
    public FP AcceptCost;
    public FP BreakCost;
    public FP JointVictorySeconds;
    public FP BreakGraceSeconds;
    public FP RealliableSeconds;
    public FP NoteCooldownSeconds;
    public int NotePhraseCount;
    /// <summary>받으면 AI의 호감이 오르는 쪽지 문구 번호.</summary>
    public int[] FriendlyPhrases;
    /// <summary>관계 점수를 다시 계산하는 간격.</summary>
    public FP OpinionIntervalSeconds;
    /// <summary>관계 점수의 절댓값 상한.</summary>
    public int OpinionLimit;
    public FP NoteSingleCost;
    public FP NoteGroupCost;
    /// <summary>정전이 시작되면 전기가 이만큼 쌓여야 풀린다. 0 근처에서 켜고 꺼지기를 막는다.</summary>
    public FP PowerResumeLevel;
    /// <summary>정전 중 방어탑 공격 간격 배율.</summary>
    public FP OutageTowerIntervalMultiplier;
    public FP SpawnOffset;
    public FP AirAltitude;
    public FP CarryCapacity;
    public FP IronGatherSeconds;
    public FP WoodGatherSeconds;
    /// <summary>철광산 둘레의 작업 지점 수.</summary>
    public int MineSpots;
    /// <summary>숲 긴 변의 작업 지점 간격.</summary>
    public FP ForestSpotSpacing;
    /// <summary>작업 지점이 노드 가장자리에서 떨어진 거리.</summary>
    public FP SpotOffset;
    /// <summary>작업 지점에 도착했다고 보는 거리.</summary>
    public FP SpotArrival;
    public FP WorkRange;
    public FP NodeSearchRadius;
    /// <summary>건물이 철광산·숲 가장자리에서 떨어져야 하는 거리.</summary>
    public FP ResourceBuildMargin;
    /// <summary>건물이 바위·언덕·협곡 벽에서 떨어져야 하는 거리.</summary>
    public FP TerrainBuildMargin;
    public FP RepairPerSecond;
    /// <summary>회복한 체력 비율 x 건물 비용 x 이 값이 수리비다.</summary>
    public FP RepairCostRatio;
    public FP FoundationHealthRatio;
    /// <summary>유닛이 건물 부지에서 떨어지는 거리.</summary>
    public FP UnitClearance;
    /// <summary>건물에서 떨어진 뒤 돌아가던 방향을 기억하는 시간.</summary>
    public FP SlideMemorySeconds;
    public DamageMatchup[] Matchups;
    public AssetRef<AiSpec> EasyAi;
    public AssetRef<AiSpec> NormalAi;
  }
}
