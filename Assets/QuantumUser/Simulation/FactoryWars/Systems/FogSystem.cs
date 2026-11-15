namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>
  /// 항목 34-A 전장의 안개. 플레이어마다 지금 보이는 칸(Visible)과 본 적 있는 칸(Explored)을 96×96 비트 격자로 들고 있고,
  /// 5틱(0.25초)마다 시야 원천(유닛·건물)의 반경으로 다시 만든다. 동맹은 시야를 공유한다.
  /// 규칙은 `FactoryWarsUtil.IsVisibleTo` 한 곳에 걸려 있어 자동 표적·공격 명령·AI가 모두 같은 판정을 쓴다.
  /// GameRulesSpec.FogOfWar가 꺼져 있으면 아무것도 하지 않는다(기본 꺼짐).
  /// 알려진 한계: lockstep이라 모든 클라이언트가 전체 상태를 가진다(맵핵 가능). 이번에는 대응하지 않는다.
  /// 모든 값은 FP·정수다(결정론).
  /// </summary>
  public unsafe class FogSystem : SystemMainThread {
    public const int Size = 96;
    const int Bytes = 1152;
    const int Players = 4;

    public static bool Enabled(Frame f) => FactoryWarsUtil.Rules(f).FogOfWar;

    public override void OnInit(Frame f) {
      var entity = f.Create();
      f.Add<Fog>(entity);
      var fog = f.Unsafe.GetPointer<Fog>(entity);
      var map = FactoryWarsUtil.MapInfo(f);
      var width = map.BoundsMax.X - map.BoundsMin.X;
      var depth = map.BoundsMax.Y - map.BoundsMin.Y;
      fog->Cell = FPMath.Max(width, depth) / Size;
      if (fog->Cell <= FP._0) {
        fog->Cell = FP._1;
      }
      fog->MinX = map.BoundsMin.X;
      fog->MinZ = map.BoundsMin.Y;
    }

    public override void Update(Frame f) {
      if (Enabled(f) == false) {
        return;
      }
      var fog = f.Unsafe.GetPointerSingleton<Fog>();
      if (f.Number < fog->NextUpdateFrame) {
        return;
      }
      fog->NextUpdateFrame = f.Number + System.Math.Max(1, FactoryWarsUtil.Rules(f).FogUpdateTicks);
      Recompute(f, fog);
      fog->Revision++;
    }

    // ---------- 조회 ----------

    static bool TryCell(Fog* fog, FPVector3 position, out int cell) {
      var x = FPMath.FloorToInt((position.X - fog->MinX) / fog->Cell);
      var z = FPMath.FloorToInt((position.Z - fog->MinZ) / fog->Cell);
      cell = z * Size + x;
      return x >= 0 && x < Size && z >= 0 && z < Size;
    }

    static bool Get(byte* bits, int cell) => (bits[cell >> 3] & (1 << (cell & 7))) != 0;

    static void Set(FogGrid* grid, int cell) {
      grid->Visible[cell >> 3] = (byte)(grid->Visible[cell >> 3] | (1 << (cell & 7)));
    }

    /// <summary>그 자리가 지금 이 플레이어(또는 동맹)에게 보이는가. 안개가 꺼져 있으면 항상 true.</summary>
    public static bool IsVisible(Frame f, PlayerRef player, FPVector3 position) {
      if (Enabled(f) == false) {
        return true;
      }
      var fog = f.Unsafe.GetPointerSingleton<Fog>();
      var index = (int)player;
      if (index < 0 || index >= Players || TryCell(fog, position, out var cell) == false) {
        return false;
      }
      return Get(fog->Grids.GetPointer(index)->Visible, cell);
    }

    /// <summary>그 자리를 본 적이 있는가(탐색됨).</summary>
    public static bool IsExplored(Frame f, PlayerRef player, FPVector3 position) {
      if (Enabled(f) == false) {
        return true;
      }
      var fog = f.Unsafe.GetPointerSingleton<Fog>();
      var index = (int)player;
      if (index < 0 || index >= Players || TryCell(fog, position, out var cell) == false) {
        return false;
      }
      return Get(fog->Grids.GetPointer(index)->Explored, cell);
    }

    /// <summary>
    /// 개체가 이 플레이어에게 보이는가. 자기·동맹 개체와 주인 없는 개체는 늘 보이고, 적 개체는 칸이 보여야 보인다.
    /// 고지대 은폐는 이 뒤에 `IsVisibleTo`가 따로 검사한다.
    /// </summary>
    public static bool CanSee(Frame f, PlayerRef player, EntityRef entity) {
      if (Enabled(f) == false) {
        return true;
      }
      if (CombatSystem.TryGetOwner(f, entity, out var owner) == false) {
        return true;
      }
      if (owner == player || DiplomacySystem.GetRelation(f, player, owner) == Relation.Allied) {
        return true;
      }
      if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false) {
        return true;
      }
      return IsVisible(f, player, transform->Position);
    }

    // ---------- 갱신 ----------

    static void Mark(Fog* fog, FogGrid* grid, FPVector3 center, FP radius) {
      if (radius <= FP._0) {
        return;
      }
      var cx = (center.X - fog->MinX) / fog->Cell;
      var cz = (center.Z - fog->MinZ) / fog->Cell;
      var r = radius / fog->Cell;
      var x0 = System.Math.Max(0, FPMath.FloorToInt(cx - r));
      var x1 = System.Math.Min(Size - 1, FPMath.CeilToInt(cx + r));
      var z0 = System.Math.Max(0, FPMath.FloorToInt(cz - r));
      var z1 = System.Math.Min(Size - 1, FPMath.CeilToInt(cz + r));
      var r2 = r * r;
      for (var z = z0; z <= z1; z++) {
        var dz = z + FP._0_50 - cz;
        for (var x = x0; x <= x1; x++) {
          var dx = x + FP._0_50 - cx;
          if (dx * dx + dz * dz <= r2) {
            Set(grid, z * Size + x);
          }
        }
      }
    }

    static void Recompute(Frame f, Fog* fog) {
      var rules = FactoryWarsUtil.Rules(f);
      var map = FactoryWarsUtil.MapInfo(f);
      for (var p = 0; p < Players; p++) {
        var grid = fog->Grids.GetPointer(p);
        for (var i = 0; i < Bytes; i++) {
          grid->Visible[i] = 0;
        }
      }
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        var owner = (int)unit->Owner;
        if (owner < 0 || owner >= Players) {
          continue;
        }
        var spec = f.FindAsset(unit->Spec);
        var radius = spec.SightRange;
        if (spec.IsAir == false && map.HighGroundAt(transform->Position) >= 0) {
          radius += rules.FogHighGroundBonus;
        }
        if (unit->Garrison != EntityRef.None) {
          radius = spec.GarrisonSightRange > FP._0 ? spec.GarrisonSightRange : radius + rules.FogGarrisonBonus;
        }
        Mark(fog, fog->Grids.GetPointer(owner), transform->Position, radius);
        // 항목 34-C: 저격수가 쏜 건물은 피격 진영에게 잠깐 보인다.
        if (f.Number < unit->MuzzleRevealUntilFrame) {
          for (var p = 0; p < Players; p++) {
            if ((unit->MuzzleVictimMask & (1 << p)) != 0) {
              Mark(fog, fog->Grids.GetPointer(p), transform->Position, rules.MuzzleRevealRadius);
            }
          }
        }
      }
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        var owner = (int)building->Owner;
        if (owner < 0 || owner >= Players) {
          continue;
        }
        Mark(fog, fog->Grids.GetPointer(owner), transform->Position, f.FindAsset(building->Spec).SightRange);
      }
      // 항목 34-B: 보급 신호탄은 모든 플레이어에게, 정찰 사진은 얻은 플레이어에게 그 자리를 잠깐 보여 준다.
      if (f.Number < fog->FlareUntilFrame) {
        for (var p = 0; p < Players; p++) {
          for (var i = 0; i < fog->FlareCount && i < Players; i++) {
            Mark(fog, fog->Grids.GetPointer(p), fog->FlarePositions[i], rules.DropFlareRadius);
          }
        }
      }
      for (var p = 0; p < Players; p++) {
        if (f.Number < fog->ReconUntilFrames[p]) {
          Mark(fog, fog->Grids.GetPointer(p), fog->ReconPositions[p], rules.DropReconRadius);
        }
      }
      ShareWithAllies(f, fog);
      for (var p = 0; p < Players; p++) {
        var grid = fog->Grids.GetPointer(p);
        for (var i = 0; i < Bytes; i++) {
          grid->Explored[i] = (byte)(grid->Explored[i] | grid->Visible[i]);
        }
      }
    }

    /// <summary>동맹끼리 보이는 칸을 합친다. 순서에 상관없도록 먼저 각자의 원래 격자를 복사해 두고 합친다.</summary>
    static void ShareWithAllies(Frame f, Fog* fog) {
      var own = stackalloc byte[Players * Bytes];
      for (var p = 0; p < Players; p++) {
        var grid = fog->Grids.GetPointer(p);
        for (var i = 0; i < Bytes; i++) {
          own[p * Bytes + i] = grid->Visible[i];
        }
      }
      for (var a = 0; a < Players; a++) {
        for (var b = 0; b < Players; b++) {
          if (a == b || DiplomacySystem.GetRelation(f, (PlayerRef)a, (PlayerRef)b) != Relation.Allied) {
            continue;
          }
          var grid = fog->Grids.GetPointer(a);
          for (var i = 0; i < Bytes; i++) {
            grid->Visible[i] = (byte)(grid->Visible[i] | own[b * Bytes + i]);
          }
        }
      }
    }
  }
}
