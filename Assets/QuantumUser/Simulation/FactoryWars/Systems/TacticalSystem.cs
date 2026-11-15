namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 항목 37 전술 깊이(일부): 베테랑 계급, 전술 도크린 3택 1, 맵별 기상.
  /// 모든 수치는 GameRulesSpec·MapSpec·FactionSpec 데이터이고 계산은 FP·정수·프레임 수뿐이다(결정론).
  /// 기상은 프레임 번호만의 함수(맵 데이터의 첫 시각·지속·주기)라 리플레이와 온라인에서 같다.
  /// </summary>
  public unsafe class TacticalSystem : SystemMainThread {
    public const int Players = 4;

    // ---------- 기상 ----------

    public static bool WeatherActive(Frame f) {
      var map = FactoryWarsUtil.MapInfo(f);
      if (map.WeatherPeriodSeconds <= 0 || map.WeatherDurationSeconds <= 0) {
        return false;
      }
      var first = FactoryWarsUtil.SecondsToFrames(f, map.WeatherFirstSeconds);
      if (f.Number < first) {
        return false;
      }
      var period = System.Math.Max(1, FactoryWarsUtil.SecondsToFrames(f, map.WeatherPeriodSeconds));
      var duration = FactoryWarsUtil.SecondsToFrames(f, map.WeatherDurationSeconds);
      return (f.Number - first) % period < duration;
    }

    /// <summary>기상 중 기본 시야 배율(없으면 1). 신호탄·정찰 사진 구역은 이 배율을 받지 않는다.</summary>
    public static FP SightMultiplier(Frame f) {
      if (WeatherActive(f) == false) {
        return FP._1;
      }
      var value = FactoryWarsUtil.MapInfo(f).WeatherSightMultiplier;
      return value > FP._0 ? value : FP._1;
    }

    // ---------- 베테랑 ----------

    public static int RankFor(GameRulesSpec rules, int kills) =>
      rules.VetKillsRank3 > 0 && kills >= rules.VetKillsRank3 ? 3 : rules.VetKillsRank2 > 0 && kills >= rules.VetKillsRank2 ? 2 : rules.VetKillsRank1 > 0 && kills >= rules.VetKillsRank1 ? 1 : 0;

    static FP Pick(FP[] values, int rank) => values != null && rank > 0 && rank <= values.Length ? values[rank - 1] : FP._0;

    public static FP VetDamage(Frame f, Unit* unit) => unit == null || unit->VetRank <= 0 ? FP._1 : FP._1 + Pick(FactoryWarsUtil.Rules(f).VetDamageBonus, unit->VetRank);

    public static FP VetRange(Frame f, Unit* unit) => unit == null || unit->VetRank <= 0 ? FP._1 : FP._1 + Pick(FactoryWarsUtil.Rules(f).VetRangeBonus, unit->VetRank);

    /// <summary>재장전 시간 배율(1보다 작을수록 빠름).</summary>
    public static FP VetReload(Frame f, Unit* unit) => unit == null || unit->VetRank <= 0 ? FP._1 : FP._1 - Pick(FactoryWarsUtil.Rules(f).VetReloadBonus, unit->VetRank);

    /// <summary>킬 하나를 센다. 기준을 넘으면 승급하고 체력 보너스 차이만큼 현재 체력을 올린다.</summary>
    public static void OnKill(Frame f, Unit* killer) {
      if (killer == null || killer->Health <= FP._0) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      killer->Kills++;
      var rank = RankFor(rules, killer->Kills);
      if (rank <= killer->VetRank) {
        return;
      }
      var spec = f.FindAsset(killer->Spec);
      var before = Pick(rules.VetHealthBonus, killer->VetRank);
      killer->VetRank = rank;
      killer->Health += spec.MaxHealth * (Pick(rules.VetHealthBonus, rank) - before);
      var state = FactoryWarsUtil.Player(f, killer->Owner);
      if (state != null) {
        state->Promotions++;
      }
      f.Events.VeteranPromoted(killer->Owner, rank);
    }

    // ---------- 도크린 ----------

    public static int DoctrineOf(Frame f, PlayerRef owner) {
      var state = FactoryWarsUtil.Player(f, owner);
      return state != null && state->DoctrineLocked ? state->Doctrine : 0;
    }

    /// <summary>전차·포격함(지상 중형 이상 대장갑 또는 해군)인가: 기갑 요새화 군단의 생산비 할인 대상.</summary>
    public static bool IsArmorClass(UnitSpec spec) =>
      spec.IsAir == false && spec.Size >= UnitSize.Medium && (spec.Attack == AttackType.AntiArmor || (spec.IsNaval && spec.Attack == AttackType.Area));

    public static void Choose(Frame f, PlayerRef player, int doctrine, bool byDefault) {
      var state = FactoryWarsUtil.Player(f, player);
      if (state == null || state->DoctrineLocked || doctrine < 1 || doctrine > 3) {
        f.Events.ActionRejected(player, ActionKind.Research, RejectReason.Doctrine, 0);
        return;
      }
      state->Doctrine = doctrine;
      state->DoctrineLocked = true;
      f.Events.DoctrineChosen(player, doctrine, byDefault);
    }

    // ---------- 갱신 ----------

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var weather = WeatherActive(f);
      if (weather != (bool)f.Global->WeatherOn) {
        f.Global->WeatherOn = weather;
        f.Events.WeatherChanged(weather);
      }
      // 도크린: AI는 시작 직후 진영 성향대로, 사람은 선택 시간이 끝나면 기본값으로 정해진다.
      var deadline = FactoryWarsUtil.SecondsToFrames(f, rules.DoctrineSelectSeconds);
      for (var p = 0; p < Players && p < f.PlayerCount; p++) {
        var state = FactoryWarsUtil.Player(f, (PlayerRef)p);
        if (state == null || state->Active == false || state->DoctrineLocked) {
          continue;
        }
        if (state->IsBot && f.Number >= 2) {
          var preferred = f.FindAsset(state->Faction).PreferredDoctrine;
          Choose(f, (PlayerRef)p, preferred > 0 ? preferred : rules.DefaultDoctrine, true);
        } else if (f.Number >= deadline) {
          Choose(f, (PlayerRef)p, rules.DefaultDoctrine, true);
        }
      }
      // 베테랑 자가 회복: 마지막으로 쏘거나 맞은 뒤 VetRegenDelaySeconds가 지나면 초당 비율만큼.
      var delay = FactoryWarsUtil.SecondsToFrames(f, rules.VetRegenDelaySeconds);
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->VetRank <= 0 || unit->Health <= FP._0 || f.Number - System.Math.Max(unit->LastFiredFrame, unit->LastDamagedFrame) < delay) {
          continue;
        }
        var spec = f.FindAsset(unit->Spec);
        var cap = spec.MaxHealth * (FP._1 + Pick(rules.VetHealthBonus, unit->VetRank));
        if (unit->Health < cap) {
          unit->Health = FPMath.Min(cap, unit->Health + spec.MaxHealth * Pick(rules.VetRegenPerSecond, unit->VetRank) * f.DeltaTime);
        }
      }
    }
  }
}
