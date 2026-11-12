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
      { "headquarters", "사령부" }, { "power_plant", "발전소" }, { "processor", "가공소" }, { "supply_depot", "보급창" }, { "barracks", "병영" },
      { "research_lab", "연구소" }, { "vehicle_factory", "군수공장" }, { "airfield", "비행장" }, { "heavy_works", "중공업 기지" },
      { "ground_tower", "지상 방어탑" }, { "air_defense", "방공포대" }, { "collection_yard", "집하장" },
      { "shipyard", "조선소" },
      { "sandbags", "모래주머니 진지" }, { "bunker", "벙커" }, { "medic_tent", "의무대" }, { "repair_bay", "정비소" },
      { "radar", "레이더 기지" }, { "helipad", "헬기장" }, { "artillery", "포병 진지" }, { "scout_boat", "정찰선" }, { "artillery_ship", "포격함" },
      { "ff_engineer", "공병" }, { "ff_ranger_squad", "레인저 분대" }, { "ff_skyguard_squad", "스카이가드 분대" }, { "ff_eagle_tank", "이글 전차" },
      { "ff_hawk_gunship", "호크 공격헬기" }, { "ff_skyfortress_bomber", "스카이포트리스 폭격기" },
      { "cf_engineer", "공병" }, { "cf_militia", "민병대" }, { "cf_flame_squad", "화염방사 분대" }, { "cf_dragonscale_tank", "용린 전차" },
      { "cf_thunder_aa", "천둥 대공차" }, { "cf_longwall_mlrs", "만리 다연장포" },
      // 항목 31 새 유닛(가칭, 실존 무기명과 무관)
      { "ff_scout_buggy", "정찰 버기" }, { "ff_field_support", "야전 정비차" }, { "ff_bulwark_tank", "방벽 중전차" },
      { "cf_scout_bike", "척후 오토바이" }, { "cf_gale_gunship", "돌풍 공격헬기" }, { "cf_fortress_tank", "요새 전차" },
      { "module_piercing_rounds", "관통탄" }, { "module_reactive_armor", "반응장갑" },
    };

    /// <summary>크기 배지 글자(항목 31).</summary>
    public static string SizeBadge(UnitSize size) => size == UnitSize.Large ? "대" : size == UnitSize.Medium ? "중" : size == UnitSize.Small ? "소" : "공병";

    public static string SizeWord(UnitSize size) => size == UnitSize.Large ? "대형" : size == UnitSize.Medium ? "중형" : size == UnitSize.Small ? "소형" : "공병";

    static string SizeName(UnitSize size, bool air) => air ? $"{SizeWord(size)} 공중" : SizeWord(size);

    public static string TierName(int tier) => tier >= 3 ? "사단 본부(3단계)" : tier == 2 ? "연대 본부(2단계)" : "기지 1단계";

    /// <summary>연구 이름: "소형 공격 +1", "정예 레인저 분대".</summary>
    public static string ResearchName(ResearchSpec spec, System.Func<AssetRef<UnitSpec>, string> unitName = null) {
      if (spec.Kind == ResearchKind.Elite) {
        return "정예화: " + (unitName != null ? unitName(spec.EliteUnit) : "소형");
      }
      return $"{SizeWord(spec.Size)} {(spec.Kind == ResearchKind.Attack ? "공격" : "방어")} +{spec.Level}";
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

    static void Part(StringBuilder text, string label, FP cost, FP stock) {
      if (cost <= FP._0) {
        return;
      }
      if (text.Length > 0) {
        text.Append(' ');
      }
      var part = $"{label} {cost.AsInt}";
      text.Append(stock < cost ? $"<color={Red}>{part}</color>" : part);
    }

    /// <summary>비용 표기. 모자란 항목은 빨간색이다.</summary>
    public static string Cost(ResourceCost cost, ResourceCost stock) {
      var text = new StringBuilder();
      Part(text, "철", cost.Iron, stock.Iron);
      Part(text, "목재", cost.Wood, stock.Wood);
      Part(text, "강철", cost.Steel, stock.Steel);
      Part(text, "전기", cost.Power, stock.Power);
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

    public static string SiteName(CaptureKind kind) => kind == CaptureKind.Comms ? "중앙 통신소" : "발전 중계소";

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
