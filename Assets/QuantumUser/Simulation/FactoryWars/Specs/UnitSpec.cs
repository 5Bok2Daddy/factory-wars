namespace Quantum {
  using Photon.Deterministic;

  public partial class UnitSpec : AssetObject {
    public UnitSize Size;
    /// <summary>생산에 필요한 기지 단계(항목 31). 소형 1, 중형 2, 대형 3.</summary>
    public int RequiredTier = 1;
    public bool IsAir;
    /// <summary>배(항목 24). 수상 NavMesh로만 다니고 점령은 못 한다. 대상으로는 지상과 같다(공중이 아니므로 지상 무기에 맞는다).</summary>
    public bool IsNaval;
    /// <summary>항목 32: 소형 차량(버기·오토바이). 보병이 아니라 진지·엄폐·의무대 대상이 아니다.</summary>
    public bool IsVehicle;
    public ResourceCost Cost;
    public FP BuildTimeSeconds;
    public FP MaxHealth;
    public AttackType Attack;
    public FP Damage;
    public FP Range;
    public FP MinRange;
    public FP SplashRadius;
    public FP AttackInterval;
    public bool IsRanged;
    public FP MoveSpeed;
    /// <summary>유닛 한 기가 차지하는 인구. 민병대는 2명이 인구 1이라 0.5.</summary>
    public FP Population;
    public int ModuleSlots;
    /// <summary>1회 생산으로 나오는 유닛 수.</summary>
    public int SpawnCount = 1;
    public SpecialAbility Ability;
    /// <summary>특수 기능의 배율. 기능마다 피해·속도·받는 피해 중 하나에 곱한다.</summary>
    public FP AbilityMultiplier = 1;
    /// <summary>표적 지시 유지 시간, 교전 종료로 보는 시간.</summary>
    public FP AbilitySeconds;
    public FP AbilityRadius;
    /// <summary>발동에 필요한 대수, 동시 공격 대수.</summary>
    public int AbilityCount;
    public FP AbilityCooldownSeconds;
  }
}
