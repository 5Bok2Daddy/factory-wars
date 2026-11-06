namespace Quantum {
  using Photon.Deterministic;

  /// <summary>AI 난이도 하나의 행동 수치.</summary>
  public partial class AiSpec : AssetObject {
    public FP ThinkSeconds;
    public int EngineerTarget;
    /// <summary>순서대로 짓는다. 같은 역할을 두 번 적으면 두 채 짓는다.</summary>
    public BuildingRole[] BuildOrder;
    public int MaxYards;
    /// <summary>일하는 노드가 반입 지점에서 이보다 멀면 노드 옆에 집하장을 짓는다.</summary>
    public FP ExpandDistance;
    public int ConcurrentBuilds;
    public int QueueTarget;
    /// <summary>생산 건물의 유닛 목록에서 앞쪽 몇 종까지 쓸지.</summary>
    public int UnitVariety;
    public FP FirstAttackSeconds;
    public FP AttackRepeatSeconds;
    public int WaveSize;
    public int WaveGrowth;
    public int MaxWaveSize;
    public int PowerMargin;
    public int PopulationMargin;
    public FP SiteGrid;
    public int SiteRings;
    /// <summary>true면 가장 강한 상대를 견제하는 동맹을 고르고, false면 제안을 무작위로 받는다.</summary>
    public bool StrategicDiplomacy;
    public FP AcceptChance;
    public FP ProposalIntervalSeconds;
    public FP DiplomacyFundsReserve;
  }
}
