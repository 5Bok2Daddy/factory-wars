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

  /// <summary>유닛 크기 규칙(항목 31). 인구 범위는 데이터 시험이 확인한다.</summary>
  [Serializable]
  public struct SizeRuleSpec {
    public UnitSize Size;
    public FP MinPopulation;
    public FP MaxPopulation;
    /// <summary>한 플레이어가 동시에 가질 수 있는 수(대기열 포함). 0이면 제한 없음.</summary>
    public int OwnedLimit;
    /// <summary>공격 연구 단계마다 주는 피해 배율 증가(0.1 = +10%).</summary>
    public FP AttackPerLevel;
    /// <summary>방어 연구 단계마다 받는 피해 감소(0.1 = -10%).</summary>
    public FP ArmorPerLevel;
  }

  /// <summary>기지 단계 업그레이드 하나(항목 31). Tier는 올라갈 단계.</summary>
  [Serializable]
  public struct BaseTierSpec {
    public int Tier;
    public ResourceCost Cost;
    public FP Seconds;
    /// <summary>모두 완공되어 있어야 하는 건물 역할.</summary>
    public BuildingRole[] RequiredRoles;
    /// <summary>AlternativeCount가 0보다 크면, RequiredRoles 대신 이 역할 건물 AlternativeCount개로도 된다.</summary>
    public BuildingRole AlternativeRole;
    public int AlternativeCount;
  }

  public partial class GameRulesSpec : AssetObject {
    /// <summary>항목 31 테크: 크기 규칙, 기지 단계 업그레이드, 사령부를 모두 잃으면 단계도 내려가는지.</summary>
    public SizeRuleSpec[] SizeRules;
    /// <summary>항목 32 엄폐: 찾는 반경, 방향 판정 코사인, 광역 공격에 대한 효과 배율, 도착 거리, 틱당 탐색 수, 점과 엄폐물 사이 간격, 맵 엄폐물(바리케이드·도시 건물) 감소율.</summary>
    public FP CoverSearchRadius;
    public FP CoverFacingCos;
    public FP CoverAreaFactor;
    public FP CoverArrival;
    public int CoverSearchPerTick;
    public FP CoverOffset;
    public FP CoverHighReduction;
    public FP RadarAlertSeconds;
    /// <summary>항목 33: 물가 찾기(상륙 접안, 배의 지원 위치). 고리 간격과 방향 수.</summary>
    public FP WaterSearchStep;
    public int WaterSearchDirections;
    public BaseTierSpec[] BaseTiers;
    public bool TierDropsWithHeadquarters = true;
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
    /// <summary>항목 34-B: 경고 원 반지름(구역 공개 때), 좁혀지는 시각·반지름, 신호탄 반경·시간, 획득 반경·시간.</summary>
    public FP DropZoneRadius;
    public FP DropClosingSeconds;
    public FP DropClosingRadius;
    public FP DropFlareRadius;
    public FP DropFlareSeconds;
    public FP DropCaptureRadius;
    public FP DropCaptureSeconds;
    /// <summary>보상 가중치: 자원 묶음, 긴급 출격권, 정찰 사진, 보급(주변 보병 회복).</summary>
    public FP[] DropRewardWeights;
    public FP DropReconSeconds;
    public FP DropReconRadius;
    public FP DropHealAmount;
    public FP DropHealRadius;
    /// <summary>고지대 위에서 고지대 밖을 쏠 때의 사거리 배율.</summary>
    public FP HighGroundRangeMultiplier;
    public FP HighGroundRevealDistance;
    public FP HighGroundAirRevealDistance;
    public FP HighGroundRevealSeconds;
    /// <summary>항목 34: 전장의 안개. 켜면 보이지 않는 칸의 적은 자동 표적·공격 명령 대상이 아니다. 기본 꺼짐(켜면 AI 밸런스를 다시 재야 한다).</summary>
    public bool FogOfWar;
    /// <summary>안개 갱신 간격(틱, 5틱 = 0.25초).</summary>
    public int FogUpdateTicks = 5;
    public FP FogHighGroundBonus;
    public FP FogGarrisonBonus;
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
    /// <summary>항목 34-C: 저격수가 쏘면 그 건물이 피격 진영에 보이는 시간(초).</summary>
    public FP MuzzleRevealSeconds;
    /// <summary>사격한 건물 주변을 피격 진영에게 보여 주는 반경(m).</summary>
    public FP MuzzleRevealRadius;
    /// <summary>항목 34-C: 레이더 기지가 은신·숨긴 장치를 드러내는 반경(m). 레이더의 넓은 정찰 반경(RevealRadius)과 작은 쪽을 쓴다.</summary>
    public FP RadarDetectRadius;
    /// <summary>항목 34-C: 교란 장치를 풀어 내는 속도(체력/초)와 가까이 와야 하는 거리(m).</summary>
    public FP JammerDisarmPerSecond;
    public FP JammerDisarmRadius;
    /// <summary>항목 34-D: 대공 화력(피해/간격)에 곱해 작전기 체력에서 깎는 배율.</summary>
    public FP SortieAntiAirFactor = 1;
    /// <summary>항목 34-C: 저격수끼리는 보이는 적 저격수에게 피해가 이 배율이다.</summary>
    public FP SniperVsSniperMultiplier = 1;
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
    /// <summary>배가 조선소 부지 가장자리에서 이만큼 더 떨어진 물 위에 나온다(항목 24).</summary>
    public FP ShipSpawnGap;
    /// <summary>조선소 부지 가장자리에서 이 거리 안에 물(강·수로)이 있어야 지을 수 있다.</summary>
    public FP ShipyardWaterReach;
    /// <summary>온라인 연결이 끊긴 자리를 대신 조종하는 봇 난이도(항목 30).</summary>
    public BotDifficulty DisconnectBotDifficulty;
    /// <summary>끊긴 뒤 이 시간 안에 돌아오지 않으면 기권 처리한다. 0이면 기권시키지 않는다(봇이 끝까지 조종).</summary>
    public int DisconnectForfeitSeconds;
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
