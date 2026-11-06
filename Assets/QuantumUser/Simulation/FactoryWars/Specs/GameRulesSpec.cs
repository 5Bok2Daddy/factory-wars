namespace Quantum {
  using Photon.Deterministic;

  public partial class GameRulesSpec : AssetObject {
    public FP StartingFunds;
    public int StartingEngineers;
    public string NavMeshName;
    public int MinDiplomacyPlayers;
    public FP ProposalSeconds;
    public FP ProposalCost;
    public FP ProposalRefund;
    public FP AcceptCost;
    public FP BreakCost;
    public FP JointVictorySeconds;
    public FP BreakGraceSeconds;
    public FP RealliableSeconds;
    public FP NoteCooldownSeconds;
    public int NotePhraseCount;
    public FP NoteSingleCost;
    public FP NoteGroupCost;
    public FP LowPowerProductionMultiplier;
    public FP LowPowerTowerMultiplier;
    public FP SpawnOffset;
    public FP AirAltitude;
  }
}
