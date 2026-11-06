namespace Quantum {
  using Photon.Deterministic;

  public partial class FactionSpec : AssetObject {
    public string FactionId;
    public FP RangedRangeMultiplier;
    public FP SmallBuildTimeMultiplier;
    public FP PopulationCapMultiplier;
    public AssetRef<BuildingSpec> Headquarters;
    public AssetRef<UnitSpec> Engineer;
    public AssetRef<BuildingSpec>[] Buildings;
    public AssetRef<UnitSpec>[] Units;
    /// <summary>경기 시작 시 사령부 옆에 배치되는 유닛.</summary>
    public AssetRef<UnitSpec>[] StartingUnits;
  }
}
