namespace FactoryWars.View {
  using System.Collections.Generic;
  using System.Text;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;

  /// <summary>화면 문구. 현지화(챕터 7) 전까지 한국어를 여기에 모은다.</summary>
  public static class Texts {
    const string Red = "#FF5A4F";

    public static readonly string[] Phrases = {
      "동맹 어때요?", "공격합니다", "도와주세요", "고맙습니다", "조금만 기다려 주세요", "함께 공격합시다",
      "방어해 주세요", "후퇴합니다", "자원이 부족합니다", "좋은 경기였습니다", "저쪽을 보세요", "알겠습니다",
    };

    static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
      { "headquarters", "산업 사령부" }, { "power_plant", "터빈 발전소" }, { "processor", "강철 제련소" }, { "supply_depot", "군수·연료 보급창" }, { "barracks", "보병 무장 공장" },
      { "research_lab", "군수 연구 공장" }, { "vehicle_factory", "기동·화력 조립 공장" }, { "airfield", "항공·드론 조립 격납고" }, { "heavy_works", "중기갑 전차 공장" },
      { "ground_tower", "지상 방어탑" }, { "air_defense", "방공포대" }, { "collection_yard", "집하장" },
      { "lumber_mill", "제재소" }, { "iron_extractor", "철 채굴 공장" },
      { "shipyard", "조선소" },
      { "sandbags", "모래주머니 진지" }, { "bunker", "벙커" }, { "medic_tent", "의무대" }, { "repair_bay", "정비소" },
      { "radar", "레이더 기지" }, { "helipad", "헬기장" }, { "artillery", "포병 진지" }, { "patriot_turret", "광역 요격 포대" }, { "scout_boat", "정찰선" }, { "artillery_ship", "포격함" },
      { "ff_engineer", "공병" }, { "ff_ranger_squad", "레인저" }, { "ff_skyguard_squad", "스카이가드" }, { "ff_eagle_tank", "이글 전차" },
      { "ff_hawk_gunship", "호크 공격헬기" }, { "ff_skyfortress_bomber", "스카이포트리스 폭격기" },
      { "cf_engineer", "공병" }, { "cf_militia", "민병대" }, { "cf_flame_squad", "화염방사병" }, { "cf_dragonscale_tank", "용린 전차" },
      { "cf_thunder_aa", "천둥 대공차" }, { "cf_longwall_mlrs", "만리 다연장포" },
      // 신규 공격 유닛 4종(양 진영)
      { "ff_at_rocket", "대전차 로켓병" }, { "cf_at_rocket", "대전차 로켓병" },
      { "ff_breacher", "화염 돌격병" }, { "cf_breacher", "중화염 돌격병" },
      { "ff_mlrs_artillery", "다연장 로켓차" }, { "cf_rocket_artillery", "화력지원 로켓차" },
      { "ff_mlrs_battery", "다연장 로켓 포대" }, { "cf_mlrs_battery", "다연장 로켓 포대" },
      { "ff_mobile_sam", "기동 대공차" }, { "cf_mobile_sam", "기동 대공미사일차" },
      // 항목 31 새 유닛(가칭, 실존 무기명과 무관)
      { "ff_scout_buggy", "정찰 버기" }, { "ff_field_support", "야전 정비차" }, { "ff_bulwark_tank", "방벽 중전차" },
      { "cf_scout_bike", "척후 오토바이" }, { "cf_gale_gunship", "돌풍 공격헬기" }, { "cf_fortress_tank", "요새 전차" },
      // 플레이어 조종 수송 유닛(APC · 수륙양용 상륙정 · 전술 수송헬기).
      { "cf_apc_transport", "장갑 병력수송차" },
      { "ff_landing_craft", "수륙양용 상륙정" }, { "cf_landing_craft", "수륙양용 상륙정" },
      { "ff_transport_heli", "전술 수송헬기" }, { "cf_transport_heli", "전술 수송헬기" },
      // 항목 34-C 주둔 저격수(가칭)
      { "ff_sortie_fighter", "제비 전투기" }, { "ff_sortie_striker", "벼락 공격기" }, { "ff_sortie_transport", "구름 수송기" },
      { "cf_sortie_fighter", "참새 전투기" }, { "cf_sortie_striker", "철매 공격기" }, { "cf_sortie_transport", "기러기 수송기" },
      { "ff_sniper", "정밀 저격수" }, { "cf_sniper", "매복 사수" },
      // 항목 33 궁극 전력(가칭, 실존 함정·항공기·미사일 이름과 무관)
      { "ff_stormnest_carrier", "스톰네스트 공중 항모" }, { "ff_nest_drone", "둥지 드론" }, { "tf_wavecrest_lander", "파도머리 상륙정" },
      { "ff_carrier_yard", "항모 조립창" }, { "cf_missile_silo", "미사일 격납고" }, { "tf_assault_dock", "강습 도크" },
      { "module_piercing_rounds", "관통탄" }, { "module_reactive_armor", "반응장갑" },
    };

    /// <summary>진영 이름.</summary>
    public static string FactionName(string factionId) => factionId == "free_federation" ? "자유 연방" : factionId == "continental_federation" ? "대륙 연방" : "시험 진영";

    /// <summary>궁극 전력 이름(항목 33).</summary>
    public static string UltimateName(UltimateKind kind) => kind == UltimateKind.Carrier ? "공중 항모" : kind == UltimateKind.StrategicMissile ? "전략 미사일" : "상륙 강습단";

    /// <summary>궁극 전력을 시작하지 못하는 이유 목록(자물쇠 설명). siteName은 전용 건물 이름.</summary>
    public static string UltimateLocks(UltimateLock locks, int tier, string siteName) {
      var parts = new List<string>();
      if ((locks & UltimateLock.Busy) != 0) {
        parts.Add("이미 준비했거나 운용 중");
      }
      if ((locks & UltimateLock.Cooldown) != 0) {
        parts.Add("재사용 대기 중");
      }
      if ((locks & UltimateLock.Tier) != 0) {
        parts.Add(TierLock(tier));
      }
      if ((locks & UltimateLock.Site) != 0) {
        parts.Add($"{siteName} 필요");
      }
      if ((locks & UltimateLock.Research) != 0) {
        parts.Add("궁극 전력 연구 필요");
      }
      if ((locks & UltimateLock.Resources) != 0) {
        parts.Add("자원 부족");
      }
      return string.Join(" · ", parts);
    }

    /// <summary>모든 플레이어에게 가는 궁극 전력 알림 문구. who는 "플레이어 2(대륙 연방)" 같은 주체.</summary>
    public static string UltimateNotice(string who, UltimateStage stage, UltimateKind kind, int value) {
      var name = UltimateName(kind);
      switch (stage) {
        case UltimateStage.Started:
          if (value < 0) {
            return kind == UltimateKind.StrategicMissile
              ? $"경고: {who} 전략 미사일 포대 건설을 시작했습니다! ({-value}초)"
              : $"경고: {who} {name} 조립창 건설을 시작했습니다! ({-value}초)";
          }
          return kind == UltimateKind.Carrier
            ? $"경고: {who}: {name} 준비 시작 — 적 공중항모 출격 준비! ({value}초)"
            : $"경고: {who}: {name} 준비 시작 ({value}초)";
        case UltimateStage.Half: return $"{who}: {name} 준비 절반 (남은 {value}초)";
        case UltimateStage.Ready: return kind == UltimateKind.Carrier ? $"경고: {who}: {name} 출격!" : $"경고: {who}: {name} 준비 완료!";
        case UltimateStage.Cancelled: return value > 0 ? $"{who}: 전용 건물 파괴로 {name} 취소" : $"{who}: {name} 취소";
        case UltimateStage.Launched: return kind == UltimateKind.StrategicMissile ? $"경고: {who}: {name} 발사! {value}초 뒤 착탄" : $"{who}: {name} 출발";
        case UltimateStage.Impact: return value < 0 ? "적 전략 미사일 공중 요격 성공!" : value > 0 ? $"{name} 착탄 (요격 {value}단계)" : $"{name} 착탄";
        case UltimateStage.Landed: return $"{who}: 상륙 부대 {value}기 하선";
        default: return kind == UltimateKind.Carrier ? $"{who}: {name} 격추" : $"{who}: 상륙정 격침";
      }
    }

    /// <summary>크기 배지 글자(항목 31).</summary>
    public static string SizeBadge(UnitSize size) => size == UnitSize.Large ? "대" : size == UnitSize.Medium ? "중" : size == UnitSize.Small ? "소" : "공병";

    public static string SizeWord(UnitSize size) => size == UnitSize.Large ? "대형" : size == UnitSize.Medium ? "중형" : size == UnitSize.Small ? "소형" : "공병";

    static string SizeName(UnitSize size, bool air) => air ? $"{SizeWord(size)} 공중" : SizeWord(size);

    public static string TierName(int tier) => tier >= 3 ? "사단 본부(3단계)" : tier == 2 ? "연대 본부(2단계)" : "기지 1단계";

    /// <summary>연구 이름: "소형 공격 Mk.2", "정예 레인저", 진영 특화 연구.</summary>
    public static string ResearchName(ResearchSpec spec, System.Func<AssetRef<UnitSpec>, string> unitName = null) {
      if (spec.Kind == ResearchKind.Elite) {
        return "정예화: " + (unitName != null ? unitName(spec.EliteUnit) : "소형");
      }
      if (spec.Kind == ResearchKind.Ultimate) {
        return "궁극 전력 연구";
      }
      if (spec.Kind == ResearchKind.Special) {
        switch (spec.Special) {
          case FactionSpecial.RapidCycleBolts: return "고속 볼트 개량";
          case FactionSpecial.StealthCoating: return "스텔스 도료";
          case FactionSpecial.ReactiveArmorEra: return "반응장갑 ERA";
          case FactionSpecial.HighExplosiveAp: return "고폭 철갑탄";
          case FactionSpecial.RapidDeployment: return "신속 전개";
          case FactionSpecial.TacticalSmoke: return "전술 연막";
          default: return "진영 특화 연구";
        }
      }
      return $"{SizeWord(spec.Size)} {(spec.Kind == ResearchKind.Attack ? "공격" : "방어")} Mk.{spec.Level + 1}";
    }

    public static string SpecialDetail(FactionSpecial special) {
      switch (special) {
        case FactionSpecial.RapidCycleBolts: return "보병·함재 드론 연사 속도 +20%";
        case FactionSpecial.StealthCoating: return "작전기 재정비 시간 -25%, 공중 유닛 피격 피해 -15%";
        case FactionSpecial.ReactiveArmorEra: return "중·대형 지상 차량 체력 +25%, 포격 폭발 반경 +25%";
        case FactionSpecial.HighExplosiveAp: return "모든 유닛의 적 건물 대상 피해 +30%";
        case FactionSpecial.RapidDeployment: return "전 부대 이동 속도 +25%";
        case FactionSpecial.TacticalSmoke: return "전 부대 받는 피해 -15%";
        default: return "";
      }
    }

    public static string TierLock(int tier) => $"기지 {tier}단계 필요";

    static string AttackName(AttackType attack) => attack == AttackType.AntiInfantry ? "대보병" : attack == AttackType.AntiArmor ? "대전차" : attack == AttackType.AntiAir ? "대공" : "범위";

    /// <summary>상성 한 줄(항목 30): "전차 · 강함: 차량 · 약함: 보병 · 조심: 대전차". 수치는 규칙의 상성표에서 읽는다.</summary>
    public static string CounterTip(UnitSpec spec, GameRulesSpec rules) {
      if (rules == null || rules.Matchups == null) {
        return "";
      }
      var strong = new List<string>();
      var weak = new List<string>();
      foreach (var m in rules.Matchups) {
        if (m.Attack != spec.Attack || spec.Attack == AttackType.None) {
          continue;
        }
        void Judge(FP value, string name) {
          if (value >= FP._1 + FP._0_25) {
            strong.Add(name);
          } else if (value <= FP._0_75) {
            weak.Add(name);
          }
        }
        Judge(m.VsSmall, "소형");
        Judge(m.VsMedium, "중형");
        Judge(m.VsLarge, "대형");
        Judge(m.VsAir, "공중");
      }
      var threats = new List<string>();
      foreach (var m in rules.Matchups) {
        var value = spec.IsAir ? m.VsAir : spec.Size == UnitSize.Large ? m.VsLarge : spec.Size == UnitSize.Medium ? m.VsMedium : m.VsSmall;
        if (value >= FP._1 + FP._0_25) {
          threats.Add(AttackName(m.Attack));
        }
      }
      var parts = new List<string> { SizeName(spec.Size, spec.IsAir) };
      if (strong.Count > 0) {
        parts.Add("강함: " + string.Join("·", strong));
      }
      if (weak.Count > 0) {
        parts.Add("약함: " + string.Join("·", weak));
      }
      if (threats.Count > 0) {
        parts.Add("조심: " + string.Join("·", threats));
      }
      return string.Join("  ", parts);
    }

    /// <summary>에셋 이름의 화면 표기. 건물 이름은 진영 접두어를 뺀다.</summary>
    public static string NameOf(string assetName) {
      if (Names.TryGetValue(assetName, out var name)) {
        return name;
      }
      return assetName.Length > 3 && Names.TryGetValue(assetName.Substring(3), out name) ? name : assetName;
    }

    static void Part(StringBuilder text, string label, FP cost, FP stock, string separator) {
      if (cost <= FP._0) {
        return;
      }
      if (text.Length > 0) {
        text.Append(separator);
      }
      var part = $"{label} {cost.AsInt}";
      text.Append(stock < cost ? $"<color={Red}>{part}</color>" : part);
    }

    /// <summary>비용 표기. 모자란 항목은 빨간색이다.</summary>
    public static string Cost(ResourceCost cost, ResourceCost stock, string separator = " ") {
      var text = new StringBuilder();
      Part(text, "철", cost.Iron, stock.Iron, separator);
      Part(text, "목재", cost.Wood, stock.Wood, separator);
      Part(text, "강철", cost.Steel, stock.Steel, separator);
      Part(text, "전기", cost.Power, stock.Power, separator);
      return text.ToString();
    }

    public static string TopBar(PlayerState state) {
      var stock = state.Stock;
      var rate = state.PowerRate.AsFloat;
      var power = $"전기 {stock.Power.AsInt}/{state.PowerCap.AsInt} ({rate:+0.0;-0.0;0}/초)";
      if (state.Outage) {
        power = $"<color={Red}>{power} 정전</color>";
      }
      return $"철 {stock.Iron.AsInt}  |  목재 {stock.Wood.AsInt}  |  강철 {stock.Steel.AsInt}  |  {power}  |  인구 {Population(state.PopulationUsed)}/{Population(state.PopulationCap)}";
    }

    /// <summary>상단 바의 아이콘 옆 숫자. 철, 목재, 강철, 전기, 인구 순서다.</summary>
    public static string[] TopBarValues(PlayerState state) {
      var stock = state.Stock;
      var power = $"{stock.Power.AsInt}/{state.PowerCap.AsInt} {state.PowerRate.AsFloat:+0.0;-0.0;0}";
      if (state.Outage) {
        power = $"<color={Red}>{power} 정전</color>";
      }
      return new[] { stock.Iron.AsInt.ToString(), stock.Wood.AsInt.ToString(), stock.Steel.AsInt.ToString(), power, $"{Population(state.PopulationUsed)}/{Population(state.PopulationCap)}" };
    }

    static string Population(FP value) => Mathf.FloorToInt(value.AsFloat + 0.001f).ToString();

    public const string BarrierAlert = "전쟁 시작 - 방어 울타리가 해제됩니다!";
    public const string DropIncoming = "보급 투하 20초 전";
    public const string BackdoorOpened = "뒷길이 열렸습니다";
    public const string BaseAttacked = "본진이 공격받고 있습니다";
    public const string ExitGarrison = "모두\n나가기";

    public static string CityName(CityBuildingPlacement placement) {
      if (placement.Topples) {
        return "고층 빌딩";
      }
      var name = placement.Kind == 0 ? "상가" : placement.Kind == 1 ? "아파트" : "창고";
      return placement.Garrison ? name + " (진지)" : name;
    }

    public static string SiteName(CaptureKind kind) => kind == CaptureKind.Comms ? "중앙 통신소" : kind == CaptureKind.Radar ? "무인 레이더탑" : kind == CaptureKind.Fuel ? "전술 유류고" : "발전 중계소";

    public static string Captured(CaptureKind kind) => $"{SiteName(kind)}를 점령했습니다";

    public static string Lost(CaptureKind kind) => $"{SiteName(kind)}를 빼앗겼습니다";

    /// <summary>상자 보상 글자. 0이 아닌 자원만 쓴다.</summary>
    public static string Reward(ResourceCost reward) {
      var parts = new System.Collections.Generic.List<string>();
      if (reward.Iron > 0) {
        parts.Add($"+{reward.Iron.AsInt} 철");
      }
      if (reward.Wood > 0) {
        parts.Add($"+{reward.Wood.AsInt} 목재");
      }
      if (reward.Steel > 0) {
        parts.Add($"+{reward.Steel.AsInt} 강철");
      }
      if (reward.Power > 0) {
        parts.Add($"+{reward.Power.AsInt} 전기");
      }
      return string.Join(" ", parts);
    }

    public static string TruceCountdown(float seconds) {
      var whole = Mathf.CeilToInt(seconds);
      return $"전쟁 시작까지 {whole / 60}:{whole % 60:00}";
    }

    public static string Missing(MissingResource missing) {
      var parts = new List<string>();
      if ((missing & MissingResource.Iron) != 0) {
        parts.Add("철");
      }
      if ((missing & MissingResource.Wood) != 0) {
        parts.Add("목재");
      }
      if ((missing & MissingResource.Steel) != 0) {
        parts.Add("강철");
      }
      if ((missing & MissingResource.Power) != 0) {
        parts.Add("전기");
      }
      var text = parts.Count > 0 ? string.Join("·", parts) + " 부족" : "";
      if ((missing & MissingResource.Population) != 0) {
        text += (text.Length > 0 ? ", " : "") + "인구 한도";
      }
      return text;
    }

    public static string Rejected(RejectReason reason, MissingResource missing) {
      switch (reason) {
        case RejectReason.InsufficientResources:
        case RejectReason.PopulationLimit: return Missing(missing);
        case RejectReason.Cooldown: return "아직 할 수 없습니다";
        case RejectReason.ModeDisabled: return "이 모드에서는 할 수 없습니다";
        case RejectReason.BlockedLocation: return "건설 불가 지형";
        case RejectReason.Locked: return "해금되지 않았습니다";
        case RejectReason.Truce: return "휴전 중입니다";
        case RejectReason.TierLocked: return "기지 단계가 부족합니다";
        case RejectReason.SizeLimit: return "대형 유닛 보유 한도";
        case RejectReason.Busy: return "이미 진행 중입니다";
        case RejectReason.NoVision: return "시야 없음";
        default: return "할 수 없습니다";
      }
    }

    public static string Relation(Quantum.Relation relation) {
      switch (relation) {
        case Quantum.Relation.Allied: return "동맹";
        case Quantum.Relation.BreakPending: return "파기 유예";
        default: return "적";
      }
    }
  }
}
