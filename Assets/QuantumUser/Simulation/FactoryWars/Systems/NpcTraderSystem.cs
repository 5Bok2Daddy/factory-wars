namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 중앙 철조망 암시장(NPC 무기상). 평화 상태에서는 거래(용병 투입·자폭 드론 타격·비행장 긴급 재정비)를 팔고,
  /// 누군가 피해를 주면 그 플레이어는 영구 적대(HostileMask 비트, 거래 영구 잠금)가 되고 경비 화력이 그 플레이어 유닛을 쏜다.
  /// 파괴한 플레이어는 한 번의 큰 현상금 자원을 받고 암시장은 모두에게 영구히 닫힌다. 수치는 전부 GameRulesSpec 데이터, 계산은 FP·프레임 수.
  /// </summary>
  public unsafe class NpcTraderSystem : SystemMainThread {
    public const int OfferMercenaries = 1;
    public const int OfferDrone = 2;
    public const int OfferRefuel = 3;
    const int Players = 4;

    public override void OnInit(Frame f) {
      var map = FactoryWarsUtil.MapInfo(f);
      if (map.HasTrader == false) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var entity = f.Create();
      f.Add(entity, Transform3D.Create(map.TraderPosition));
      f.Add(entity, new NpcTrader { Health = rules.TraderHealth, MaxHealth = rules.TraderHealth, Killer = PlayerRef.None });
      // Task 39 card 3: on camp maps the trader is the mercenary camp (garrison, stock).
      MercenaryCamp.Init(f, f.Unsafe.GetPointer<NpcTrader>(entity), map.TraderPosition);
    }

    public static bool IsHostile(NpcTrader* trader, PlayerRef player) {
      var index = (int)player;
      return index >= 0 && index < Players && (trader->HostileMask & (1 << index)) != 0;
    }

    /// <summary>피해: 공격한 플레이어를 영구 적대로 바꾸고, 체력이 다하면 파괴·현상금.</summary>
    public static void Hit(Frame f, EntityRef entity, NpcTrader* trader, PlayerRef attacker, FP amount) {
      if (trader->Destroyed || amount <= FP._0) {
        return;
      }
      var index = (int)attacker;
      if (MercenaryCamp.Spec(f) != null) {
        MercenaryCamp.Attacked(f, trader, attacker);
      }
      if (index >= 0 && index < Players && (trader->HostileMask & (1 << index)) == 0) {
        trader->HostileMask |= 1 << index;
        f.Events.TraderHostile(attacker);
      }
      trader->Health -= amount;
      if (trader->Health > FP._0) {
        return;
      }
      trader->Health = FP._0;
      trader->Destroyed = true;
      trader->Killer = attacker;
      var rules = FactoryWarsUtil.Rules(f);
      var state = FactoryWarsUtil.Player(f, attacker);
      if (state != null) {
        state->Stock += rules.TraderBounty;
      }
      f.Events.TraderDestroyed(attacker, rules.TraderBounty);
    }

    /// <summary>거래. 실패하면 이유를 알린다(적대·닫힘·대기·자원·시야).</summary>
    public static void Trade(Frame f, PlayerRef player, int offer, FPVector3 target) {
      if (MercenaryCamp.Trade(f, player, offer)) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var state = FactoryWarsUtil.Player(f, player);
      NpcTrader* trader = null;
      var position = FPVector3.Zero;
      var traders = f.Filter<Transform3D, NpcTrader>();
      while (traders.NextUnsafe(out _, out var transform, out var found)) {
        trader = found;
        position = transform->Position;
      }
      if (trader == null || state == null || trader->Destroyed) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Closed, 0);
        return;
      }
      if (IsHostile(trader, player) || (MercenaryCamp.Spec(f) != null && MercenaryCamp.HasAgreement(trader, player) == false)) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Hostile, 0);
        return;
      }
      if (f.Number < state->TradeReadyFrame) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Cooldown, 0);
        return;
      }
      var cost = offer == OfferMercenaries ? rules.TraderMercCost : offer == OfferDrone ? rules.TraderDroneCost : offer == OfferRefuel ? rules.TraderRefuelCost : default;
      if (offer < OfferMercenaries || offer > OfferRefuel) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Invalid, 0);
        return;
      }
      if (offer == OfferDrone && FogSystem.IsVisible(f, player, target) == false) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.NoVision, 0);
        return;
      }
      var missing = state->Stock.Missing(cost);
      if (missing != MissingResource.None) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.InsufficientResources, (int)missing);
        return;
      }
      state->Stock -= cost;
      state->TradeReadyFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.TraderCooldownSeconds);
      state->TradesMade++;
      switch (offer) {
        case OfferMercenaries: {
          // 용병: 진영 기본 보병을 암시장 출입구(본진 쪽)에 투입.
          var faction = f.FindAsset(state->Faction);
          var unit = faction.StartingUnits != null && faction.StartingUnits.Length > 0 ? faction.StartingUnits[0] : default;
          var home = FactoryWarsUtil.StartPosition(f, player);
          var toward = new FPVector3(home.X - position.X, FP._0, home.Z - position.Z);
          toward = toward.SqrMagnitude > FP._0 ? toward.Normalized : new FPVector3(FP._1, FP._0, FP._0);
          var side = new FPVector3(-toward.Z, FP._0, toward.X);
          for (var i = 0; unit.IsValid && i < rules.TraderMercCount; i++) {
            var at = position + toward * (rules.TraderRadius + FP._3) + side * ((i - (rules.TraderMercCount - 1) / FP._2) * FP._2);
            ProductionSystem.SpawnUnitAt(f, player, unit, at, null);
          }
          break;
        }
        case OfferDrone: {
          var weapon = new Weapon { Owner = player, Attack = AttackType.Area, IsAir = true, IsRanged = true, Origin = target, Area = true };
          CombatSystem.Blast(f, weapon, target, rules.TraderDroneRadius, rules.TraderDroneDamage, FP._0_50, FP._1, FP._1, false);
          break;
        }
        case OfferRefuel: {
          var buildings = f.Filter<Building>();
          while (buildings.NextUnsafe(out _, out var building)) {
            if (building->Owner != player) {
              continue;
            }
            for (var i = 0; i < SortieSystem.Slots; i++) {
              var slot = building->Hangar.GetPointer(i);
              if (slot->State == 1) {
                slot->ReadyFrame = f.Number;
              }
            }
          }
          state->SortieTickets++;
          break;
        }
      }
      f.Events.TraderTrade(player, offer);
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var traders = f.Filter<Transform3D, NpcTrader>();
      while (traders.NextUnsafe(out var traderEntity, out var transform, out var trader)) {
        MercenaryCamp.Update(f, traderEntity, trader, transform->Position);
        if (trader->Destroyed || trader->HostileMask == 0) {
          continue;
        }
        trader->Cooldown = FPMath.Max(FP._0, trader->Cooldown - f.DeltaTime);
        if (trader->Cooldown > FP._0) {
          continue;
        }
        // 경비 화력: 적대한 플레이어의 가장 가까운 유닛(사거리 안)을 쏜다.
        var best = EntityRef.None;
        var bestDistance = rules.TraderRange * rules.TraderRange;
        var units = f.Filter<Transform3D, Unit>();
        while (units.NextUnsafe(out var entity, out var other, out var unit)) {
          if (unit->Health <= FP._0 || unit->Garrison != EntityRef.None || IsHostile(trader, unit->Owner) == false) {
            continue;
          }
          var distance = FactoryWarsUtil.FlatDistanceSqr(other->Position, transform->Position);
          if (distance < bestDistance) {
            best = entity;
            bestDistance = distance;
          }
        }
        if (best == EntityRef.None) {
          continue;
        }
        trader->Cooldown = rules.TraderInterval;
        trader->Target = best;
        var victim = f.Unsafe.GetPointer<Unit>(best);
        var targetPosition = f.Unsafe.GetPointer<Transform3D>(best)->Position;
        f.Events.TraderShot(transform->Position, targetPosition);
        victim->Health -= rules.TraderDamage;
        victim->LastDamagedFrame = f.Number;
        if (victim->Health <= FP._0) {
          var spec = f.FindAsset(victim->Spec);
          f.Events.UnitDestroyed(best, victim->Owner, victim->Spec);
          f.Events.Wrecked(targetPosition, (int)spec.Size, FP._0);
          FactoryWarsUtil.Player(f, victim->Owner)->UnitsLost++;
          f.Destroy(best);
        }
      }
    }
  }
}
