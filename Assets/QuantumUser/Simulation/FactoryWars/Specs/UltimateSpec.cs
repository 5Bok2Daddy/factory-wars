namespace Quantum {
  using System;
  using Photon.Deterministic;

  /// <summary>요격 단계 하나. 요격 점수가 Score 이상이면 미사일 피해에 DamageMultiplier를 곱한다(가장 높은 단계 하나만).</summary>
  [Serializable]
  public struct InterceptStep {
    public int Score;
    public FP DamageMultiplier;
  }

  /// <summary>
  /// 진영 궁극 전력 하나(항목 33). FactionSpec.Ultimate로 진영에 붙인다. 수치는 전부 여기에 있고 UltimateSystem이 읽는다.
  /// 시작 조건: 기지 RequiredTier단계 + 전용 건물(Site) 완공 + Research 완료 + Cost.
  /// </summary>
  public partial class UltimateSpec : AssetObject {
    public UltimateKind Kind;
    /// <summary>전용 건물. 이 건물의 선행 조건(3단계 건물 2개 이상)은 BuildingSpec.Prerequisites에 있다.</summary>
    public AssetRef<BuildingSpec> Site;
    public int RequiredTier = 3;
    /// <summary>연구소의 "궁극 전력 연구". FactionSpec.Researches에도 들어 있어야 한다.</summary>
    public AssetRef<ResearchSpec> Research;
    public ResourceCost Cost;
    public FP PrepareSeconds;
    /// <summary>한 번 쓴 뒤 다시 준비할 때의 비용과 시간.</summary>
    public ResourceCost RearmCost;
    public FP RearmSeconds;
    /// <summary>동시에 가질 수 있는 수. 지금은 1만 쓴다.</summary>
    public int OwnedLimit = 1;
    /// <summary>쓰거나 잃은 뒤 다시 준비를 시작할 수 있을 때까지.</summary>
    public FP CooldownSeconds;
    /// <summary>준비·대기 중 전용 건물이 부서지면 돌려받는 비율.</summary>
    public FP RefundFraction;
    /// <summary>직접 취소하면 돌려받는 비율.</summary>
    public FP CancelRefundFraction;

    // ---------- 공중 항모 ----------
    public AssetRef<UnitSpec> Carrier;
    public AssetRef<UnitSpec> Drone;
    public int DroneCount;
    /// <summary>드론은 항모에서 이 반경 안의 적만 쫓는다.</summary>
    public FP DroneRadius;
    public FP DroneLaunchSeconds;
    /// <summary>잃은 드론 한 기를 다시 만드는 시간과 비용.</summary>
    public FP DroneBuildSeconds;
    public ResourceCost DroneCost;
    /// <summary>반경 안에 적이 이만큼 없으면 드론을 불러들인다.</summary>
    public FP DroneRecoverSeconds;
    public FP DroneDockDistance;
    /// <summary>항모가 대공 무기에 받는 피해 배율(카운터).</summary>
    public FP AntiAirTakenMultiplier = 1;

    // ---------- 전략 미사일 ----------
    /// <summary>발사부터 착탄까지. 이 동안 모든 플레이어에게 착탄 지점이 보인다.</summary>
    public FP WarningSeconds;
    public FP BlastRadius;
    /// <summary>중심 피해. 가장자리에서는 BlastEdgeFraction배까지 직선으로 줄어든다.</summary>
    public FP BlastDamage;
    public FP BlastEdgeFraction;
    public FP BlastBuildingMultiplier = 1;
    public FP BlastCityMultiplier = 1;
    /// <summary>아군·동맹·자기 것도 맞는다.</summary>
    public bool FriendlyFire = true;
    /// <summary>착탄 지점에서 이 반경 안의, 발사자와 동맹이 아닌 플레이어의 방공포대·레이더가 요격한다.</summary>
    public FP InterceptRadius;
    public int InterceptPerAirDefense;
    /// <summary>방공포대가 하나 이상 있을 때 레이더 기지가 있으면 더하는 점수.</summary>
    public int InterceptRadarBonus;
    public InterceptStep[] InterceptSteps;

    // ---------- 상륙 강습 ----------
    public AssetRef<UnitSpec> LandingCraft;
    public int CraftCount;
    /// <summary>탑재 부대. 상륙정에 번갈아 싣는다([i]는 i % CraftCount번 상륙정).</summary>
    public AssetRef<UnitSpec>[] Cargo;
    public FP LandedBonusSeconds;
    public FP LandedDamageMultiplier = 1;
    /// <summary>하선 뒤 상륙정이 해안에서 포격 지원하는 시간.</summary>
    public FP SupportSeconds;
    /// <summary>지정한 지점에서 이 거리 안에 물이 있어야 한다.</summary>
    public FP LandingReach;
    /// <summary>접안 지점에 이만큼 가까워지면 내린다.</summary>
    public FP UnloadDistance;
  }
}
