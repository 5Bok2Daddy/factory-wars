namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>규칙 기반 AI. 사람과 같은 함수로 행동해 비용과 제한을 그대로 받는다.</summary>
  public unsafe class BotSystem : SystemMainThread {
    const int RoleCount = 12;
    const int MaxOrder = MoveUnitsCommand.MaxUnits;

    public static void Init(Frame f, PlayerRef player, BotDifficulty difficulty, FPVector3 home) {
      var state = FactoryWarsUtil.Player(f, player);
      state->IsBot = true;
      state->Difficulty = difficulty;
      var ai = Spec(f, difficulty);
      var bot = f.Global->Bots.GetPointer(player);
      bot->Home = home;
      bot->NextThinkFrame = (int)player + 1;
      bot->NextAttackFrame = FactoryWarsUtil.SecondsToFrames(f, ai.FirstAttackSeconds);
      bot->WaveSize = ai.WaveSize;
    }

    static AiSpec Spec(Frame f, BotDifficulty difficulty) {
      var rules = FactoryWarsUtil.Rules(f);
      return f.FindAsset(difficulty == BotDifficulty.Easy ? rules.EasyAi : rules.NormalAi);
    }

    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      if (f.Unsafe.TryGetPointerSingleton<Diplomacy>(out _) && DiplomacySystem.IsAllowed(f)) {
        UpdateOpinions(f);
      }
      for (var i = 0; i < f.PlayerCount; i++) {
        var state = f.Global->Players.GetPointer(i);
        var bot = f.Global->Bots.GetPointer(i);
        if (state->IsBot == false || state->Defeated || f.Number < bot->NextThinkFrame) {
          continue;
        }
        var ai = Spec(f, state->Difficulty);
        bot->NextThinkFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.ThinkSeconds);
        Think(f, i, state, bot, ai);
      }
    }

    static void Think(Frame f, PlayerRef player, PlayerState* state, BotState* bot, AiSpec ai) {
      var faction = f.FindAsset(state->Faction);
      var counts = stackalloc int[RoleCount];
      var building = 0;
      var foundation = EntityRef.None;
      for (var i = 0; i < RoleCount; i++) {
        counts[i] = 0;
      }
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out var entity, out var owned)) {
        if (owned->Owner != player) {
          continue;
        }
        counts[(int)f.FindAsset(owned->Spec).Role]++;
        if (owned->ConstructionRemaining > FP._0) {
          building++;
          foundation = entity;
        }
      }

      var engineers = ManageWorkers(f, player, ai, foundation, counts);
      ManageProcessors(f, player, state, ai);
      // 싸울 상대가 없어진 동맹을 끊을 철을 먼저 모은다. 안 그러면 서로 공격하지 못한 채 경기가 끝나지 않는다.
      if (state->Stock.Iron >= FactoryWarsUtil.Rules(f).BreakCost || HasIdleAlliance(f, player) == false) {
        var reserve = building < ai.ConcurrentBuilds ? BuildNext(f, player, state, bot, ai, faction, counts) : default;
        Produce(f, player, state, ai, faction, engineers, reserve);
      }
      Attack(f, player, bot, ai);
      Negotiate(f, player, state, bot, ai);
    }

    static bool HasIdleAlliance(Frame f, PlayerRef player) {
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        if (other != player && DiplomacySystem.NoEnemyFrame(f, player, other) > 0) {
          return true;
        }
      }
      return false;
    }

    static bool IsGathering(Frame f, Worker* worker, ResourceKind kind) {
      return (worker->Task == WorkerTask.Gather || worker->Task == WorkerTask.Deliver)
        && f.Unsafe.TryGetPointer<ResourceNode>(worker->Target, out var node) && node->Kind == kind;
    }

    /// <summary>쉬는 공병에게 건설이나 채집을 맡기고 공병 수를 돌려준다. 철과 목재 비율을 맞춘다.</summary>
    static int ManageWorkers(Frame f, PlayerRef player, AiSpec ai, EntityRef foundation, int* counts) {
      var count = 0;
      var iron = 0;
      var wood = 0;
      var needsBuilder = foundation != EntityRef.None;
      var workers = f.Filter<Unit, Worker>();
      while (workers.NextUnsafe(out _, out var unit, out var worker)) {
        if (unit->Owner != player) {
          continue;
        }
        count++;
        if (worker->Task == WorkerTask.Build && worker->Target == foundation) {
          needsBuilder = false;
        }
        iron += IsGathering(f, worker, ResourceKind.Iron) ? 1 : 0;
        wood += IsGathering(f, worker, ResourceKind.Wood) ? 1 : 0;
      }

      var share = counts[(int)BuildingRole.PowerPlant] > 0 ? ai.IronShareLate : ai.IronShareEarly;
      workers = f.Filter<Unit, Worker>();
      while (workers.NextUnsafe(out var entity, out var unit, out var worker)) {
        if (unit->Owner != player || worker->Task != WorkerTask.Idle) {
          continue;
        }
        if (needsBuilder) {
          WorkerSystem.AssignToBuilding(f, entity, foundation);
          needsBuilder = false;
          continue;
        }
        var kind = iron < share * (iron + wood + 1) ? ResourceKind.Iron : ResourceKind.Wood;
        var position = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
        var node = WorkerSystem.NearestNode(f, player, position, FP.UseableMax, kind);
        if (node != EntityRef.None) {
          WorkerSystem.Assign(f, entity, WorkerTask.Gather, node);
          iron += kind == ResourceKind.Iron ? 1 : 0;
          wood += kind == ResourceKind.Wood ? 1 : 0;
        }
      }

      // 발전소가 생기면 비율이 바뀐다. 목재 쪽이 넘치면 한 명씩 철로 옮긴다.
      if (iron + 1 < share * (iron + wood)) {
        workers = f.Filter<Unit, Worker>();
        while (workers.NextUnsafe(out var entity, out var unit, out var worker)) {
          if (unit->Owner == player && IsGathering(f, worker, ResourceKind.Wood)) {
            var position = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
            var node = WorkerSystem.NearestNode(f, player, position, FP.UseableMax, ResourceKind.Iron);
            if (node != EntityRef.None) {
              WorkerSystem.Assign(f, entity, WorkerTask.Gather, node);
            }
            break;
          }
        }
      }
      return count;
    }

    /// <summary>철이 모자라거나 강철이 충분하면 가공소를 끈다.</summary>
    static void ManageProcessors(Frame f, PlayerRef player, PlayerState* state, AiSpec ai) {
      var off = state->Stock.Iron < ai.ProcessorIronFloor || state->Stock.Steel >= ai.SteelCap;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player && f.FindAsset(building->Spec).ProcessSeconds > FP._0) {
          building->ProcessorOff = off;
        }
      }
    }

    static AssetRef<BuildingSpec> FindRole(Frame f, FactionSpec faction, BuildingRole role) {
      for (var i = 0; i < faction.Buildings.Length; i++) {
        if (f.FindAsset(faction.Buildings[i]).Role == role) {
          return faction.Buildings[i];
        }
      }
      return default;
    }

    static bool NextRole(PlayerRef player, PlayerState* state, AiSpec ai, int* counts, Frame f, out BuildingRole role) {
      // 전기·인구·강철이 모자라면 순서보다 먼저 채운다.
      if (counts[(int)BuildingRole.PowerPlant] > 0 && state->PowerRate < ai.PowerMargin) {
        role = BuildingRole.PowerPlant;
        return true;
      }
      if (state->PopulationCap - state->PopulationUsed - ProductionSystem.QueuedPopulation(f, player) < ai.PopulationMargin) {
        role = BuildingRole.SupplyDepot;
        return true;
      }
      var processors = counts[(int)BuildingRole.Processor];
      // 강철을 쓸 군수공장이 생긴 뒤에야 강철이 병목이다.
      if (processors > 0 && processors < ai.MaxProcessors && counts[(int)BuildingRole.VehicleFactory] > 0
          && state->Stock.Steel < ai.SteelTarget && state->Stock.Iron >= ai.IronSurplus) {
        role = BuildingRole.Processor;
        return true;
      }

      var seen = stackalloc int[RoleCount];
      for (var i = 0; i < RoleCount; i++) {
        seen[i] = 0;
      }
      for (var i = 0; i < ai.BuildOrder.Length; i++) {
        var index = (int)ai.BuildOrder[i];
        seen[index]++;
        if (counts[index] < seen[index]) {
          role = ai.BuildOrder[i];
          return true;
        }
      }
      role = default;
      return false;
    }

    /// <summary>다음 건물을 짓는다. 자원을 모으는 중이면 남겨 둘 비용을 돌려준다.</summary>
    static ResourceCost BuildNext(Frame f, PlayerRef player, PlayerState* state, BotState* bot, AiSpec ai, FactionSpec faction, int* counts) {
      var center = bot->Home;
      var hasRole = NextRole(player, state, ai, counts, f, out var role);

      // 확장: 멀리 일하러 간 공병 옆에 사령부나 집하장을 둔다.
      if (FindDistantNode(f, player, ai, out var distant)) {
        var room = state->PopulationCap - state->PopulationUsed - ProductionSystem.QueuedPopulation(f, player);
        var reserves = NearbyReserves(f, distant, ai.ExpandReserveRadius);
        if (FactoryWarsUtil.InTruce(f) == false && ExpandsWithHeadquarters(ai, state->Stock, reserves, room, counts[(int)BuildingRole.Headquarters])) {
          role = BuildingRole.Headquarters;
          hasRole = true;
          center = distant;
        } else if (counts[(int)BuildingRole.CollectionYard] < ai.MaxYards) {
          role = BuildingRole.CollectionYard;
          hasRole = true;
          center = distant;
        }
      }
      if (hasRole == false) {
        return default;
      }

      var specRef = FindRole(f, faction, role);
      var spec = f.FindAsset(specRef);
      if (spec == null || ProductionSystem.IsUnlocked(f, player, spec) == false) {
        return default;
      }
      if (state->Stock.Covers(spec.Cost) == false) {
        return spec.Cost;
      }
      if (FindSite(f, player, ai, spec, center, out var site) && FindBuilder(f, player, site, out var engineer)) {
        ProductionSystem.TryBuild(f, player, engineer, specRef, site);
      }
      return default;
    }

    /// <summary>큰 자원이거나 인구가 막혔을 때, 비용을 낼 수 있으면 사령부로 확장한다. 아니면 집하장이다.</summary>
    public static bool ExpandsWithHeadquarters(AiSpec ai, ResourceCost stock, FP reserves, FP populationRoom, int headquarters) {
      if (headquarters >= ai.MaxHeadquarters || stock.Iron < ai.ExpandIron || stock.Wood < ai.ExpandWood) {
        return false;
      }
      return reserves >= ai.ExpandReserves || populationRoom < ai.ExpandPopulationRoom;
    }

    static FP NearbyReserves(Frame f, FPVector3 position, FP radius) {
      var total = FP._0;
      var limit = radius * radius;
      var nodes = f.Filter<Transform3D, ResourceNode>();
      while (nodes.NextUnsafe(out _, out var transform, out var node)) {
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= limit) {
          total += node->Remaining;
        }
      }
      return total;
    }

    static bool FindDistantNode(Frame f, PlayerRef player, AiSpec ai, out FPVector3 position) {
      var limit = ai.ExpandDistance * ai.ExpandDistance;
      var workers = f.Filter<Unit, Worker>();
      while (workers.NextUnsafe(out _, out var unit, out var worker)) {
        if (unit->Owner == player && worker->Task == WorkerTask.Gather && f.Has<ResourceNode>(worker->Target)) {
          position = f.Unsafe.GetPointer<Transform3D>(worker->Target)->Position;
          if (DistanceToDropOff(f, player, position) > limit) {
            return true;
          }
        }
      }
      position = default;
      return false;
    }

    static FP DistanceToDropOff(Frame f, PlayerRef player, FPVector3 position) {
      var best = FP.UseableMax;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->Owner == player && f.FindAsset(building->Spec).IsDropOff) {
          best = FPMath.Min(best, FactoryWarsUtil.FlatDistanceSqr(transform->Position, position));
        }
      }
      return best;
    }

    /// <summary>중심에서 바깥으로 격자를 돌며 지을 수 있는 첫 칸을 찾는다.</summary>
    static bool FindSite(Frame f, PlayerRef player, AiSpec ai, BuildingSpec spec, FPVector3 center, out FPVector3 site) {
      for (var ring = 1; ring <= ai.SiteRings; ring++) {
        for (var x = -ring; x <= ring; x++) {
          for (var z = -ring; z <= ring; z++) {
            if (FPMath.Abs(x) != ring && FPMath.Abs(z) != ring) {
              continue;
            }
            site = center + new FPVector3(ai.SiteGrid * x, FP._0, ai.SiteGrid * z);
            if (FactoryWarsUtil.IsReachable(f, player, site) && ProductionSystem.CanPlace(f, spec, site)) {
              return true;
            }
          }
        }
      }
      site = default;
      return false;
    }

    /// <summary>쉬는 공병을 먼저 쓰고, 없으면 현장에서 가장 가까운 공병을 데려온다.</summary>
    static bool FindBuilder(Frame f, PlayerRef player, FPVector3 site, out EntityRef engineer) {
      engineer = EntityRef.None;
      var best = FP.UseableMax;
      var workers = f.Filter<Transform3D, Unit, Worker>();
      while (workers.NextUnsafe(out var entity, out var transform, out var unit, out var worker)) {
        if (unit->Owner != player || worker->Task == WorkerTask.Build) {
          continue;
        }
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, site);
        if (worker->Task == WorkerTask.Idle) {
          distance /= 4;
        }
        if (distance < best) {
          best = distance;
          engineer = entity;
        }
      }
      return engineer != EntityRef.None;
    }

    /// <summary>이 유닛이 쓰는 자원만 따져 건물 비용으로 남겨 둘 몫을 건드리지 않는지 본다.</summary>
    static bool CanSpend(ResourceCost stock, ResourceCost cost, ResourceCost reserve) {
      return (cost.Iron <= FP._0 || stock.Iron >= cost.Iron + reserve.Iron)
        && (cost.Wood <= FP._0 || stock.Wood >= cost.Wood + reserve.Wood)
        && (cost.Steel <= FP._0 || stock.Steel >= cost.Steel + reserve.Steel)
        && (cost.Power <= FP._0 || stock.Power >= cost.Power + reserve.Power);
    }

    static void Produce(Frame f, PlayerRef player, PlayerState* state, AiSpec ai, FactionSpec faction, int engineers, ResourceCost reserve) {
      var room = state->PopulationCap - state->PopulationUsed - ProductionSystem.QueuedPopulation(f, player);
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out var entity, out var building)) {
        if (building->Owner != player || building->ConstructionRemaining > FP._0) {
          continue;
        }
        var spec = f.FindAsset(building->Spec);
        if (spec.Role == BuildingRole.Headquarters) {
          var engineer = f.FindAsset(faction.Engineer);
          if (engineers + building->QueueCount < ai.EngineerTarget && building->QueueCount == 0 && room >= engineer.Population && state->Stock.Covers(engineer.Cost)) {
            ProductionSystem.Enqueue(f, player, entity, faction.Engineer, 1);
            room -= engineer.Population;
          }
          continue;
        }
        if (spec.Producible == null || spec.Producible.Length == 0 || building->QueueCount >= ai.QueueTarget) {
          continue;
        }
        if (spec.Upkeep != UpkeepMode.None && state->Stock.Power < ai.PowerReserve) {
          continue;
        }
        var choices = System.Math.Min(ai.UnitVariety, spec.Producible.Length);
        var unitRef = spec.Producible[f.RNG->Next(0, choices)];
        var unit = f.FindAsset(unitRef);
        var population = unit.Population * unit.SpawnCount;
        if (room >= population && CanSpend(state->Stock, unit.Cost, reserve)) {
          ProductionSystem.Enqueue(f, player, entity, unitRef, 1);
          room -= population;
        }
      }
    }

    static bool FindEnemyTarget(Frame f, PlayerRef player, FPVector3 home, out FPVector3 target) {
      var best = FP.UseableMax;
      var found = false;
      target = default;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (DiplomacySystem.CanAttack(f, player, building->Owner) == false) {
          continue;
        }
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, home);
        if (f.FindAsset(building->Spec).IsCoreBuilding == false) {
          distance *= 4;
        }
        if (distance < best) {
          best = distance;
          target = transform->Position;
          found = true;
        }
      }
      return found;
    }

    static void Attack(Frame f, PlayerRef player, BotState* bot, AiSpec ai) {
      if (f.Number < bot->NextAttackFrame || FactoryWarsUtil.InTruce(f)) {
        return;
      }
      var army = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->Owner == player && f.FindAsset(unit->Spec).Attack != AttackType.None) {
          army++;
        }
      }
      if (army < bot->WaveSize || FindEnemyTarget(f, player, bot->Home, out var target) == false) {
        return;
      }

      var ordered = 0;
      units = f.Filter<Unit>();
      while (ordered < MaxOrder && units.NextUnsafe(out var entity, out var unit)) {
        if (unit->Owner == player && f.FindAsset(unit->Spec).Attack != AttackType.None) {
          MovementSystem.Order(f, entity, unit, UnitOrder.AttackMove, target);
          ordered++;
        }
      }
      bot->WaveSize = System.Math.Min(bot->WaveSize + ai.WaveGrowth, ai.MaxWaveSize);
      bot->NextAttackFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.AttackRepeatSeconds);
    }

    /// <summary>강철은 철 2 + 전기 1로 만들므로 3으로 친다.</summary>
    static FP Worth(ResourceCost cost) => cost.Iron + cost.Wood + cost.Power + cost.Steel * 3;

    /// <summary>유닛·건물 가치와 누적 수입으로 본 세력. 가중치는 AiSpec에 있다.</summary>
    public static FP Strength(Frame f, PlayerRef player, AiSpec ai) {
      var units = FP._0;
      var filter = f.Filter<Unit>();
      while (filter.NextUnsafe(out _, out var unit)) {
        if (unit->Owner == player) {
          units += Worth(f.FindAsset(unit->Spec).Cost);
        }
      }
      var buildings = FP._0;
      var owned = f.Filter<Building>();
      while (owned.NextUnsafe(out _, out var building)) {
        if (building->Owner == player) {
          buildings += Worth(f.FindAsset(building->Spec).Cost);
        }
      }
      var state = FactoryWarsUtil.Player(f, player);
      return units * ai.StrengthUnitWeight + buildings * ai.StrengthBuildingWeight + (state->Earned.Iron + state->Earned.Wood) * ai.StrengthIncomeWeight;
    }

    /// <summary>자신을 뺀 생존자 중 가장 강한 상대와 그다음 상대의 세력.</summary>
    static PlayerRef Strongest(Frame f, PlayerRef player, AiSpec ai, out FP best, out FP second) {
      PlayerRef strongest = default;
      best = -FP._1;
      second = -FP._1;
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        var state = f.Global->Players.GetPointer(i);
        if (other == player || state->Active == false || state->Defeated) {
          continue;
        }
        var strength = Strength(f, other, ai);
        if (strength > best) {
          second = best;
          best = strength;
          strongest = other;
        } else if (strength > second) {
          second = strength;
        }
      }
      return strongest;
    }

    static bool IsAlive(Frame f, PlayerRef player) {
      var state = FactoryWarsUtil.Player(f, player);
      return state->Active && state->Defeated == false;
    }

    public static void OnNoteReceived(Frame f, PlayerRef receiver, PlayerRef sender, int phrase) {
      var state = FactoryWarsUtil.Player(f, receiver);
      var friendly = FactoryWarsUtil.Rules(f).FriendlyPhrases;
      if (state->IsBot && friendly != null && System.Array.IndexOf(friendly, phrase) >= 0) {
        DiplomacySystem.AddOpinion(f, receiver, sender, Spec(f, state->Difficulty).FriendlyNoteOpinion);
      }
    }

    public static void OnAllianceBroken(Frame f, PlayerRef breaker, PlayerRef victim) {
      var state = FactoryWarsUtil.Player(f, victim);
      if (state->IsBot) {
        DiplomacySystem.AddOpinion(f, victim, breaker, Spec(f, state->Difficulty).BrokenOpinion);
      }
    }

    /// <summary>AI의 점수를 주기마다 갱신하고, 동맹의 공동의 적이 사라진 시각을 기록한다.</summary>
    static void UpdateOpinions(Frame f) {
      var d = f.Unsafe.GetPointerSingleton<Diplomacy>();
      if (f.Number < d->NextOpinionFrame) {
        return;
      }
      d->NextOpinionFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, FactoryWarsUtil.Rules(f).OpinionIntervalSeconds);
      for (var a = 0; a < f.PlayerCount; a++) {
        var state = f.Global->Players.GetPointer(a);
        if (state->IsBot == false || state->Defeated) {
          continue;
        }
        var ai = Spec(f, state->Difficulty);
        // 공격 기록이 없으면 프레임 0이다. 경기 초반에도 "최근"이 되지 않게 1 이상으로 둔다.
        var recent = System.Math.Max(1, f.Number - FactoryWarsUtil.SecondsToFrames(f, ai.RecentFightSeconds));
        var strongest = Strongest(f, a, ai, out _, out _);
        for (var b = 0; b < f.PlayerCount; b++) {
          if (a == b || IsAlive(f, b) == false) {
            continue;
          }
          var attacked = DiplomacySystem.LastAttackFrame(f, b, a) >= recent;
          var delta = attacked ? ai.AttackedOpinion : ai.PeaceOpinion;
          if (attacked == false && FightSameEnemy(f, a, b, recent)) {
            delta += ai.CommonEnemyOpinion;
          }
          if (b == (int)strongest) {
            delta += ai.StrongestOpinion;
          }
          DiplomacySystem.AddOpinion(f, a, b, delta);
        }
      }

      for (var a = 0; a < f.PlayerCount; a++) {
        for (var b = a + 1; b < f.PlayerCount; b++) {
          if (DiplomacySystem.GetRelation(f, a, b) != Relation.Allied || DiplomacySystem.HasCommonEnemy(f, a, b)) {
            DiplomacySystem.SetNoEnemyFrame(f, a, b, 0);
          } else if (DiplomacySystem.NoEnemyFrame(f, a, b) == 0) {
            DiplomacySystem.SetNoEnemyFrame(f, a, b, f.Number);
          }
        }
      }
    }

    static bool FightSameEnemy(Frame f, PlayerRef a, PlayerRef b, int recent) {
      for (var c = 0; c < f.PlayerCount; c++) {
        PlayerRef enemy = c;
        if (enemy != a && enemy != b && DiplomacySystem.LastAttackFrame(f, a, enemy) >= recent && DiplomacySystem.LastAttackFrame(f, b, enemy) >= recent) {
          return true;
        }
      }
      return false;
    }

    /// <summary>수락 조건. 점수가 기준 이상이고, 제안자가 최강이 아니고, 맺은 뒤에도 공동의 적이 남아야 한다.</summary>
    public static bool WouldAccept(Frame f, PlayerRef player, PlayerRef proposer, AiSpec ai) {
      if (DiplomacySystem.HasCommonEnemy(f, player, proposer) == false || proposer == Strongest(f, player, ai, out _, out _)) {
        return false;
      }
      return DiplomacySystem.GetOpinion(f, player, proposer) >= ai.AcceptOpinion || f.RNG->Next() < ai.AcceptChance;
    }

    /// <summary>동맹이 다른 누구보다 BreakStrengthRatio배 이상 강하면 true. 2위가 없으면 비교하지 않는다.</summary>
    public static bool IsOverpowered(FP allyStrength, FP others, AiSpec ai) {
      return others > FP._0 && allyStrength >= others * ai.BreakStrengthRatio;
    }

    static bool ShouldBreak(Frame f, PlayerRef player, PlayerRef ally, AiSpec ai) {
      var strongest = Strongest(f, player, ai, out var best, out var second);
      if (ally == strongest && IsOverpowered(best, second, ai)) {
        return true;
      }
      var since = DiplomacySystem.NoEnemyFrame(f, player, ally);
      return since > 0 && f.Number >= since + FactoryWarsUtil.SecondsToFrames(f, ai.BreakAfterEnemySeconds);
    }

    static void Negotiate(Frame f, PlayerRef player, PlayerState* state, BotState* bot, AiSpec ai) {
      if (DiplomacySystem.IsAllowed(f) == false) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);

      // 받은 제안에 답한다.
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        if (other == player || DiplomacySystem.HasProposal(f, other, player) == false) {
          continue;
        }
        var accept = WouldAccept(f, player, other, ai);
        if (accept && state->Stock.Iron < rules.AcceptCost) {
          continue;
        }
        DiplomacySystem.Handle(f, player, other, accept ? DiplomacyAction.Accept : DiplomacyAction.Reject);
      }

      // 동맹이 너무 강해졌거나 싸울 이유가 사라졌으면 파기한다.
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        if (other != player && DiplomacySystem.GetRelation(f, player, other) == Relation.Allied
            && state->Stock.Iron >= rules.BreakCost && ShouldBreak(f, player, other, ai)) {
          DiplomacySystem.Handle(f, player, other, DiplomacyAction.Break);
          return;
        }
      }

      if (ai.StrategicDiplomacy == false || state->Stock.Iron < ai.DiplomacyIronReserve) {
        return;
      }

      // 가장 강한 상대를 견제하려고 2·3위를 고른다. 호감이 모자라면 쪽지부터 보낸다.
      var strongest = Strongest(f, player, ai, out _, out _);
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        if (other == player || other == strongest || IsAlive(f, other) == false
            || DiplomacySystem.GetRelation(f, player, other) != Relation.Enemy
            || DiplomacySystem.CanPropose(f, player, other) == false
            || DiplomacySystem.HasCommonEnemy(f, player, other) == false) {
          continue;
        }
        if (DiplomacySystem.GetOpinion(f, other, player) < ai.AcceptOpinion) {
          if (f.Number >= bot->NextNoteFrame && rules.FriendlyPhrases != null && rules.FriendlyPhrases.Length > 0) {
            bot->NextNoteFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.NoteIntervalSeconds);
            new SendNoteCommand { PhraseId = rules.FriendlyPhrases[0], TargetMode = NoteTarget.Player, Receiver = other }.Execute(f, player);
          }
          return;
        }
        if (f.Number >= bot->NextProposalFrame) {
          bot->NextProposalFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.ProposalIntervalSeconds);
          DiplomacySystem.Handle(f, player, other, DiplomacyAction.Propose);
        }
        return;
      }
    }
  }
}
