namespace Quantum {
  using Photon.Deterministic;

  public partial class UnitSpec : AssetObject {
    public UnitSize Size;
    public bool IsAir;
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
