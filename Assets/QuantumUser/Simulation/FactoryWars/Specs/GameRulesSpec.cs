namespace Quantum {
  using Photon.Deterministic;

  public partial class GameRulesSpec : AssetObject {
    public FP StartingFunds;
    public int StartingEngineers;
    public FPVector3[] StartPositions;
    public string NavMeshName;
    public FP ProposalSeconds;
    public FP BreakGraceSeconds;
    public FP RealliableSeconds;
    public FP NoteCooldownSeconds;
    public int NotePhraseCount;
    public FP LowPowerProductionMultiplier;
    public FP LowPowerTowerMultiplier;
    public FP SpawnOffset;
    public FP AirAltitude;
  }
}
