namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// Task 39 card 3: the mercenary camp layer on top of NpcTraderSystem (only on maps with MapSpec.MercenaryCamp).
  /// - Garrison (MercGuard entities) sorties against players in ThreatMask (intruders without an agreement, or attackers) inside the
  ///   leash radius, and walks back to its posts when no threat is left.
  /// - Diplomacy via TradeCommand: OfferMessage (reply after a delay) → OfferDispatch (cargo escrowed, armoured truck drives itself to
  ///   the nearest camp gate) → Agreement on arrival. A destroyed truck fails the trade and drops part of the cargo as a supply crate.
  /// - Shop: OfferShop + i buys MercenarySpec.Offers[i] (shared stock with restock timer, per-player cooldown), delivered at the gate.
  /// - Attacking the camp or a guard revokes the agreement. Without an agreement move orders into the camp stop at the gate.
  /// All numbers are MercenarySpec data; state lives in NpcTrader / MercGuard / MercCargo (rollback safe).
  /// </summary>
  public static unsafe class MercenaryCamp {
    public const int OfferMessage = 20;
    public const int OfferDispatch = 21;
    public const int OfferShop = 30;
    public const int MaxOffers = 8;
    const int Players = 4;

    public static MercenarySpec Spec(Frame f) {
      if (FactoryWarsUtil.MapInfo(f).MercenaryCamp == false) return null;
      var rules = FactoryWarsUtil.Rules(f);
      return rules.Mercenary.IsValid ? f.FindAsset(rules.Mercenary) : null;
    }

    static int Bit(PlayerRef player) => (int)player >= 0 && (int)player < Players ? 1 << (int)player : 0;

    public static bool HasAgreement(NpcTrader* trader, PlayerRef player) => (trader->AgreementMask & Bit(player)) != 0;

    public static bool Find(Frame f, out EntityRef entity, out NpcTrader* trader, out FPVector3 center) {
      var traders = f.Filter<Transform3D, NpcTrader>();
      while (traders.NextUnsafe(out entity, out var transform, out trader)) {
        center = transform->Position;
        return true;
      }
      entity = EntityRef.None;
      trader = null;
      center = FPVector3.Zero;
      return false;
    }

    /// <summary>The gate point (outside the wall, on an axis) nearest to a position.</summary>
    public static FPVector3 GateToward(MercenarySpec spec, FPVector3 center, FPVector3 from) {
      var d = from - center;
      var axis = FPMath.Abs(d.X) >= FPMath.Abs(d.Z) ? new FPVector3(d.X >= FP._0 ? FP._1 : -FP._1, FP._0, FP._0) : new FPVector3(FP._0, FP._0, d.Z >= FP._0 ? FP._1 : -FP._1);
      var p = center + axis * spec.GateDistance;
      p.Y = FP._0;
      return p;
    }

    // ---------- init ----------

    public static void Init(Frame f, NpcTrader* trader, FPVector3 center) {
      var spec = Spec(f);
      if (spec == null) return;
      for (var i = 0; i < MaxOffers; i++) {
        *(trader->Stock + (i)) = spec.Offers != null && i < spec.Offers.Length ? spec.Offers[i].MaxStock : 0;
      }
      trader->RestockFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.RestockSeconds);
      for (var i = 0; i < spec.GuardCount; i++) {
        var angle = FP.Pi * FP._2 * i / spec.GuardCount + FP.Pi / 8;
        var post = center + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle)) * spec.GuardPostRadius;
        post.Y = FP._0;
        var guard = f.Create();
        f.Add(guard, Transform3D.Create(post));
        f.Add(guard, new MercGuard { Slot = i, Post = post, Health = spec.GuardHealth, MaxHealth = spec.GuardHealth });
      }
    }

    // ---------- hostility ----------

    /// <summary>The camp (trader or a guard) was hit by this player: revoke any agreement and let the garrison engage.</summary>
    public static void Attacked(Frame f, NpcTrader* trader, PlayerRef attacker) {
      var bit = Bit(attacker);
      if (bit == 0) return;
      trader->ThreatMask |= bit;
      if ((trader->AgreementMask & bit) != 0) {
        trader->AgreementMask &= ~bit;
        trader->ContactMask &= ~bit;
        f.Events.MercDiplomacy(attacker, 6);
      }
    }

    public static bool IsValidGuardTarget(Frame f, EntityRef target) =>
      f.Unsafe.TryGetPointer<MercGuard>(target, out var guard) && guard->Health > FP._0 && FactoryWarsUtil.InTruce(f) == false;

    public static void HitGuard(Frame f, EntityRef entity, MercGuard* guard, PlayerRef attacker, FP amount) {
      if (guard->Health <= FP._0 || amount <= FP._0) return;
      if (Find(f, out _, out var trader, out _)) Attacked(f, trader, attacker);
      guard->Health -= amount;
      if (guard->Health <= FP._0) {
        guard->Health = FP._0;
        f.Events.Wrecked(f.Unsafe.GetPointer<Transform3D>(entity)->Position, (int)UnitSize.Small, FP._0);
        f.Destroy(entity);
      }
    }

    /// <summary>Without an agreement a move order into the camp stops at the nearest gate point.</summary>
    public static FPVector3 ClampEntry(Frame f, PlayerRef player, FPVector3 target) {
      var spec = Spec(f);
      if (spec == null || Find(f, out _, out var trader, out var center) == false || trader->Destroyed || HasAgreement(trader, player)) return target;
      return FactoryWarsUtil.FlatDistanceSqr(target, center) < spec.CampRadius * spec.CampRadius ? GateToward(spec, center, target) : target;
    }

    // ---------- commands ----------

    /// <summary>Handles diplomacy and shop offers. Returns false when the offer is not a camp offer (legacy trades continue).</summary>
    public static bool Trade(Frame f, PlayerRef player, int offer) {
      var spec = Spec(f);
      if (spec == null) return false;
      var state = FactoryWarsUtil.Player(f, player);
      var bit = Bit(player);
      if (Find(f, out _, out var trader, out var center) == false || state == null || trader->Destroyed || bit == 0) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Closed, 0);
        return true;
      }
      if (offer == OfferMessage) {
        if ((trader->ContactMask & bit) != 0 || *(trader->ReplyFrames + ((int)player)) > 0) {
          f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Busy, 0);
          return true;
        }
        *(trader->ReplyFrames + ((int)player)) = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.MessageDelaySeconds);
        f.Events.MercDiplomacy(player, 1);
        return true;
      }
      if (offer == OfferDispatch) {
        if ((trader->ContactMask & bit) == 0 || (trader->AgreementMask & bit) != 0 || f.Exists(*trader->Trucks.GetPointer((int)player))) {
          f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Locked, 0);
          return true;
        }
        if (FactoryWarsUtil.InTruce(f)) {
          f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Truce, 0);
          return true;
        }
        var missing = state->Stock.Missing(spec.Cargo);
        if (missing != MissingResource.None) {
          f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.InsufficientResources, (int)missing);
          return true;
        }
        state->Stock -= spec.Cargo;
        var home = FactoryWarsUtil.StartPosition(f, player);
        var toward = new FPVector3(center.X - home.X, FP._0, center.Z - home.Z).Normalized;
        var truck = ProductionSystem.SpawnUnitAt(f, player, spec.Truck, home + toward * 14, null);
        f.Add(truck, new MercCargo { Owner = player, Cargo = spec.Cargo });
        *trader->Trucks.GetPointer((int)player) = truck;
        *trader->TruckAt.GetPointer((int)player) = home;
        MovementSystem.Order(f, truck, f.Unsafe.GetPointer<Unit>(truck), UnitOrder.Move, GateToward(spec, center, home));
        f.Events.MercDiplomacy(player, 3);
        return true;
      }
      if (offer < OfferShop || offer >= OfferShop + MaxOffers) return false;
      var index = offer - OfferShop;
      if (spec.Offers == null || index >= spec.Offers.Length) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Invalid, 0);
        return true;
      }
      if (HasAgreement(trader, player) == false) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Hostile, 0);
        return true;
      }
      var line = spec.Offers[index];
      var ready = (trader->ReadyFrames + ((int)player * MaxOffers + index));
      if (f.Number < *ready) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Cooldown, 0);
        return true;
      }
      var stock = (trader->Stock + (index));
      if (*stock <= 0) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.Limit, 0);
        return true;
      }
      var lack = state->Stock.Missing(line.Cost);
      if (lack != MissingResource.None) {
        f.Events.ActionRejected(player, ActionKind.Produce, RejectReason.InsufficientResources, (int)lack);
        return true;
      }
      state->Stock -= line.Cost;
      state->Stock += line.Give;
      (*stock)--;
      *ready = f.Number + FactoryWarsUtil.SecondsToFrames(f, line.CooldownSeconds);
      state->TradesMade++;
      if (line.Unit.IsValid) {
        var gate = GateToward(spec, center, FactoryWarsUtil.StartPosition(f, player));
        var outward = (gate - center).Normalized;
        var side = new FPVector3(-outward.Z, FP._0, outward.X);
        for (var i = 0; i < line.Count; i++) {
          ProductionSystem.SpawnUnitAt(f, player, line.Unit, gate + outward * 4 + side * ((i - (line.Count - 1) / FP._2) * FP._3), null);
        }
      }
      f.Events.MercPurchase(player, index);
      return true;
    }

    // ---------- update ----------

    public static void Update(Frame f, EntityRef traderEntity, NpcTrader* trader, FPVector3 center) {
      var spec = Spec(f);
      if (spec == null || trader->Destroyed) return;
      Diplomacy(f, spec, trader, center);
      Restock(f, spec, trader);
      Threats(f, spec, trader, center);
      Garrison(f, spec, trader, center);
      if (f.Number % System.Math.Max(1, FactoryWarsUtil.SecondsToFrames(f, FP._2)) == 0) Bots(f, spec, trader, center);
    }

    static void Diplomacy(Frame f, MercenarySpec spec, NpcTrader* trader, FPVector3 center) {
      for (var p = 0; p < Players; p++) {
        var player = (PlayerRef)p;
        var reply = (trader->ReplyFrames + (p));
        if (*reply > 0 && f.Number >= *reply) {
          *reply = 0;
          trader->ContactMask |= 1 << p;
          f.Events.MercDiplomacy(player, 2);
        }
        var truck = trader->Trucks.GetPointer(p);
        if (truck->IsValid == false) continue;
        if (f.Exists(*truck) == false || f.Unsafe.TryGetPointer<Unit>(*truck, out var unit) == false || unit->Health <= FP._0) {
          // Truck lost: trade fails, part of the cargo drops as a crate anyone can grab.
          var crate = f.Create();
          f.Add(crate, Transform3D.Create(*trader->TruckAt.GetPointer(p)));
          var drop = spec.Cargo;
          drop.Iron *= spec.CargoDropShare; drop.Wood *= spec.CargoDropShare; drop.Steel *= spec.CargoDropShare; drop.Power *= spec.CargoDropShare;
          f.Add(crate, new SupplyCrate { Seat = -1, Reward = drop });
          *truck = EntityRef.None;
          f.Events.MercDiplomacy(player, 5);
          continue;
        }
        var at = f.Unsafe.GetPointer<Transform3D>(*truck)->Position;
        *trader->TruckAt.GetPointer(p) = at;
        var gate = GateToward(spec, center, FactoryWarsUtil.StartPosition(f, player));
        if (FactoryWarsUtil.FlatDistanceSqr(at, gate) <= spec.DeliveryRadius * spec.DeliveryRadius) {
          trader->AgreementMask |= 1 << p;
          trader->ThreatMask &= ~(1 << p);
          trader->HostileMask &= ~(1 << p);
          f.Destroy(*truck);
          *truck = EntityRef.None;
          f.Events.MercDiplomacy(player, 4);
        } else if (unit->Order == UnitOrder.Stop) {
          MovementSystem.Order(f, *truck, unit, UnitOrder.Move, gate);
        }
      }
    }

    static void Restock(Frame f, MercenarySpec spec, NpcTrader* trader) {
      if (f.Number < trader->RestockFrame) return;
      trader->RestockFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.RestockSeconds);
      for (var i = 0; spec.Offers != null && i < spec.Offers.Length && i < MaxOffers; i++) {
        var stock = (trader->Stock + (i));
        if (*stock < spec.Offers[i].MaxStock) (*stock)++;
      }
    }

    /// <summary>Intruders without an agreement join ThreatMask; a player leaves it once none of its units is inside the leash radius.</summary>
    static void Threats(Frame f, MercenarySpec spec, NpcTrader* trader, FPVector3 center) {
      var inside = 0;
      var near = 0;
      var camp2 = spec.CampRadius * spec.CampRadius;
      var leash2 = spec.LeashRadius * spec.LeashRadius;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        var bit = Bit(unit->Owner);
        if (bit == 0 || unit->Health <= FP._0 || unit->Garrison != EntityRef.None || f.Has<MercCargo>(entity)) continue;
        var d = FactoryWarsUtil.FlatDistanceSqr(transform->Position, center);
        if (d <= leash2) near |= bit;
        if (d <= camp2 && (trader->AgreementMask & bit) == 0) inside |= bit;
      }
      if (FactoryWarsUtil.InTruce(f) == false) trader->ThreatMask |= inside;
      trader->ThreatMask &= near;
    }

    static void Garrison(Frame f, MercenarySpec spec, NpcTrader* trader, FPVector3 center) {
      var dt = f.DeltaTime;
      var leash2 = spec.LeashRadius * spec.LeashRadius;
      var range2 = spec.GuardRange * spec.GuardRange;
      var guards = f.Filter<Transform3D, MercGuard>();
      while (guards.NextUnsafe(out var guardEntity, out var transform, out var guard)) {
        guard->Cooldown = FPMath.Max(FP._0, guard->Cooldown - dt);
        // Nearest unit of a threatening player inside the leash radius.
        var best = EntityRef.None;
        var bestDistance = FP.MaxValue;
        var bestPosition = FPVector3.Zero;
        if (trader->ThreatMask != 0) {
          var units = f.Filter<Transform3D, Unit>();
          while (units.NextUnsafe(out var entity, out var other, out var unit)) {
            if ((trader->ThreatMask & Bit(unit->Owner)) == 0 || unit->Health <= FP._0 || unit->Garrison != EntityRef.None) continue;
            if (FactoryWarsUtil.FlatDistanceSqr(other->Position, center) > leash2) continue;
            var d = FactoryWarsUtil.FlatDistanceSqr(other->Position, transform->Position);
            if (d < bestDistance) { best = entity; bestDistance = d; bestPosition = other->Position; }
          }
        }
        guard->Target = best;
        var goal = best != EntityRef.None ? bestPosition : guard->Post;
        if (best != EntityRef.None && bestDistance <= range2) {
          if (guard->Cooldown <= FP._0) {
            guard->Cooldown = spec.GuardInterval;
            Shoot(f, transform->Position, best, spec.GuardDamage);
          }
          continue;
        }
        var to = goal - transform->Position;
        to.Y = FP._0;
        var distance = to.Magnitude;
        if (distance <= FP._0_10) continue;
        var step = FPMath.Min(distance, spec.GuardSpeed * dt);
        var next = transform->Position + to / distance * step;
        // Leash: never leave the camp area.
        var off = next - center;
        off.Y = FP._0;
        if (off.SqrMagnitude > leash2) next = center + off.Normalized * spec.LeashRadius;
        next.Y = FP._0;
        transform->Position = next;
      }
    }

    static void Shoot(Frame f, FPVector3 from, EntityRef target, FP damage) {
      var victim = f.Unsafe.GetPointer<Unit>(target);
      var at = f.Unsafe.GetPointer<Transform3D>(target)->Position;
      f.Events.TraderShot(from, at);
      victim->Health -= damage;
      victim->LastDamagedFrame = f.Number;
      if (victim->Health > FP._0) return;
      var spec = f.FindAsset(victim->Spec);
      f.Events.UnitDestroyed(target, victim->Owner, victim->Spec);
      f.Events.Wrecked(at, (int)spec.Size, FP._0);
      var state = FactoryWarsUtil.Player(f, victim->Owner);
      if (state != null) state->UnitsLost++;
      f.Destroy(target);
    }

    // ---------- AI ----------

    /// <summary>Bots: message after AiContactAfterSeconds, dispatch cargo when rich, escort the truck, buy when rich. Never attack the camp.</summary>
    static void Bots(Frame f, MercenarySpec spec, NpcTrader* trader, FPVector3 center) {
      if (FactoryWarsUtil.InTruce(f)) return;
      for (var p = 0; p < Players && p < f.PlayerCount; p++) {
        var player = (PlayerRef)p;
        var state = FactoryWarsUtil.Player(f, player);
        if (state == null || state->IsBot == false || state->Active == false) continue;
        var bit = 1 << p;
        if ((trader->ContactMask & bit) == 0) {
          if (*(trader->ReplyFrames + (p)) == 0 && f.Number >= FactoryWarsUtil.SecondsToFrames(f, spec.AiContactAfterSeconds)) Trade(f, player, OfferMessage);
          continue;
        }
        var truck = *trader->Trucks.GetPointer(p);
        if ((trader->AgreementMask & bit) == 0) {
          if (truck.IsValid == false) {
            if (state->Stock.Missing(spec.Cargo + spec.AiReserve) == MissingResource.None) Trade(f, player, OfferDispatch);
          } else if (f.Exists(truck)) {
            Escort(f, spec, player, truck);
          }
          continue;
        }
        for (var i = 0; spec.Offers != null && i < spec.Offers.Length && i < MaxOffers; i++) {
          if (spec.Offers[i].Unit.IsValid == false || *(trader->Stock + (i)) <= 0 || f.Number < *(trader->ReadyFrames + (p * MaxOffers + i))) continue;
          if (state->Stock.Missing(spec.Offers[i].Cost + spec.AiReserve) != MissingResource.None) continue;
          Trade(f, player, OfferShop + i);
          break;
        }
      }
    }

    static void Escort(Frame f, MercenarySpec spec, PlayerRef player, EntityRef truck) {
      var at = f.Unsafe.GetPointer<Transform3D>(truck)->Position;
      var count = 0;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit) && count < spec.AiEscorts) {
        if (unit->Owner != player || entity == truck || unit->Health <= FP._0 || unit->Garrison != EntityRef.None) continue;
        var us = f.FindAsset(unit->Spec);
        if (us.Attack == AttackType.None || us.IsNaval || us.IsAir || us.Size == UnitSize.Engineer || us.Size == UnitSize.Small) continue;
        count++;
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, at) > 64) MovementSystem.Order(f, entity, unit, UnitOrder.AttackMove, at);
      }
    }
  }
}
