namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>규칙 기반 AI. 사람과 같은 함수로 행동해 비용과 제한을 그대로 받는다.</summary>
  public unsafe class BotSystem : SystemMainThread {
    const int RoleCount = 10;
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

      var engineers = ManageWorkers(f, player, bot, foundation);
      var reserve = building < ai.ConcurrentBuilds ? BuildNext(f, player, state, bot, ai, faction, counts) : FP._0;
      Produce(f, player, state, ai, faction, engineers, reserve);
      Attack(f, player, bot, ai);
      Negotiate(f, player, state, bot, ai);
    }

    /// <summary>쉬는 공병에게 건설이나 채집을 맡기고 공병 수를 돌려준다.</summary>
    static int ManageWorkers(Frame f, PlayerRef player, BotState* bot, EntityRef foundation) {
      var count = 0;
      var needsBuilder = foundation != EntityRef.None;
      var workers = f.Filter<Unit, Worker>();
      while (workers.NextUnsafe(out _, out var unit, out var worker)) {
        if (unit->Owner == player) {
          count++;
          if (worker->Task == WorkerTask.Build && worker->Target == foundation) {
            needsBuilder = false;
          }
        }
      }

      var rules = FactoryWarsUtil.Rules(f);
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
        var position = f.Unsafe.GetPointer<Transform3D>(entity)->Position;
        var node = WorkerSystem.NearestNode(f, position, FP.UseableMax);
        if (node != EntityRef.None) {
          WorkerSystem.Assign(f, entity, WorkerTask.Gather, node);
        }
      }
      return count;
    }

    static AssetRef<BuildingSpec> FindRole(Frame f, FactionSpec faction, BuildingRole role) {
      for (var i = 0; i < faction.Buildings.Length; i++) {
        if (f.FindAsset(faction.Buildings[i]).Role == role) {
          return faction.Buildings[i];
        }
      }
      return default;
    }

    static bool NextRole(PlayerState* state, BotState* bot, AiSpec ai, int* counts, Frame f, FactionSpec faction, out BuildingRole role) {
      // 전력·인구가 모자라면 순서보다 먼저 채운다.
      if (state->PowerProduced - state->PowerConsumed < ai.PowerMargin) {
        role = BuildingRole.PowerPlant;
        return true;
      }
      if (state->PopulationCap - state->PopulationUsed < ai.PopulationMargin) {
        role = BuildingRole.SupplyDepot;
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

    /// <summary>다음 건물을 짓는다. 자금을 모으는 중이면 그 비용을 돌려준다.</summary>
    static FP BuildNext(Frame f, PlayerRef player, PlayerState* state, BotState* bot, AiSpec ai, FactionSpec faction, int* counts) {
      var center = bot->Home;
      var hasRole = NextRole(state, bot, ai, counts, f, faction, out var role);

      if (hasRole == false || role == BuildingRole.Refinery) {
        // 확장: 멀리 일하러 간 공병 옆에 정제소를 둔다.
        if (counts[(int)BuildingRole.Refinery] > 0 && counts[(int)BuildingRole.Refinery] < ai.MaxRefineries
            && FindDistantNode(f, player, ai, out var distant)) {
          role = BuildingRole.Refinery;
          hasRole = true;
          center = distant;
        } else if (hasRole && NearestNodePosition(f, bot->Home, out var node)) {
          center = node;
        }
      }
      if (hasRole == false) {
        return FP._0;
      }

      var specRef = FindRole(f, faction, role);
      var spec = f.FindAsset(specRef);
      if (spec == null || ProductionSystem.IsUnlocked(f, player, spec) == false) {
        return FP._0;
      }
      if (state->Stock.Iron < spec.Cost.Iron) {
        return spec.Cost.Iron;
      }
      if (FindSite(f, ai, spec, center, out var site) && FindBuilder(f, player, site, out var engineer)) {
        ProductionSystem.TryBuild(f, player, engineer, specRef, site);
      }
      return FP._0;
    }

    static bool NearestNodePosition(Frame f, FPVector3 position, out FPVector3 result) {
      var node = WorkerSystem.NearestNode(f, position, FP.UseableMax);
      result = default;
      if (node == EntityRef.None) {
        return false;
      }
      result = f.Unsafe.GetPointer<Transform3D>(node)->Position;
      return true;
    }

    static bool FindDistantNode(Frame f, PlayerRef player, AiSpec ai, out FPVector3 position) {
      var limit = ai.ExpandDistance * ai.ExpandDistance;
      var workers = f.Filter<Unit, Worker>();
      while (workers.NextUnsafe(out _, out var unit, out var worker)) {
        if (unit->Owner == player && worker->Task == WorkerTask.Gather && f.Has<ResourceNode>(worker->Target)) {
          position = f.Unsafe.GetPointer<Transform3D>(worker->Target)->Position;
          if (DistanceToRefinery(f, player, position) > limit) {
            return true;
          }
        }
      }
      position = default;
      return false;
    }

    static FP DistanceToRefinery(Frame f, PlayerRef player, FPVector3 position) {
      var best = FP.UseableMax;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->Owner == player && f.FindAsset(building->Spec).IsRefinery) {
          best = FPMath.Min(best, FactoryWarsUtil.FlatDistanceSqr(transform->Position, position));
        }
      }
      return best;
    }

    /// <summary>중심에서 바깥으로 격자를 돌며 지을 수 있는 첫 칸을 찾는다.</summary>
    static bool FindSite(Frame f, AiSpec ai, BuildingSpec spec, FPVector3 center, out FPVector3 site) {
      for (var ring = 1; ring <= ai.SiteRings; ring++) {
        for (var x = -ring; x <= ring; x++) {
          for (var z = -ring; z <= ring; z++) {
            if (FPMath.Abs(x) != ring && FPMath.Abs(z) != ring) {
              continue;
            }
            site = center + new FPVector3(ai.SiteGrid * x, FP._0, ai.SiteGrid * z);
            if (ProductionSystem.CanPlace(f, spec, site)) {
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

    static void Produce(Frame f, PlayerRef player, PlayerState* state, AiSpec ai, FactionSpec faction, int engineers, FP reserve) {
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out var entity, out var building)) {
        if (building->Owner != player || building->ConstructionRemaining > FP._0) {
          continue;
        }
        var spec = f.FindAsset(building->Spec);
        if (spec.Role == BuildingRole.Headquarters) {
          if (engineers + building->QueueCount < ai.EngineerTarget) {
            ProductionSystem.Enqueue(f, player, entity, faction.Engineer, 1);
          }
          continue;
        }
        if (spec.Producible == null || spec.Producible.Length == 0 || building->QueueCount >= ai.QueueTarget) {
          continue;
        }
        var choices = System.Math.Min(ai.UnitVariety, spec.Producible.Length);
        var unitRef = spec.Producible[f.RNG->Next(0, choices)];
        if (state->Stock.Iron >= f.FindAsset(unitRef).Cost.Iron + reserve) {
          ProductionSystem.Enqueue(f, player, entity, unitRef, 1);
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
      if (f.Number < bot->NextAttackFrame) {
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

    /// <summary>유닛과 건물의 비용 합으로 본 세력.</summary>
    static FP Strength(Frame f, PlayerRef player) {
      var total = FP._0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->Owner == player) {
          total += f.FindAsset(unit->Spec).Cost.Iron;
        }
      }
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player) {
          total += f.FindAsset(building->Spec).Cost.Iron;
        }
      }
      return total;
    }

    /// <summary>자신을 뺀 생존자 중 가장 강한 상대.</summary>
    static PlayerRef Strongest(Frame f, PlayerRef player) {
      PlayerRef best = default;
      var bestStrength = -FP._1;
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        var state = f.Global->Players.GetPointer(i);
        if (other == player || state->Active == false || state->Defeated) {
          continue;
        }
        var strength = Strength(f, other);
        if (strength > bestStrength) {
          bestStrength = strength;
          best = other;
        }
      }
      return best;
    }

    static void Negotiate(Frame f, PlayerRef player, PlayerState* state, BotState* bot, AiSpec ai) {
      if (DiplomacySystem.IsAllowed(f) == false) {
        return;
      }
      var strongest = Strongest(f, player);

      // 받은 제안에 답한다.
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        if (other == player || DiplomacySystem.HasProposal(f, other, player) == false) {
          continue;
        }
        var accept = ai.StrategicDiplomacy ? other != strongest : f.RNG->Next() < ai.AcceptChance;
        if (accept && state->Stock.Iron < FactoryWarsUtil.Rules(f).AcceptCost) {
          continue;
        }
        DiplomacySystem.Handle(f, player, other, accept ? DiplomacyAction.Accept : DiplomacyAction.Reject);
      }

      // 보통은 가장 강한 상대가 아닌 쪽에 먼저 손을 내민다.
      if (ai.StrategicDiplomacy == false || f.Number < bot->NextProposalFrame || state->Stock.Iron < ai.DiplomacyFundsReserve) {
        return;
      }
      bot->NextProposalFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.ProposalIntervalSeconds);
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        var otherState = f.Global->Players.GetPointer(i);
        if (other != player && other != strongest && otherState->Active && otherState->Defeated == false
            && DiplomacySystem.GetRelation(f, player, other) == Relation.Enemy && DiplomacySystem.CanPropose(f, player, other)) {
          DiplomacySystem.Handle(f, player, other, DiplomacyAction.Propose);
          return;
        }
      }
    }
  }
}
