namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// Task 41: expansion-site logistics (maps with MapSpec.LogisticsEconomy and GameRulesSpec.Logistics).
  /// - One LogisticsYard per edge expansion site (ExpansionKind.Shared), created on the first frame.
  /// - Workers delivering to a drop-off building inside a site radius fill that yard's Buffer instead of the stock
  ///   (WorkerSystem asks <see cref="TryDeposit"/>); a full buffer stops extraction (the worker waits).
  /// - Yard owner = the player with a finished drop-off building inside the radius (keeps the current owner while it still has one).
  /// - One automatic truck per owned yard: spawned at the owner's dock (garage + dock = fixed base fixture in front of the start
  ///   position), drives to the yard bay, loads up to ShipmentSize, returns to the dock and credits the cargo ON UNLOAD exactly
  ///   once (DeliveryId / CreditedId), then repeats.
  /// - Truck lost: a share of the cargo drops as a lootable crate, a new truck after RespawnSeconds. Owner change: the old owner's
  ///   truck aborts (an empty truck drives home and is removed; a loaded one still delivers what it carries). The buffer stays with
  ///   the site and the new owner's truck picks it up.
  /// - No progress while driving for StuckSeconds → WaitingRoute, re-order (the NavMesh picks an alternative route if one exists).
  /// All numbers are LogisticsSpec data; the state is in LogisticsYard / LogisticsTruck components (rollback safe).
  /// </summary>
  public unsafe class LogisticsSystem : SystemMainThread {
    public static LogisticsSpec Spec(Frame f) {
      if (FactoryWarsUtil.MapInfo(f).LogisticsEconomy == false) return null;
      var rules = FactoryWarsUtil.Rules(f);
      return rules.Logistics.IsValid ? f.FindAsset(rules.Logistics) : null;
    }

    public override void Update(Frame f) {
      var spec = Spec(f);
      if (spec == null) return;
      EnsureYards(f);
      var yards = f.Filter<LogisticsYard>();
      while (yards.NextUnsafe(out var entity, out var yard)) {
        UpdateOwner(f, spec, yard);
        UpdateTruck(f, spec, yard);
      }
      UpdateOrphans(f, spec);
      // Trucks whose yard lost them (aborted, owner changed) drive home and are removed there.
      var trucks = f.Filter<Transform3D, Unit, LogisticsTruck>();
      while (trucks.NextUnsafe(out var entity, out var transform, out var unit, out var truck)) {
        if (truck->State == LogisticsState.Aborting) Abort(f, spec, entity, transform, unit, truck);
      }
    }

    static void EnsureYards(Frame f) {
      if (f.ComponentCount<LogisticsYard>() > 0) return;
      var sites = FactoryWarsUtil.MapInfo(f).Expansions;
      for (var i = 0; sites != null && i < sites.Length; i++) {
        if (sites[i].Kind != ExpansionKind.Shared) continue;
        var e = f.Create();
        f.Add(e, new LogisticsYard { Site = i, Position = sites[i].Position, Owner = PlayerRef.None, Truck = EntityRef.None });
      }
    }

    /// <summary>The yard whose radius contains <paramref name="position"/>, or null.</summary>
    public static LogisticsYard* YardAt(Frame f, LogisticsSpec spec, FPVector3 position) {
      var yards = f.Filter<LogisticsYard>();
      while (yards.NextUnsafe(out _, out var yard)) {
        if (FactoryWarsUtil.FlatDistanceSqr(yard->Position, position) <= spec.SiteRadius * spec.SiteRadius) return yard;
      }
      return null;
    }

    static FP Total(ResourceCost c) => c.Iron + c.Wood + c.Steel + c.Power;

    /// <summary>
    /// WorkerSystem: a worker of <paramref name="owner"/> unloads <paramref name="amount"/> at a drop-off building at
    /// <paramref name="dropOff"/>. Returns false when the drop-off is not in a yard (normal stock credit). When it is in a yard,
    /// <paramref name="accepted"/> says whether it went into the buffer (false = buffer full or not this player's yard → wait).
    /// </summary>
    public static bool TryDeposit(Frame f, PlayerRef owner, FPVector3 dropOff, ResourceKind kind, FP amount, out bool accepted) {
      accepted = false;
      var spec = Spec(f);
      if (spec == null) return false;
      var yard = YardAt(f, spec, dropOff);
      if (yard == null) return false;
      if (yard->Owner != owner || Total(yard->Buffer) + amount > spec.BufferCapacity) {
        if (yard->Owner == owner && f.Number % FactoryWarsUtil.SecondsToFrames(f, 10) == 0) f.Events.LogisticsNotice(owner, yard->Site, 4, yard->Buffer);
        return true;
      }
      if (kind == ResourceKind.Iron) yard->Buffer.Iron += amount; else yard->Buffer.Wood += amount;
      accepted = true;
      return true;
    }

    static void UpdateOwner(Frame f, LogisticsSpec spec, LogisticsYard* yard) {
      if (f.Number % 10 != 0) return;
      var keep = false;
      var best = PlayerRef.None;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->ConstructionRemaining > FP._0 || f.FindAsset(building->Spec).IsDropOff == false) continue;
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, yard->Position) > spec.SiteRadius * spec.SiteRadius) continue;
        if (building->Owner == yard->Owner) keep = true;
        if (best == PlayerRef.None || (int)building->Owner < (int)best) best = building->Owner;
      }
      if (keep || best == yard->Owner) return;
      var old = yard->Owner;
      yard->Owner = best;
      yard->RespawnFrame = f.Number;
      if (f.Exists(yard->Truck) && f.Unsafe.TryGetPointer<LogisticsTruck>(yard->Truck, out var truck)) {
        // The old owner's truck aborts: loaded → finish delivering what it carries; empty → drive home and leave.
        truck->State = Total(truck->Cargo) > FP._0 && truck->State != LogisticsState.Unloading ? LogisticsState.ToDock : truck->State == LogisticsState.Unloading ? LogisticsState.Unloading : LogisticsState.Aborting;
        truck->Site = -1 - truck->Site;
        truck->Detour = 0;
      }
      yard->Truck = EntityRef.None;
      yard->InTransit = default;
      if (old != PlayerRef.None) f.Events.LogisticsNotice(old, yard->Site, 5, yard->Buffer);
      if (best != PlayerRef.None) f.Events.LogisticsNotice(best, yard->Site, 5, yard->Buffer);
    }

    /// <summary>Dock bay of <paramref name="owner"/> for this site: in front of the start position, one bay per site.</summary>
    public static FPVector3 DockOf(Frame f, LogisticsSpec spec, PlayerRef owner, int site) {
      var start = FactoryWarsUtil.StartPosition(f, owner);
      var inward = FPVector3.Zero - start;
      inward.Y = FP._0;
      inward = inward.SqrMagnitude > FP._0 ? inward.Normalized : FPVector3.Forward;
      var side = new FPVector3(-inward.Z, FP._0, inward.X);
      var slot = site % 4 - FP._1 - FP._0_50;
      return start + inward * spec.DockForward + side * (spec.DockSpacing * slot);
    }

    /// <summary>Yard bay: inside the site, pulled from the site centre toward the owner's base (off the ring road).</summary>
    public static FPVector3 BayOf(Frame f, LogisticsSpec spec, LogisticsYard* yard) {
      var toBase = FactoryWarsUtil.StartPosition(f, yard->Owner) - yard->Position;
      toBase.Y = FP._0;
      return toBase.SqrMagnitude > FP._0 ? yard->Position + toBase.Normalized * spec.YardInset : yard->Position;
    }

    static void UpdateTruck(Frame f, LogisticsSpec spec, LogisticsYard* yard) {
      if (yard->Owner == PlayerRef.None || FactoryWarsUtil.Player(f, yard->Owner) == null) return;
      // Lost truck: drop part of the in-transit cargo as a crate anyone can grab, then a new truck after RespawnSeconds.
      if (yard->Truck != EntityRef.None && (f.Exists(yard->Truck) == false || f.Unsafe.GetPointer<Unit>(yard->Truck)->Health <= FP._0)) {
        if (Total(yard->InTransit) > FP._0) {
          var drop = yard->InTransit;
          drop.Iron *= spec.CrateShare; drop.Wood *= spec.CrateShare; drop.Steel *= spec.CrateShare; drop.Power *= spec.CrateShare;
          var crate = f.Create();
          f.Add(crate, Transform3D.Create(yard->TruckAt));
          f.Add(crate, new SupplyCrate { Seat = -1, Reward = drop });
        }
        f.Events.LogisticsNotice(yard->Owner, yard->Site, 2, yard->InTransit);
        yard->Truck = EntityRef.None;
        yard->InTransit = default;
        yard->RespawnFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.RespawnSeconds);
      }
      if (yard->Truck == EntityRef.None) {
        if (f.Number < yard->RespawnFrame || spec.Truck.IsValid == false) return;
        var truckEntity = ProductionSystem.SpawnUnitAt(f, yard->Owner, spec.Truck, DockOf(f, spec, yard->Owner, yard->Site), null);
        if (truckEntity == EntityRef.None) return;
        f.Add(truckEntity, new LogisticsTruck { Site = yard->Site, State = LogisticsState.ToYard });
        f.Unsafe.GetPointer<Unit>(truckEntity)->ReservedUntilFrame = int.MaxValue;
        yard->Truck = truckEntity;
        yard->TruckAt = f.Unsafe.GetPointer<Transform3D>(truckEntity)->Position;
      }
      var entity = yard->Truck;
      var unit = f.Unsafe.GetPointer<Unit>(entity);
      var t = f.Unsafe.GetPointer<LogisticsTruck>(entity);
      var at = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
      yard->TruckAt = at;
      var bay = BayOf(f, spec, yard);
      var dock = DockOf(f, spec, yard->Owner, yard->Site);
      var arrive = spec.ArriveRadius * spec.ArriveRadius;
      switch (t->State) {
        case LogisticsState.ToYard:
          if (FactoryWarsUtil.FlatDistanceSqr(at, bay) <= arrive) { Stop(f, entity, unit, t, LogisticsState.Loading); break; }
          Drive(f, spec, entity, unit, t, at, bay);
          break;
        case LogisticsState.Loading: {
          var ready = Total(yard->Buffer) >= FPMath.Min(spec.MinLoad, spec.ShipmentSize) || Total(yard->Buffer) >= spec.BufferCapacity;
          if (ready == false) { t->Timer = FP._0; break; }
          t->Timer += f.DeltaTime;
          if (t->Timer < spec.LoadSeconds) break;
          // Take up to ShipmentSize, iron first then wood (deterministic split).
          var room = spec.ShipmentSize;
          var iron = FPMath.Min(room, yard->Buffer.Iron);
          var wood = FPMath.Min(room - iron, yard->Buffer.Wood);
          yard->Buffer.Iron -= iron;
          yard->Buffer.Wood -= wood;
          t->Cargo = new ResourceCost { Iron = iron, Wood = wood };
          yard->InTransit = t->Cargo;
          t->DeliveryId++;
          t->State = LogisticsState.ToDock;
          t->Timer = FP._0;
          f.Events.LogisticsNotice(yard->Owner, yard->Site, 0, t->Cargo);
          break;
        }
        case LogisticsState.ToDock:
          if (FactoryWarsUtil.FlatDistanceSqr(at, dock) <= arrive) { Stop(f, entity, unit, t, LogisticsState.Unloading); break; }
          Drive(f, spec, entity, unit, t, at, dock);
          break;
        case LogisticsState.Unloading:
          t->Timer += f.DeltaTime;
          if (t->Timer < spec.UnloadSeconds) break;
          if (Credit(f, yard->Owner, yard->Site, t)) yard->Delivered++;
          yard->InTransit = default;
          t->State = LogisticsState.ToYard;
          t->Timer = FP._0;
          break;
        case LogisticsState.WaitingRoute:
          t->Timer += f.DeltaTime;
          if (t->Timer >= FP._2) {
            t->StuckTimer = FP._0;
            t->Detour = 0;
            t->State = t->Resume;
            t->Timer = FP._0;
            unit->Order = UnitOrder.Stop;
          }
          break;
      }
    }

    /// <summary>Credits the cargo once per DeliveryId (an interrupted/retried unload can never pay twice).</summary>
    static bool Credit(Frame f, PlayerRef owner, int site, LogisticsTruck* t) {
      if (t->CreditedId == t->DeliveryId || Total(t->Cargo) <= FP._0) {
        t->Cargo = default;
        return false;
      }
      var state = FactoryWarsUtil.Player(f, owner);
      if (state != null) {
        state->Stock += t->Cargo;
        state->Earned += t->Cargo;
      }
      t->CreditedId = t->DeliveryId;
      f.Events.LogisticsNotice(owner, site, 1, t->Cargo);
      t->Cargo = default;
      return true;
    }

    static void Stop(Frame f, EntityRef entity, Unit* unit, LogisticsTruck* t, LogisticsState next) {
      MovementSystem.Halt(f, entity);
      unit->Order = UnitOrder.Stop;
      t->State = next;
      t->Timer = FP._0;
      t->StuckTimer = FP._0;
      t->Detour = 0;
    }

    static void Drive(Frame f, LogisticsSpec spec, EntityRef entity, Unit* unit, LogisticsTruck* t, FPVector3 at, FPVector3 target) {
      // Task 46: a visible hostile armed ship covering the route → drive to an inland waypoint first, then finish the leg.
      // Detour: odd = heading to waypoint n, even = after it; at most 3 waypoints per leg (Stop / re-route resets it to 0).
      if (t->Detour % 2 == 1) {
        var near = spec.ArriveRadius * 2;
        if (FactoryWarsUtil.FlatDistanceSqr(at, t->Waypoint) <= near * near) t->Detour++;
        else target = t->Waypoint;
      } else if (t->Detour < 6 && f.Number % 10 == 0 && TryDetour(f, spec, entity, unit, at, target, out var waypoint)) {
        t->Detour++;
        t->Waypoint = waypoint;
        t->Detours++;
        target = waypoint;
        if (t->Detour == 1) f.Events.LogisticsNotice(unit->Owner, t->Site < 0 ? -1 - t->Site : t->Site, 6, default);
      }
      if (unit->Order != UnitOrder.Move || FactoryWarsUtil.FlatDistanceSqr(unit->TargetPosition, target) > FP._1) {
        MovementSystem.Order(f, entity, unit, UnitOrder.Move, target);
        t->LastPosition = at;
        t->StuckTimer = FP._0;
        return;
      }
      // Progress = 2 m away from the last anchor (a truck jiggling in place against an obstacle is stuck, not moving).
      if (FactoryWarsUtil.FlatDistanceSqr(at, t->LastPosition) > FP._4) {
        t->LastPosition = at;
        t->StuckTimer = FP._0;
        return;
      }
      t->StuckTimer += f.DeltaTime;
      if (t->StuckTimer >= spec.StuckSeconds) {
        t->Resume = t->State;
        t->State = LogisticsState.WaitingRoute;
        t->Timer = FP._0;
        f.Events.LogisticsNotice(unit->Owner, t->Site, 3, default);
      }
    }

    /// <summary>
    /// Task 46: the deepest intrusion of a visible hostile armed ship's circle (Range + ThreatMargin) into the truck's remaining
    /// route (current NavMesh path corners, then the leg end). The waypoint is that route point pushed straight away from the ship
    /// to the circle edge (at most DetourMax), snapped to free ground. False when nothing threatens the route or no ground is free.
    /// </summary>
    public static bool TryDetour(Frame f, LogisticsSpec spec, EntityRef entity, Unit* truck, FPVector3 at, FPVector3 target, out FPVector3 waypoint) {
      waypoint = target;
      var bestDepth = FP._0;
      var bestPoint = FPVector3.Zero;
      var bestAway = FPVector3.Zero;
      var from = at;
      // Only the route the truck really drives: wait until the NavMesh path to this leg's end exists (the straight line can cross
      // the ship itself and give no usable side).
      if (f.Unsafe.TryGetPointer<NavMeshPathfinder>(entity, out var path) == false || path->IsActive == false
          || FactoryWarsUtil.FlatDistanceSqr(path->Target, target) > spec.ArriveRadius * spec.ArriveRadius) return false;
      for (var i = path->WaypointIndex; i < path->WaypointCount; i++) {
        var corner = path->GetWaypoint(f, i);
        Threat(f, spec, truck->Owner, from, corner, ref bestDepth, ref bestPoint, ref bestAway);
        from = corner;
      }
      Threat(f, spec, truck->Owner, from, target, ref bestDepth, ref bestPoint, ref bestAway);
      if (bestDepth <= FP._0) return false;
      var point = FactoryWarsUtil.MapInfo(f).ClampToPlayable(bestPoint + bestAway * FPMath.Min(bestDepth, spec.DetourMax));
      if (GroundOccupancy.TryGoal(f, point, MovementSystem.UnitRadius(f.FindAsset(truck->Spec)), out waypoint) == false) return false;
      // A waypoint next to the truck or the leg's end is no detour.
      var minimum = spec.ArriveRadius * 2;
      return FactoryWarsUtil.FlatDistanceSqr(waypoint, at) > minimum * minimum && FactoryWarsUtil.FlatDistanceSqr(waypoint, target) > minimum * minimum;
    }

    static void Threat(Frame f, LogisticsSpec spec, PlayerRef owner, FPVector3 a, FPVector3 b, ref FP bestDepth, ref FPVector3 bestPoint, ref FPVector3 bestAway) {
      var leg = b - a;
      leg.Y = FP._0;
      var length = leg.SqrMagnitude;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Health <= FP._0 || DiplomacySystem.CanAttack(f, owner, unit->Owner) == false) continue;
        var shipSpec = f.FindAsset(unit->Spec);
        if (shipSpec.IsNaval == false || shipSpec.Attack == AttackType.None || FactoryWarsUtil.IsVisibleTo(f, owner, entity) == false) continue;
        var s = transform->Position;
        var u = length > FP._0 ? FPMath.Clamp01(FPVector3.Dot(s - a, leg) / length) : FP._0;
        var p = a + leg * u;
        var radius = shipSpec.Range + spec.ThreatMargin;
        var gap = p - s;
        gap.Y = FP._0;
        var distance = gap.Magnitude;
        if (distance >= radius || radius - distance <= bestDepth) continue;
        // Ship (almost) on the route: no side to prefer → inland, toward the map centre.
        if (distance < FP._2) {
          gap = FPVector3.Zero - p;
          gap.Y = FP._0;
          distance = gap.Magnitude;
          if (distance < FP._0_10) continue;
        }
        bestDepth = radius - distance;
        bestPoint = p;
        bestAway = gap / distance;
      }
    }

    /// <summary>Owner changed: an empty truck drives home and is removed; a truck still carrying cargo finishes its delivery first.</summary>
    static void Abort(Frame f, LogisticsSpec spec, EntityRef entity, Transform3D* transform, Unit* unit, LogisticsTruck* t) {
      var site = t->Site < 0 ? -1 - t->Site : t->Site;
      var dock = DockOf(f, spec, unit->Owner, site);
      if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, dock) <= spec.ArriveRadius * spec.ArriveRadius) {
        f.Destroy(entity);
        return;
      }
      Drive(f, spec, entity, unit, t, transform->Position, dock);
    }

    /// <summary>Orphaned loaded trucks (owner changed while carrying) finish their delivery, then go home.</summary>
    public static void UpdateOrphans(Frame f, LogisticsSpec spec) {
      var trucks = f.Filter<Transform3D, Unit, LogisticsTruck>();
      while (trucks.NextUnsafe(out var entity, out var transform, out var unit, out var t)) {
        if (t->Site >= 0 || t->State == LogisticsState.Aborting) continue;
        var site = -1 - t->Site;
        var dock = DockOf(f, spec, unit->Owner, site);
        if (t->State == LogisticsState.ToDock) {
          if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, dock) <= spec.ArriveRadius * spec.ArriveRadius) Stop(f, entity, unit, t, LogisticsState.Unloading);
          else Drive(f, spec, entity, unit, t, transform->Position, dock);
        } else if (t->State == LogisticsState.Unloading) {
          t->Timer += f.DeltaTime;
          if (t->Timer < spec.UnloadSeconds) continue;
          Credit(f, unit->Owner, site, t);
          t->State = LogisticsState.Aborting;
        } else {
          t->State = LogisticsState.Aborting;
        }
      }
    }

    /// <summary>Truck state for the HUD: 0 이동 중(야적장으로), 1 적재 중, 2 이동 중(본진으로), 3 하역 중, 4 귀환, 5 경로 대기.</summary>
    public static int StateOf(LogisticsTruck* t) => (int)t->State;
  }
}
