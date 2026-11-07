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
      { "ff_engineer", "공병" }, { "ff_ranger_squad", "레인저 분대" }, { "ff_skyguard_squad", "스카이가드 분대" }, { "ff_eagle_tank", "이글 전차" },
      { "ff_hawk_gunship", "호크 공격헬기" }, { "ff_skyfortress_bomber", "스카이포트리스 폭격기" },
      { "cf_engineer", "공병" }, { "cf_militia", "민병대" }, { "cf_flame_squad", "화염방사 분대" }, { "cf_dragonscale_tank", "용린 전차" },
      { "cf_thunder_aa", "천둥 대공차" }, { "cf_longwall_mlrs", "만리 다연장포" },
      { "module_piercing_rounds", "관통탄" }, { "module_reactive_armor", "반응장갑" },
    };

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
      var power = $"{stock.Power.AsInt}/{state.PowerCap.AsInt} ({state.PowerRate.AsFloat:+0.0;-0.0;0}/초)";
      if (state.Outage) {
        power = $"<color={Red}>{power} 정전</color>";
      }
      return new[] { stock.Iron.AsInt.ToString(), stock.Wood.AsInt.ToString(), stock.Steel.AsInt.ToString(), power, $"{Population(state.PopulationUsed)}/{Population(state.PopulationCap)}" };
    }

    static string Population(FP value) => Mathf.FloorToInt(value.AsFloat + 0.001f).ToString();

    public static string TruceCountdown(float seconds) {
      var whole = Mathf.CeilToInt(seconds);
      return $"다리 개방까지 {whole / 60}:{whole % 60:00}";
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
