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
    /// <summary>이 건물에서 생산할 수 있는 유닛.</summary>
    public AssetRef<UnitSpec>[] Producible;
    public AttackType Attack;
    public FP Damage;
    public FP Range;
    public FP AttackInterval;
  }
}
