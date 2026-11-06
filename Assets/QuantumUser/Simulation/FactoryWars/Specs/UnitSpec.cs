namespace Quantum {
  using Photon.Deterministic;

  public partial class UnitSpec : AssetObject {
    public UnitSize Size;
    public bool IsAir;
    public FP Cost;
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
  }
}
