namespace Quantum {
  using Photon.Deterministic;

  public partial class BuildingSpec : AssetObject {
    public FP Cost;
    public FP BuildTimeSeconds;
    public FP MaxHealth;
    public int PowerProduced;
    public int PowerConsumed;
    public FP PopulationCapBonus;
    public int ModuleSlots;
    /// <summary>정사각형 부지의 중심에서 변까지 거리.</summary>
    public FP FootprintRadius;
    /// <summary>공병이 자금을 반입할 수 있는 건물.</summary>
    public bool IsRefinery;
    /// <summary>이 건물에서 생산할 수 있는 유닛.</summary>
    public AssetRef<UnitSpec>[] Producible;
    public AttackType Attack;
    public FP Damage;
    public FP Range;
    public FP AttackInterval;
  }
}
