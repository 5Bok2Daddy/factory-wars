namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>규칙 기반 AI. 사람과 같은 함수로 행동해 비용과 제한을 그대로 받는다.</summary>
  public unsafe class BotSystem : SystemMainThread, ISignalOnPlayerDisconnected, ISignalOnPlayerConnected {
    const int RoleCount = 13;
    const int MaxOrder = MoveUnitsCommand.MaxUnits;

    /// <summary>
    /// 온라인에서 사람 자리의 연결이 끊기면(Quantum PlayerConnectedSystem, 확정 프레임) 봇이 대신 조종한다.
    /// 처음부터 접속하지 않은 자리(AI 자리)는 연결된 적이 없어 이 신호가 오지 않는다.
    /// </summary>
    public void OnPlayerDisconnected(Frame f, PlayerRef player) {
      var state = FactoryWarsUtil.Player(f, player);
      if (state == null || state->Active == false || state->Defeated || state->IsBot) {
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      state->DisconnectedFrame = f.Number;
      var bot = f.Global->Bots.GetPointer(player);
      var home = bot->Home == FPVector3.Zero ? FactoryWarsUtil.StartPosition(f, player) : bot->Home;
      var attackFrame = bot->NextAttackFrame;
      Init(f, player, rules.DisconnectBotDifficulty, home);
      bot->NextThinkFrame = f.Number + 1;
      // 끊기기 전 사람이 정한 공격 시점보다 앞당기지 않는다.
      bot->NextAttackFrame = System.Math.Max(attackFrame, f.Number + FactoryWarsUtil.SecondsToFrames(f, Spec(f, rules.DisconnectBotDifficulty).AttackRepeatSeconds));
      f.Events.PlayerConnection(player, false);
    }

    /// <summary>끊겼던 사람이 돌아오면 다시 사람이 조종한다. 봇으로 시작한 자리는 건드리지 않는다.</summary>
    public void OnPlayerConnected(Frame f, PlayerRef player) {
      var state = FactoryWarsUtil.Player(f, player);
      if (state == null || state->DisconnectedFrame == 0) {
        return;
      }
      state->IsBot = false;
      state->DisconnectedFrame = 0;
      f.Events.PlayerConnection(player, true);
    }

    /// <summary>끊긴 채 규칙 시간이 지나면 기권. 0초면 봇이 끝까지 조종한다.</summary>
    static void CheckForfeits(Frame f) {
      var limit = FactoryWarsUtil.Rules(f).DisconnectForfeitSeconds;
      if (limit <= 0) {
        return;
      }
      var frames = FactoryWarsUtil.SecondsToFrames(f, limit);
      for (var i = 0; i < f.PlayerCount; i++) {
        var state = f.Global->Players.GetPointer(i);
        if (state->DisconnectedFrame > 0 && state->Defeated == false && state->Forfeited == false && f.Number - state->DisconnectedFrame >= frames) {
          state->Forfeited = true;
        }
      }
    }

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
      CheckForfeits(f);
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
      StaffYards(f, player, ai);
      var breakCost = FactoryWarsUtil.Rules(f).BreakCost;
      var idleAlliance = HasIdleAlliance(f, player);
      ManageProcessors(f, player, ProcessorsOff(ai, state->Stock, breakCost, idleAlliance));
      // 싸울 상대가 없어진 동맹을 끊을 철을 먼저 모은다. 안 그러면 서로 공격하지 못한 채 경기가 끝나지 않는다.
      if (state->Stock.Iron >= breakCost || idleAlliance == false) {
        var reserve = building < ai.ConcurrentBuilds ? BuildNext(f, player, state, bot, ai, faction, counts) : default;
        // 항목 31: 단계 업그레이드(때가 되면 비용을 남겨 둔다)와 연구.
        reserve = reserve + ManageTech(f, player, state, ai);
        // 철이 바닥나 가면 나중에 동맹을 끊을 돈이 없어진다. 동맹이 있는 동안 파기 비용을 쓰지 않는다.
        if (HasAlly(f, player) && RemainingIron(f) < ai.BreakReserveIronLeft) {
          reserve.Iron += breakCost;
        }
        Produce(f, player, state, ai, faction, engineers, reserve);
      }
      Attack(f, player, bot, ai);
      CollectCrates(f, player, bot);
      PlayBattlefield(f, player, bot, ai);
      Negotiate(f, player, state, bot, ai);
    }

    /// <summary>
    /// 항목 31 테크. TierSeconds가 지나면 사령부를 올리고(자원이 모자라면 그 비용을 남겨 둘 몫으로 돌려준다),
    /// ResearchStartSeconds가 지나면 쉬는 연구소에서 ResearchOrder 순서로 가능한 첫 연구를 시작한다.
    /// </summary>
    static ResourceCost ManageTech(Frame f, PlayerRef player, PlayerState* state, AiSpec ai) {
      var reserve = default(ResourceCost);
      var tier = TechSystem.Tier(f, player);
      var rules = FactoryWarsUtil.Rules(f);
      if (ai.TierSeconds != null && tier < TechSystem.MaxTier && tier - 1 < ai.TierSeconds.Length
          && f.Number >= FactoryWarsUtil.SecondsToFrames(f, ai.TierSeconds[tier - 1])
          && TechSystem.FindTierSpec(rules, tier + 1, out var next) && TechSystem.MeetsTierRequirement(f, player, next, out _)) {
        var buildings = f.Filter<Building>();
        while (buildings.NextUnsafe(out var entity, out var building)) {
          if (building->Owner != player || f.FindAsset(building->Spec).Role != BuildingRole.Headquarters) {
            continue;
          }
          if (TechSystem.CanUpgrade(f, player, entity, out var reason, out _)) {
            TechSystem.StartUpgrade(f, player, entity);
            break;
          }
          if (reason == RejectReason.InsufficientResources) {
            reserve = next.Cost;
            break;
          }
          if (reason == RejectReason.Busy) {
            break;
          }
        }
      }
      if (ai.ResearchOrder == null || f.Number < FactoryWarsUtil.SecondsToFrames(f, ai.ResearchStartSeconds)) {
        return reserve;
      }
      // 연구가 다음 건물(예: 군수공장 전기 200)을 굶기지 않게, 건설 순서상 다음 건물 비용을 남겨 두고 연구한다.
      var researchReserve = reserve + NextOrderCost(f, player, state, ai);
      var labs = f.Filter<Building>();
      while (labs.NextUnsafe(out var entity, out var lab)) {
        if (lab->Owner != player || lab->Research > 0 || lab->ConstructionRemaining > FP._0 || f.FindAsset(lab->Spec).Role != BuildingRole.ResearchLab) {
          continue;
        }
        var list = f.FindAsset(state->Faction).Researches;
        for (var p = 0; list != null && p < ai.ResearchOrder.Length; p++) {
          var pick = ai.ResearchOrder[p];
          for (var i = 0; i < list.Length; i++) {
            var spec = f.FindAsset(list[i]);
            if (spec.Kind != pick.Kind || (pick.Kind != ResearchKind.Elite && (spec.Size != pick.Size || spec.Level != pick.Level))) {
              continue;
            }
            if (TechSystem.CanResearch(f, player, i, out _) && CanSpend(state->Stock, spec.Cost, researchReserve)) {
              TechSystem.StartResearch(f, player, entity, i);
              return reserve;
            }
          }
        }
        break;
      }
      return reserve;
    }

    /// <summary>건설 순서에서 아직 없는 첫 건물(단계·선행 조건을 채운 것)의 비용. 없으면 0.</summary>
    static ResourceCost NextOrderCost(Frame f, PlayerRef player, PlayerState* state, AiSpec ai) {
      var faction = f.FindAsset(state->Faction);
      var seen = stackalloc int[RoleCount];
      var counts = stackalloc int[RoleCount];
      for (var i = 0; i < RoleCount; i++) {
        seen[i] = 0;
        counts[i] = 0;
      }
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player) {
          counts[(int)f.FindAsset(building->Spec).Role]++;
        }
      }
      for (var i = 0; ai.BuildOrder != null && i < ai.BuildOrder.Length; i++) {
        var role = ai.BuildOrder[i];
        if (faction.AiSkipRoles != null && System.Array.IndexOf(faction.AiSkipRoles, role) >= 0) {
          continue;
        }
        seen[(int)role]++;
        if (counts[(int)role] >= seen[(int)role]) {
          continue;
        }
        var spec = f.FindAsset(FindRole(f, faction, role));
        if (spec != null && ProductionSystem.IsUnlocked(f, player, spec)) {
          return spec.Cost;
        }
      }
      return default;
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

    static bool HasAlly(Frame f, PlayerRef player) {
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        if (other != player && DiplomacySystem.GetRelation(f, player, other) == Relation.Allied && IsAlive(f, other)) {
          return true;
        }
      }
      return false;
    }

    static FP RemainingIron(Frame f) {
      var total = FP._0;
      var nodes = f.Filter<ResourceNode>();
      while (nodes.NextUnsafe(out _, out var node)) {
        if (node->Kind == ResourceKind.Iron) {
          total += node->Remaining;
        }
      }
      return total;
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
      var runner = f.Global->Bots.GetPointer(player)->CrateRunner;
      while (workers.NextUnsafe(out var entity, out var unit, out var worker)) {
        if (unit->Owner != player || worker->Task != WorkerTask.Idle || entity == runner) {
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

      // 항목 31: 짓다 만 건물에 일꾼이 없고 노는 공병도 없으면 채집 중인 공병 하나를 데려온다(군수공장 기초가 멈춰 2단계 생산이 막혔다).
      if (needsBuilder) {
        workers = f.Filter<Unit, Worker>();
        while (workers.NextUnsafe(out var entity, out var unit, out var worker)) {
          if (unit->Owner == player && entity != runner && (worker->Task == WorkerTask.Gather || worker->Task == WorkerTask.Deliver)) {
            WorkerSystem.AssignToBuilding(f, entity, foundation);
            break;
          }
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

    /// <summary>
    /// 철이 모자라거나 강철이 충분하면 가공소를 끈다. 동맹 파기 비용을 모으는 동안과,
    /// 강철을 이미 넉넉히 쌓았는데 철이 적을 때도 끈다. 안 그러면 가공소가 철을 계속 태워 파기 비용이 모이지 않는다.
    /// </summary>
    public static bool ProcessorsOff(AiSpec ai, ResourceCost stock, FP breakCost, bool savingForBreak) {
      return stock.Iron < ai.ProcessorIronFloor || stock.Steel >= ai.SteelCap
        || (savingForBreak && stock.Iron < breakCost)
        || (stock.Steel >= ai.SteelTarget && stock.Iron < ai.IronSurplus);
    }

    static void ManageProcessors(Frame f, PlayerRef player, bool off) {
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
      // 진영별 조정(항목 22): 당길 역할은 차량 공장이 생기면 바로, 뺄 역할은 순서에서 건너뛴다.
      var faction = f.FindAsset(state->Faction);
      if (faction.AiEarlyRoles != null && counts[(int)BuildingRole.VehicleFactory] > 0) {
        foreach (var early in faction.AiEarlyRoles) {
          var earlySpec = f.FindAsset(FindRole(f, faction, early));
          if (counts[(int)early] == 0 && (earlySpec == null || earlySpec.RequiredTier <= TechSystem.Tier(f, player))) {
            role = early;
            return true;
          }
        }
      }
      var tier = TechSystem.Tier(f, player);
      for (var i = 0; i < ai.BuildOrder.Length; i++) {
        if (faction.AiSkipRoles != null && System.Array.IndexOf(faction.AiSkipRoles, ai.BuildOrder[i]) >= 0) {
          continue;
        }
        // 항목 31: 아직 단계가 안 된 건물은 건너뛰고 다음 것을 짓는다(단계가 오르면 다시 차례가 온다).
        var roleSpec = f.FindAsset(FindRole(f, faction, ai.BuildOrder[i]));
        if (roleSpec != null && roleSpec.RequiredTier > tier) {
          continue;
        }
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
      var orderRole = role;
      var hasOrder = hasRole;
      // 항목 31: 다음 단계 시각이 TierLeadSeconds 안으로 다가오면 그 단계 조건 건물(연구소·중공업 기지)을 확장보다 먼저 짓는다.
      var techFirst = TierPrerequisite(f, player, ai, out var techRole);
      if (techFirst) {
        role = techRole;
        hasRole = true;
      }

      // 계획된 확장: 앞마당은 모자란 자원 쪽부터, 공유 확장과 중앙은 중반 이후에 잡는다.
      if (techFirst == false && FactoryWarsUtil.InTruce(f) == false && counts[(int)BuildingRole.PowerPlant] > 0 && counts[(int)BuildingRole.CollectionYard] < ai.MaxYards
          && FindExpansion(f, player, state, bot, ai, out var planned)) {
        role = BuildingRole.CollectionYard;
        hasRole = true;
        center = planned;
      }
      // 확장: 멀리 일하러 간 공병 옆에 사령부나 집하장을 둔다.
      else if (techFirst == false && FindDistantNode(f, player, ai, out var distant)) {
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

      var result = TryBuildRole(f, player, state, ai, faction, role, center, out var built);
      // 항목 31: 확장(집하장·사령부) 자리를 못 찾거나 짓지 못하면 그 시도만 반복하느라 건설 순서가 막혔다(휴전 없는 경기에서 군수공장 0).
      // 그때는 건설 순서의 건물을 짓는다.
      if (built == false && hasOrder && (role != orderRole || center != bot->Home)) {
        return TryBuildRole(f, player, state, ai, faction, orderRole, bot->Home, out _);
      }
      return result;
    }

    static ResourceCost TryBuildRole(Frame f, PlayerRef player, PlayerState* state, AiSpec ai, FactionSpec faction, BuildingRole role, FPVector3 center, out bool built) {
      built = false;
      var specRef = FindRole(f, faction, role);
      var spec = f.FindAsset(specRef);
      if (spec == null || ProductionSystem.IsUnlocked(f, player, spec) == false) {
        return default;
      }
      if (state->Stock.Covers(spec.Cost) == false) {
        return spec.Cost;
      }
      if (FindSite(f, player, ai, spec, center, out var site) && FindBuilder(f, player, site, out var engineer)) {
        var before = f.ComponentCount<Building>();
        ProductionSystem.TryBuild(f, player, engineer, specRef, site);
        built = f.ComponentCount<Building>() > before;
      }
      return default;
    }

    /// <summary>다음 단계 조건 중 아직 없는(짓는 중도 아닌) 역할. 그 단계 시각 - TierLeadSeconds부터 true.</summary>
    static bool TierPrerequisite(Frame f, PlayerRef player, AiSpec ai, out BuildingRole role) {
      role = default;
      var tier = TechSystem.Tier(f, player);
      if (ai.TierSeconds == null || tier >= TechSystem.MaxTier || tier - 1 >= ai.TierSeconds.Length
          || f.Number < FactoryWarsUtil.SecondsToFrames(f, FPMath.Max(FP._0, ai.TierSeconds[tier - 1] - ai.TierLeadSeconds))
          || TechSystem.FindTierSpec(FactoryWarsUtil.Rules(f), tier + 1, out var next) == false
          || TechSystem.MeetsTierRequirement(f, player, next, out role)) {
        return false;
      }
      // 선행 건물이 없어 아직 못 짓는 것이면 평소 순서대로 짓는다(그 선행 건물이 순서에 있다).
      var spec = f.FindAsset(FindRole(f, f.FindAsset(FactoryWarsUtil.Player(f, player)->Faction), role));
      if (spec == null || ProductionSystem.IsUnlocked(f, player, spec) == false) {
        return false;
      }
      // 이미 짓는 중이면 기다린다.
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->Owner == player && f.FindAsset(building->Spec).Role == role) {
          return false;
        }
      }
      return true;
    }

    /// <summary>철이 목재보다 적으면 철 앞마당, 아니면 숲 앞마당이 먼저다.</summary>
    public static ExpansionKind PreferredForecourt(ResourceCost stock) {
      return stock.Iron <= stock.Wood ? ExpansionKind.IronForecourt : ExpansionKind.WoodForecourt;
    }

    /// <summary>자기 집하장이나 사령부가 이미 있거나, 다른 플레이어의 건물이 있으면 빈자리가 아니다.</summary>
    static bool IsFreeSite(Frame f, FPVector3 position, FP radius) {
      var limit = radius * radius;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out _)) {
        if (FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= limit) {
          return false;
        }
      }
      return true;
    }

    static bool FindExpansion(Frame f, PlayerRef player, PlayerState* state, BotState* bot, AiSpec ai, out FPVector3 site) {
      site = default;
      var map = FactoryWarsUtil.MapInfo(f);
      var sites = map.Expansions;
      if (sites == null) {
        return false;
      }
      var seat = 1 << FactoryWarsUtil.SeatOf(f, player);
      var wanted = PreferredForecourt(state->Stock);
      var late = ai.LateExpansionSeconds > FP._0 && f.Number >= FactoryWarsUtil.SecondsToFrames(f, ai.LateExpansionSeconds);
      var best = FP.MaxValue;
      for (var i = 0; i < sites.Length; i++) {
        var kind = sites[i].Kind;
        var forecourt = kind == ExpansionKind.IronForecourt || kind == ExpansionKind.WoodForecourt;
        if ((sites[i].SeatMask & seat) == 0 || (forecourt == false && late == false) || IsFreeSite(f, sites[i].Position, ai.ExpandReserveRadius) == false) {
          continue;
        }
        // 앞마당을 먼저, 그중 원하는 쪽을 먼저, 나머지는 가까운 순서로 고른다.
        var distance = FactoryWarsUtil.FlatDistanceSqr(sites[i].Position, bot->Home);
        var score = kind == wanted ? distance / 16 : forecourt ? distance / 4 : distance;
        if (score < best) {
          best = score;
          site = sites[i].Position;
        }
      }
      // 빈 본진 자리(중립 대형 확장지): 중반 이후에 높은 가치(거리/2)로 후보에 넣는다.
      if (late && map.StartPositions != null) {
        for (var s = 0; s < map.StartPositions.Length; s++) {
          var pos = map.StartPositions[s];
          if (FactoryWarsUtil.PlayerAtSeat(f, s) < 0 && IsFreeSite(f, pos, ai.ExpandReserveRadius)) {
            var score = FactoryWarsUtil.FlatDistanceSqr(pos, bot->Home) / 2;
            if (score < best) {
              best = score;
              site = pos;
            }
          }
        }
      }
      return best < FP.MaxValue;
    }

    /// <summary>집하장 옆 자원에 일꾼이 모자라면 본진에서 한 명씩 옮긴다.</summary>
    static void StaffYards(Frame f, PlayerRef player, AiSpec ai) {
      var limit = ai.ExpandReserveRadius * ai.ExpandReserveRadius;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var yard, out var building)) {
        if (building->Owner != player || building->ConstructionRemaining > FP._0 || f.FindAsset(building->Spec).Role != BuildingRole.CollectionYard) {
          continue;
        }
        var staffed = 0;
        var from = EntityRef.None;
        var workers = f.Filter<Unit, Worker>();
        while (workers.NextUnsafe(out var entity, out var unit, out var worker)) {
          if (unit->Owner != player || worker->Task != WorkerTask.Gather && worker->Task != WorkerTask.Deliver || f.Has<ResourceNode>(worker->Target) == false) {
            continue;
          }
          if (FactoryWarsUtil.FlatDistanceSqr(f.Unsafe.GetPointer<Transform3D>(worker->Target)->Position, yard->Position) <= limit) {
            staffed++;
          } else if (DistanceToYard(f, player, f.Unsafe.GetPointer<Transform3D>(worker->Target)->Position) > limit) {
            from = entity;
          }
        }
        if (staffed >= ai.YardWorkers || from == EntityRef.None) {
          continue;
        }
        var node = NearestNodeAround(f, yard->Position, limit);
        if (node != EntityRef.None) {
          WorkerSystem.Assign(f, from, WorkerTask.Gather, node);
          return;
        }
      }
    }

    static FP DistanceToYard(Frame f, PlayerRef player, FPVector3 position) {
      var best = FP.MaxValue;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (building->Owner == player && f.FindAsset(building->Spec).Role == BuildingRole.CollectionYard) {
          best = FPMath.Min(best, FactoryWarsUtil.FlatDistanceSqr(transform->Position, position));
        }
      }
      return best;
    }

    static EntityRef NearestNodeAround(Frame f, FPVector3 position, FP limit) {
      var best = limit;
      var found = EntityRef.None;
      var nodes = f.Filter<Transform3D, ResourceNode>();
      while (nodes.NextUnsafe(out var entity, out var transform, out var node)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, position);
        if (node->Remaining > FP._0 && distance <= best) {
          best = distance;
          found = entity;
        }
      }
      return found;
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
      var best = FP.MaxValue;
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
      var isShipyard = spec.Role == BuildingRole.Shipyard;
      var step = isShipyard ? ai.SiteGrid / System.Math.Max(1, ai.ShipyardGridDivisor) : ai.SiteGrid;
      var rings = isShipyard ? ai.SiteRings * System.Math.Max(1, ai.ShipyardRingMultiplier) : ai.SiteRings;
      for (var ring = 1; ring <= rings; ring++) {
        for (var x = -ring; x <= ring; x++) {
          for (var z = -ring; z <= ring; z++) {
            if (FPMath.Abs(x) != ring && FPMath.Abs(z) != ring) {
              continue;
            }
            site = center + new FPVector3(step * x, FP._0, step * z);
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
      var best = FP.MaxValue;
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
        // 배는 MaxShips척까지만(대기열 포함). 인구를 배가 다 차지하면 지상군이 못 큰다.
        if (spec.Role == BuildingRole.Shipyard && Ships(f, player) + building->QueueCount >= ai.MaxShips) {
          continue;
        }
        // 항목 31: 단계·크기 상한으로 막힌 유닛과 목표 비율을 넘은 크기는 뺀다(다른 후보가 없으면 비율은 무시).
        var allowed = AllowedMask(f, player, ai, spec.Producible, out var overShare);
        if (allowed == 0) {
          continue;
        }
        var unitRef = ai.CounterPicks ? CounterPick(f, player, ai, spec.Producible, allowed) : RandomPick(f, ai, spec.Producible, allowed);
        var unit = f.FindAsset(unitRef);
        var population = unit.Population * unit.SpawnCount;
        if (room >= population && CanSpend(state->Stock, unit.Cost, reserve)) {
          ProductionSystem.Enqueue(f, player, entity, unitRef, 1);
          room -= population;
        }
      }
    }

    static AssetRef<UnitSpec> RandomPick(Frame f, AiSpec ai, AssetRef<UnitSpec>[] producible, int allowed) {
      var limit = System.Math.Min(ai.UnitVariety, producible.Length);
      var choices = 0;
      for (var i = 0; i < limit; i++) {
        choices += (allowed & (1 << i)) != 0 ? 1 : 0;
      }
      if (choices == 0) {
        // 앞쪽 UnitVariety개가 모두 막혔으면 뒤쪽 후보에서 고른다.
        limit = producible.Length;
        for (var i = 0; i < limit; i++) {
          choices += (allowed & (1 << i)) != 0 ? 1 : 0;
        }
      }
      var pick = f.RNG->Next(0, System.Math.Max(1, choices));
      for (var i = 0; i < limit; i++) {
        if ((allowed & (1 << i)) != 0 && pick-- == 0) {
          return producible[i];
        }
      }
      return producible[0];
    }

    /// <summary>뽑을 수 있는 후보 비트. 단계·크기 상한은 반드시 지키고, 목표 비율 초과 크기는 다른 후보가 남을 때만 뺀다.</summary>
    static int AllowedMask(Frame f, PlayerRef player, AiSpec ai, AssetRef<UnitSpec>[] producible, out bool overShare) {
      overShare = false;
      var allowed = 0;
      for (var i = 0; i < producible.Length && i < 30; i++) {
        if (TechSystem.CanProduce(f, player, f.FindAsset(producible[i]), out _)) {
          allowed |= 1 << i;
        }
      }
      if (allowed == 0 || ai.SizeShares == null || ai.SizeShares.Length < 3) {
        return allowed;
      }
      var bySize = stackalloc long[4];
      for (var i = 0; i < 4; i++) {
        bySize[i] = 0;
      }
      FP total = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->Owner != player) {
          continue;
        }
        var spec = f.FindAsset(unit->Spec);
        if (spec.Size == UnitSize.Engineer || spec.IsNaval) {
          continue;
        }
        bySize[(int)spec.Size] += spec.Population.RawValue;
        total += spec.Population;
      }
      if (total <= FP._0) {
        return allowed;
      }
      var trimmed = allowed;
      for (var i = 0; i < producible.Length && i < 30; i++) {
        var spec = f.FindAsset(producible[i]);
        if ((allowed & (1 << i)) == 0 || spec.Size == UnitSize.Engineer || spec.IsNaval) {
          continue;
        }
        var share = FP.FromRaw(bySize[(int)spec.Size]) / total;
        if (share > ai.SizeShares[(int)spec.Size - 1] + ai.SizeShareMargin) {
          trimmed &= ~(1 << i);
        }
      }
      overShare = trimmed == 0;
      return trimmed != 0 ? trimmed : allowed;
    }

    static int Ships(Frame f, PlayerRef player) {
      var count = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        if (unit->Owner == player && f.FindAsset(unit->Spec).IsNaval) {
          count++;
        }
      }
      return count;
    }

    /// <summary>
    /// 상대 구성 대응 생산(항목 22). 보이는 적 전투 유닛을 크기(소·중·대)·공중으로 나눠 비율을 내고,
    /// 후보마다 (내 공격이 적 구성에 주는 상성 배율 평균) ÷ (적 공격이 나에게 주는 상성 배율 평균)으로 점수를 매긴다.
    /// 상대 공중이 적으면 대공 유닛은 몇 기까지만 둔다. 상위 CounterTop개 중 RNG로 고른다. 수치는 규칙의 상성표와 AiSpec에만 있다.
    /// </summary>
    static AssetRef<UnitSpec> CounterPick(Frame f, PlayerRef player, AiSpec ai, AssetRef<UnitSpec>[] producible, int allowed) {
      var rules = FactoryWarsUtil.Rules(f);
      FP small = 0, medium = 0, large = 0, air = 0, total = 0;
      FP antiInfantry = 0, antiArmor = 0, antiAir = 0, area = 0;
      var ownAntiAir = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out var entity, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        if (spec.Attack == AttackType.None) {
          continue;
        }
        if (unit->Owner == player) {
          ownAntiAir += spec.Attack == AttackType.AntiAir ? 1 : 0;
          continue;
        }
        if (DiplomacySystem.CanAttack(f, player, unit->Owner) == false || FactoryWarsUtil.IsVisibleTo(f, player, entity) == false) {
          continue;
        }
        var weight = spec.Population;
        total += weight;
        if (spec.IsAir) {
          air += weight;
        } else if (spec.Size == UnitSize.Large) {
          large += weight;
        } else if (spec.Size == UnitSize.Medium) {
          medium += weight;
        } else {
          small += weight;
        }
        switch (spec.Attack) {
          case AttackType.AntiInfantry: antiInfantry += weight; break;
          case AttackType.AntiArmor: antiArmor += weight; break;
          case AttackType.AntiAir: antiAir += weight; break;
          default: area += weight; break;
        }
      }
      var count = producible.Length;
      var scores = stackalloc long[count];
      for (var i = 0; i < count; i++) {
        var candidate = f.FindAsset(producible[i]);
        FP score;
        if (total <= FP._0) {
          // 보이는 적이 없으면 모두 같은 점수(무작위와 같다). 대공만 아래 규칙으로 줄인다.
          score = FP._1;
        } else {
          var offense = (small * Matchup(rules, candidate.Attack, UnitSize.Small, false) + medium * Matchup(rules, candidate.Attack, UnitSize.Medium, false)
            + large * Matchup(rules, candidate.Attack, UnitSize.Large, false) + air * Matchup(rules, candidate.Attack, UnitSize.Small, true)) / total;
          var taken = (antiInfantry * CombatSystem.UnitMultiplier(rules, AttackType.AntiInfantry, candidate) + antiArmor * CombatSystem.UnitMultiplier(rules, AttackType.AntiArmor, candidate)
            + antiAir * CombatSystem.UnitMultiplier(rules, AttackType.AntiAir, candidate) + area * CombatSystem.UnitMultiplier(rules, AttackType.Area, candidate)) / total;
          score = offense / FPMath.Max(FP._0_25, taken);
        }
        var airShare = total > FP._0 ? air / total : FP._0;
        if (candidate.Attack == AttackType.AntiAir && airShare < ai.AirShareForAntiAir && ownAntiAir >= ai.IdleAntiAir) {
          score = FP._0;
        }
        // 정렬용 정수(동점은 앞 순서). 결정론: FP 원값만 쓴다.
        scores[i] = (allowed & (1 << i)) != 0 ? score.RawValue : -1;
      }
      var top = System.Math.Max(1, System.Math.Min(ai.CounterTop, count));
      var picked = stackalloc int[top];
      for (var k = 0; k < top; k++) {
        var best = -1;
        for (var i = 0; i < count; i++) {
          var used = false;
          for (var j = 0; j < k; j++) {
            used |= picked[j] == i;
          }
          if (used == false && (best < 0 || scores[i] > scores[best])) {
            best = i;
          }
        }
        picked[k] = best;
      }
      // 0점(막힌 대공)은 다른 후보가 있으면 고르지 않는다.
      var usable = 0;
      for (var k = 0; k < top; k++) {
        usable += scores[picked[k]] > 0 ? 1 : 0;
      }
      return producible[picked[usable > 0 ? f.RNG->Next(0, usable) : 0]];
    }

    static FP Matchup(GameRulesSpec rules, AttackType attack, UnitSize size, bool isAir) {
      for (var i = 0; rules.Matchups != null && i < rules.Matchups.Length; i++) {
        if (rules.Matchups[i].Attack == attack) {
          var m = rules.Matchups[i];
          return isAir ? m.VsAir : size == UnitSize.Large ? m.VsLarge : size == UnitSize.Medium ? m.VsMedium : m.VsSmall;
        }
      }
      return FP._1;
    }

    /// <summary>적 군대가 사실상 없으면 true. 남은 건물을 치우는 데 웨이브를 기다릴 이유가 없다.</summary>
    public static bool IsSweeping(int army, int enemyArmy, AiSpec ai) {
      return army > 0 && enemyArmy * ai.SweepArmyRatio <= army;
    }

    static FPVector3 ArmyCenter(Frame f, PlayerRef player, FPVector3 fallback) {
      var sum = FPVector3.Zero;
      var count = 0;
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out _, out var transform, out var unit)) {
        if (unit->Owner == player && f.FindAsset(unit->Spec).Attack != AttackType.None && f.Number >= unit->ReservedUntilFrame && unit->Garrison == EntityRef.None) {
          sum += transform->Position;
          count++;
        }
      }
      return count > 0 ? sum / count : fallback;
    }

    /// <summary>패배 조건은 핵심 건물이다. 소탕할 때는 군대에서 가장 가까운 핵심 건물만 본다.</summary>
    static bool FindCoreTarget(Frame f, PlayerRef player, FPVector3 from, out FPVector3 target) {
      var best = FP.MaxValue;
      var found = false;
      target = default;
      var buildings = f.Filter<Transform3D, Building>();
      while (buildings.NextUnsafe(out _, out var transform, out var building)) {
        if (DiplomacySystem.CanAttack(f, player, building->Owner) == false || f.FindAsset(building->Spec).IsCoreBuilding == false) {
          continue;
        }
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, from);
        if (distance < best) {
          best = distance;
          target = transform->Position;
          found = true;
        }
      }
      return found;
    }

    public static bool FindEnemyTarget(Frame f, PlayerRef player, FPVector3 home, out FPVector3 target) => FindEnemyTarget(f, player, home, out target, out _);

    static bool FindEnemyTarget(Frame f, PlayerRef player, FPVector3 home, out FPVector3 target, out PlayerRef owner) {
      var best = FP.MaxValue;
      var found = false;
      target = default;
      owner = default;
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
          owner = building->Owner;
          found = true;
        }
      }
      return found;
    }

    /// <summary>
    /// 이번 웨이브의 경유점. 측면은 두 자리에 모두 닿는 공유 확장이고, 대각선 상대(2인 대전 등)일 때는 빈 본진 자리를 측면 경유점으로 쓴다.
    /// </summary>
    static bool PickSideLane(Frame f, PlayerRef player, PlayerRef enemy, BotState* bot, AiSpec ai, out FPVector3 point) {
      point = default;
      var map = FactoryWarsUtil.MapInfo(f);
      var sites = map.Expansions;
      if (sites == null || f.RNG->Next() >= ai.SideLaneChance) {
        return false;
      }
      var seats = 1 << FactoryWarsUtil.SeatOf(f, player) | 1 << FactoryWarsUtil.SeatOf(f, enemy);
      var count = 0;
      for (var i = 0; i < sites.Length; i++) {
        count += sites[i].Kind == ExpansionKind.Shared && (sites[i].SeatMask & seats) == seats ? 1 : 0;
      }
      if (count > 0) {
        var pick = f.RNG->Next(0, count);
        for (var i = 0; i < sites.Length; i++) {
          if (sites[i].Kind == ExpansionKind.Shared && (sites[i].SeatMask & seats) == seats && pick-- == 0) {
            point = sites[i].Position;
            return true;
          }
        }
      }
      var emptyCount = 0;
      for (var s = 0; map.StartPositions != null && s < map.StartPositions.Length; s++) {
        emptyCount += FactoryWarsUtil.PlayerAtSeat(f, s) < 0 ? 1 : 0;
      }
      if (emptyCount > 0) {
        var pick = f.RNG->Next(0, emptyCount);
        for (var s = 0; s < map.StartPositions.Length; s++) {
          if (FactoryWarsUtil.PlayerAtSeat(f, s) < 0 && pick-- == 0) {
            point = map.StartPositions[s];
            return true;
          }
        }
      }
      return false;
    }

    /// <summary>웨이브 크기를 채웠거나, 공격 시각이 오래 지났고 병력이 남아 있으면 나간다.</summary>
    public static bool ShouldLaunch(int army, int waveSize, int overdueFrames, int staleFrames) {
      return army >= waveSize || (army > 0 && overdueFrames >= staleFrames);
    }

    /// <summary>군대가 경유점보다 목표에 가까우면 그 길은 이미 지난 것이다.</summary>
    public static bool IsPastLane(FPVector3 army, FPVector3 lane, FPVector3 target) {
      return FactoryWarsUtil.FlatDistanceSqr(army, target) <= FactoryWarsUtil.FlatDistanceSqr(lane, target);
    }

    static void OrderArmy(Frame f, PlayerRef player, FPVector3 target) {
      var ordered = 0;
      var units = f.Filter<Unit>();
      while (ordered < MaxOrder && units.NextUnsafe(out var entity, out var unit)) {
        if (unit->Owner == player && f.FindAsset(unit->Spec).Attack != AttackType.None && f.Number >= unit->ReservedUntilFrame && unit->Garrison == EntityRef.None) {
          MovementSystem.Order(f, entity, unit, UnitOrder.AttackMove, target);
          ordered++;
        }
      }
    }

    /// <summary>군대가 바리케이드를 지정 공격한다.</summary>
    static void OrderBreach(Frame f, PlayerRef player, EntityRef barricade) {
      var ordered = 0;
      var units = f.Filter<Unit>();
      while (ordered < MaxOrder && units.NextUnsafe(out _, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        if (unit->Owner == player && spec.Attack != AttackType.None && spec.Attack != AttackType.AntiAir && f.Number >= unit->ReservedUntilFrame && unit->Garrison == EntityRef.None) {
          unit->TargetEntity = barricade;
          unit->Order = UnitOrder.AttackTarget;
          ordered++;
        }
      }
    }

    static EntityRef NearestShortcut(Frame f, FPVector3 from) {
      var map = FactoryWarsUtil.MapInfo(f);
      var best = FP.MaxValue;
      var found = EntityRef.None;
      var buildings = f.Filter<Transform3D, CityBuilding>();
      while (buildings.NextUnsafe(out var entity, out var transform, out var city)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, from);
        if (map.CityBuildings[city->Index].Shortcut && distance < best) {
          best = distance;
          found = entity;
        }
      }
      return found;
    }

    static EntityRef NearestBarricade(Frame f, FPVector3 from) {
      var best = FP.MaxValue;
      var found = EntityRef.None;
      var barricades = f.Filter<Transform3D, Barricade>();
      while (barricades.NextUnsafe(out var entity, out var transform, out _)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, from);
        if (distance < best) {
          best = distance;
          found = entity;
        }
      }
      return found;
    }

    /// <summary>경유점에 닿았거나 시간이 지났으면 목표로 보낸다.</summary>
    static void FollowLane(Frame f, PlayerRef player, BotState* bot, AiSpec ai) {
      if (bot->LaneUntilFrame == 0) {
        return;
      }
      if (bot->LaneBarricade != EntityRef.None) {
        if (f.Exists(bot->LaneBarricade) && f.Number < bot->LaneUntilFrame) {
          return;
        }
        bot->LaneBarricade = EntityRef.None;
        OrderArmy(f, player, bot->LanePoint);
        bot->LaneUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.LaneTimeoutSeconds);
        return;
      }
      var center = ArmyCenter(f, player, bot->Home);
      if (f.Number >= bot->LaneUntilFrame || FactoryWarsUtil.FlatDistanceSqr(center, bot->LanePoint) <= ai.LaneArrivalDistance * ai.LaneArrivalDistance) {
        bot->LaneUntilFrame = 0;
        OrderArmy(f, player, bot->LaneTarget);
      }
    }

    /// <summary>은폐된 적은 AI도 모른다.</summary>
    static void CountArmies(Frame f, PlayerRef player, out int army, out int enemyArmy) {
      army = 0;
      enemyArmy = 0;
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out var entity, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        // 배는 지상 웨이브에 끼지 못하므로 웨이브·소탕 판단에서 뺀다(항목 30: 배만 쌓여 웨이브가 안 나가던 교착).
        if (spec.Attack == AttackType.None || spec.IsNaval) {
          continue;
        }
        if (unit->Owner == player) {
          army++;
        } else if (DiplomacySystem.CanAttack(f, player, unit->Owner) && FactoryWarsUtil.IsVisibleTo(f, player, entity)) {
          enemyArmy++;
        }
      }
    }

    /// <summary>본진 상자를 공병 하나가 돌며 줍는다.</summary>
    static void CollectCrates(Frame f, PlayerRef player, BotState* bot) {
      var best = FP.MaxValue;
      var target = default(FPVector3);
      var crates = f.Filter<Transform3D, SupplyCrate>();
      while (crates.NextUnsafe(out _, out var transform, out var crate)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, bot->Home);
        if (crate->Seat == (int)player && distance < best) {
          best = distance;
          target = transform->Position;
        }
      }
      if (best == FP.MaxValue) {
        bot->CrateRunner = EntityRef.None;
        return;
      }
      if (bot->CrateRunner == EntityRef.None || f.Exists(bot->CrateRunner) == false) {
        if (FindBuilder(f, player, target, out var engineer) == false) {
          return;
        }
        bot->CrateRunner = engineer;
      }
      var runner = f.Unsafe.GetPointer<Unit>(bot->CrateRunner);
      if (runner->Order != UnitOrder.Move || runner->TargetPosition != target) {
        MovementSystem.Order(f, bot->CrateRunner, runner, UnitOrder.Move, target);
      }
    }

    /// <summary>목표에서 가까운 지상 전투 유닛을 골라 분대로 묶는다. 묶인 동안 본대 명령을 받지 않는다.</summary>
    static void SendSquad(Frame f, PlayerRef player, FPVector3 target, int count, AiSpec ai, FP minRange) {
      var until = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.SquadSeconds);
      for (var i = 0; i < count; i++) {
        var best = FP.MaxValue;
        var pick = EntityRef.None;
        var units = f.Filter<Transform3D, Unit>();
        while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
          var spec = f.FindAsset(unit->Spec);
          if (unit->Owner != player || spec.Attack == AttackType.None || spec.IsAir || spec.IsNaval || spec.Range < minRange || f.Number < unit->ReservedUntilFrame || unit->Garrison != EntityRef.None) {
            continue;
          }
          var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, target);
          if (distance < best) {
            best = distance;
            pick = entity;
          }
        }
        if (pick == EntityRef.None) {
          return;
        }
        var chosen = f.Unsafe.GetPointer<Unit>(pick);
        chosen->ReservedUntilFrame = until;
        MovementSystem.Order(f, pick, chosen, UnitOrder.AttackMove, target);
      }
    }

    static bool IsFriendlySite(Frame f, PlayerRef player, CaptureSite* site) {
      return site->Owner.IsValid && (site->Owner == player || DiplomacySystem.GetRelation(f, player, site->Owner) == Relation.Allied);
    }

    static bool FindSite(Frame f, PlayerRef player, FPVector3 home, CaptureKind kind, out FPVector3 position) {
      var best = FP.MaxValue;
      position = default;
      var sites = f.Filter<Transform3D, CaptureSite>();
      while (sites.NextUnsafe(out _, out var transform, out var site)) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, home);
        if (site->Kind == kind && IsFriendlySite(f, player, site) == false && distance < best) {
          best = distance;
          position = transform->Position;
        }
      }
      return best < FP.MaxValue;
    }

    /// <summary>보통 AI의 전장 판단: 중계소, 통신소, 보급 투하, 고지대.</summary>
    static void PlayBattlefield(Frame f, PlayerRef player, BotState* bot, AiSpec ai) {
      // 확장 후보가 없는 맵(규칙 시험장)에서는 분대를 빼지 않는다.
      var sites = FactoryWarsUtil.MapInfo(f).Expansions;
      if (FactoryWarsUtil.InTruce(f) || sites == null || sites.Length == 0) {
        return;
      }
      if (ai.DropSquad > 0 && f.Global->DropAnnounced && bot->RespondedDropFrame != f.Global->NextDropFrame) {
        bot->RespondedDropFrame = f.Global->NextDropFrame;
        if (NearestDrop(f, bot->Home, out var drop)) {
          SendSquad(f, player, drop, ai.DropSquad, ai, FP._0);
        }
      }
      if (ai.CaptureSquad > 0 && f.Number >= bot->NextSquadFrame) {
        CountArmies(f, player, out var army, out var enemyArmy);
        var strong = army >= enemyArmy * ai.CenterArmyRatio;
        if ((strong && FindSite(f, player, bot->Home, CaptureKind.Comms, out var site)) || FindSite(f, player, bot->Home, CaptureKind.Relay, out site)) {
          SendSquad(f, player, site, ai.CaptureSquad, ai, FP._0);
          bot->NextSquadFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.SquadSeconds);
        }
      }
      PlayCity(f, player, bot, ai);
      if (ai.HighGroundUnits > 0 && f.Number >= bot->NextHighGroundFrame) {
        bot->NextHighGroundFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.SquadSeconds);
        if (NearestHighGround(f, bot->Home, out var ground)) {
          SendSquad(f, player, ground, ai.HighGroundUnits, ai, ai.HighGroundMinRange);
        }
      }
    }

    /// <summary>진지에 보병을 넣고, 적 진지는 화염·범위 유닛으로 깨고, 앞마당 근처 쓰러지는 건물로 길을 끊는다.</summary>
    static void PlayCity(Frame f, PlayerRef player, BotState* bot, AiSpec ai) {
      var map = FactoryWarsUtil.MapInfo(f);
      if (map.CityBuildings == null || map.CityBuildings.Length == 0) {
        return;
      }
      if (ai.GarrisonBuildings > 0 && f.Number >= bot->NextGarrisonFrame) {
        bot->NextGarrisonFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.SquadSeconds);
        FillGarrisons(f, player, bot, ai, map);
      }
      var center = ArmyCenter(f, player, bot->Home);
      if (ai.BreakGarrisons) {
        var limit = ai.LaneArrivalDistance * ai.LaneArrivalDistance * 4;
        var buildings = f.Filter<Transform3D, CityBuilding>();
        while (buildings.NextUnsafe(out var entity, out var transform, out var city)) {
          if (city->Occupant.IsValid && DiplomacySystem.CanAttack(f, player, city->Occupant) && FactoryWarsUtil.FlatDistanceSqr(transform->Position, center) <= limit) {
            OrderSiege(f, player, entity);
            break;
          }
        }
      }
      if (ai.ToppleDefense && f.Number >= bot->NextToppleFrame) {
        bot->NextToppleFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.SquadSeconds);
        DefendWithTopple(f, player, ai, map);
      }
    }

    static void FillGarrisons(Frame f, PlayerRef player, BotState* bot, AiSpec ai, MapSpec map) {
      for (var n = 0; n < ai.GarrisonBuildings; n++) {
        var best = FP.MaxValue;
        var target = EntityRef.None;
        var buildings = f.Filter<Transform3D, CityBuilding>();
        while (buildings.NextUnsafe(out var entity, out var transform, out var city)) {
          var distance = FactoryWarsUtil.FlatDistanceSqr(transform->Position, bot->Home);
          if (map.CityBuildings[city->Index].Garrison && city->Occupant.IsValid == false && CitySystem.CanEnter(f, player, entity) && distance < best) {
            best = distance;
            target = entity;
          }
        }
        if (target == EntityRef.None) {
          return;
        }
        var capacity = FactoryWarsUtil.Rules(f).GarrisonCapacity;
        var position = f.Unsafe.GetPointer<Transform3D>(target)->Position;
        var shape = map.CityBuildings[f.Unsafe.GetPointer<CityBuilding>(target)->Index].Shape;
        var sent = 0;
        var units = f.Filter<Transform3D, Unit>();
        while (sent < capacity && units.NextUnsafe(out var entity, out var transform, out var unit)) {
          if (unit->Owner != player || unit->Garrison != EntityRef.None || unit->GarrisonTarget != EntityRef.None || f.Number < unit->ReservedUntilFrame
              || CitySystem.IsInfantry(f.FindAsset(unit->Spec)) == false) {
            continue;
          }
          MovementSystem.Order(f, entity, unit, UnitOrder.Move, shape.Approach(transform->Position, FP._1));
          unit->GarrisonTarget = target;
          unit->ReservedUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.SquadSeconds);
          sent++;
        }
        if (sent == 0) {
          return;
        }
      }
    }

    /// <summary>화염방사와 범위 유닛이 진지 건물을 지정 공격한다.</summary>
    static void OrderSiege(Frame f, PlayerRef player, EntityRef building) {
      var units = f.Filter<Unit>();
      while (units.NextUnsafe(out _, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        if (unit->Owner == player && unit->Garrison == EntityRef.None && unit->TargetEntity != building
            && (spec.Attack == AttackType.Area || spec.Ability == SpecialAbility.FlameBurst)) {
          unit->TargetEntity = building;
          unit->Order = UnitOrder.AttackTarget;
        }
      }
    }

    /// <summary>앞마당 근처에 적 지상군이 있으면, 그 앞마당 가까이 낙하 구역이 있는 쓰러지는 건물을 쓰러뜨린다.</summary>
    static void DefendWithTopple(Frame f, PlayerRef player, AiSpec ai, MapSpec map) {
      var sites = map.Expansions;
      // 확장지 SeatMask는 본진 자리 기준이다(항목 25 점검에서 고침).
      var seat = 1 << FactoryWarsUtil.SeatOf(f, player);
      var watch = ai.LaneArrivalDistance * ai.LaneArrivalDistance * 2;
      for (var i = 0; sites != null && i < sites.Length; i++) {
        var kind = sites[i].Kind;
        if ((sites[i].SeatMask & seat) == 0 || (kind != ExpansionKind.IronForecourt && kind != ExpansionKind.WoodForecourt) || EnemyNear(f, player, sites[i].Position, watch) == false) {
          continue;
        }
        var buildings = f.Filter<Transform3D, CityBuilding>();
        while (buildings.NextUnsafe(out var entity, out _, out var city)) {
          var placement = map.CityBuildings[city->Index];
          if (placement.Topples && placement.FallZone.Distance(sites[i].Position) <= ai.LaneArrivalDistance * 2) {
            SendSiege(f, player, entity, ai);
            return;
          }
        }
      }
    }

    static bool EnemyNear(Frame f, PlayerRef player, FPVector3 position, FP limit) {
      var units = f.Filter<Transform3D, Unit>();
      while (units.NextUnsafe(out var entity, out var transform, out var unit)) {
        if (DiplomacySystem.CanAttack(f, player, unit->Owner) && f.FindAsset(unit->Spec).IsAir == false && FactoryWarsUtil.FlatDistanceSqr(transform->Position, position) <= limit
            && FactoryWarsUtil.IsVisibleTo(f, player, entity)) {
          return true;
        }
      }
      return false;
    }

    static void SendSiege(Frame f, PlayerRef player, EntityRef building, AiSpec ai) {
      var until = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.SquadSeconds);
      var sent = 0;
      var units = f.Filter<Unit>();
      while (sent < ai.CaptureSquad && units.NextUnsafe(out _, out var unit)) {
        var spec = f.FindAsset(unit->Spec);
        if (unit->Owner == player && unit->Garrison == EntityRef.None && spec.Attack != AttackType.None && spec.Attack != AttackType.AntiAir && f.Number >= unit->ReservedUntilFrame) {
          unit->TargetEntity = building;
          unit->Order = UnitOrder.AttackTarget;
          unit->ReservedUntilFrame = until;
          sent++;
        }
      }
    }

    static bool NearestDrop(Frame f, FPVector3 home, out FPVector3 position) {
      var best = FP.MaxValue;
      position = default;
      var spots = FactoryWarsUtil.MapInfo(f).CrateSpots;
      for (var i = 0; spots != null && i < spots.Length; i++) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(spots[i].Position, home);
        if (spots[i].Seat < 0 && spots[i].Group == f.Global->DropGroup && distance < best) {
          best = distance;
          position = spots[i].Position;
        }
      }
      return best < FP.MaxValue;
    }

    static bool NearestHighGround(Frame f, FPVector3 home, out FPVector3 position) {
      var best = FP.MaxValue;
      position = default;
      var grounds = FactoryWarsUtil.MapInfo(f).HighGrounds;
      for (var i = 0; grounds != null && i < grounds.Length; i++) {
        var distance = FactoryWarsUtil.FlatDistanceSqr(grounds[i].Center, home);
        if (distance < best) {
          best = distance;
          position = grounds[i].Center;
        }
      }
      return best < FP.MaxValue;
    }

    static void Attack(Frame f, PlayerRef player, BotState* bot, AiSpec ai) {
      FollowLane(f, player, bot, ai);
      if (f.Number < bot->NextAttackFrame || FactoryWarsUtil.InTruce(f)) {
        return;
      }
      CountArmies(f, player, out var army, out var enemyArmy);
      var sweeping = IsSweeping(army, enemyArmy, ai);
      FPVector3 target;
      PlayerRef enemy = default;
      if (sweeping) {
        if (FindCoreTarget(f, player, ArmyCenter(f, player, bot->Home), out target) == false) {
          return;
        }
      } else if (ShouldLaunch(army, bot->WaveSize, f.Number - bot->NextAttackFrame, FactoryWarsUtil.SecondsToFrames(f, ai.StaleWaveSeconds)) == false
          || FindEnemyTarget(f, player, bot->Home, out target, out enemy) == false) {
        return;
      }

      if (sweeping) {
        bot->LaneUntilFrame = 0;
        OrderArmy(f, player, target);
        bot->NextAttackFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.SweepSeconds);
        return;
      }
      // 이미 나가 있는 군대를 경유점으로 되돌리지 않는다. 공격 주기보다 이동이 긴 맵에서 왕복하게 된다.
      var center = ArmyCenter(f, player, bot->Home);
      var hasSide = PickSideLane(f, player, enemy, bot, ai, out var lane);
      if (IsPastLane(center, lane, target)) {
        OrderArmy(f, player, target);
      } else {
        if (hasSide) {
          bot->SideWaves++;
        } else {
          bot->CenterWaves++;
        }
        bot->LanePoint = lane;
        bot->LaneTarget = target;
        bot->LaneUntilFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, ai.LaneTimeoutSeconds);
        var breach = EntityRef.None;
        if (hasSide && f.RNG->Next() < ai.ShortcutChance) {
          breach = NearestShortcut(f, center);
        }
        if (breach == EntityRef.None && hasSide && f.RNG->Next() < ai.BarricadeChance) {
          breach = NearestBarricade(f, center);
        }
        if (breach != EntityRef.None) {
          bot->LaneBarricade = breach;
          OrderBreach(f, player, breach);
        } else {
          OrderArmy(f, player, lane);
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
