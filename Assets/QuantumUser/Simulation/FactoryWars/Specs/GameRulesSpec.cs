namespace Quantum {
  using System;
  using Photon.Deterministic;

  /// <summary>공격 유형 하나의 대상별 피해 배율.</summary>
  [Serializable]
  public struct DamageMatchup {
    public AttackType Attack;
    public FP VsSmall;
    public FP VsMedium;
    public FP VsLarge;
    public FP VsAir;
    public FP VsBuilding;
  }

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
    public FP CarryCapacity;
    public FP GatherSeconds;
    public FP WorkRange;
    public FP NodeSearchRadius;
    public FP ResourceNodeRadius;
    public FP RepairPerSecond;
    /// <summary>회복한 체력 비율 x 건물 비용 x 이 값이 수리비다.</summary>
    public FP RepairCostRatio;
    public FP FoundationHealthRatio;
    /// <summary>유닛이 건물 부지에서 떨어지는 거리.</summary>
    public FP UnitClearance;
    public DamageMatchup[] Matchups;
    public AssetRef<AiSpec> EasyAi;
    public AssetRef<AiSpec> NormalAi;
  }
}
