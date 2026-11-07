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
    /// <summary>생산 프리셋에 넣을 수 있는 모듈.</summary>
    public AssetRef<ModuleSpec>[] Modules;
    /// <summary>경기 시작 시 사령부 옆에 배치되는 유닛.</summary>
    public AssetRef<UnitSpec>[] StartingUnits;
  }
}
