namespace Quantum {
  using Photon.Deterministic;

  public partial class FactionSpec : AssetObject {
    public string FactionId;
    public FP RangedRangeMultiplier;
    public FP SmallBuildTimeMultiplier;
    public FP PopulationCapMultiplier;
    public AssetRef<BuildingSpec> Headquarters;
    public AssetRef<UnitSpec> Engineer;
    /// <summary>AI 빌드 순서에서 이 진영은 짓지 않는 역할(예: 공중 유닛이 없는 진영의 비행장). 항목 22.</summary>
    public BuildingRole[] AiSkipRoles;
    /// <summary>AI가 차량 공장 다음에 바로 당겨 짓는 역할(예: 대륙 연방의 중공업 기지). 항목 22.</summary>
    public BuildingRole[] AiEarlyRoles;
    public AssetRef<BuildingSpec>[] Buildings;
    public AssetRef<UnitSpec>[] Units;
    /// <summary>생산 프리셋에 넣을 수 있는 모듈.</summary>
    public AssetRef<ModuleSpec>[] Modules;
    /// <summary>경기 시작 시 사령부 옆에 배치되는 유닛.</summary>
    public AssetRef<UnitSpec>[] StartingUnits;
    /// <summary>연구소에서 할 수 있는 연구(항목 31). 순서가 PlayerState.ResearchDone 비트 번호다(최대 31개).</summary>
    public AssetRef<ResearchSpec>[] Researches;
  }
}
