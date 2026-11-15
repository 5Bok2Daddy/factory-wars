namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 항목 34-D 정찰 연동 비조작 항공 작전(Sortie). 비행장(Building.Hangar) 격납 칸 4개에 생산된 작전기가 대기하고,
  /// OrderSortieCommand(칸, 목표, 진입 방향)로 출격한다. 목표는 지금 보이는 칸이어야 한다(안개).
  /// 궤적은 출격 때 점 6개(비행장 A → 진입점 E → 목표 T → 이탈점 X → 선회점 R → 비행장 A)와 누적 길이를 한 번 계산해 컴포넌트에 저장하고,
  /// 위치는 (경과 프레임 × 속도 × DeltaTime)만큼 구간 누적 거리를 따라 계산한다(FP·정수뿐, 물리·실시계 없음).
  /// 기총소사·폭격·공수 투하·대공 격추·재정비는 모두 UnitSpec(Sortie*)·GameRulesSpec 데이터다.
  /// </summary>
  public unsafe class SortieSystem : SystemMainThread {
    public const int Points = 6;
    public const int Slots = 4;

    // ---------- 궤적(순수 함수: 시험이 직접 부른다) ----------

    /// <summary>점 6개와 누적 길이를 만든다. heading은 진입 방향(0이면 +X).</summary>
    public static void BuildPath(FPVector3 airfield, FPVector3 target, FPVector2 heading, UnitSpec spec, FPVector3* points, FP* cumulative) {
      var d = heading.SqrMagnitude > FP._0 ? heading.Normalized : new FPVector2(FP._1, FP._0);
      var dir = new FPVector3(d.X, FP._0, d.Y);
      var side = new FPVector3(-d.Y, FP._0, d.X);
      var a = new FPVector3(airfield.X, FP._0, airfield.Z);
      var t = new FPVector3(target.X, FP._0, target.Z);
      points[0] = a;
      points[1] = t - dir * spec.SortieApproach;
      points[2] = t;
      points[3] = t + dir * spec.SortieExit;
      points[4] = points[3] + dir * spec.SortieTurn + side * (spec.SortieTurn * 2);
      points[5] = a;
      cumulative[0] = FP._0;
      for (var i = 1; i < Points; i++) {
        cumulative[i] = cumulative[i - 1] + (points[i] - points[i - 1]).Magnitude;
      }
    }

    /// <summary>누적 거리 s에서의 위치와 진행 방향. s가 끝을 넘으면 마지막 점.</summary>
    public static FPVector3 PositionAt(FPVector3* points, FP* cumulative, FP s, out FPVector3 direction) {
      direction = new FPVector3(FP._1, FP._0, FP._0);
      for (var i = 0; i < Points - 1; i++) {
        var length = cumulative[i + 1] - cumulative[i];
        if (s <= cumulative[i + 1] || i == Points - 2) {
          var t = length > FP._0 ? FPMath.Clamp((s - cumulative[i]) / length, FP._0, FP._1) : FP._0;
          var segment = points[i + 1] - points[i];
          if (length > FP._0) {
            direction = segment / length;
          }
          return points[i] + segment * t;
        }
      }
      return points[Points - 1];
    }

    public static FP DistanceAt(Frame f, Sortie* sortie) => FP.FromRaw((long)(f.Number - sortie->StartFrame) * sortie->Speed.RawValue) * f.DeltaTime;

    // ---------- 격납 칸 ----------

    public static int FreeSlot(Building* airfield) {
      for (var i = 0; i < Slots; i++) {
        if (airfield->Hangar[i].State == 0) {
          return i;
        }
      }
      return -1;
    }

    public static bool Store(Frame f, Building* airfield, AssetRef<UnitSpec> spec) {
      var slot = FreeSlot(airfield);
      if (slot < 0) {
        return false;
      }
      var hangar = airfield->Hangar.GetPointer(slot);
      hangar->Spec = spec;
      hangar->State = 1;
      hangar->ReadyFrame = f.Number;
      return true;
    }

    // ---------- 출격 ----------

    public static void TryLaunch(Frame f, PlayerRef player, EntityRef airfieldEntity, int slot, FPVector3 target, FPVector2 heading) {
      if (f.Unsafe.TryGetPointer<Building>(airfieldEntity, out var airfield) == false || airfield->Owner != player || airfield->ConstructionRemaining > FP._0
          || slot < 0 || slot >= Slots || airfield->Hangar[slot].State != 1) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Invalid, 0);
        return;
      }
      var hangar = airfield->Hangar.GetPointer(slot);
      if (f.Number < hangar->ReadyFrame) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Cooldown, 0);
        return;
      }
      if (FogSystem.IsVisible(f, player, target) == false) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.NoVision, 0);
        return;
      }
      // 항목 37 기상: 신호탄·정찰 사진 구역이 아니면 기상 중에는 일반 출격을 못 한다.
      if (TacticalSystem.WeatherActive(f) && FogSystem.InSignalArea(f, player, target) == false) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.Weather, 0);
        return;
      }
      var spec = f.FindAsset(hangar->Spec);
      var state = FactoryWarsUtil.Player(f, player);
      var missing = state->Stock.Missing(spec.SortieCost);
      if (missing != MissingResource.None) {
        f.Events.ActionRejected(player, ActionKind.Attack, RejectReason.InsufficientResources, (int)missing);
        return;
      }
      state->Stock -= spec.SortieCost;
      var entity = f.Create();
      var origin = f.Unsafe.GetPointer<Transform3D>(airfieldEntity)->Position;
      var sortie = new Sortie {
        Owner = player, Spec = hangar->Spec, Airfield = airfieldEntity, Slot = slot, StartFrame = f.Number, Speed = spec.SortieSpeed,
        Health = spec.SortieHealth, Target = target, Heading = heading,
      };
      BuildPath(origin, target, heading, spec, sortie.Points.GetPointer(0), sortie.Cum.GetPointer(0));
      f.Add(entity, Transform3D.Create(new FPVector3(origin.X, FactoryWarsUtil.Rules(f).AirAltitude, origin.Z)));
      f.Add(entity, sortie);
      hangar->State = 2;
      state->SortiesLaunched++;
      f.Events.SortieLaunched(player, entity, (int)spec.SortieKind, target);
    }

    // ---------- 갱신 ----------

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      var sorties = f.Filter<Transform3D, Sortie>();
      while (sorties.NextUnsafe(out var entity, out var transform, out var sortie)) {
        var spec = f.FindAsset(sortie->Spec);
        var s = DistanceAt(f, sortie);
        var position = PositionAt(sortie->Points.GetPointer(0), sortie->Cum.GetPointer(0), s, out var direction);
        transform->Position = new FPVector3(position.X, rules.AirAltitude, position.Z);
        transform->Rotation = FPQuaternion.LookRotation(direction);

        if (ShotDown(f, rules, sortie, spec, position)) {
          End(f, entity, sortie, true, position);
          continue;
        }
        switch (spec.SortieKind) {
          case SortieKind.Strafe:
            Strafe(f, sortie, spec, s, position, direction);
            break;
          case SortieKind.Bomb:
            Bomb(f, sortie, spec, s, position);
            break;
          case SortieKind.Drop:
            Drop(f, sortie, spec, s);
            break;
        }
        if (s >= sortie->Cum[Points - 1]) {
          End(f, entity, sortie, false, position);
        }
      }
    }

    static Weapon WeaponOf(Sortie* sortie, UnitSpec spec, FPVector3 origin) =>
      new Weapon { Owner = sortie->Owner, Attack = spec.Attack, IsAir = true, IsRanged = true, Spec = spec, Origin = origin, Area = spec.SortieKind == SortieKind.Bomb };

    static void Strafe(Frame f, Sortie* sortie, UnitSpec spec, FP s, FPVector3 position, FPVector3 direction) {
      if (s < sortie->Cum[1] || s > sortie->Cum[3] || spec.SortieTicks <= 0 || f.Number % spec.SortieTicks != 0) {
        return;
      }
      var weapon = WeaponOf(sortie, spec, position);
      var along = new FPVector2(direction.X, direction.Z);
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (unit->Garrison != EntityRef.None || unit->Health <= FP._0 || f.FindAsset(unit->Spec).IsAir || DiplomacySystem.CanAttack(f, sortie->Owner, unit->Owner) == false) {
          continue;
        }
        var offset = new FPVector2(transform->Position.X - position.X, transform->Position.Z - position.Z);
        var forward = FPMath.Abs(FPVector2.Dot(offset, along));
        var lateral = FPMath.Abs(offset.X * along.Y - offset.Y * along.X);
        if (forward <= spec.SortieWidth && lateral <= spec.SortieWidth) {
          CombatSystem.Damage(f, weapon, entity, spec.Damage);
        }
      }
    }

    static void Bomb(Frame f, Sortie* sortie, UnitSpec spec, FP s, FPVector3 position) {
      // 목표 T를 중심으로 gap 간격으로 n발: 첫 발은 T보다 gap*(n-1)/2 앞.
      var gap = spec.SortieBombGap;
      var first = sortie->Cum[2] - gap * (spec.SortieBombs - 1) / FP._2;
      while (sortie->NextEvent < spec.SortieBombs && s >= first + gap * sortie->NextEvent) {
        sortie->NextEvent++;
        var weapon = WeaponOf(sortie, spec, position);
        CombatSystem.Blast(f, weapon, position, spec.SortieWidth, spec.Damage, FP._0_50, spec.SortieBuildingMultiplier, spec.SortieBuildingMultiplier, false);
      }
    }

    static void Drop(Frame f, Sortie* sortie, UnitSpec spec, FP s) {
      if (sortie->Dropped || s < sortie->Cum[2]) {
        return;
      }
      sortie->Dropped = true;
      for (var i = 0; i < spec.SortieCargoCount; i++) {
        // 낙하 위치는 반경 안 frame.RNG 흩뿌림(결정론).
        var angle = f.RNG->Next(FP._0, FP.PiTimes2);
        var radius = f.RNG->Next(FP._0, spec.SortieWidth);
        var at = sortie->Target + new FPVector3(FPMath.Cos(angle) * radius, FP._0, FPMath.Sin(angle) * radius);
        var spawned = ProductionSystem.SpawnUnitAt(f, sortie->Owner, spec.SortieCargo, at, null);
        if (f.Unsafe.TryGetPointer<Unit>(spawned, out var cargo)) {
          cargo->ReservedUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, FP._4);
        }
      }
    }

    /// <summary>대공(공중만 맞히는 무기)이 사거리 안에 있으면 매 틱 초당 피해량(피해/간격 × SortieAntiAirFactor)만큼 체력이 준다.</summary>
    static bool ShotDown(Frame f, GameRulesSpec rules, Sortie* sortie, UnitSpec spec, FPVector3 position) {
      var damage = FP._0;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        var other = f.FindAsset(unit->Spec);
        if (other.Attack != AttackType.AntiAir || unit->Health <= FP._0 || DiplomacySystem.CanAttack(f, unit->Owner, sortie->Owner) == false) {
          continue;
        }
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= other.Range * other.Range && other.AttackInterval > FP._0) {
          damage += other.Damage / other.AttackInterval;
        }
      }
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        var other = f.FindAsset(building->Spec);
        if (other.Attack != AttackType.AntiAir || building->Health <= FP._0 || building->ConstructionRemaining > FP._0 || DiplomacySystem.CanAttack(f, building->Owner, sortie->Owner) == false) {
          continue;
        }
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= other.Range * other.Range && other.AttackInterval > FP._0) {
          damage += other.Damage / other.AttackInterval;
        }
      }
      sortie->Health -= damage * rules.SortieAntiAirFactor * f.DeltaTime;
      return sortie->Health <= FP._0;
    }

    static void End(Frame f, EntityRef entity, Sortie* sortie, bool shotDown, FPVector3 position) {
      var spec = f.FindAsset(sortie->Spec);
      if (f.Unsafe.TryGetPointer<Building>(sortie->Airfield, out var airfield)) {
        var hangar = airfield->Hangar.GetPointer(sortie->Slot);
        if (shotDown) {
          // 격추: 탑재·폭탄은 사라지고 칸은 비며 재생산해야 한다.
          hangar->State = 0;
          hangar->Spec = default;
        } else {
          hangar->State = 1;
          // 항목 37 공군 타격 사령부: 재정비 시간 단축.
          var rearm = TacticalSystem.DoctrineOf(f, sortie->Owner) == 1 ? spec.SortieRearmSeconds * FactoryWarsUtil.Rules(f).AirRearmMultiplier : spec.SortieRearmSeconds;
          hangar->ReadyFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, rearm);
        }
      }
      var state = FactoryWarsUtil.Player(f, sortie->Owner);
      if (shotDown) {
        state->SortiesShotDown++;
      } else {
        state->SortiesCompleted++;
      }
      f.Events.SortieEnded(sortie->Owner, shotDown, position);
      f.Destroy(entity);
    }
  }
}
