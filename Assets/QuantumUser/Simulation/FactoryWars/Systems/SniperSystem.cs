namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 항목 34-C 주둔 저격수. 진지 건물(도시 진지·벙커)에 올라가 있을 때만 사격하고, 올라간 직후 SetupSeconds 동안은 못 쏜다.
  /// 지상에서는 은신(지속 StealthSeconds, 적 근접·탐지로 노출)과 채굴 교란 장치 설치를 한다.
  /// 규칙은 전부 UnitSpec·GameRulesSpec 데이터이고, 계산은 FP·정수·프레임 수뿐이라 결정론이다. 사격은 CombatSystem 대신 여기서만 한다.
  /// </summary>
  public unsafe class SniperSystem : SystemMainThread {
    const int Players = 4;
    const int DetectionTicks = 5;

    static bool Friendly(Frame f, PlayerRef a, PlayerRef b) => a == b || DiplomacySystem.GetRelation(f, a, b) == Relation.Allied;

    // ---------- 조회 ----------

    /// <summary>은신한 저격수가 viewer에게 안 보이면 true(자기·동맹, 탐지한 진영에게는 보임).</summary>
    public static bool IsConcealedFrom(Frame f, Unit* unit, PlayerRef viewer) {
      if (unit->StealthUntilFrame <= f.Number || unit->Owner == viewer || DiplomacySystem.GetRelation(f, viewer, unit->Owner) == Relation.Allied) {
        return false;
      }
      var index = (int)viewer;
      return index < 0 || index >= Players || (unit->DetectedMask & (1 << index)) == 0;
    }

    /// <summary>플레이어가 가진(대기열 포함) 같은 종류 유닛 수. 보유 상한(MaxOwned) 검사에 쓴다.</summary>
    public static int CountOwnedAndQueued(Frame f, PlayerRef player, UnitSpec spec) {
      var count = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->Owner == player && ReferenceEquals(f.FindAsset(unit->Spec), spec)) {
          count++;
        }
      }
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner != player) {
          continue;
        }
        for (var i = 0; i < building->QueueCount; i++) {
          if (ReferenceEquals(f.FindAsset(building->Queue[i].Unit), spec)) {
            count += spec.SpawnCount;
          }
        }
      }
      return count;
    }

    /// <summary>교란 장치가 걸린 노드의 채집 속도 배율(없으면 1).</summary>
    public static FP GatherMultiplier(Frame f, EntityRef node) {
      var result = FP._1;
      var jammers = f.Filter<Jammer>();
      while (jammers.NextUnsafe(out _, out var jammer)) {
        if (jammer->Node == node && jammer->GatherMultiplier < result) {
          result = jammer->GatherMultiplier;
        }
      }
      return result;
    }

    // ---------- 명령 ----------

    public static void TryStealth(Frame f, PlayerRef player, Unit* unit) {
      var spec = f.FindAsset(unit->Spec);
      if (spec.StealthSeconds <= FP._0 || unit->Garrison != EntityRef.None) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Invalid, 0);
        return;
      }
      if (f.Number < unit->StealthReadyFrame || unit->StealthUntilFrame > f.Number) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Cooldown, 0);
        return;
      }
      var bonus = TacticalSystem.DoctrineOf(f, player) == 2 ? FactoryWarsUtil.Rules(f).ShadowStealthBonusSeconds : FP._0;
      unit->StealthUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.StealthSeconds + bonus);
      // 재사용 대기는 은신을 시작한 때부터 센다(지속 20초 + 대기 60초 = 시작 후 60초).
      unit->StealthReadyFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.StealthCooldownSeconds);
      unit->DetectedMask = 0;
    }

    public static void TryPlaceJammer(Frame f, PlayerRef player, EntityRef entity, Unit* unit, EntityRef node) {
      var spec = f.FindAsset(unit->Spec);
      if (spec.JammerRange <= FP._0 || unit->Garrison != EntityRef.None || unit->StealthUntilFrame <= f.Number
          || f.Unsafe.TryGetPointer<ResourceNode>(node, out var resource) == false) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Invalid, 0);
        return;
      }
      var position = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
      if (resource->Shape.Distance(position) > spec.JammerRange) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Invalid, 0);
        return;
      }
      // 1인당 동시 1개(설치 중 포함).
      var jammers = f.Filter<Jammer>();
      while (jammers.NextUnsafe(out _, out var jammer)) {
        if (jammer->Owner == player) {
          f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Limit, 0);
          return;
        }
      }
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var other)) {
        if (other->Owner == player && other->JammerUntilFrame > f.Number) {
          f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Limit, 0);
          return;
        }
      }
      MovementSystem.Stop(f, entity, unit);
      unit->JammerNode = node;
      unit->JammerUntilFrame = f.Number + System.Math.Max(1, FactoryWarsUtil.SecondsToFrames(f, spec.JammerSetupSeconds));
    }

    // ---------- 갱신 ----------

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      if (f.Number % DetectionTicks == 0) {
        UpdateDetection(f, rules);
      }
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Health <= FP._0) {
          continue;
        }
        var spec = f.FindAsset(unit->Spec);
        if (spec.StealthSeconds <= FP._0 && spec.GarrisonFireOnly == false) {
          continue;
        }
        if (unit->StealthUntilFrame > f.Number) {
          ExposeByProximity(f, unit, spec, transform->Position);
        }
        if (unit->JammerUntilFrame > 0) {
          InstallJammer(f, unit, spec, transform->Position);
        }
        if (spec.GarrisonFireOnly && unit->Garrison != EntityRef.None) {
          Fire(f, rules, unit, spec, transform->Position);
        }
      }
      UpdateJammers(f, rules);
    }

    /// <summary>적 유닛이 StealthExposeRadius 안으로 오면 은신이 풀린다.</summary>
    static void ExposeByProximity(Frame f, Unit* sniper, UnitSpec spec, FPVector3 position) {
      var limit = spec.StealthExposeRadius * spec.StealthExposeRadius;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var other)) {
        if (other->Owner == sniper->Owner || Friendly(f, other->Owner, sniper->Owner) || other->Health <= FP._0 || other->Garrison != EntityRef.None) {
          continue;
        }
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= limit) {
          sniper->StealthUntilFrame = f.Number;
          return;
        }
      }
    }

    static void InstallJammer(Frame f, Unit* unit, UnitSpec spec, FPVector3 position) {
      if (unit->Garrison != EntityRef.None || unit->StealthUntilFrame <= f.Number
          || f.Unsafe.TryGetPointer<ResourceNode>(unit->JammerNode, out var resource) == false || resource->Shape.Distance(position) > spec.JammerRange) {
        unit->JammerUntilFrame = 0;
        return;
      }
      if (f.Number < unit->JammerUntilFrame) {
        return;
      }
      var jammer = f.Create();
      f.Add(jammer, Transform3D.Create(position));
      f.Add(jammer, new Jammer {
        Owner = unit->Owner, Node = unit->JammerNode, Health = spec.JammerHealth, GatherMultiplier = spec.JammerGatherMultiplier,
        UntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, spec.JammerSeconds),
      });
      unit->JammerUntilFrame = 0;
      FactoryWarsUtil.Player(f, unit->Owner)->JammersPlaced++;
      f.Events.JammerPlaced(unit->Owner, jammer, position);
    }

    static void UpdateJammers(Frame f, GameRulesSpec rules) {
      var jammers = f.Filter<Transform3D, Jammer>();
      while (jammers.NextUnsafe(out var entity, out var transform, out var jammer)) {
        if (f.Number >= jammer->UntilFrame) {
          f.Events.JammerRemoved(jammer->Owner, transform->Position, false);
          f.Destroy(entity);
          continue;
        }
        if (jammer->DetectedMask == 0) {
          continue;
        }
        // 탐지로 드러난 장치는 탐지한 쪽 유닛이 가까이 서 있으면 풀린다(공병의 "수리로 해제"와 같은 규칙).
        var limit = rules.JammerDisarmRadius * rules.JammerDisarmRadius;
        var disarmer = PlayerRef.None;
        var units = f.Filter<Transform3D, Unit>();
        while (units.NextUnsafe(out _, out var other, out var unit)) {
          var index = (int)unit->Owner;
          if (index >= 0 && index < Players && (jammer->DetectedMask & (1 << index)) != 0 && unit->Health > FP._0 && unit->Garrison == EntityRef.None
              && FactoryWarsUtil.FlatDistanceSqr(other->Position, transform->Position) <= limit) {
            disarmer = unit->Owner;
            break;
          }
        }
        if (disarmer.IsValid == false) {
          continue;
        }
        jammer->Health -= rules.JammerDisarmPerSecond * f.DeltaTime;
        if (jammer->Health <= FP._0) {
          var state = FactoryWarsUtil.Player(f, disarmer);
          if (state != null) {
            state->JammersDestroyed++;
          }
          f.Events.JammerRemoved(jammer->Owner, transform->Position, true);
          f.Destroy(entity);
        }
      }
    }

    // ---------- 탐지 ----------

    static void UpdateDetection(Frame f, GameRulesSpec rules) {
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        if (unit->Garrison != EntityRef.None && spec.GarrisonFireOnly) {
          // 공중 항모 광역 정찰: 건물 내부에 주둔한 적 은신 저격수를 탐지·노출시킨다.
          var hostPos = f.Unsafe.TryGetPointer<Transform3D>(unit->Garrison, out var ht) ? ht->Position : transform->Position;
          var carrierMask = DetectByCarrier(f, unit->Owner, hostPos, unit->DetectedMask);
          unit->DetectedMask = carrierMask;
          if (carrierMask != 0) {
            unit->MuzzleRevealUntilFrame = System.Math.Max(unit->MuzzleRevealUntilFrame, f.Number + 10);
            unit->MuzzleVictimMask |= carrierMask;
          }
          continue;
        }
        if (unit->StealthUntilFrame <= f.Number) {
          unit->DetectedMask = 0;
          continue;
        }
        unit->DetectedMask = Detect(f, rules, unit->Owner, transform->Position, unit->DetectedMask);
      }
      var jammers = f.Filter<Transform3D, Jammer>();
      while (jammers.NextUnsafe(out _, out var transform, out var jammer)) {
        jammer->DetectedMask = Detect(f, rules, jammer->Owner, transform->Position, jammer->DetectedMask);
      }
    }

    static int DetectByCarrier(Frame f, PlayerRef owner, FPVector3 position, int previous) {
      var mask = 0;
      var carriers = f.Filter<Transform3D, Unit, Carrier>();
      while (carriers.NextUnsafe(out _, out var transform, out var unit, out var carrier)) {
        if (carrier->Assembling || unit->Health <= FP._0 || Friendly(f, unit->Owner, owner)) {
          continue;
        }
        var radius = f.FindAsset(unit->Spec).DetectRadius;
        if (radius <= FP._0) {
          radius = (FP)26;
        }
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= radius * radius) {
          var p = (int)unit->Owner;
          if (p >= 0 && p < Players) {
            mask |= 1 << p;
            if ((previous & (1 << p)) == 0 && FactoryWarsUtil.Player(f, unit->Owner) != null) {
              FactoryWarsUtil.Player(f, unit->Owner)->Detections++;
            }
          }
        }
      }
      return mask;
    }

    /// <summary>owner 쪽이 아닌 진영 중에서, 레이더·탐지 유닛이 position을 덮는 진영의 비트. 새로 드러난 진영은 탐지 횟수를 센다.</summary>
    static int Detect(Frame f, GameRulesSpec rules, PlayerRef owner, FPVector3 position, int previous) {
      var mask = 0;
      for (var p = 0; p < Players && p < f.PlayerCount; p++) {
        var viewer = (PlayerRef)p;
        if (Friendly(f, viewer, owner) || FactoryWarsUtil.Player(f, viewer) == null || FactoryWarsUtil.Player(f, viewer)->Active == false) {
          continue;
        }
        if (IsDetectedBy(f, rules, viewer, position)) {
          mask |= 1 << p;
          if ((previous & (1 << p)) == 0) {
            FactoryWarsUtil.Player(f, viewer)->Detections++;
          }
        }
      }
      return mask;
    }

    static bool IsDetectedBy(Frame f, GameRulesSpec rules, PlayerRef viewer, FPVector3 position) {
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (Friendly(f, building->Owner, viewer) == false || building->ConstructionRemaining > FP._0 || building->Paused != PauseReason.None) {
          continue;
        }
        var radius = f.FindAsset(building->Spec).RevealRadius;
        if (radius <= FP._0) {
          continue;
        }
        if (rules.RadarDetectRadius > FP._0 && radius > rules.RadarDetectRadius) {
          radius = rules.RadarDetectRadius;
        }
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= radius * radius) {
          return true;
        }
      }
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (Friendly(f, unit->Owner, viewer) == false || unit->Health <= FP._0) {
          continue;
        }
        if (f.Unsafe.TryGetPointer<Carrier>(entity, out var carrier) && carrier->Assembling) {
          continue;
        }
        var radius = f.FindAsset(unit->Spec).DetectRadius;
        if (radius > FP._0 && FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= radius * radius) {
          return true;
        }
      }
      return false;
    }

    // ---------- 사격 ----------

    static void Fire(Frame f, GameRulesSpec rules, Unit* unit, UnitSpec spec, FPVector3 position) {
      unit->AttackCooldown = FPMath.Max(FP._0, unit->AttackCooldown - f.DeltaTime);
      if (f.Number < unit->SetupUntilFrame || unit->AttackCooldown > FP._0 || FactoryWarsUtil.InTruce(f)) {
        return;
      }
      var range = spec.Range * (FP._1 + FP._0_10 + FP._0_05) + rules.GarrisonRangeBonus;
      if (f.Unsafe.TryGetPointer<Building>(unit->Garrison, out var host)) {
        range += f.FindAsset(host->Spec).FootprintRadius;
      }
      var target = Pick(f, unit, spec, position, range);
      if (target == EntityRef.None) {
        return;
      }
      var victim = f.Unsafe.GetPointer<Unit>(target);
      var targetSpec = f.FindAsset(victim->Spec);
      var victimOwner = victim->Owner;
      var targetPosition = f.Unsafe.GetPointer<Transform3D>(target)->Position;
      // 보병·공병은 Damage, 차량(IsVehicle·중형 이상)은 VehicleDamageRatio배, 저격수끼리는 SniperVsSniperMultiplier배가 최종 피해다.
      // DamageUnit이 크기 상성 배율을 곱하므로 미리 나눠 두어 표의 피해가 그대로 들어가게 한다.
      var wanted = spec.Damage * TechSystem.OutgoingMultiplier(f, unit->Owner, unit, spec) * TacticalSystem.VetDamage(f, unit);
      if (targetSpec.IsVehicle || targetSpec.Size >= UnitSize.Medium) {
        wanted *= spec.VehicleDamageRatio;
      }
      if (targetSpec.GarrisonFireOnly) {
        wanted *= rules.SniperVsSniperMultiplier;
      }
      var matchup = CombatSystem.UnitMultiplier(rules, spec.Attack, targetSpec);
      var weapon = new Weapon { Owner = unit->Owner, Attack = spec.Attack, IsAir = false, IsRanged = true, Unit = unit, Spec = spec, Origin = position };
      CombatSystem.Damage(f, weapon, target, matchup > FP._0 ? wanted / matchup : wanted);
      var killed = f.Exists(target) == false;
      unit->AttackCooldown = spec.AttackInterval * TacticalSystem.VetReload(f, unit);
      unit->LastFiredFrame = f.Number;
      if (killed) {
        var state = FactoryWarsUtil.Player(f, unit->Owner);
        if (targetSpec.Size == UnitSize.Engineer) {
          state->SnipeKillsEngineers++;
        } else {
          state->SnipeKillsInfantry++;
        }
      }
      // 사격하면 그 건물이 피격 진영(과 동맹)에게 잠깐 보이고 방향이 표시된다.
      unit->MuzzleRevealUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.MuzzleRevealSeconds);
      var mask = 0;
      for (var p = 0; p < Players && p < f.PlayerCount; p++) {
        if (Friendly(f, (PlayerRef)p, victimOwner)) {
          mask |= 1 << p;
        }
      }
      unit->MuzzleVictimMask = mask;
      f.Events.SniperFired(unit->Owner, victimOwner, position, targetPosition, killed);
    }

    static EntityRef Pick(Frame f, Unit* sniper, UnitSpec spec, FPVector3 position, FP range) {
      // 직접 지시한 표적이 사거리 안이면 그것을 먼저 쏜다.
      var ordered = sniper->TargetEntity;
      if (ordered != EntityRef.None && Eligible(f, sniper, spec, ordered, position, range)) {
        return ordered;
      }
      var best = EntityRef.None;
      var bestDistance = FP.MaxValue;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out _)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, position);
        if (distance < bestDistance && Eligible(f, sniper, spec, entity, position, range)) {
          best = entity;
          bestDistance = distance;
        }
      }
      return best;
    }

    static bool Eligible(Frame f, Unit* sniper, UnitSpec spec, EntityRef target, FPVector3 position, FP range) {
      if (f.Unsafe.TryGetPointer<Unit>(target, out var other) == false || other->Owner == sniper->Owner || other->Health <= FP._0 || other->Garrison != EntityRef.None) {
        return false;
      }
      return f.FindAsset(other->Spec).IsAir == false
        && CombatSystem.CanEngage(f, sniper->Owner, spec, target, position, range)
        && FactoryWarsUtil.IsVisibleTo(f, sniper->Owner, target);
    }
  }
}
