namespace Quantum {
  using Photon.Deterministic;

  /// <summary>
  /// 연구소 연구 하나(항목 31). 공격·방어는 Size 크기 유닛 전체에 Level 단계를 준다(배율은 GameRulesSpec.SizeRules).
  /// 정예화는 EliteUnit 한 종의 체력·사거리·피해를 올리고 이미 있는 유닛에도 바로 적용된다.
  /// </summary>
  public partial class ResearchSpec : AssetObject {
    public ResearchKind Kind;
    public UnitSize Size;
    public int Level = 1;
    public ResourceCost Cost;
    public FP Seconds;
    /// <summary>필요한 기지 단계.</summary>
    public int RequiredTier = 1;
    /// <summary>먼저 끝내야 하는 연구(없으면 비움).</summary>
    public AssetRef<ResearchSpec> Requires;
    public AssetRef<UnitSpec> EliteUnit;
    public FP EliteHealthMultiplier = 1;
    public FP EliteRangeBonus;
    public FP EliteDamageMultiplier = 1;
  }
}
