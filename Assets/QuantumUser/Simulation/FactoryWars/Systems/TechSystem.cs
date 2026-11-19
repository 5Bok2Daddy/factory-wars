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
      if (f.Unsafe.TryGetPointer<Building>(lab, out var building) == false || building->Owner != player || building->ConstructionRemaining > FP._0) {
        f.Events.ActionRejected(player, ActionKind.Research, RejectReason.Invalid, 0);
        return;
      }
      var role = f.FindAsset(building->Spec).Role;
      if (role != BuildingRole.ResearchLab && role != BuildingRole.Headquarters) {
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

    /// <summary>유닛 업그레이드 단계(Mk.1 / Mk.2 / Mk.3). 해당 크기 공격·방어 연구 최고 단계 기준.</summary>
    public static int UnitMark(Frame f, PlayerRef owner, UnitSpec spec) {
      var state = FactoryWarsUtil.Player(f, owner);
      if (state == null || spec == null || spec.Size == UnitSize.Engineer) {
        return 1;
      }
      var size = (int)spec.Size;
      var level = System.Math.Max(state->AttackLevels[size], state->ArmorLevels[size]);
      return level >= 2 ? 3 : level >= 1 ? 2 : 1;
    }

    public static string FactionIdOf(Frame f, PlayerRef owner) {
      var state = FactoryWarsUtil.Player(f, owner);
      return state != null ? f.FindAsset(state->Faction).FactionId : "";
    }

    /// <summary>진영 특화 업그레이드 완료 여부.</summary>
    public static bool HasSpecial(Frame f, PlayerRef owner, FactionSpecial special) {
      if (special == FactionSpecial.None) {
        return false;
      }
      var state = FactoryWarsUtil.Player(f, owner);
      if (state == null || state->ResearchDone == 0) {
        return false;
      }
      var list = f.FindAsset(state->Faction).Researches;
      for (var i = 0; list != null && i < list.Length && i < 31; i++) {
        if ((state->ResearchDone & (1 << i)) == 0) {
          continue;
        }
        var spec = f.FindAsset(list[i]);
        if (spec.Kind == ResearchKind.Special && spec.Special == special) {
          return true;
        }
      }
      return false;
    }

    /// <summary>자유 연방 고유 패시브(전술 네트워크): 아군 사령부(25m) 또는 가동 중 레이더 반경 안이면 true.</summary>
    public static bool InTacticalNetwork(Frame f, PlayerRef owner, FPVector3 position) {
      if (FactionIdOf(f, owner) != "free_federation") {
        return false;
      }
      if (SupportSystem.InRadar(f, owner, position)) {
        return true;
      }
      var hqRadiusSqr = (FP)25 * (FP)25;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->Owner == owner && building->ConstructionRemaining <= FP._0
            && f.FindAsset(building->Spec).Role == BuildingRole.Headquarters
            && FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= hqRadiusSqr) {
          return true;
        }
      }
      return false;
    }

    /// <summary>대륙 연방 고유 패시브(참호 교리): 지상 유닛이 엄폐 또는 정지(Hold) 진지 고수 상태면 true.</summary>
    public static bool IsEntrenched(Frame f, EntityRef entity, Unit* unit, UnitSpec spec) {
      if (unit == null || spec == null || spec.IsAir || spec.IsNaval || FactionIdOf(f, unit->Owner) != "continental_federation") {
        return false;
      }
      return unit->InCover || unit->Order == UnitOrder.Hold;
    }

    /// <summary>공격 간격 배율(낮을수록 연사 속도 상승). 자유 연방 전술 네트워크(+10%) + 고속 볼트(+20%) + 공중 항모 지휘 오라(+15%).</summary>
    public static FP AttackCooldownMultiplier(Frame f, PlayerRef owner, UnitSpec spec, FPVector3 position) {
      var mul = FP._1;
      if (InTacticalNetwork(f, owner, position)) {
        mul = mul * (FP)10 / (FP)11;
      }
      if (spec != null && spec.Size == UnitSize.Small && spec.IsVehicle == false && spec.IsNaval == false
          && HasSpecial(f, owner, FactionSpecial.RapidCycleBolts)) {
        mul = mul * (FP)5 / (FP)6;
      }
      if (spec != null && spec.IsAir == false && UltimateSystem.InCarrierAura(f, owner, position)) {
        mul = mul * (FP)100 / (FP)115;
      }
      return mul;
    }

    /// <summary>이동 속도 배율. 자유 연방 전술 네트워크(+8%), 제3진영 신속 전개(+25%), 공중 항모 지휘 오라(+10%).</summary>
    public static FP MoveSpeedMultiplier(Frame f, PlayerRef owner, UnitSpec spec, FPVector3 position) {
      var mul = FP._1;
      if (InTacticalNetwork(f, owner, position)) {
        mul = mul * (FP)108 / (FP)100;
      }
      if (HasSpecial(f, owner, FactionSpecial.RapidDeployment)) {
        mul = mul * (FP)125 / (FP)100;
      }
      if (spec != null && spec.IsAir == false && UltimateSystem.InCarrierAura(f, owner, position)) {
        mul = mul * (FP)110 / (FP)100;
      }
      return mul;
    }

    /// <summary>진영 패시브·특화 연구에 따른 피격 피해 배율(대륙 참호 -15%, 자유 스텔스 공중 -15%, 제3진영 전술 연막 -15%).</summary>
    public static FP SpecialIncomingMultiplier(Frame f, EntityRef entity, Unit* unit, UnitSpec spec) {
      var mul = FP._1;
      if (IsEntrenched(f, entity, unit, spec)) {
        mul = mul * (FP)85 / (FP)100;
      }
      if (spec != null && spec.IsAir && HasSpecial(f, unit->Owner, FactionSpecial.StealthCoating)) {
        mul = mul * (FP)85 / (FP)100;
      }
      if (HasSpecial(f, unit->Owner, FactionSpecial.TacticalSmoke)) {
        mul = mul * (FP)85 / (FP)100;
      }
      return mul;
    }

    /// <summary>건물 공격 배율(대륙 연방 고폭 철갑탄 +30%).</summary>
    public static FP StructureDamageMultiplier(Frame f, PlayerRef attacker) {
      return HasSpecial(f, attacker, FactionSpecial.HighExplosiveAp) ? (FP)130 / (FP)100 : FP._1;
    }

    /// <summary>폭발 반경 배율(대륙 연방 반응장갑·포병 강장 +25%).</summary>
    public static FP SplashRadiusMultiplier(Frame f, PlayerRef attacker, UnitSpec spec) {
      if (spec != null && spec.IsAir == false && spec.IsNaval == false && spec.Size >= UnitSize.Medium
          && HasSpecial(f, attacker, FactionSpecial.ReactiveArmorEra)) {
        return (FP)125 / (FP)100;
      }
      return FP._1;
    }

    /// <summary>작전기 재정비 시간 배율(자유 연방 스텔스 코팅 -25%).</summary>
    public static FP SortieRearmMultiplier(Frame f, PlayerRef owner) {
      return HasSpecial(f, owner, FactionSpecial.StealthCoating) ? FP._0_75 : FP._1;
    }

    /// <summary>제3진영 고유 패시브(전장 노획): 적 유닛 격파 시 철 +15, 강철 +5 즉시 획득.</summary>
    public static void OnEnemyDestroyed(Frame f, PlayerRef killer, PlayerRef victim) {
      if (killer.IsValid == false || killer == victim || FactionIdOf(f, killer) != "test_assault") {
        return;
      }
      var state = FactoryWarsUtil.Player(f, killer);
      if (state != null) {
        state->Stock.Iron += (FP)15;
        state->Stock.Steel += (FP)5;
      }
    }

    public static FP MaxHealth(Frame f, PlayerRef owner, AssetRef<UnitSpec> unit) {
      var spec = f.FindAsset(unit);
      var elite = Elite(f, owner, unit);
      var max = elite != null ? spec.MaxHealth * elite.EliteHealthMultiplier : spec.MaxHealth;
      if (spec.IsAir == false && spec.IsNaval == false && spec.Size >= UnitSize.Medium
          && HasSpecial(f, owner, FactionSpecial.ReactiveArmorEra)) {
        max = max * (FP)125 / (FP)100;
      }
      return max;
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
      } else if (spec.Kind == ResearchKind.Special && spec.Special == FactionSpecial.ReactiveArmorEra) {
        var scale = (FP)125 / (FP)100;
        var units = f.Filter<Unit>();
        while (units.NextUnsafe(out _, out var unit)) {
          if (unit->Owner == player && unit->Health > FP._0) {
            var uSpec = f.FindAsset(unit->Spec);
            if (uSpec.IsAir == false && uSpec.IsNaval == false && uSpec.Size >= UnitSize.Medium) {
              unit->Health = FPMath.Min(MaxHealth(f, player, unit->Spec), unit->Health * scale);
            }
          }
        }
      }
      f.Events.ResearchCompleted(player, index);
    }
  }
}
