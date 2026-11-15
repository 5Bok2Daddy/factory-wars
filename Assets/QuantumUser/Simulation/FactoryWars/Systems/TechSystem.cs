namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 항목 31 테크: 기지 단계(사령부 업그레이드), 연구소 연구(크기별 공격·방어, 진영 정예화), 크기별 보유 상한.
  /// 수치는 GameRulesSpec(SizeRules·BaseTiers)와 ResearchSpec에만 있다.
  /// </summary>
  public unsafe class TechSystem : SystemMainThread {
    public const int MaxTier = 3;

    public static int Tier(Frame f, PlayerRef player) {
      var state = FactoryWarsUtil.Player(f, player);
      return state == null ? 1 : System.Math.Max(1, state->BaseTier);
    }

    public static int StartingTier(Frame f) {
      var tier = f.RuntimeConfig.StartingTier > 0 ? f.RuntimeConfig.StartingTier : FactoryWarsUtil.MapInfo(f).StartingTier;
      return System.Math.Min(System.Math.Max(tier, 1), MaxTier);
    }

    public static bool FindSizeRule(GameRulesSpec rules, UnitSize size, out SizeRuleSpec rule) {
      for (var i = 0; rules.SizeRules != null && i < rules.SizeRules.Length; i++) {
        if (rules.SizeRules[i].Size == size) {
          rule = rules.SizeRules[i];
          return true;
        }
      }
      rule = default;
      return false;
    }

    public static bool FindTierSpec(GameRulesSpec rules, int tier, out BaseTierSpec spec) {
      for (var i = 0; rules.BaseTiers != null && i < rules.BaseTiers.Length; i++) {
        if (rules.BaseTiers[i].Tier == tier) {
          spec = rules.BaseTiers[i];
          return true;
        }
      }
      spec = default;
      return false;
    }

    /// <summary>완공된 이 역할 건물 수.</summary>
    public static int CountRole(Frame f, PlayerRef player, BuildingRole role) {
      var count = 0;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player && building->ConstructionRemaining <= FP._0 && f.FindAsset(building->Spec).Role == role) {
          count++;
        }
      }
      return count;
    }

    /// <summary>단계 업그레이드의 건물 조건을 채웠으면 true. 모자란 첫 역할을 missing에 준다.</summary>
    public static bool MeetsTierRequirement(Frame f, PlayerRef player, BaseTierSpec tier, out BuildingRole missing) {
      missing = default;
      var all = true;
      for (var i = 0; tier.RequiredRoles != null && i < tier.RequiredRoles.Length; i++) {
        if (CountRole(f, player, tier.RequiredRoles[i]) == 0) {
          if (all) {
            missing = tier.RequiredRoles[i];
          }
          all = false;
        }
      }
      return all || (tier.AlternativeCount > 0 && CountRole(f, player, tier.AlternativeRole) >= tier.AlternativeCount);
    }

    /// <summary>이 사령부에서 다음 단계 업그레이드를 시작할 수 있는지. 자원은 따로 본다(reason이 InsufficientResources).</summary>
    public static bool CanUpgrade(Frame f, PlayerRef player, EntityRef entity, out RejectReason reason, out BaseTierSpec tier) {
      tier = default;
      reason = RejectReason.Invalid;
      var state = FactoryWarsUtil.Player(f, player);
      if (state == null || f.Unsafe.TryGetPointer<Building>(entity, out var building) == false || building->Owner != player
          || building->ConstructionRemaining > FP._0 || f.FindAsset(building->Spec).Role != BuildingRole.Headquarters) {
        return false;
      }
      if (building->UpgradeTier > 0 || AnyUpgrade(f, player)) {
        reason = RejectReason.Busy;
        return false;
      }
      var next = System.Math.Max(Tier(f, player), System.Math.Max(1, building->Tier)) + 1;
      if (next > MaxTier || FindTierSpec(FactoryWarsUtil.Rules(f), next, out tier) == false) {
        return false;
      }
      if (MeetsTierRequirement(f, player, tier, out _) == false) {
        reason = RejectReason.Locked;
        return false;
      }
      if (state->Stock.Covers(tier.Cost) == false) {
        reason = RejectReason.InsufficientResources;
        return false;
      }
      return true;
    }

    static bool AnyUpgrade(Frame f, PlayerRef player) {
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player && building->UpgradeTier > 0) {
          return true;
        }
      }
      return false;
    }

    public static void StartUpgrade(Frame f, PlayerRef player, EntityRef entity) {
      if (CanUpgrade(f, player, entity, out var reason, out var tier) == false) {
        var missing = reason == RejectReason.InsufficientResources && FindTierSpec(FactoryWarsUtil.Rules(f), Tier(f, player) + 1, out var wanted)
          ? (int)FactoryWarsUtil.Player(f, player)->Stock.Missing(wanted.Cost) : 0;
        f.Events.ActionRejected(player, ActionKind.Upgrade, reason, missing);
        return;
      }
      var state = FactoryWarsUtil.Player(f, player);
      var building = f.Unsafe.GetPointer<Building>(entity);
      state->Stock -= tier.Cost;
      building->UpgradeTier = tier.Tier;
      building->UpgradeRemaining = tier.Seconds;
      building->UpgradePaid = tier.Cost;
    }

    public static ResearchSpec ResearchAt(Frame f, PlayerRef player, int index) {
      var state = FactoryWarsUtil.Player(f, player);
      var list = state != null ? f.FindAsset(state->Faction).Researches : null;
      return list != null && index >= 0 && index < list.Length ? f.FindAsset(list[index]) : null;
    }

    public static int IndexOf(Frame f, PlayerRef player, AssetRef<ResearchSpec> research) {
      var list = f.FindAsset(FactoryWarsUtil.Player(f, player)->Faction).Researches;
      for (var i = 0; list != null && i < list.Length; i++) {
        if (list[i] == research) {
          return i;
        }
      }
      return -1;
    }

    public static bool IsResearched(Frame f, PlayerRef player, int index) {
      var state = FactoryWarsUtil.Player(f, player);
      return state != null && index >= 0 && index < 31 && (state->ResearchDone & (1 << index)) != 0;
    }

    /// <summary>어느 연구소에서든 진행 중이면 true.</summary>
    public static bool IsResearching(Frame f, PlayerRef player, int index) {
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player && building->Research == index + 1) {
          return true;
        }
      }
      return false;
    }

    /// <summary>연구를 시작할 수 있는지(자원 제외는 하지 않는다). 이유: TierLocked, Locked(앞 연구), Busy(진행 중·완료).</summary>
    public static bool CanResearch(Frame f, PlayerRef player, int index, out RejectReason reason) {
      var spec = ResearchAt(f, player, index);
      reason = RejectReason.Invalid;
      if (spec == null || index >= 31) {
        return false;
      }
      if (IsResearched(f, player, index) || IsResearching(f, player, index)) {
        reason = RejectReason.Busy;
        return false;
      }
      if (spec.RequiredTier > Tier(f, player)) {
        reason = RejectReason.TierLocked;
        return false;
      }
      if (spec.Requires.IsValid && IsResearched(f, player, IndexOf(f, player, spec.Requires)) == false) {
        reason = RejectReason.Locked;
        return false;
      }
      if (FactoryWarsUtil.Player(f, player)->Stock.Covers(spec.Cost) == false) {
        reason = RejectReason.InsufficientResources;
        return false;
      }
      return true;
    }

    public static void StartResearch(Frame f, PlayerRef player, EntityRef lab, int index) {
      if (f.Unsafe.TryGetPointer<Building>(lab, out var building) == false || building->Owner != player || building->ConstructionRemaining > FP._0
          || f.FindAsset(building->Spec).Role != BuildingRole.ResearchLab) {
        f.Events.ActionRejected(player, ActionKind.Research, RejectReason.Invalid, 0);
        return;
      }
      if (building->Research > 0) {
        f.Events.ActionRejected(player, ActionKind.Research, RejectReason.Busy, 0);
        return;
      }
      if (CanResearch(f, player, index, out var reason) == false) {
        var spec = ResearchAt(f, player, index);
        var missing = reason == RejectReason.InsufficientResources ? (int)FactoryWarsUtil.Player(f, player)->Stock.Missing(spec.Cost) : 0;
        f.Events.ActionRejected(player, ActionKind.Research, reason, missing);
        return;
      }
      var research = ResearchAt(f, player, index);
      FactoryWarsUtil.Player(f, player)->Stock -= research.Cost;
      building->Research = index + 1;
      building->ResearchRemaining = research.Seconds;
    }

    /// <summary>이 유닛 종류에 적용되는 정예화 연구. 없으면 null.</summary>
    public static ResearchSpec Elite(Frame f, PlayerRef owner, AssetRef<UnitSpec> unit) {
      var state = FactoryWarsUtil.Player(f, owner);
      if (state == null || state->ResearchDone == 0) {
        return null;
      }
      var list = f.FindAsset(state->Faction).Researches;
      for (var i = 0; list != null && i < list.Length && i < 31; i++) {
        if ((state->ResearchDone & (1 << i)) == 0) {
          continue;
        }
        var spec = f.FindAsset(list[i]);
        if (spec.Kind == ResearchKind.Elite && spec.EliteUnit == unit) {
          return spec;
        }
      }
      return null;
    }

    public static FP MaxHealth(Frame f, PlayerRef owner, AssetRef<UnitSpec> unit) {
      var spec = f.FindAsset(unit);
      var elite = Elite(f, owner, unit);
      return elite != null ? spec.MaxHealth * elite.EliteHealthMultiplier : spec.MaxHealth;
    }

    public static FP RangeBonus(Frame f, PlayerRef owner, AssetRef<UnitSpec> unit) {
      var elite = Elite(f, owner, unit);
      return elite != null ? elite.EliteRangeBonus : FP._0;
    }

    /// <summary>공격 연구 단계와 정예화 피해 배율.</summary>
    public static FP OutgoingMultiplier(Frame f, PlayerRef owner, Unit* unit, UnitSpec spec) {
      var state = FactoryWarsUtil.Player(f, owner);
      if (state == null || spec.Size == UnitSize.Engineer) {
        return FP._1;
      }
      var multiplier = FP._1;
      var level = state->AttackLevels[(int)spec.Size];
      if (level > 0 && FindSizeRule(FactoryWarsUtil.Rules(f), spec.Size, out var rule)) {
        multiplier += rule.AttackPerLevel * level;
      }
      if (unit != null) {
        var elite = Elite(f, owner, unit->Spec);
        if (elite != null) {
          multiplier *= elite.EliteDamageMultiplier;
        }
        // 항목 33: 상륙 직후 보너스.
        multiplier *= UltimateSystem.AssaultMultiplier(f, unit);
      }
      return multiplier;
    }

    /// <summary>방어 연구 단계만큼 받는 피해를 줄인다.</summary>
    public static FP IncomingMultiplier(Frame f, PlayerRef owner, UnitSpec spec) {
      var state = FactoryWarsUtil.Player(f, owner);
      if (state == null || spec.Size == UnitSize.Engineer) {
        return FP._1;
      }
      var level = state->ArmorLevels[(int)spec.Size];
      if (level > 0 && FindSizeRule(FactoryWarsUtil.Rules(f), spec.Size, out var rule)) {
        return FPMath.Max(FP._1 / 10, FP._1 - rule.ArmorPerLevel * level);
      }
      return FP._1;
    }

    /// <summary>이 크기 유닛 보유 수(대기열 포함, 분대는 한 번 생산을 SpawnCount기로 센다).</summary>
    public static int OwnedOfSize(Frame f, PlayerRef player, UnitSize size) {
      var count = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->Owner == player) {
          var spec = f.FindAsset(unit->Spec);
          if (spec.Size == size && spec.IsNaval == false) {
            count++;
          }
        }
      }
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner != player) {
          continue;
        }
        for (var i = 0; i < building->QueueCount; i++) {
          var spec = f.FindAsset(building->Queue[i].Unit);
          if (spec.Size == size && spec.IsNaval == false) {
            count += spec.SpawnCount;
          }
        }
      }
      return count;
    }

    /// <summary>생산 가능 여부(단계·크기 상한). 자원·인구는 ProductionSystem이 본다.</summary>
    public static bool CanProduce(Frame f, PlayerRef player, UnitSpec unit, out RejectReason reason) {
      reason = RejectReason.Invalid;
      if (unit.RequiredTier > Tier(f, player)) {
        reason = RejectReason.TierLocked;
        return false;
      }
      // 항목 37 그림자 특수전단: 저격수 보유 한도 +1.
      var ownedCap = unit.MaxOwned + (unit.GarrisonFireOnly && TacticalSystem.DoctrineOf(f, player) == 2 ? FactoryWarsUtil.Rules(f).ShadowSniperBonus : 0);
      if (unit.MaxOwned > 0 && SniperSystem.CountOwnedAndQueued(f, player, unit) + unit.SpawnCount > ownedCap) {
        reason = RejectReason.Limit;
        return false;
      }
      if (unit.IsNaval == false && FindSizeRule(FactoryWarsUtil.Rules(f), unit.Size, out var rule) && rule.OwnedLimit > 0
          && OwnedOfSize(f, player, unit.Size) + unit.SpawnCount > rule.OwnedLimit) {
        reason = RejectReason.SizeLimit;
        return false;
      }
      return true;
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var best = stackalloc int[Constants.MAX_PLAYERS];
      for (var i = 0; i < Constants.MAX_PLAYERS; i++) {
        best[i] = 0;
      }
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out var entity, out var building)) {
        if (building->ConstructionRemaining > FP._0) {
          continue;
        }
        var spec = f.FindAsset(building->Spec);
        if (spec.Role == BuildingRole.Headquarters) {
          if (building->UpgradeTier > 0) {
            building->UpgradeRemaining -= f.DeltaTime;
            if (building->UpgradeRemaining <= FP._0) {
              building->Tier = building->UpgradeTier;
              building->UpgradeTier = 0;
              building->UpgradeRemaining = FP._0;
            }
          }
          var owner = (int)building->Owner;
          if (owner >= 0 && owner < Constants.MAX_PLAYERS) {
            best[owner] = System.Math.Max(best[owner], System.Math.Max(1, building->Tier));
          }
        }
        if (building->Research > 0 && building->Paused == PauseReason.None) {
          building->ResearchRemaining -= f.DeltaTime;
          if (building->ResearchRemaining <= FP._0) {
            Complete(f, building->Owner, building->Research - 1);
            building->Research = 0;
            building->ResearchRemaining = FP._0;
          }
        }
      }

      var drops = FactoryWarsUtil.Rules(f).TierDropsWithHeadquarters;
      for (var i = 0; i < f.PlayerCount && i < Constants.MAX_PLAYERS; i++) {
        var state = f.Global->Players.GetPointer(i);
        if (state->Active == false) {
          continue;
        }
        var tier = best[i] > 0 ? best[i] : drops ? 1 : System.Math.Max(1, state->BaseTier);
        if (tier != state->BaseTier) {
          var first = state->BaseTier == 0;
          state->BaseTier = tier;
          for (var t = 1; t <= tier; t++) {
            if (state->TierFrames[t - 1] == 0) {
              state->TierFrames[t - 1] = System.Math.Max(1, f.Number);
            }
          }
          if (first == false) {
            f.Events.BaseTierChanged(i, tier);
          }
        }
      }
    }

    static void Complete(Frame f, PlayerRef player, int index) {
      var state = FactoryWarsUtil.Player(f, player);
      var spec = ResearchAt(f, player, index);
      if (state == null || spec == null) {
        return;
      }
      FP before = 0;
      var eliteRef = spec.EliteUnit;
      if (spec.Kind == ResearchKind.Elite) {
        before = MaxHealth(f, player, eliteRef);
      }
      state->ResearchDone |= 1 << index;
      var size = (int)spec.Size;
      if (spec.Kind == ResearchKind.Attack && state->AttackLevels[size] < spec.Level) {
        state->AttackLevels[size] = (byte)spec.Level;
      } else if (spec.Kind == ResearchKind.Armor && state->ArmorLevels[size] < spec.Level) {
        state->ArmorLevels[size] = (byte)spec.Level;
      } else if (spec.Kind == ResearchKind.Elite && before > FP._0) {
        // 이미 있는 유닛도 같은 비율로 체력이 오른다.
        var scale = MaxHealth(f, player, eliteRef) / before;
        var units = f.Filter<Unit>();
        while (units.NextUnsafe(out _, out var unit)) {
          if (unit->Owner == player && unit->Spec == eliteRef) {
            unit->Health *= scale;
          }
        }
      }
      f.Events.ResearchCompleted(player, index);
    }
  }
}
