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

  /// <summary>도시 건물 종류 하나. 0 소형, 1 중형, 2 대형, 3 쓰러지는 고층.</summary>
  [Serializable]
  public struct CityKindSpec {
    public FP Health;
    /// <summary>화면 높이이자 쓰러질 때 낙하 구역 길이.</summary>
    public FP Height;
  }

  public partial class GameRulesSpec : AssetObject {
    public string NavMeshName;
    /// <summary>배가 쓰는 수상 NavMesh 이름과 에이전트 설정(물 밖을 누르면 가까운 물 위로 보정하는 반경이 넓다). 항목 24.</summary>
    public string WaterNavMeshName;
    public AssetRef<NavMeshAgentConfig> NavalAgent;
    /// <summary>수문 영역 이름 앞부분. 자리 번호를 붙인다. 휴전 중 닫히고 울타리와 함께 열린다.</summary>
    public string WaterGateRegion;
    /// <summary>건물끼리 떨어져야 하는 최소 간격(m). 바닥판 사이 길이 보이고 유닛이 지나갈 틈이 생긴다(항목 19 5-b).</summary>
    public FP BuildingGap;
    /// <summary>빈 본진 자리(2·3인 경기)의 자원 노드 매장량 비율. 중립 대형 확장지가 된다(항목 17B).</summary>
    public FP EmptyBaseResourceRatio;
    /// <summary>경기 시작부터 울타리가 열릴 때까지의 휴전 시간. 공격할 수 없고 본진 밖으로 나갈 수 없다.</summary>
    public int TruceSeconds;
    /// <summary>다리 입구를 막는 울타리의 NavMesh 영역 이름.</summary>
    public string BarrierRegion;
    /// <summary>점령지를 중립에서 소유로, 또는 소유에서 중립으로 바꾸는 시간.</summary>
    public FP CaptureSeconds;
    /// <summary>아무도 없을 때 진행도가 다 빠지는 시간.</summary>
    public FP CaptureDecaySeconds;
    public FP CommsSteelPerSecond;
    public FP RelayPowerPerSecond;
    public FP BarricadeHealth;
    public FP CratePickRadius;
    /// <summary>본진 상자의 보상. 상자 자리의 Group 번호로 고른다.</summary>
    public ResourceCost[] BaseCrateRewards;
    public ResourceCost DropReward;
    public FP DropIntervalSeconds;
    public FP DropWarningSeconds;
    /// <summary>고지대 위에서 고지대 밖을 쏠 때의 사거리 배율.</summary>
    public FP HighGroundRangeMultiplier;
    public FP HighGroundRevealDistance;
    public FP HighGroundAirRevealDistance;
    public FP HighGroundRevealSeconds;
    public FP RoadSpeedMultiplier;
    public FP FordSpeedMultiplier;
    /// <summary>본진 공격 알림의 최소 간격.</summary>
    public FP BaseAlertSeconds;
    public CityKindSpec[] CityKinds;
    /// <summary>범위 공격(다연장·폭격)의 도시 건물 피해 배율.</summary>
    public FP CityAreaMultiplier;
    /// <summary>화염방사의 진지 건물 피해 배율.</summary>
    public FP GarrisonFlameMultiplier;
    public FP RubbleSpeedMultiplier;
    public FP CollapseRadius;
    public FP CollapseDamage;
    /// <summary>붕괴로 튀어나온 진지 병력의 피해. 최대 체력의 비율이다.</summary>
    public FP GarrisonEjectFraction;
    public FP ToppleDamage;
    public FP TopplePushMargin;
    public int GarrisonCapacity;
    public FP GarrisonRangeBonus;
    public FP GarrisonEnterDistance;
    /// <summary>체력 비율이 이 아래로 내려가면 금 간 단계다.</summary>
    public FP CityCrackedFraction;
    /// <summary>체력 비율이 이 아래로 내려가면 불타는 단계이고, 쓰러지는 건물은 예고가 뜬다.</summary>
    public FP CityBurningFraction;
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
    /// <summary>고를 수 있는 플레이어 색 수.</summary>
    public int PlayerColorCount;
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
