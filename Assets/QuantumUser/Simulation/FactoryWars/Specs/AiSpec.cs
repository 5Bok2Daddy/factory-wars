namespace Quantum {
  using Photon.Deterministic;

  /// <summary>AI 난이도 하나의 행동 수치.</summary>
  public partial class AiSpec : AssetObject {
    public FP ThinkSeconds;
    public int EngineerTarget;
    /// <summary>순서대로 짓는다. 같은 역할을 두 번 적으면 두 채 짓는다.</summary>
    public BuildingRole[] BuildOrder;
    /// <summary>공병 중 철을 캐는 비율. 발전소 전과 후.</summary>
    public FP IronShareEarly;
    public FP IronShareLate;
    public int MaxYards;
    /// <summary>일하는 노드가 반입 지점에서 이보다 멀면 노드 옆에 집하장을 짓는다.</summary>
    public FP ExpandDistance;
    /// <summary>시작 사령부를 포함한 사령부 상한.</summary>
    public int MaxHeadquarters;
    /// <summary>노드 주변 ExpandReserveRadius 안의 남은 매장량이 이 이상이면 집하장 대신 사령부를 짓는다.</summary>
    public FP ExpandReserves;
    public FP ExpandReserveRadius;
    /// <summary>사령부로 확장하려면 가지고 있어야 하는 철과 목재.</summary>
    public FP ExpandIron;
    public FP ExpandWood;
    /// <summary>인구 여유가 이보다 적으면 매장량과 상관없이 사령부를 짓는다.</summary>
    public FP ExpandPopulationRoom;
    public int ConcurrentBuilds;
    public int QueueTarget;
    /// <summary>생산 건물의 유닛 목록에서 앞쪽 몇 종까지 쓸지.</summary>
    public int UnitVariety;
    public FP FirstAttackSeconds;
    public FP AttackRepeatSeconds;
    public int WaveSize;
    public int WaveGrowth;
    public int MaxWaveSize;
    /// <summary>내 군대가 적 군대의 이 배수를 넘으면 웨이브를 기다리지 않고 남은 핵심 건물을 소탕한다.</summary>
    public int SweepArmyRatio;
    public FP SweepSeconds;
    /// <summary>맵에 남은 철이 이보다 적고 동맹이 있으면 파기 비용을 쓰지 않고 남겨 둔다.</summary>
    public FP BreakReserveIronLeft;
    /// <summary>전기 순증감이 이보다 낮으면 발전소를 먼저 짓는다.</summary>
    public FP PowerMargin;
    public int PopulationMargin;
    /// <summary>전기가 이보다 적으면 유지비가 드는 건물의 생산을 미룬다.</summary>
    public FP PowerReserve;
    public int MaxProcessors;
    /// <summary>강철이 이보다 적고 철이 IronSurplus 이상이면 가공소를 더 짓는다.</summary>
    public FP SteelTarget;
    public FP IronSurplus;
    /// <summary>철이 이보다 적거나 강철이 SteelCap 이상이면 가공소를 끈다.</summary>
    public FP ProcessorIronFloor;
    public FP SteelCap;
    public FP SiteGrid;
    public int SiteRings;
    /// <summary>true면 가장 강한 상대를 견제하는 동맹을 고르고, false면 제안을 무작위로 받는다.</summary>
    public bool StrategicDiplomacy;
    /// <summary>점수가 모자라도 수락할 확률. 쉬움의 무작위성이다.</summary>
    public FP AcceptChance;
    /// <summary>제안자를 보는 점수가 이 이상이어야 수락한다.</summary>
    public int AcceptOpinion;
    /// <summary>세력 = 유닛 가치 x StrengthUnitWeight + 건물 가치 x StrengthBuildingWeight + 누적 수입 x StrengthIncomeWeight.</summary>
    public FP StrengthUnitWeight;
    public FP StrengthBuildingWeight;
    public FP StrengthIncomeWeight;
    /// <summary>동맹이 2위의 이 배수 이상으로 강해지면 파기한다.</summary>
    public FP BreakStrengthRatio;
    /// <summary>공동의 적이 사라지고 이만큼 지나면 파기한다.</summary>
    public FP BreakAfterEnemySeconds;
    /// <summary>제안 전에 우호 쪽지를 보내는 간격.</summary>
    public FP NoteIntervalSeconds;
    /// <summary>점수 변화 폭. 쪽지 하나, 주기마다 평화·공동 전투·피격·최강 상대, 파기 한 번.</summary>
    public int FriendlyNoteOpinion;
    public int PeaceOpinion;
    public int CommonEnemyOpinion;
    public int AttackedOpinion;
    public int StrongestOpinion;
    public int BrokenOpinion;
    /// <summary>"공격받음"·"같은 적과 싸움"으로 보는 최근 시간.</summary>
    public FP RecentFightSeconds;
    public FP ProposalIntervalSeconds;
    public FP DiplomacyIronReserve;
  }
}
