namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>전장 요소: 점령지, 바리케이드, 보급 상자. 위치는 MapSpec, 수치는 GameRulesSpec에 있다.</summary>
  public unsafe class BattlefieldSystem : SystemMainThread {
    public static void Init(Frame f) {
      var map = FactoryWarsUtil.MapInfo(f);
      var rules = FactoryWarsUtil.Rules(f);
      for (var i = 0; map.CaptureSites != null && i < map.CaptureSites.Length; i++) {
        var site = f.Create();
        f.Add(site, Transform3D.Create(map.CaptureSites[i].Center));
        f.Add(site, new CaptureSite { Kind = map.CaptureSites[i].Kind, Radius = map.CaptureSites[i].Radius, Index = i, Owner = PlayerRef.None, Claimant = PlayerRef.None });
      }
      for (var i = 0; map.Barricades != null && i < map.Barricades.Length; i++) {
        var barricade = f.Create();
        f.Add(barricade, Transform3D.Create(map.Barricades[i].Shape.Center));
        f.Add(barricade, new Barricade { Index = i, Health = rules.BarricadeHealth, Shape = map.Barricades[i].Shape });
      }
      for (var i = 0; map.CrateSpots != null && i < map.CrateSpots.Length; i++) {
        var spot = map.CrateSpots[i];
        if (spot.Seat >= 0 && rules.BaseCrateRewards != null && rules.BaseCrateRewards.Length > 0) {
          SpawnCrate(f, spot.Position, spot.Seat, rules.BaseCrateRewards[spot.Group % rules.BaseCrateRewards.Length]);
        }
      }
      f.Global->NextDropFrame = f.Global->TruceEndFrame + FactoryWarsUtil.SecondsToFrames(f, rules.DropIntervalSeconds);
    }

    static void SpawnCrate(Frame f, FPVector3 position, int seat, ResourceCost reward) {
      var crate = f.Create();
      f.Add(crate, Transform3D.Create(position));
      f.Add(crate, new SupplyCrate { Seat = seat, Reward = reward });
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var sites = f.Filter<Transform3D, CaptureSite>();
      while (sites.NextUnsafe(out var entity, out var transform, out var site)) {
        PayOwner(f, site, rules);
        if (FactoryWarsUtil.InTruce(f) == false) {
          UpdateSite(f, entity, transform->Position, site, rules);
        }
      }
      PickCrates(f, rules);
      UpdateDrops(f, rules);
    }

    /// <summary>지상 전투 유닛만 센다. 공병은 무기가 없어서 빠진다.</summary>
    static int PresentPlayers(Frame f, FPVector3 center, FP radius) {
      var mask = 0;
      var limit = radius * radius;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        if (spec.IsAir == false && spec.Attack != AttackType.None && FactoryWarsUtil.FlatDistanceSqr(transform->Position, center) <= limit) {
          mask |= 1 << (int)unit->Owner;
        }
      }
      return mask;
    }

    static bool Friendly(Frame f, PlayerRef a, PlayerRef b) => a == b || DiplomacySystem.GetRelation(f, a, b) == Relation.Allied;

    /// <summary>모두 서로 동맹이면 한편이다. 적대하는 쌍이 하나라도 있으면 경합이다.</summary>
    static bool IsOneSide(Frame f, int mask) {
      for (var a = 0; a < f.PlayerCount; a++) {
        for (var b = a + 1; (mask & (1 << a)) != 0 && b < f.PlayerCount; b++) {
          if ((mask & (1 << b)) != 0 && Friendly(f, a, b) == false) {
            return false;
          }
        }
      }
      return true;
    }

    static PlayerRef LowestPlayer(Frame f, int mask) {
      for (var i = 0; i < f.PlayerCount; i++) {
        if ((mask & (1 << i)) != 0) {
          return i;
        }
      }
      return PlayerRef.None;
    }

    static bool OnSide(Frame f, PlayerRef player, int mask) {
      if (player.IsValid == false) {
        return false;
      }
      return Friendly(f, player, LowestPlayer(f, mask));
    }

    static void Decay(Frame f, CaptureSite* site, GameRulesSpec rules) {
      site->Progress = FPMath.Max(FP._0, site->Progress - f.DeltaTime * rules.CaptureSeconds / rules.CaptureDecaySeconds);
      if (site->Progress <= FP._0) {
        site->Claimant = PlayerRef.None;
      }
    }

    static void UpdateSite(Frame f, EntityRef entity, FPVector3 center, CaptureSite* site, GameRulesSpec rules) {
      if (site->Owner.IsValid && FactoryWarsUtil.Player(f, site->Owner)->Defeated) {
        Change(f, entity, site, PlayerRef.None);
      }
      var present = PresentPlayers(f, center, site->Radius);
      if (present == 0) {
        site->Contested = false;
        Decay(f, site, rules);
        return;
      }
      if (IsOneSide(f, present) == false) {
        if (site->Contested == false) {
          site->Contested = true;
          f.Events.CaptureContested(entity);
        }
        return;
      }
      site->Contested = false;
      if (OnSide(f, site->Owner, present)) {
        Decay(f, site, rules);
        return;
      }
      // 다른 편이 쌓아 둔 진행도는 먼저 되돌린다.
      if (site->Claimant.IsValid && OnSide(f, site->Claimant, present) == false) {
        site->Progress -= f.DeltaTime;
        if (site->Progress <= FP._0) {
          site->Progress = FP._0;
          site->Claimant = PlayerRef.None;
        }
        return;
      }
      if (site->Claimant.IsValid == false) {
        site->Claimant = LowestPlayer(f, present);
        f.Events.CaptureStarted(entity, site->Claimant);
      }
      site->Progress += f.DeltaTime;
      if (site->Progress >= rules.CaptureSeconds) {
        site->Progress = FP._0;
        var claimant = site->Claimant;
        if (site->Owner.IsValid) {
          Change(f, entity, site, PlayerRef.None);
        } else {
          site->Claimant = PlayerRef.None;
          Change(f, entity, site, claimant);
          FactoryWarsUtil.Player(f, claimant)->Captures++;
        }
      }
    }

    static void Change(Frame f, EntityRef entity, CaptureSite* site, PlayerRef next) {
      var previous = site->Owner;
      site->Owner = next;
      f.Events.CaptureChanged(entity, previous, next);
    }

    static void PayOwner(Frame f, CaptureSite* site, GameRulesSpec rules) {
      if (site->Owner.IsValid == false) {
        return;
      }
      var state = FactoryWarsUtil.Player(f, site->Owner);
      state->CaptureFrames++;
      if (site->Kind == CaptureKind.Comms) {
        state->Stock.Steel += rules.CommsSteelPerSecond * f.DeltaTime;
      } else {
        state->Stock.Power = FPMath.Min(state->PowerCap, state->Stock.Power + rules.RelayPowerPerSecond * f.DeltaTime);
      }
    }

    static void PickCrates(Frame f, GameRulesSpec rules) {
      var limit = rules.CratePickRadius * rules.CratePickRadius;
      var crates = f.Filter<Transform3D, SupplyCrate>();
      while (crates.NextUnsafe(out var entity, out var transform, out var crate)) {
        var units = f.Filter<Transform3D, Unit>();
        while (units.NextUnsafe(out _, out var position, out var unit)) {
          var own = crate->Seat < 0 || (int)unit->Owner == crate->Seat;
          if (own && f.FindAsset(unit->Spec).IsAir == false && FactoryWarsUtil.FlatDistanceSqr(position->Position, transform->Position) <= limit) {
            Collect(f, unit->Owner, crate->Reward);
            f.Events.CratePicked(unit->Owner, transform->Position, crate->Reward);
            f.Destroy(entity);
            break;
          }
        }
      }
    }

    static void Collect(Frame f, PlayerRef player, ResourceCost reward) {
      var state = FactoryWarsUtil.Player(f, player);
      state->Stock.Iron += reward.Iron;
      state->Stock.Wood += reward.Wood;
      state->Stock.Steel += reward.Steel;
      state->Stock.Power = FPMath.Min(state->PowerCap, state->Stock.Power + reward.Power);
      state->CratesPicked++;
    }

    static int DropGroups(MapSpec map) {
      var groups = 0;
      for (var i = 0; map.CrateSpots != null && i < map.CrateSpots.Length; i++) {
        if (map.CrateSpots[i].Seat < 0) {
          groups = System.Math.Max(groups, map.CrateSpots[i].Group + 1);
        }
      }
      return groups;
    }

    static void UpdateDrops(Frame f, GameRulesSpec rules) {
      var map = FactoryWarsUtil.MapInfo(f);
      var groups = DropGroups(map);
      if (groups == 0) {
        return;
      }
      var warning = f.Global->NextDropFrame - FactoryWarsUtil.SecondsToFrames(f, rules.DropWarningSeconds);
      if (f.Global->DropAnnounced == false && f.Number >= warning) {
        f.Global->DropAnnounced = true;
        f.Global->DropGroup = f.RNG->Next(0, groups);
        f.Events.SupplyDropIncoming(f.Global->DropGroup, f.Global->NextDropFrame);
      }
      if (f.Number < f.Global->NextDropFrame) {
        return;
      }
      var crates = f.Filter<SupplyCrate>();
      while (crates.NextUnsafe(out var entity, out var crate)) {
        if (crate->Seat < 0) {
          f.Destroy(entity);
        }
      }
      for (var i = 0; i < map.CrateSpots.Length; i++) {
        if (map.CrateSpots[i].Seat < 0 && map.CrateSpots[i].Group == f.Global->DropGroup) {
          SpawnCrate(f, map.CrateSpots[i].Position, -1, rules.DropReward);
        }
      }
      f.Global->DropAnnounced = false;
      f.Global->NextDropFrame += FactoryWarsUtil.SecondsToFrames(f, rules.DropIntervalSeconds);
    }

    /// <summary>체력이 다하면 틈의 NavMesh 영역을 연다. 다시 세울 수 없다.</summary>
    public static void HitBarricade(Frame f, EntityRef entity, Barricade* barricade, FP amount) {
      if (barricade->Health <= FP._0) {
        return;
      }
      barricade->Health -= amount;
      if (barricade->Health > FP._0) {
        return;
      }
      var placements = FactoryWarsUtil.MapInfo(f).Barricades;
      if (f.Map.RegionMap.TryGetValue(placements[barricade->Index].Region, out var region)) {
        f.NavMeshRegionMask->ToggleRegion(region, true);
      }
      f.Events.BarricadeDestroyed(barricade->Index);
      f.Destroy(entity);
    }

    /// <summary>바리케이드 앞에서 from과 같은 쪽 지점. 막힌 틈 안으로는 길을 찾을 수 없다.</summary>
    public static FPVector3 FrontOf(Barricade* barricade, FPVector3 from) {
      var shape = barricade->Shape;
      var angle = shape.Yaw * FP.Deg2Rad;
      var across = new FPVector3(FPMath.Sin(angle), FP._0, FPMath.Cos(angle)) * (shape.HalfWidth + FP._2);
      var a = shape.Center + across;
      var b = shape.Center - across;
      return FactoryWarsUtil.FlatDistanceSqr(a, from) <= FactoryWarsUtil.FlatDistanceSqr(b, from) ? a : b;
    }
  }
}
