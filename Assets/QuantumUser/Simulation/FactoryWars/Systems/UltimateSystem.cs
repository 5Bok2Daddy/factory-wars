namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 항목 33 진영 궁극 전력: 준비(전용 건물)·알림·취소·발동, 공중 항모와 드론, 전략 미사일과 요격, 상륙 강습.
  /// 수치는 UltimateSpec(FactionSpec.Ultimate)에만 있다. 상태는 PlayerState.Ultimate*와 Carrier·Drone·MissileStrike·LandingCraft 컴포넌트다.
  /// 시작 조건: 기지 단계 + 전용 건물 완공 + 궁극 전력 연구 + 비용. 준비 시작·절반·완료는 모든 플레이어에게 알린다.
  /// 준비·대기 중 전용 건물이 부서지면 취소되고 비용의 RefundFraction만 돌려받는다.
  /// </summary>
  public unsafe class UltimateSystem : SystemMainThread {
    public static UltimateSpec SpecOf(Frame f, PlayerRef player) {
      var state = FactoryWarsUtil.Player(f, player);
      if (state == null) {
        return null;
      }
      var faction = f.FindAsset(state->Faction);
      return faction != null && faction.Ultimate.IsValid ? f.FindAsset(faction.Ultimate) : null;
    }

    /// <summary>플레이어 명령을 받지 않고 스스로 움직이는 자율 유닛(항모 드론·오퍼레이터 드론·상륙정·자동 물류 트럭). 단, 플레이어 수동 "자원채취" 트럭(Site == -100)은 명령 가능.</summary>
    public static bool IsAutonomous(Frame f, EntityRef entity) =>
      f.Has<Drone>(entity) || f.Has<OperatorDrone>(entity) || f.Has<LandingCraft>(entity)
      || (f.Unsafe.TryGetPointer<LogisticsTruck>(entity, out var lt) && lt->Site != LogisticsSystem.PlayerPickupSite);

    /// <summary>궁극 전력에 속한 유닛(항모·드론·상륙정). 측정용 구분이다.</summary>
    public static bool IsUltimateUnit(Frame f, EntityRef entity) => f.Has<Carrier>(entity) || f.Has<Drone>(entity) || f.Has<LandingCraft>(entity);

    /// <summary>전략 미사일 격납고 배치 제한: player가 점령한 중립 거점 또는 비적대 암시장 반경(radius) 안이면 true.</summary>
    public static bool IsNearNeutralOutpost(Frame f, PlayerRef player, FPVector3 position, FP radius) {
      var r = radius > FP._0 ? radius : (FP)18;
      var rSqr = r * r;
      var sites = f.Filter<Transform3D, CaptureSite>();
      while (sites.NextUnsafe(out _, out var transform, out var site)) {
        if (site->Owner == player && FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= rSqr) {
          return true;
        }
      }
      var traders = f.Filter<Transform3D, NpcTrader>();
      var playerBit = player.IsValid ? (1 << (int)player) : 0;
      while (traders.NextUnsafe(out _, out var transform, out var trader)) {
        if (trader->Destroyed == false && (trader->HostileMask & playerBit) == 0
            && FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= rSqr) {
          return true;
        }
      }
      return false;
    }

    /// <summary>공중 항모가 50% 미만 체력으로 로터 손상 상태인지 여부.</summary>
    public static bool IsCarrierRotorDamaged(Frame f, EntityRef entity, Unit* unit) {
      if (unit == null || unit->Health <= FP._0 || f.Unsafe.TryGetPointer<Carrier>(entity, out var c) == false || c->Assembling) {
        return false;
      }
      var spec = SpecOf(f, unit->Owner);
      var ratio = spec != null && spec.CarrierDamagedRatio > FP._0 ? spec.CarrierDamagedRatio : FP._0_50;
      return unit->Health < TechSystem.MaxHealth(f, unit->Owner, unit->Spec) * ratio;
    }

    /// <summary>공중 항모 지휘 오라: 완공 비행 중인 아군 공중 항모 반경(14m) 이내인지 여부.</summary>
    public static bool InCarrierAura(Frame f, PlayerRef owner, FPVector3 position) {
      var carriers = f.Filter<Transform3D, Unit, Carrier>();
      while (carriers.NextUnsafe(out _, out var transform, out var unit, out var carrier)) {
        if (carrier->Assembling || unit->Health <= FP._0) {
          continue;
        }
        if (unit->Owner != owner && DiplomacySystem.GetRelation(f, owner, unit->Owner) != Relation.Allied) {
          continue;
        }
        var spec = SpecOf(f, unit->Owner);
        var radius = spec != null && spec.CarrierAuraRadius > FP._0 ? spec.CarrierAuraRadius : (FP)14;
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= radius * radius) {
          return true;
        }
      }
      return false;
    }

    /// <summary>초토화 구역(15초) 내부에 있는 적 지상 유닛 이동 속도 배율(0.65).</summary>
    public static FP ScorchedSpeedMultiplier(Frame f, PlayerRef owner, FPVector3 position) {
      var zones = f.Filter<ScorchedZone>();
      while (zones.NextUnsafe(out _, out var zone)) {
        if (f.Number >= zone->UntilFrame) {
          continue;
        }
        if (zone->Owner != owner && DiplomacySystem.GetRelation(f, zone->Owner, owner) != Relation.Allied
            && FactoryWarsUtil.FlatDistanceSqr(zone->Position, position) <= zone->Radius * zone->Radius) {
          return zone->SlowMultiplier > FP._0 ? zone->SlowMultiplier : (FP)65 / (FP)100;
        }
      }
      return FP._1;
    }

    static void DestroyAssemblingCarriers(Frame f, PlayerRef player) {
      var carriers = f.Filter<Unit, Carrier>();
      while (carriers.NextUnsafe(out var entity, out var unit, out var carrier)) {
        if (unit->Owner == player && carrier->Assembling) {
          unit->Health = FP._0;
          f.Destroy(entity);
        }
      }
    }

    static EntityRef FindAssemblingCarrier(Frame f, PlayerRef player) {
      var carriers = f.Filter<Unit, Carrier>();
      while (carriers.NextUnsafe(out var entity, out var unit, out var carrier)) {
        if (unit->Owner == player && carrier->Assembling && unit->Health > FP._0) {
          return entity;
        }
      }
      return EntityRef.None;
    }

    /// <summary>공중 항모 로터 손상 시 이동 속도·드론 출격 속도 배율(0.7 = -30%).</summary>
    public static FP CarrierSpeedMultiplier(Frame f, EntityRef entity, Unit* unit) {
      if (IsCarrierRotorDamaged(f, entity, unit) == false) {
        return FP._1;
      }
      var spec = SpecOf(f, unit->Owner);
      return spec != null && spec.CarrierDamagedMultiplier > FP._0 ? spec.CarrierDamagedMultiplier : (FP)70 / (FP)100;
    }

    /// <summary>완공된 전용 건물. 없으면 None.</summary>
    public static EntityRef FindSite(Frame f, PlayerRef player, UltimateSpec spec) {
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out var entity, out var building)) {
        if (building->Owner == player && building->Spec == spec.Site && building->ConstructionRemaining <= FP._0) {
          return entity;
        }
      }
      return EntityRef.None;
    }

    static bool Rearming(PlayerState* state, UltimateSpec spec) => state->UltimateUses > 0 && spec.RearmSeconds > FP._0;

    public static ResourceCost CostOf(Frame f, PlayerRef player, UltimateSpec spec) {
      return Rearming(FactoryWarsUtil.Player(f, player), spec) ? spec.RearmCost : spec.Cost;
    }

    public static FP SecondsOf(Frame f, PlayerRef player, UltimateSpec spec) {
      return Rearming(FactoryWarsUtil.Player(f, player), spec) ? spec.RearmSeconds : spec.PrepareSeconds;
    }

    public static bool HasResearch(Frame f, PlayerRef player, UltimateSpec spec) {
      return spec.Research.IsValid == false || TechSystem.IsResearched(f, player, TechSystem.IndexOf(f, player, spec.Research));
    }

    /// <summary>준비를 시작하지 못하는 이유 전부. None이면 시작할 수 있다. 화면(자물쇠 이유 목록)과 AI가 같이 쓴다.</summary>
    public static UltimateLock Locks(Frame f, PlayerRef player) {
      var state = FactoryWarsUtil.Player(f, player);
      var spec = SpecOf(f, player);
      if (state == null || spec == null) {
        return UltimateLock.NoSpec;
      }
      var locks = UltimateLock.None;
      if (state->UltimatePhase == UltimatePhase.Cooldown) {
        locks |= UltimateLock.Cooldown;
      } else if (state->UltimatePhase != UltimatePhase.None
          || (spec.Kind == UltimateKind.Carrier && CountOwned<Carrier>(f, player) >= spec.OwnedLimit)
          || (spec.Kind == UltimateKind.StrategicMissile && spec.MobileMls.IsValid
              && SniperSystem.CountOwnedAndQueued(f, player, f.FindAsset(spec.MobileMls)) >= (spec.OwnedLimit > 0 ? spec.OwnedLimit : 1))) {
        locks |= UltimateLock.Busy;
      }
      if (TechSystem.Tier(f, player) < spec.RequiredTier) {
        locks |= UltimateLock.Tier;
      }
      if (FindSite(f, player, spec) == EntityRef.None) {
        locks |= UltimateLock.Site;
      }
      if (HasResearch(f, player, spec) == false) {
        locks |= UltimateLock.Research;
      }
      if (state->Stock.Covers(CostOf(f, player, spec)) == false) {
        locks |= UltimateLock.Resources;
      }
      return locks;
    }

    static void Notice(Frame f, PlayerRef player, UltimateStage stage, UltimateSpec spec, FPVector3 position, int value) {
      // 기동형 MLS로 교체된 전략 미사일 궁극기는 발사/준비 경보 알림을 출력하지 않는다.
      if (spec != null && spec.Kind == UltimateKind.StrategicMissile) {
        return;
      }
      f.Events.UltimateNotice(player, stage, spec.Kind, position, value);
    }

    static FPVector3 PositionOf(Frame f, EntityRef entity) {
      return f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) ? transform->Position : default;
    }

    /// <summary>준비를 시작한다. 비용은 지금 낸다. 모든 플레이어에게 알리고 AI가 반응한다.</summary>
    public static bool Start(Frame f, PlayerRef player) {
      var state = FactoryWarsUtil.Player(f, player);
      var spec = SpecOf(f, player);
      var locks = Locks(f, player);
      if (locks != UltimateLock.None) {
        var reason = (locks & UltimateLock.NoSpec) != 0 ? RejectReason.Invalid
          : (locks & UltimateLock.Busy) != 0 ? RejectReason.Busy
          : (locks & UltimateLock.Cooldown) != 0 ? RejectReason.Cooldown
          : (locks & UltimateLock.Tier) != 0 ? RejectReason.TierLocked
          : (locks & (UltimateLock.Site | UltimateLock.Research)) != 0 ? RejectReason.Locked
          : RejectReason.InsufficientResources;
        var missing = reason == RejectReason.InsufficientResources ? (int)state->Stock.Missing(CostOf(f, player, spec)) : 0;
        f.Events.ActionRejected(player, ActionKind.Ultimate, reason, missing);
        return false;
      }
      var cost = CostOf(f, player, spec);
      var seconds = SecondsOf(f, player, spec);
      var site = FindSite(f, player, spec);
      state->Stock -= cost;
      state->UltimatePhase = UltimatePhase.Preparing;
      state->UltimateRemaining = seconds;
      state->UltimateTotal = seconds;
      state->UltimatePaid = cost;
      state->UltimateSite = site;
      state->UltimateHalf = false;
      if (state->UltimateStartFrame == 0) {
        state->UltimateStartFrame = f.Number;
      }
      // 공중 항모 지상 조립 취약 상태: 80초 동안 패드 위 지상 표적으로 배치되어 지상군 공격을 받는다.
      if (spec.Kind == UltimateKind.Carrier) {
        var padPos = PositionOf(f, site);
        var hull = ProductionSystem.SpawnUnitAt(f, player, spec.Carrier, padPos, null);
        if (f.Unsafe.TryGetPointer<Transform3D>(hull, out var t)) {
          t->Position = new FPVector3(padPos.X, FP._0, padPos.Z);
        }
        if (f.Unsafe.TryGetPointer<Unit>(hull, out var u)) {
          u->Health = TechSystem.MaxHealth(f, player, spec.Carrier) * FP._0_25;
          u->ReservedUntilFrame = int.MaxValue;
        }
        f.Add(hull, new Carrier { Docked = 0, Assembling = true, Site = site });
      }
      Notice(f, player, UltimateStage.Started, spec, PositionOf(f, site), FPMath.CeilToInt(seconds));
      BotSystem.OnUltimateStarted(f, player, site);
      return true;
    }

    static void Refund(PlayerState* state, FP fraction) {
      state->Stock += state->UltimatePaid * fraction;
      state->Stock.Power = FPMath.Min(state->Stock.Power, state->PowerCap);
      state->UltimatePaid = default;
    }

    static void Clear(PlayerState* state) {
      state->UltimatePhase = UltimatePhase.None;
      state->UltimateRemaining = FP._0;
      state->UltimateTotal = FP._0;
      state->UltimateSite = EntityRef.None;
      state->UltimateHalf = false;
    }

    /// <summary>준비 중이거나 발동 대기 중인 궁극 전력을 직접 취소한다. CancelRefundFraction만큼 돌려받는다.</summary>
    public static void Cancel(Frame f, PlayerRef player) {
      var state = FactoryWarsUtil.Player(f, player);
      var spec = SpecOf(f, player);
      if (state == null || spec == null || (state->UltimatePhase != UltimatePhase.Preparing && state->UltimatePhase != UltimatePhase.Ready)) {
        f.Events.ActionRejected(player, ActionKind.Ultimate, RejectReason.Invalid, 0);
        return;
      }
      var position = PositionOf(f, state->UltimateSite);
      DestroyAssemblingCarriers(f, player);
      Refund(state, spec.CancelRefundFraction);
      Clear(state);
      Notice(f, player, UltimateStage.Cancelled, spec, position, 0);
    }

    /// <summary>준비가 끝난 궁극 전력을 목표에 쓴다(미사일 발사, 상륙 출발). 항모는 준비가 끝나면 바로 나오므로 발동이 없다.</summary>
    public static void Launch(Frame f, PlayerRef player, FPVector3 target) {
      var state = FactoryWarsUtil.Player(f, player);
      var spec = SpecOf(f, player);
      if (state == null || spec == null || state->UltimatePhase != UltimatePhase.Ready) {
        f.Events.ActionRejected(player, ActionKind.Ultimate, RejectReason.Invalid, 0);
        return;
      }
      target.Y = FP._0;
      var map = FactoryWarsUtil.MapInfo(f);
      if (target.X < map.BoundsMin.X || target.X > map.BoundsMax.X || target.Z < map.BoundsMin.Y || target.Z > map.BoundsMax.Y) {
        f.Events.ActionRejected(player, ActionKind.Ultimate, RejectReason.BlockedLocation, 0);
        return;
      }
      if (spec.Kind == UltimateKind.StrategicMissile) {
        if (FactorySystem.IsCriticalLowPower(state)) {
          f.Events.ActionRejected(player, ActionKind.Ultimate, RejectReason.InsufficientResources, 0);
          return;
        }
        var origin = PositionOf(f, state->UltimateSite);
        var missileHp = spec.MissileHealth > FP._0 ? spec.MissileHealth : (FP)300;
        var entity = f.Create();
        var startPos = new FPVector3(origin.X, FP._2, origin.Z);
        f.Add(entity, Transform3D.Create(startPos));
        f.Add(entity, new MissileStrike {
          Owner = player, Spec = f.FindAsset(state->Faction).Ultimate,
          Origin = origin, Target = target, CurrentPosition = startPos,
          Health = missileHp, MaxHealth = missileHp,
          LaunchFrame = f.Number,
          ImpactFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.WarningSeconds),
        });
        // 발사 후폭풍(Backblast): 격납고 반경 6m 내 피아 구분 없이 모든 유닛에 화염·폭풍 피해.
        var backblastRadius = spec.LaunchBackblastRadius > FP._0 ? spec.LaunchBackblastRadius : (FP)6;
        var backblastDamage = spec.LaunchBackblastDamage > FP._0 ? spec.LaunchBackblastDamage : (FP)180;
        if (backblastRadius > FP._0 && backblastDamage > FP._0) {
          var blastR2 = backblastRadius * backblastRadius;
          var blastWeapon = new Weapon {
            Owner = player, Attack = AttackType.Area, IsRanged = true, Origin = origin, Area = true, IgnoreCover = true, Ultimate = true,
          };
          var units = f.Filter<Transform3D, Unit>();
          while (units.NextUnsafe(out var uEntity, out var uTransform, out var uUnit)) {
            if (uUnit->Health <= FP._0 || uUnit->Garrison != EntityRef.None || uUnit->Transport != EntityRef.None) {
              continue;
            }
            if (FactoryWarsUtil.FlatDistanceSqr(uTransform->Position, origin) <= blastR2) {
              CombatSystem.DamageUnit(f, blastWeapon, uEntity, uUnit, backblastDamage);
            }
          }
          f.Events.MissileBackblast(origin, backblastRadius);
        }
        Used(f, state);
        state->UltimatePhase = UltimatePhase.Cooldown;
        state->UltimateRemaining = spec.CooldownSeconds;
        state->UltimateTotal = spec.CooldownSeconds;
        state->UltimatePaid = default;
        Notice(f, player, UltimateStage.Launched, spec, target, FPMath.CeilToInt(spec.WarningSeconds));
        return;
      }
      if (spec.Kind == UltimateKind.AmphibiousAssault) {
        if (f.Unsafe.TryGetPointer<Building>(state->UltimateSite, out var site) == false || FindLanding(f, target, spec, out var water, out var shore) == false) {
          f.Events.ActionRejected(player, ActionKind.Ultimate, RejectReason.BlockedLocation, 0);
          return;
        }
        var origin = PositionOf(f, state->UltimateSite);
        var grace = f.Number + FactoryWarsUtil.SecondsToFrames(f, FP._2);
        for (var i = 0; i < spec.CraftCount; i++) {
          var craft = ProductionSystem.SpawnUnit(f, player, spec.LandingCraft, origin, i, site);
          var unit = f.Unsafe.GetPointer<Unit>(craft);
          // 웨이브·분대에 뽑히지 않게 묶어 둔다.
          unit->ReservedUntilFrame = int.MaxValue;
          f.Add(craft, new LandingCraft { Stage = 0, Index = i, Shore = shore, Water = water, Goal = target, Home = PositionOf(f, craft), UntilFrame = grace });
          MovementSystem.Order(f, craft, unit, UnitOrder.Move, water);
        }
        Used(f, state);
        state->UltimatePhase = UltimatePhase.Active;
        state->UltimateRemaining = FP._0;
        state->UltimatePaid = default;
        Notice(f, player, UltimateStage.Launched, spec, target, 0);
        return;
      }
      f.Events.ActionRejected(player, ActionKind.Ultimate, RejectReason.Invalid, 0);
    }

    static void Used(Frame f, PlayerState* state) {
      state->UltimateUses++;
      if (state->UltimateUseFrame == 0) {
        state->UltimateUseFrame = f.Number;
      }
    }

    /// <summary>
    /// 상륙 자리 찾기. goal에서 가장 가까운 땅(shore 후보)과 그 근처 물(water)을 찾고, 물에서 땅 쪽으로 걸어 들어가며 처음 만나는 땅을 하선 지점으로 한다.
    /// </summary>
    public static bool FindLanding(Frame f, FPVector3 goal, UltimateSpec spec, out FPVector3 water, out FPVector3 shore) {
      var rules = FactoryWarsUtil.Rules(f);
      water = goal;
      shore = goal;
      if (FactoryWarsUtil.FindNavPointNear(f, rules.NavMeshName, goal, spec.LandingReach, out var land) == false
          || FactoryWarsUtil.FindNavPointNear(f, rules.WaterNavMeshName, land, spec.LandingReach, out water) == false) {
        return false;
      }
      shore = land;
      var ground = f.Map.GetNavMesh(rules.NavMeshName);
      var mask = *f.NavMeshRegionMask;
      var delta = land - water;
      delta.Y = FP._0;
      var length = delta.Magnitude;
      if (length > FP._0) {
        var step = delta / length;
        for (var d = FP._1; d < length; d += FP._1) {
          var point = water + step * d;
          if (ground.Contains(point, mask, true)) {
            shore = point;
            break;
          }
        }
      }
      return true;
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      for (var i = 0; i < f.PlayerCount && i < Constants.MAX_PLAYERS; i++) {
        UpdatePlayer(f, i);
      }
      UpdateStrikes(f);
      UpdateScorchedZones(f);
      UpdateCarriers(f);
      UpdateOrphans(f);
      UpdateCraft(f);
    }

    static void UpdateScorchedZones(Frame f) {
      var zones = f.Filter<ScorchedZone>();
      while (zones.NextUnsafe(out var entity, out var zone)) {
        if (f.Number >= zone->UntilFrame) {
          f.Destroy(entity);
          continue;
        }
        if (f.Number < zone->StartFrame) {
          continue;
        }
        var rSqr = zone->Radius * zone->Radius;
        var dot = zone->DamagePerSecond * f.DeltaTime;
        if (dot <= FP._0) {
          continue;
        }
        var weapon = new Weapon {
          Owner = zone->Owner, Attack = AttackType.Area, IsRanged = true, Origin = zone->Position, Area = true, IgnoreCover = true, Ultimate = true,
        };
        var units = f.Filter<Transform3D, Unit>();
        while (units.NextUnsafe(out var uEntity, out var uTransform, out var uUnit)) {
          if (uUnit->Health <= FP._0 || uUnit->Garrison != EntityRef.None || uUnit->Transport != EntityRef.None || f.FindAsset(uUnit->Spec).IsAir) {
            continue;
          }
          if (zone->Owner != uUnit->Owner && DiplomacySystem.GetRelation(f, zone->Owner, uUnit->Owner) != Relation.Allied
              && FactoryWarsUtil.FlatDistanceSqr(uTransform->Position, zone->Position) <= rSqr) {
            CombatSystem.DamageUnit(f, weapon, uEntity, uUnit, dot);
          }
        }
      }
    }

    static void UpdatePlayer(Frame f, PlayerRef player) {
      var state = f.Global->Players.GetPointer(player);
      if (state->Active == false || state->UltimatePhase == UltimatePhase.None) {
        return;
      }
      var spec = SpecOf(f, player);
      if (spec == null) {
        Clear(state);
        return;
      }
      switch (state->UltimatePhase) {
        case UltimatePhase.Preparing:
        case UltimatePhase.Ready:
          if (f.Unsafe.TryGetPointer<Building>(state->UltimateSite, out var site) == false || site->Owner != player) {
            // 전용 건물을 잃었다: 취소되고 일부만 돌려받는다.
            DestroyAssemblingCarriers(f, player);
            Refund(state, spec.RefundFraction);
            Clear(state);
            state->UltimateSitesLost++;
            Notice(f, player, UltimateStage.Cancelled, spec, default, 1);
            return;
          }
          // 공중 항모 지상 조립 중 선체가 파괴되면 즉시 취소된다.
          if (state->UltimatePhase == UltimatePhase.Preparing && spec.Kind == UltimateKind.Carrier) {
            var hull = FindAssemblingCarrier(f, player);
            if (hull == EntityRef.None) {
              Refund(state, spec.RefundFraction);
              Clear(state);
              state->UltimateSitesLost++;
              Notice(f, player, UltimateStage.Cancelled, spec, PositionOf(f, state->UltimateSite), 1);
              return;
            }
            if (site->Paused == PauseReason.None && state->UltimateTotal > FP._0 && f.Unsafe.TryGetPointer<Unit>(hull, out var hullUnit)) {
              var maxHp = TechSystem.MaxHealth(f, player, hullUnit->Spec);
              var regen = maxHp * FP._0_75 / state->UltimateTotal * f.DeltaTime;
              hullUnit->Health = FPMath.Min(maxHp, hullUnit->Health + regen);
            }
          }
          // 정전이면 준비가 멈춘다.
          if (state->UltimatePhase == UltimatePhase.Ready || site->Paused != PauseReason.None) {
            return;
          }
          state->UltimateRemaining -= f.DeltaTime;
          if (state->UltimateHalf == false && state->UltimateRemaining <= state->UltimateTotal / 2) {
            state->UltimateHalf = true;
            Notice(f, player, UltimateStage.Half, spec, PositionOf(f, state->UltimateSite), FPMath.CeilToInt(state->UltimateRemaining));
          }
          if (state->UltimateRemaining <= FP._0) {
            Complete(f, player, state, spec);
          }
          return;
        case UltimatePhase.Active:
          var activeGone = spec.Kind == UltimateKind.Carrier
            ? CountOwned<Carrier>(f, player) == 0
            : (spec.Kind == UltimateKind.StrategicMissile && spec.MobileMls.IsValid)
              ? CountOwnedUnit(f, player, spec.MobileMls) == 0
              : CountOwned<LandingCraft>(f, player) == 0;
          if (activeGone) {
            state->UltimatePhase = UltimatePhase.Cooldown;
            state->UltimateRemaining = spec.CooldownSeconds;
            state->UltimateTotal = spec.CooldownSeconds;
            state->UltimateSite = EntityRef.None;
          }
          return;
        case UltimatePhase.Cooldown:
          state->UltimateRemaining -= f.DeltaTime;
          if (state->UltimateRemaining <= FP._0) {
            Clear(state);
          }
          return;
      }
    }

    static int CountOwned<T>(Frame f, PlayerRef player) where T : unmanaged, IComponent {
      var count = 0;
      var units = f.Filter<Unit, T>();
      while (units.NextUnsafe(out _, out var unit, out _)) {
        count += unit->Owner == player && unit->Health > FP._0 ? 1 : 0;
      }
      return count;
    }

    static int CountOwnedUnit(Frame f, PlayerRef player, AssetRef<UnitSpec> specRef) {
      var count = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->Owner == player && unit->Health > FP._0 && unit->Spec == specRef) {
          count++;
        }
      }
      return count;
    }

    static void Complete(Frame f, PlayerRef player, PlayerState* state, UltimateSpec spec) {
      var position = PositionOf(f, state->UltimateSite);
      state->UltimateRemaining = FP._0;
      if (state->UltimateReadyFrame == 0) {
        state->UltimateReadyFrame = f.Number;
      }
      if (spec.Kind == UltimateKind.Carrier) {
        // 지상 조립 선체가 완성되면 고도 AirAltitude로 이륙하며 완편 드론 윙(6기)을 탑재한다.
        var carrier = FindAssemblingCarrier(f, player);
        if (carrier == EntityRef.None) {
          carrier = ProductionSystem.SpawnUnitAt(f, player, spec.Carrier, position, null);
          f.Add(carrier, new Carrier { Docked = spec.DroneCount, Assembling = false, Site = state->UltimateSite });
        } else {
          var c = f.Unsafe.GetPointer<Carrier>(carrier);
          c->Assembling = false;
          c->Docked = spec.DroneCount;
          if (f.Unsafe.TryGetPointer<Transform3D>(carrier, out var ct)) {
            ct->Position = new FPVector3(ct->Position.X, FactoryWarsUtil.Rules(f).AirAltitude, ct->Position.Z);
          }
          if (f.Unsafe.TryGetPointer<Unit>(carrier, out var cu)) {
            cu->Health = TechSystem.MaxHealth(f, player, cu->Spec);
            cu->ReservedUntilFrame = 0;
          }
        }
        Used(f, state);
        state->UltimatePhase = UltimatePhase.Active;
        state->UltimatePaid = default;
      } else if (spec.Kind == UltimateKind.StrategicMissile && spec.MobileMls.IsValid) {
        // 고정 미사일 포대 대신 기동형 MLS(1기 상한) 유닛을 소환한다.
        Building* sitePtr = f.Unsafe.TryGetPointer<Building>(state->UltimateSite, out var b) ? b : null;
        ProductionSystem.SpawnUnit(f, player, spec.MobileMls, position, 0, sitePtr);
        Used(f, state);
        state->UltimatePhase = UltimatePhase.Active;
        state->UltimatePaid = default;
      } else {
        state->UltimatePhase = UltimatePhase.Ready;
      }
      Notice(f, player, UltimateStage.Ready, spec, position, 0);
    }

    // ---------- 전략 미사일 ----------

    /// <summary>
    /// 요격 점수: 착탄 지점 InterceptRadius 안, 발사자와 동맹이 아닌 플레이어의 완공된 방공포대 수 x InterceptPerAirDefense.
    /// 방공포대가 하나라도 있고 레이더 기지도 있으면 InterceptRadarBonus를 더한다. 정전인 플레이어의 것은 빼고 센다. 확률은 없다.
    /// </summary>
    public static int InterceptScore(Frame f, PlayerRef launcher, FPVector3 target, UltimateSpec spec) {
      var airDefenses = 0;
      var radars = 0;
      var radiusSqr = spec.InterceptRadius * spec.InterceptRadius;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->ConstructionRemaining > FP._0 || building->Owner == launcher
            || DiplomacySystem.GetRelation(f, launcher, building->Owner) == Relation.Allied
            || FactoryWarsUtil.FlatDistanceSqr(transform->Position, target) > radiusSqr) {
          continue;
        }
        var owner = FactoryWarsUtil.Player(f, building->Owner);
        if (owner == null || owner->Outage) {
          continue;
        }
        var role = f.FindAsset(building->Spec).Role;
        airDefenses += (role == BuildingRole.AirDefense || role == BuildingRole.PatriotTurret) ? 1 : 0;
        radars += role == BuildingRole.Radar ? 1 : 0;
      }
      if (airDefenses == 0) {
        return 0;
      }
      return airDefenses * spec.InterceptPerAirDefense + (radars > 0 ? spec.InterceptRadarBonus : 0);
    }

    /// <summary>요격 점수에 맞는 피해 배율. step은 적용된 단계(0이면 요격 없음).</summary>
    public static FP InterceptMultiplier(UltimateSpec spec, int score, out int step) {
      step = 0;
      var multiplier = FP._1;
      var best = 0;
      for (var i = 0; spec.InterceptSteps != null && i < spec.InterceptSteps.Length; i++) {
        var entry = spec.InterceptSteps[i];
        if (entry.Score > 0 && score >= entry.Score && entry.Score > best) {
          best = entry.Score;
          multiplier = entry.DamageMultiplier;
          step = i + 1;
        }
      }
      return multiplier;
    }

    static void UpdateStrikes(Frame f) {
      var strikes = f.Filter<MissileStrike>();
      while (strikes.NextUnsafe(out var entity, out var strike)) {
        var spec = f.FindAsset(strike->Spec);
        if (f.Number < strike->ImpactFrame) {
          // 6~8초 결정론적 포물선 비행 궤적 업데이트.
          var span = System.Math.Max(1, strike->ImpactFrame - strike->LaunchFrame);
          var t = FPMath.Clamp01((FP)(f.Number - strike->LaunchFrame) / (FP)span);
          var flat = FPVector3.Lerp(strike->Origin, strike->Target, t);
          var arcY = (FP)2 + (FP)4 * t * (FP._1 - t) * (FP)36;
          strike->CurrentPosition = new FPVector3(flat.X, arcY, flat.Z);
          if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var mTransform)) {
            mTransform->Position = strike->CurrentPosition;
          }
          // 패트리어트 대탄도 요격 포대: 사거리 내 비행 중인 적 전략 미사일을 자동 요격.
          var destroyedInFlight = false;
          var buildings = f.Filter<Transform3D, Building>();
          while (buildings.NextUnsafe(out _, out var bTransform, out var building)) {
            if (building->ConstructionRemaining > FP._0 || building->Health <= FP._0 || building->Paused != PauseReason.None
                || building->Owner == strike->Owner || DiplomacySystem.GetRelation(f, strike->Owner, building->Owner) == Relation.Allied) {
              continue;
            }
            var bOwner = FactoryWarsUtil.Player(f, building->Owner);
            if (bOwner == null || FactorySystem.IsCriticalLowPower(bOwner)) {
              continue;
            }
            var bSpec = f.FindAsset(building->Spec);
            if (bSpec.InterceptRange <= FP._0 || bSpec.InterceptDamage <= FP._0 || building->AttackCooldown > FP._0) {
              continue;
            }
            if (FactoryWarsUtil.FlatDistanceSqr(bTransform->Position, strike->CurrentPosition) <= bSpec.InterceptRange * bSpec.InterceptRange) {
              building->AttackCooldown = bSpec.InterceptInterval > FP._0 ? bSpec.InterceptInterval : FP._0_75;
              strike->Health -= bSpec.InterceptDamage;
              var killed = strike->Health <= FP._0;
              f.Events.MissileInterceptShot(bTransform->Position + new FPVector3(FP._0, FP._3, FP._0), strike->CurrentPosition, killed);
              if (killed) {
                var shooter = FactoryWarsUtil.Player(f, strike->Owner);
                if (shooter != null) {
                  shooter->UltimateIntercepted++;
                }
                Notice(f, strike->Owner, UltimateStage.Impact, spec, strike->CurrentPosition, -1);
                f.Destroy(entity);
                destroyedInFlight = true;
                break;
              }
            }
          }
          if (destroyedInFlight) {
            continue;
          }
          continue;
        }
        var hpRatio = strike->MaxHealth > FP._0 ? FPMath.Clamp01(strike->Health / strike->MaxHealth) : FP._1;
        var scoreMul = InterceptMultiplier(spec, InterceptScore(f, strike->Owner, strike->Target, spec), out var step);
        var multiplier = FPMath.Min(hpRatio, scoreMul);
        if (hpRatio < FP._1 && step == 0) {
          step = 1;
        }
        var weapon = new Weapon {
          Owner = strike->Owner, Attack = AttackType.None, IsRanged = true, Origin = strike->Target, Area = true, IgnoreCover = true, Ultimate = true,
        };
        CombatSystem.Blast(f, weapon, strike->Target, spec.BlastRadius, spec.BlastDamage * multiplier, spec.BlastEdgeFraction,
          spec.BlastBuildingMultiplier, spec.BlastCityMultiplier, spec.FriendlyFire);
        // EMP 충격파(5초): 생존한 적 방어 포탑·생산 건물을 일시 마비시킨다.
        var empSec = spec.EmpDurationSeconds > FP._0 ? spec.EmpDurationSeconds : (FP)5;
        var empUntil = f.Number + FactoryWarsUtil.SecondsToFrames(f, empSec);
        var blastR2 = spec.BlastRadius * spec.BlastRadius;
        var empBuildings = f.Filter<Transform3D, Building>();
        while (empBuildings.NextUnsafe(out _, out var bt, out var b)) {
          if (b->Health <= FP._0 || b->Owner == strike->Owner || DiplomacySystem.GetRelation(f, strike->Owner, b->Owner) == Relation.Allied) {
            continue;
          }
          if (FactoryWarsUtil.FlatDistanceSqr(bt->Position, strike->Target) <= blastR2) {
            var bs = f.FindAsset(b->Spec);
            if (bs.Attack != AttackType.None || (bs.Producible != null && bs.Producible.Length > 0)) {
              b->EmpUntilFrame = System.Math.Max(b->EmpUntilFrame, empUntil);
            }
          }
        }
        // 초토화·방사능 구역(15초): 적 지상 유닛 지속 화상 피해 및 이동 속도 저하.
        var scorchSec = spec.ScorchedDurationSeconds > FP._0 ? spec.ScorchedDurationSeconds : (FP)15;
        var scorchDps = spec.ScorchedDamagePerSecond > FP._0 ? spec.ScorchedDamagePerSecond : (FP)28;
        var scorchSlow = spec.ScorchedSlowMultiplier > FP._0 ? spec.ScorchedSlowMultiplier : (FP)65 / (FP)100;
        var zoneEntity = f.Create();
        f.Add(zoneEntity, Transform3D.Create(strike->Target));
        f.Add(zoneEntity, new ScorchedZone {
          Owner = strike->Owner, Position = strike->Target, Radius = spec.BlastRadius,
          DamagePerSecond = scorchDps, SlowMultiplier = scorchSlow,
          StartFrame = f.Number + 1,
          UntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, scorchSec),
        });
        var owner = FactoryWarsUtil.Player(f, strike->Owner);
        if (owner != null && step > 0) {
          owner->UltimateIntercepted++;
        }
        Notice(f, strike->Owner, UltimateStage.Impact, spec, strike->Target, step);
        f.Destroy(entity);
      }
    }

    // ---------- 공중 항모 ----------

    static void UpdateCarriers(Frame f) {
      var carriers = f.Filter<Transform3D, Unit, Carrier>();
      while (carriers.NextUnsafe(out var entity, out var transform, out var unit, out var carrier)) {
        var spec = SpecOf(f, unit->Owner);
        var state = FactoryWarsUtil.Player(f, unit->Owner);
        if (spec == null || state == null || unit->Health <= FP._0 || carrier->Assembling) {
          continue;
        }
        var droneSpec = f.FindAsset(spec.Drone);
        var flying = 0;
        var counted = f.Filter<Drone>();
        while (counted.NextUnsafe(out _, out var other)) {
          flying += other->Carrier == entity ? 1 : 0;
        }
        // 잃은 드론을 다시 만든다. 자원이 모자라면 다 만든 채로 기다린다.
        if (carrier->Docked + flying < spec.DroneCount) {
          carrier->BuildProgress = FPMath.Min(spec.DroneBuildSeconds, carrier->BuildProgress + f.DeltaTime);
          if (carrier->BuildProgress >= spec.DroneBuildSeconds && state->Stock.Covers(spec.DroneCost)) {
            state->Stock -= spec.DroneCost;
            carrier->Docked++;
            carrier->BuildProgress = FP._0;
          }
        } else {
          carrier->BuildProgress = FP._0;
        }

        // 표적: 항모 반경 안에서 드론이 맞힐 수 있는 가장 가까운 적. 반 초마다 다시 찾는다.
        var position = transform->Position;
        if (carrier->Target != EntityRef.None && CombatSystem.CanEngage(f, unit->Owner, droneSpec, carrier->Target, position, spec.DroneRadius) == false) {
          carrier->Target = EntityRef.None;
        }
        if (carrier->Target == EntityRef.None && (entity.Index + f.Number) % FactoryWarsUtil.SecondsToFrames(f, FP._0_50) == 0) {
          carrier->Target = CombatSystem.FindTargetFor(f, unit->Owner, droneSpec, position, spec.DroneRadius);
        }
        if (carrier->Target != EntityRef.None) {
          carrier->LastContactFrame = f.Number;
        }
        var engaged = carrier->LastContactFrame > 0 && f.Number - carrier->LastContactFrame <= FactoryWarsUtil.SecondsToFrames(f, spec.DroneRecoverSeconds);

        var rateFactor = CarrierSpeedMultiplier(f, entity, unit);
        carrier->LaunchCooldown = FPMath.Max(FP._0, carrier->LaunchCooldown - f.DeltaTime * rateFactor);
        if (carrier->Target != EntityRef.None && carrier->Docked > 0 && carrier->LaunchCooldown <= FP._0) {
          var angle = FP.PiTimes2 * (flying % 8) / 8;
          var drone = ProductionSystem.SpawnUnitAt(f, unit->Owner, spec.Drone, position + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle)), null);
          f.Add(drone, new Drone { Carrier = entity });
          f.Unsafe.GetPointer<Unit>(drone)->ReservedUntilFrame = int.MaxValue;
          carrier->Docked--;
          carrier->LaunchCooldown = spec.DroneLaunchSeconds;
        }

        var radiusSqr = spec.DroneRadius * spec.DroneRadius;
        var dockSqr = spec.DroneDockDistance * spec.DroneDockDistance;
        var drones = f.Filter<Transform3D, Unit, Drone>();
        while (drones.NextUnsafe(out var droneEntity, out var droneTransform, out var droneUnit, out var link)) {
          if (link->Carrier != entity) {
            continue;
          }
          var away = FactoryWarsUtil.FlatDistanceSqr(droneTransform->Position, position);
          if (carrier->Target != EntityRef.None && away <= radiusSqr) {
            if (droneUnit->TargetEntity != carrier->Target || droneUnit->Order != UnitOrder.AttackTarget) {
              droneUnit->TargetEntity = carrier->Target;
              droneUnit->Order = UnitOrder.AttackTarget;
            }
            continue;
          }
          // 표적이 없거나 반경을 벗어났다: 항모로 돌아온다. 한동안 적이 없으면 격납한다.
          droneUnit->TargetEntity = EntityRef.None;
          droneUnit->Order = UnitOrder.Move;
          droneUnit->TargetPosition = position;
          if (engaged == false && away <= dockSqr) {
            carrier->Docked++;
            f.Destroy(droneEntity);
          }
        }
      }
    }

    /// <summary>항모가 격추되면 그 드론도 함께 추락한다.</summary>
    static void UpdateOrphans(Frame f) {
      var drones = f.Filter<Transform3D, Unit, Drone>();
      while (drones.NextUnsafe(out var entity, out var transform, out var unit, out var drone)) {
        if (f.Exists(drone->Carrier) && f.Has<Carrier>(drone->Carrier)) {
          continue;
        }
        f.Events.UnitDestroyed(entity, unit->Owner, unit->Spec);
        f.Events.Wrecked(transform->Position, (int)UnitSize.Small, FP._0);
        f.Destroy(entity);
      }
    }

    /// <summary>CombatSystem이 항모가 부서질 때 부른다(알림·측정).</summary>
    public static void OnUnitKilled(Frame f, EntityRef entity, Unit* unit) {
      var isCarrier = f.Unsafe.TryGetPointer<Carrier>(entity, out var carrier);
      if (isCarrier && carrier->Assembling) {
        return;
      }
      if (isCarrier == false && f.Unsafe.TryGetPointer<LandingCraft>(entity, out _) == false) {
        return;
      }
      var state = FactoryWarsUtil.Player(f, unit->Owner);
      var spec = SpecOf(f, unit->Owner);
      if (state == null || spec == null) {
        return;
      }
      // 상륙정은 하선 전에 가라앉았을 때만 손실로 센다(탑재 부대도 함께 잃는다).
      if (isCarrier || f.Unsafe.GetPointer<LandingCraft>(entity)->Stage == 0) {
        state->UltimateLost++;
        Notice(f, unit->Owner, UltimateStage.Lost, spec, PositionOf(f, entity), 0);
      }
    }

    // ---------- 상륙 강습 ----------

    static void UpdateCraft(Frame f) {
      var crafts = f.Filter<Transform3D, Unit, LandingCraft>();
      while (crafts.NextUnsafe(out var entity, out var transform, out var unit, out var craft)) {
        var spec = SpecOf(f, unit->Owner);
        if (spec == null || unit->Health <= FP._0) {
          continue;
        }
        var reach = spec.UnloadDistance * spec.UnloadDistance;
        if (craft->Stage == 0) {
          var near = FactoryWarsUtil.FlatDistanceSqr(transform->Position, craft->Water) <= reach;
          // 길을 못 찾았거나 막혀서 멈췄으면, 해안이 닿는 거리일 때만 내린다.
          var stalled = unit->Order != UnitOrder.Move && f.Number >= craft->UntilFrame;
          if (near == false && stalled == false) {
            continue;
          }
          if (near || FactoryWarsUtil.FlatDistanceSqr(transform->Position, craft->Shore) <= spec.LandingReach * spec.LandingReach) {
            Unload(f, unit, craft, spec);
            craft->Stage = 1;
            craft->UntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.SupportSeconds);
            MovementSystem.Stop(f, entity, unit);
            unit->Order = UnitOrder.Hold;
          } else {
            craft->Stage = 2;
            MovementSystem.Order(f, entity, unit, UnitOrder.Move, craft->Home);
          }
        } else if (craft->Stage == 1) {
          if (f.Number >= craft->UntilFrame) {
            craft->Stage = 2;
            MovementSystem.Order(f, entity, unit, UnitOrder.Move, craft->Home);
          }
        } else if (unit->Order != UnitOrder.Move || FactoryWarsUtil.FlatDistanceSqr(transform->Position, craft->Home) <= reach) {
          // 도크로 돌아오면 사라진다(다음 강습에 다시 쓴다).
          f.Destroy(entity);
        }
      }
    }

    static void Unload(Frame f, Unit* unit, LandingCraft* craft, UltimateSpec spec) {
      var rules = FactoryWarsUtil.Rules(f);
      var ground = f.Map.GetNavMesh(rules.NavMeshName);
      var mask = *f.NavMeshRegionMask;
      var until = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.LandedBonusSeconds);
      var slot = 0;
      for (var i = 0; spec.Cargo != null && i < spec.Cargo.Length; i++) {
        if (spec.CraftCount > 0 && i % spec.CraftCount != craft->Index) {
          continue;
        }
        var angle = FP.PiTimes2 * (slot % 8) / 8;
        var position = craft->Shore + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle)) * (FP._2 + slot / 8);
        if (ground == null || ground.Contains(position, mask, true) == false) {
          position = craft->Shore;
        }
        slot++;
        var cargo = ProductionSystem.SpawnUnitAt(f, unit->Owner, spec.Cargo[i], position, null);
        var landed = f.Unsafe.GetPointer<Unit>(cargo);
        landed->AssaultUntilFrame = until;
        MovementSystem.Order(f, cargo, landed, UnitOrder.AttackMove, craft->Goal);
      }
      Notice(f, unit->Owner, UltimateStage.Landed, spec, craft->Shore, slot);
    }

    /// <summary>상륙 직후 공격력 보너스(TechSystem.OutgoingMultiplier가 곱한다).</summary>
    public static FP AssaultMultiplier(Frame f, Unit* unit) {
      if (unit == null || unit->AssaultUntilFrame <= f.Number) {
        return FP._1;
      }
      var spec = SpecOf(f, unit->Owner);
      return spec != null ? spec.LandedDamageMultiplier : FP._1;
    }

    /// <summary>PowerSystem이 부른다: 준비·대기·보유 중인 전용 건물이면 유지비를 낸다.</summary>
    public static bool WantsPower(Frame f, EntityRef entity, Building* building) {
      var state = FactoryWarsUtil.Player(f, building->Owner);
      return state != null && state->UltimateSite == entity
        && (state->UltimatePhase == UltimatePhase.Preparing || state->UltimatePhase == UltimatePhase.Ready || state->UltimatePhase == UltimatePhase.Active);
    }
  }
}
