namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 항목 37 전술 깊이(나머지): 기갑 잔해(30초 엄폐·공병 고철 회수), 교량 폭파·가교 복구(길찾기 영역 전환),
  /// 사령부 비상 방어 프로토콜(체력 40% 미만에서 한 번), 추락 수송기 블랙박스 회수(15초 점령, 영구 화력 보너스 또는 격납고 즉시 재정비),
  /// 전술 거점 2종(무인 레이더탑·전술 유류고)의 효과 조회.
  /// 수치는 전부 GameRulesSpec·MapSpec 데이터, 계산은 FP·정수·프레임 수뿐이다(결정론). 블랙박스 자리는 맵 데이터의 순환 목록이라 난수를 쓰지 않는다.
  /// </summary>
  public unsafe class DepthSystem : SystemMainThread {
    const int Players = 4;

    // ---------- 기갑 잔해 ----------

    /// <summary>전차·장갑차(지상 차량, 중형 이상)만 잔해를 남긴다.</summary>
    public static bool LeavesWreck(UnitSpec spec) => spec.Size >= UnitSize.Medium && spec.IsAir == false && spec.IsNaval == false && spec.Attack != AttackType.None && CitySystem.IsInfantry(spec) == false;

    public static void LeaveWreck(Frame f, FPVector3 position, UnitSpec spec, AssetRef<UnitSpec> specRef) {
      var rules = FactoryWarsUtil.Rules(f);
      if (rules.WreckSeconds <= FP._0 || LeavesWreck(spec) == false) {
        return;
      }
      position.Y = FP._0;
      var entity = f.Create();
      f.Add(entity, Transform3D.Create(position));
      f.Add(entity, new Wreck {
        UntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.WreckSeconds),
        Scrap = spec.Cost.Iron * rules.WreckScrapRatio, Size = (int)spec.Size, Spec = specRef,
      });
      // 임시 엄폐점 넷(잔해 쪽을 막는다). 잔해가 사라지면 CoverSystem이 점을 지우고 보병을 푼다.
      CoverSystem.ShapeSpots(f, new MapShape { Center = position, Radius = rules.WreckCoverRadius }, rules.WreckCoverReduction, entity, false);
      f.Events.WreckLeft(entity, position, (int)spec.Size);
    }

    static void Salvage(Frame f, EntityRef entity, Transform3D* transform, Unit* unit, Worker* worker, GameRulesSpec rules) {
      if (f.Unsafe.TryGetPointer<Wreck>(worker->Target, out var wreck) == false) {
        worker->Task = WorkerTask.Idle;
        return;
      }
      var position = f.Unsafe.GetPointer<Transform3D>(worker->Target)->Position;
      if (WorkerSystem.MoveInto(f, entity, worker, transform->Position, position, rules.WreckCoverRadius + rules.WorkRange) == false) {
        return;
      }
      wreck->Work += f.DeltaTime;
      if (wreck->Work < rules.WreckSalvageSeconds) {
        return;
      }
      // 기갑 요새화 군단은 회수량이 늘어난다(도크린 데이터).
      var iron = TacticalSystem.DoctrineOf(f, unit->Owner) == 3 ? wreck->Scrap * rules.ArmorScrapMultiplier : wreck->Scrap;
      var state = FactoryWarsUtil.Player(f, unit->Owner);
      state->Stock.Iron += iron;
      state->Earned.Iron += iron;
      state->WrecksSalvaged++;
      f.Events.WreckSalvaged(unit->Owner, position, iron);
      f.Destroy(worker->Target);
      worker->Task = WorkerTask.Idle;
    }

    // ---------- 교량 ----------

    /// <summary>다리 i가 지키는 본진 자리(가장 가까운 본진). 본진 구역이 없으면 -1.</summary>
    public static int BridgeSeat(MapSpec map, int index) {
      var areas = map.BaseAreas;
      if (areas == null || areas.Length == 0 || map.Bridges == null || index >= map.Bridges.Length) {
        return -1;
      }
      var seat = 0;
      var best = FP.MaxValue;
      var bridge = map.Bridges[index];
      for (var i = 0; i < areas.Length; i++) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(bridge.Center, areas[i].Center);
        if (distance < best) {
          best = distance;
          seat = i;
        }
      }
      // 본진 사각형에서 다리 길이(Reach)보다 멀면 본진 다리가 아니다(표준 맵 중앙 섬 다리). 휴전 울타리를 세우지 않는다.
      return areas[seat].Distance(bridge.Center) > bridge.Reach ? -1 : seat;
    }

    public static EntityRef BridgeEntity(Frame f, int index) {
      var bridges = f.Filter<Bridge>();
      while (bridges.NextUnsafe(out var entity, out var bridge)) {
        if (bridge->Index == index) {
          return entity;
        }
      }
      return EntityRef.None;
    }

    public static bool BridgeDown(Frame f, int index) {
      var entity = BridgeEntity(f, index);
      return entity != EntityRef.None && f.Unsafe.GetPointer<Bridge>(entity)->Destroyed;
    }

    /// <summary>다리 영역을 지금 상태(휴전 울타리·빈 자리·파괴 여부)에 맞춘다.</summary>
    public static void ApplyBridgeRegion(Frame f, int index, bool destroyed) {
      var rules = FactoryWarsUtil.Rules(f);
      if (string.IsNullOrEmpty(rules.BridgeRegion) || f.Map.RegionMap.TryGetValue($"{rules.BridgeRegion}{index}", out var region) == false) {
        return;
      }
      var seat = BridgeSeat(FactoryWarsUtil.MapInfo(f), index);
      var barrierOpen = seat < 0 || f.Global->BarrierOpen || FactoryWarsUtil.PlayerAtSeat(f, seat) < 0;
      // Task 39: a drawbridge that is not fully down closes the ground passage.
      var entity = BridgeEntity(f, index);
      var raised = entity != EntityRef.None && f.Unsafe.GetPointer<Bridge>(entity)->Lift != BridgeTraffic.Down;
      f.NavMeshRegionMask->ToggleRegion(region, barrierOpen && destroyed == false && raised == false);
    }

    /// <summary>다리 양 끝(건너는 방향 = 사각형의 짧은 변) 중 이 공병이 갈 수 있는 가까운 쪽.</summary>
    static FPVector3 BridgeEnd(Frame f, PlayerRef owner, MapShape shape, FPVector3 from, FP gap) {
      var alongLength = FactoryWarsUtil.MapInfo(f).SealedPerimeter || shape.HalfLength <= shape.HalfWidth;
      var yaw = shape.Yaw * FP.Deg2Rad;
      var axis = alongLength ? new FPVector3(FPMath.Cos(yaw), FP._0, -FPMath.Sin(yaw)) : new FPVector3(FPMath.Sin(yaw), FP._0, FPMath.Cos(yaw));
      var reach = (alongLength ? shape.HalfLength : shape.HalfWidth) + gap;
      var a = shape.Center + axis * reach;
      var b = shape.Center - axis * reach;
      a.Y = FP._0;
      b.Y = FP._0;
      var nearA = FactoryWarsUtil.FlatDistanceSqr(a, from) <= FactoryWarsUtil.FlatDistanceSqr(b, from);
      var first = nearA ? a : b;
      var second = nearA ? b : a;
      return FactoryWarsUtil.IsReachable(f, owner, first) || FactoryWarsUtil.IsReachable(f, owner, second) == false ? first : second;
    }

    public static void HitBridge(Frame f, EntityRef entity, Bridge* bridge, FP amount, PlayerRef by) {
      if (bridge->Destroyed || amount <= FP._0 || FactoryWarsUtil.InTruce(f)) {
        return;
      }
      bridge->Health -= amount;
      if (bridge->Health <= FP._0) {
        SetBridge(f, entity, bridge, true, by);
      }
    }

    /// <summary>폭발 반경 안의 다리에 피해(폭격·포격·미사일).</summary>
    public static void BlastBridges(Frame f, PlayerRef by, FPVector3 center, FP radius, FP amount) {
      var rules = FactoryWarsUtil.Rules(f);
      if (rules.BridgeHealth <= FP._0 || rules.BridgeBlastMultiplier <= FP._0) {
        return;
      }
      var map = FactoryWarsUtil.MapInfo(f);
      var bridges = f.Filter<Bridge>();
      while (bridges.NextUnsafe(out var entity, out var bridge)) {
        if (bridge->Destroyed == false && map.Bridges[bridge->Index].Distance(center) <= radius) {
          HitBridge(f, entity, bridge, amount * rules.BridgeBlastMultiplier, by);
        }
      }
    }

    public static void SetBridge(Frame f, EntityRef entity, Bridge* bridge, bool destroyed, PlayerRef by) {
      var rules = FactoryWarsUtil.Rules(f);
      bridge->Destroyed = destroyed;
      bridge->Health = destroyed ? FP._0 : bridge->MaxHealth;
      bridge->Work = FP._0;
      if (destroyed == false) {
        bridge->Pontoon = true;
      }
      ApplyBridgeRegion(f, bridge->Index, destroyed);
      var state = FactoryWarsUtil.Player(f, by);
      if (state != null) {
        if (destroyed) {
          state->BridgesDown++;
        } else {
          state->BridgesRepaired++;
        }
      }
      f.Events.BridgeChanged(bridge->Index, destroyed, by);
      if (destroyed == false || rules.BridgeCollapseDamage <= FP._0) {
        return;
      }
      // 무너질 때 다리 위 지상 유닛은 큰 피해를 입는다.
      var shape = FactoryWarsUtil.MapInfo(f).Bridges[bridge->Index];
      var weapon = new Weapon { Owner = by, Attack = AttackType.Area, IsRanged = true, Area = true, IgnoreCover = true, Origin = shape.Center };
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var victim, out var transform, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        if (spec.IsAir == false && spec.IsNaval == false && unit->Garrison == EntityRef.None && unit->Health > FP._0 && shape.Distance(transform->Position) <= FP._0) {
          CombatSystem.DamageUnit(f, weapon, victim, unit, rules.BridgeCollapseDamage);
        }
      }
    }

    static void WorkBridge(Frame f, EntityRef entity, Transform3D* transform, Unit* unit, Worker* worker, GameRulesSpec rules) {
      if (f.Unsafe.TryGetPointer<Bridge>(worker->Target, out var bridge) == false) {
        worker->Task = WorkerTask.Idle;
        return;
      }
      var demolish = worker->Task == WorkerTask.Demolish;
      if (bridge->Destroyed == demolish || (demolish && FactoryWarsUtil.InTruce(f))) {
        worker->Task = WorkerTask.Idle;
        return;
      }
      var shape = FactoryWarsUtil.MapInfo(f).Bridges[bridge->Index];
      var end = BridgeEnd(f, unit->Owner, shape, transform->Position, rules.BridgeWorkRange * FP._0_50);
      if (WorkerSystem.MoveInto(f, entity, worker, transform->Position, end, rules.BridgeWorkRange) == false) {
        return;
      }
      if (demolish) {
        worker->WorkTimer += f.DeltaTime;
        if (worker->WorkTimer >= rules.BridgeDemolishSeconds) {
          worker->Task = WorkerTask.Idle;
          SetBridge(f, worker->Target, bridge, true, unit->Owner);
        }
        return;
      }
      // 가교 복구: 여러 공병이 같이 하면 빨라진다(진행도는 다리에 쌓인다).
      bridge->Work += f.DeltaTime;
      if (bridge->Work >= rules.BridgeRepairSeconds) {
        worker->Task = WorkerTask.Idle;
        SetBridge(f, worker->Target, bridge, false, unit->Owner);
      }
    }

    /// <summary>AI: 자기 본진 다리가 끊기면 가장 가까운 공병 하나를 복구에 보낸다.</summary>
    static void BotRepairs(Frame f) {
      var map = FactoryWarsUtil.MapInfo(f);
      var bridges = f.Filter<Bridge>();
      while (bridges.NextUnsafe(out var bridgeEntity, out var bridge)) {
        if (bridge->Destroyed == false || bridge->Seat < 0) {
          continue;
        }
        var player = FactoryWarsUtil.PlayerAtSeat(f, bridge->Seat);
        if (player < 0) {
          continue;
        }
        var state = FactoryWarsUtil.Player(f, (PlayerRef)player);
        if (state == null || state->IsBot == false || state->Active == false) {
          continue;
        }
        var center = map.Bridges[bridge->Index].Center;
        var best = EntityRef.None;
        var bestDistance = FP.MaxValue;
        var busy = false;
        var workers = f.Filter<Transform3D, Unit, Worker>();
        while (workers.NextUnsafe(out var entity, out var transform, out var unit, out var worker)) {
          if (unit->Owner != (PlayerRef)player || unit->Health <= FP._0) {
            continue;
          }
          if (worker->Task == WorkerTask.Pontoon && worker->Target == bridgeEntity) {
            busy = true;
            break;
          }
          if (worker->Task == WorkerTask.Build || worker->Task == WorkerTask.Demolish) {
            continue;
          }
          var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, center);
          if (distance < bestDistance) {
            bestDistance = distance;
            best = entity;
          }
        }
        if (busy == false && best != EntityRef.None) {
          WorkerSystem.Assign(f, best, WorkerTask.Pontoon, bridgeEntity);
        }
      }
    }

    // ---------- 사령부 비상 방어 프로토콜 ----------

    /// <summary>프로토콜이 켜진 동안 사령부가 받는 피해 배율.</summary>
    public static FP ProtocolFactor(Frame f, Building* building) =>
      building->ProtocolUntilFrame > f.Number ? FactoryWarsUtil.Rules(f).HqProtocolDamageTaken : FP._1;

    /// <summary>사령부 체력이 기준 아래로 처음 떨어지면 한 번 발동: 일정 시간 피해 감소 + 자동 방어 사격 + 수비 분대 투입.</summary>
    public static void CheckProtocol(Frame f, EntityRef entity, Building* building) {
      var rules = FactoryWarsUtil.Rules(f);
      if (rules.HqProtocolSeconds <= FP._0 || building->Health <= FP._0 || building->ConstructionRemaining > FP._0) {
        return;
      }
      var spec = f.FindAsset(building->Spec);
      var state = FactoryWarsUtil.Player(f, building->Owner);
      if (spec.Role != BuildingRole.Headquarters || state == null || state->ProtocolUsed || building->Health >= spec.MaxHealth * rules.HqProtocolHealthRatio) {
        return;
      }
      state->ProtocolUsed = true;
      building->ProtocolUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.HqProtocolSeconds);
      building->ProtocolNextShotFrame = f.Number;
      var position = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
      var faction = f.FindAsset(state->Faction);
      var squad = faction.StartingUnits != null && faction.StartingUnits.Length > 0 ? faction.StartingUnits[0] : default;
      var reach = spec.FootprintRadius + FP._3;
      for (var i = 0; squad.IsValid && i < rules.HqProtocolSquads; i++) {
        var angle = FP.PiTimes2 * i / FPMath.Max(FP._1, rules.HqProtocolSquads);
        ProductionSystem.SpawnUnitAt(f, building->Owner, squad, position + new FPVector3(FPMath.Cos(angle), FP._0, FPMath.Sin(angle)) * reach, null);
      }
      f.Events.HqProtocol(building->Owner, entity, position);
    }

    static void ProtocolFire(Frame f, GameRulesSpec rules) {
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->ProtocolUntilFrame <= f.Number || f.Number < building->ProtocolNextShotFrame || building->Health <= FP._0) {
          continue;
        }
        building->ProtocolNextShotFrame = f.Number + System.Math.Max(1, FactoryWarsUtil.SecondsToFrames(f, rules.HqProtocolInterval));
        var best = EntityRef.None;
        var bestDistance = rules.HqProtocolRange * rules.HqProtocolRange;
        var units = f.Filter<Transform3D, Unit>();
        while (units.NextUnsafe(out var entity, out var other, out var unit)) {
          if (unit->Health <= FP._0 || unit->Garrison != EntityRef.None || DiplomacySystem.CanAttack(f, building->Owner, unit->Owner) == false) {
            continue;
          }
          var distance = FactoryWarsUtil.FlatDistanceSqr(other->Position, transform->Position);
          if (distance < bestDistance) {
            bestDistance = distance;
            best = entity;
          }
        }
        if (best == EntityRef.None) {
          continue;
        }
        var target = f.Unsafe.GetPointer<Transform3D>(best)->Position;
        f.Events.HqProtocolShot(transform->Position, target);
        var weapon = new Weapon { Owner = building->Owner, Attack = AttackType.AntiArmor, IsRanged = true, Origin = transform->Position };
        CombatSystem.DamageUnit(f, weapon, best, f.Unsafe.GetPointer<Unit>(best), rules.HqProtocolDamage);
      }
    }

    // ---------- 블랙박스 ----------

    /// <summary>블랙박스 화력 보너스(중첩 수 × 보너스).</summary>
    public static FP OutgoingBonus(Frame f, PlayerRef owner) {
      var state = FactoryWarsUtil.Player(f, owner);
      return state == null || state->BlackBoxStacks <= 0 ? FP._1 : FP._1 + FactoryWarsUtil.Rules(f).BlackBoxDamageBonus * state->BlackBoxStacks;
    }

    static void Reward(Frame f, GameRulesSpec rules, PlayerRef player, FPVector3 position) {
      var state = FactoryWarsUtil.Player(f, player);
      state->BlackBoxes++;
      // 재정비 중인 격납 칸이 있으면 즉시 재정비 + 긴급 출격권, 없으면 영구 화력 보너스(상한까지).
      var rearming = false;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner != player) {
          continue;
        }
        for (var i = 0; i < SortieSystem.Slots; i++) {
          var slot = building->Hangar.GetPointer(i);
          if (slot->State == 1 && slot->ReadyFrame > f.Number) {
            rearming = true;
          }
        }
      }
      var refill = rearming || state->BlackBoxStacks >= rules.BlackBoxMaxStacks;
      if (refill) {
        buildings = f.Filter<Building>();
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
      } else {
        state->BlackBoxStacks++;
      }
      f.Events.BlackBoxEnded(position, player, refill ? 1 : 0);
    }

    static void BlackBoxes(Frame f, GameRulesSpec rules) {
      var map = FactoryWarsUtil.MapInfo(f);
      var spots = map.BlackBoxSpots;
      if (spots == null || spots.Length == 0 || rules.BlackBoxPeriodSeconds <= FP._0) {
        return;
      }
      if (f.Global->NextBlackBoxFrame == 0) {
        f.Global->NextBlackBoxFrame = System.Math.Max(1, FactoryWarsUtil.SecondsToFrames(f, rules.BlackBoxFirstSeconds));
      }
      var any = false;
      var boxes = f.Filter<Transform3D, BlackBox>();
      while (boxes.NextUnsafe(out var entity, out var transform, out var box)) {
        any = true;
        // 반경 안 지상 유닛의 주인들.
        var mask = 0;
        var radius = rules.BlackBoxRadius * rules.BlackBoxRadius;
        var units = f.Filter<Transform3D, Unit>();
        while (units.NextUnsafe(out _, out var other, out var unit)) {
          var owner = (int)unit->Owner;
          if (owner < 0 || owner >= Players || unit->Health <= FP._0 || unit->Garrison != EntityRef.None || f.FindAsset(unit->Spec).IsAir
              || FactoryWarsUtil.FlatDistanceSqr(other->Position, transform->Position) > radius) {
            continue;
          }
          mask |= 1 << owner;
        }
        var claimant = PlayerRef.None;
        var contested = false;
        for (var p = 0; p < Players; p++) {
          if ((mask & (1 << p)) == 0) {
            continue;
          }
          if (claimant.IsValid == false) {
            claimant = (PlayerRef)p;
          } else if (DiplomacySystem.GetRelation(f, claimant, (PlayerRef)p) != Relation.Allied) {
            contested = true;
          }
        }
        box->Contested = contested;
        if (claimant.IsValid == false) {
          box->Progress = FPMath.Max(FP._0, box->Progress - f.DeltaTime);
        } else if (contested == false) {
          if (box->Claimant != claimant) {
            box->Claimant = claimant;
            box->Progress = FP._0;
          }
          box->Progress += f.DeltaTime;
          if (box->Progress >= rules.BlackBoxCaptureSeconds) {
            Reward(f, rules, claimant, transform->Position);
            f.Destroy(entity);
            continue;
          }
        }
        if (f.Number >= box->UntilFrame) {
          f.Events.BlackBoxEnded(transform->Position, PlayerRef.None, -1);
          f.Destroy(entity);
        }
      }
      if (any == false && f.Number >= f.Global->NextBlackBoxFrame && FactoryWarsUtil.InTruce(f) == false) {
        var position = spots[f.Global->BlackBoxCount % spots.Length];
        position.Y = FP._0;
        f.Global->BlackBoxCount++;
        f.Global->NextBlackBoxFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.BlackBoxPeriodSeconds);
        var entity = f.Create();
        f.Add(entity, Transform3D.Create(position));
        f.Add(entity, new BlackBox { UntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.BlackBoxLifeSeconds), Claimant = PlayerRef.None });
        f.Events.BlackBoxDown(entity, position, rules.BlackBoxLifeSeconds.AsInt);
      }
    }

    // ---------- 전술 거점 ----------

    /// <summary>이 플레이어(또는 동맹)가 그 종류의 거점을 하나라도 갖고 있는가.</summary>
    public static bool OwnsSite(Frame f, PlayerRef player, CaptureKind kind) {
      var sites = f.Filter<CaptureSite>();
      while (sites.NextUnsafe(out _, out var site)) {
        if (site->Kind == kind && site->Owner.IsValid && (site->Owner == player || DiplomacySystem.GetRelation(f, player, site->Owner) == Relation.Allied)) {
          return true;
        }
      }
      return false;
    }

    /// <summary>내(동맹) 레이더탑 반경 안인가: 안개가 걷히고, 기상 중에도 출격 목표로 쓸 수 있다.</summary>
    public static bool InRadarArea(Frame f, PlayerRef player, FPVector3 position) {
      var radius = FactoryWarsUtil.Rules(f).RadarSiteRadius;
      if (radius <= FP._0) {
        return false;
      }
      var sites = f.Filter<Transform3D, CaptureSite>();
      while (sites.NextUnsafe(out _, out var transform, out var site)) {
        if (site->Kind == CaptureKind.Radar && site->Owner.IsValid && (site->Owner == player || DiplomacySystem.GetRelation(f, player, site->Owner) == Relation.Allied)
            && FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= radius * radius) {
          return true;
        }
      }
      return false;
    }

    /// <summary>출격기 재정비 시간 배율(유류고를 갖고 있으면 줄어든다).</summary>
    public static FP RearmMultiplier(Frame f, PlayerRef player) {
      var value = FactoryWarsUtil.Rules(f).FuelRearmMultiplier;
      return value > FP._0 && OwnsSite(f, player, CaptureKind.Fuel) ? value : FP._1;
    }

    // ---------- 갱신 ----------

    public override void OnInit(Frame f) {
      var rules = FactoryWarsUtil.Rules(f);
      var map = FactoryWarsUtil.MapInfo(f);
      if (rules.BridgeHealth <= FP._0 || map.Bridges == null) {
        return;
      }
      for (var i = 0; i < map.Bridges.Length; i++) {
        var entity = f.Create();
        f.Add(entity, Transform3D.Create(map.Bridges[i].Center));
        f.Add(entity, new Bridge {
          Index = i, Seat = BridgeSeat(map, i), Health = rules.BridgeHealth, MaxHealth = rules.BridgeHealth,
          Clearance = BridgeTraffic.ClearanceOf(map, rules, i), Drawbridge = BridgeTraffic.IsDrawbridge(map, i),
        });
      }
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      // 잔해 수명.
      var wrecks = f.Filter<Wreck>();
      while (wrecks.NextUnsafe(out var entity, out var wreck)) {
        if (f.Number >= wreck->UntilFrame) {
          f.Destroy(entity);
        }
      }
      // 공병 작업(고철 회수·폭파·가교).
      var workers = f.Filter<Transform3D, Unit, Worker>();
      while (workers.NextUnsafe(out var entity, out var transform, out var unit, out var worker)) {
        switch (worker->Task) {
          case WorkerTask.Salvage: Salvage(f, entity, transform, unit, worker, rules); break;
          case WorkerTask.Demolish:
          case WorkerTask.Pontoon: WorkBridge(f, entity, transform, unit, worker, rules); break;
        }
      }
      if (f.Number % System.Math.Max(1, FactoryWarsUtil.SecondsToFrames(f, FP._2)) == 0) {
        BotRepairs(f);
      }
      ProtocolFire(f, rules);
      BlackBoxes(f, rules);
      BridgeTraffic.Update(f, rules);
    }
  }
}
