namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  public static unsafe class FactoryWarsUtil {
    public static GameRulesSpec Rules(Frame f) => f.FindAsset(f.RuntimeConfig.Rules);

    public static MapSpec MapInfo(Frame f) => f.FindAsset<MapSpec>(f.Map.UserAsset);

    /// <summary>수평 거리의 제곱. 공중 유닛의 고도는 사거리·작업 범위에 넣지 않는다.</summary>
    public static FP FlatDistanceSqr(FPVector3 a, FPVector3 b) {
      var x = a.X - b.X;
      var z = a.Z - b.Z;
      return x * x + z * z;
    }

    public static int SecondsToFrames(Frame f, FP seconds) => FPMath.CeilToInt(seconds * f.SessionConfig.UpdateFPS);

    public static PlayerState* Player(Frame f, PlayerRef player) {
      if (player.IsValid == false || (int)player >= f.PlayerCount) {
        return null;
      }
      return f.Global->Players.GetPointer(player);
    }
  }

  /// <summary>플레이어 합류 시 진영·자금 초기화, 사령부와 시작 공병 배치.</summary>
  public unsafe class BootstrapSystem : SystemSignalsOnly, ISignalOnPlayerAdded {
    public override void OnInit(Frame f) {
      var entity = f.Create();
      f.Add<Diplomacy>(entity);
      var diplomacy = f.Unsafe.GetPointer<Diplomacy>(entity);
      diplomacy->JointVictory = f.RuntimeConfig.JointVictory;
      diplomacy->TeamMode = f.RuntimeConfig.TeamMode;

      var nodes = FactoryWarsUtil.MapInfo(f).ResourceNodes;
      for (var i = 0; nodes != null && i < nodes.Length; i++) {
        var node = f.Create();
        f.Add(node, Transform3D.Create(nodes[i].Position));
        f.Add(node, new ResourceNode { RemainingFunds = nodes[i].Funds });
      }

      var bots = f.RuntimeConfig.Bots;
      for (var i = 0; bots != null && i < bots.Length; i++) {
        PlayerRef player = bots[i].Player;
        SetUpPlayer(f, player, bots[i].Faction, bots[i].Team);
        BotSystem.Init(f, player, bots[i].Difficulty, StartPosition(f, player));
      }
    }

    static FPVector3 StartPosition(Frame f, PlayerRef player) {
      var starts = FactoryWarsUtil.MapInfo(f).StartPositions;
      return starts[(int)player % starts.Length];
    }

    public void OnPlayerAdded(Frame f, PlayerRef player, bool firstTime) {
      if (firstTime == false) {
        return;
      }
      var data = f.GetPlayerData(player);
      SetUpPlayer(f, player, data.Faction, data.Team);
    }

    static void SetUpPlayer(Frame f, PlayerRef player, AssetRef<FactionSpec> factionRef, int team) {
      var state = FactoryWarsUtil.Player(f, player);
      var faction = f.FindAsset(factionRef);
      if (state == null || faction == null) {
        Log.Error($"FactoryWars: 플레이어 {player} 초기화 실패 (진영 없음)");
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      state->Faction = factionRef;
      state->Funds = rules.StartingFunds;
      state->Team = team;
      state->Active = true;

      if (f.RuntimeConfig.TeamMode) {
        DiplomacySystem.ApplyTeam(f, player);
      }

      var start = StartPosition(f, player);
      ProductionSystem.CreateFinishedBuilding(f, player, faction.Headquarters, start);
      for (var i = 0; i < rules.StartingEngineers; i++) {
        ProductionSystem.SpawnUnit(f, player, faction.Engineer, start, i, null);
      }
      if (faction.StartingUnits != null) {
        for (var i = 0; i < faction.StartingUnits.Length; i++) {
          ProductionSystem.SpawnUnit(f, player, faction.StartingUnits[i], start, rules.StartingEngineers + i, null);
        }
      }

      var buildings = f.RuntimeConfig.ScenarioBuildings;
      for (var i = 0; buildings != null && i < buildings.Length; i++) {
        if (buildings[i].Player == (int)player) {
          ProductionSystem.CreateFinishedBuilding(f, player, buildings[i].Building, buildings[i].Position);
        }
      }

      var scenario = f.RuntimeConfig.ScenarioUnits;
      for (var i = 0; scenario != null && i < scenario.Length; i++) {
        if (scenario[i].Player == (int)player) {
          ProductionSystem.SpawnUnitAt(f, player, scenario[i].Unit, scenario[i].Position, null);
        }
      }
    }
  }

  /// <summary>플레이어 커맨드를 읽어 실행한다.</summary>
  public unsafe class CommandSystem : SystemMainThread {
    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      for (var i = 0; i < f.PlayerCount; i++) {
        if (f.GetPlayerCommand(i) is FactoryCommand command) {
          command.Execute(f, i);
        }
      }
    }
  }

  /// <summary>외교 규칙. 관계표는 대칭으로 유지한다.</summary>
  public unsafe class DiplomacySystem : SystemMainThread {
    const int Size = 4;

    static int Cell(PlayerRef a, PlayerRef b) => (int)a * Size + (int)b;

    public static Relation GetRelation(Frame f, PlayerRef a, PlayerRef b) {
      return f.Unsafe.GetPointerSingleton<Diplomacy>()->Relations[Cell(a, b)];
    }

    /// <summary>파기 유예 중(BreakPending)이나 동맹은 공격할 수 없다.</summary>
    public static bool CanAttack(Frame f, PlayerRef a, PlayerRef b) {
      return a != b && GetRelation(f, a, b) == Relation.Enemy;
    }

    static void SetRelation(Frame f, Diplomacy* d, PlayerRef a, PlayerRef b, Relation relation) {
      *d->Relations.GetPointer(Cell(a, b)) = relation;
      *d->Relations.GetPointer(Cell(b, a)) = relation;
      f.Events.AllianceChanged(a, b, relation);
    }

    public static void ApplyTeam(Frame f, PlayerRef player) {
      var d = f.Unsafe.GetPointerSingleton<Diplomacy>();
      var state = FactoryWarsUtil.Player(f, player);
      for (var i = 0; i < f.PlayerCount; i++) {
        PlayerRef other = i;
        var otherState = f.Global->Players.GetPointer(i);
        if (other != player && otherState->Active && otherState->Team == state->Team) {
          SetRelation(f, d, player, other, Relation.Allied);
        }
      }
    }

    /// <summary>자유 대전(3인 이상)에서만 외교가 가능하다. 패배한 플레이어도 인원에 포함한다.</summary>
    static bool DiplomacyAllowed(Frame f, Diplomacy* d, GameRulesSpec rules) {
      if (d->TeamMode) {
        return false;
      }
      var players = 0;
      for (var i = 0; i < f.PlayerCount; i++) {
        if (f.Global->Players.GetPointer(i)->Active) {
          players++;
        }
      }
      return players >= rules.MinDiplomacyPlayers;
    }

    public static bool IsAllowed(Frame f) {
      return DiplomacyAllowed(f, f.Unsafe.GetPointerSingleton<Diplomacy>(), FactoryWarsUtil.Rules(f));
    }

    public static bool HasProposal(Frame f, PlayerRef from, PlayerRef to) {
      return FindProposal(f.Unsafe.GetPointerSingleton<Diplomacy>(), from, to) >= 0;
    }

    /// <summary>지금 제안해도 거부되지 않으면 true. 자금은 따로 본다.</summary>
    public static bool CanPropose(Frame f, PlayerRef from, PlayerRef to) {
      var d = f.Unsafe.GetPointerSingleton<Diplomacy>();
      return FindProposal(d, from, to) < 0 && FindProposal(d, to, from) < 0 && f.Number >= d->RealliableFrames[Cell(from, to)];
    }

    static ActionKind Kind(DiplomacyAction action) {
      switch (action) {
        case DiplomacyAction.Propose: return ActionKind.Propose;
        case DiplomacyAction.Accept: return ActionKind.Accept;
        case DiplomacyAction.Reject: return ActionKind.Reject;
        default: return ActionKind.Break;
      }
    }

    public static void Handle(Frame f, PlayerRef sender, PlayerRef target, DiplomacyAction action) {
      var d = f.Unsafe.GetPointerSingleton<Diplomacy>();
      var rules = FactoryWarsUtil.Rules(f);
      var kind = Kind(action);
      if (DiplomacyAllowed(f, d, rules) == false) {
        f.Events.ActionRejected(sender, kind, RejectReason.ModeDisabled);
        return;
      }
      var state = FactoryWarsUtil.Player(f, sender);
      var targetState = FactoryWarsUtil.Player(f, target);
      if (target == sender || state == null || state->Defeated || targetState == null || targetState->Active == false || targetState->Defeated) {
        f.Events.ActionRejected(sender, kind, RejectReason.Invalid);
        return;
      }

      var reason = Apply(f, d, rules, state, sender, target, action);
      if (reason.HasValue) {
        f.Events.ActionRejected(sender, kind, reason.Value);
      }
    }

    /// <summary>외교 행동을 실행한다. 실행하지 못하면 사유를 돌려주고 상태를 바꾸지 않는다.</summary>
    static RejectReason? Apply(Frame f, Diplomacy* d, GameRulesSpec rules, PlayerState* state, PlayerRef sender, PlayerRef target, DiplomacyAction action) {
      var cell = Cell(sender, target);
      var relation = d->Relations[cell];

      switch (action) {
        case DiplomacyAction.Propose:
          // 서로에게 동시에 제안하지 못한다. 상대 제안이 있으면 수락하면 된다.
          if (relation != Relation.Enemy || FindProposal(d, sender, target) >= 0 || FindProposal(d, target, sender) >= 0) {
            return RejectReason.Invalid;
          }
          if (f.Number < d->RealliableFrames[cell]) {
            return RejectReason.Cooldown;
          }
          if (state->Funds < rules.ProposalCost) {
            return RejectReason.InsufficientFunds;
          }
          state->Funds -= rules.ProposalCost;
          AddProposal(d, sender, target, f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.ProposalSeconds));
          return null;

        case DiplomacyAction.Accept: {
          var index = FindProposal(d, target, sender);
          if (index < 0 || relation != Relation.Enemy) {
            return RejectReason.Invalid;
          }
          // 자금이 모자라면 제안은 그대로 대기한다.
          if (state->Funds < rules.AcceptCost) {
            return RejectReason.InsufficientFunds;
          }
          state->Funds -= rules.AcceptCost;
          ClearProposal(d, index);
          SetRelation(f, d, sender, target, Relation.Allied);
          return null;
        }

        case DiplomacyAction.Reject: {
          var index = FindProposal(d, target, sender);
          if (index < 0) {
            return RejectReason.Invalid;
          }
          RefundProposal(f, d, rules, index);
          return null;
        }

        case DiplomacyAction.Break: {
          if (relation != Relation.Allied) {
            return RejectReason.Invalid;
          }
          if (state->Funds < rules.BreakCost) {
            return RejectReason.InsufficientFunds;
          }
          state->Funds -= rules.BreakCost;
          var grace = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.BreakGraceSeconds);
          var ban = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.RealliableSeconds);
          d->BreakExpireFrames[cell] = grace;
          d->BreakExpireFrames[Cell(target, sender)] = grace;
          d->RealliableFrames[cell] = ban;
          d->RealliableFrames[Cell(target, sender)] = ban;
          SetRelation(f, d, sender, target, Relation.BreakPending);
          return null;
        }
      }
      return RejectReason.Invalid;
    }

    static void RefundProposal(Frame f, Diplomacy* d, GameRulesSpec rules, int index) {
      FactoryWarsUtil.Player(f, d->Proposals.GetPointer(index)->From)->Funds += rules.ProposalRefund;
      ClearProposal(d, index);
    }

    static int FindProposal(Diplomacy* d, PlayerRef from, PlayerRef to) {
      for (var i = 0; i < d->Proposals.Length; i++) {
        var proposal = d->Proposals.GetPointer(i);
        if (proposal->ExpireFrame > 0 && proposal->From == from && proposal->To == to) {
          return i;
        }
      }
      return -1;
    }

    static void AddProposal(Diplomacy* d, PlayerRef from, PlayerRef to, int expireFrame) {
      for (var i = 0; i < d->Proposals.Length; i++) {
        var proposal = d->Proposals.GetPointer(i);
        if (proposal->ExpireFrame == 0) {
          proposal->From = from;
          proposal->To = to;
          proposal->ExpireFrame = expireFrame;
          return;
        }
      }
    }

    static void ClearProposal(Diplomacy* d, int index) {
      *d->Proposals.GetPointer(index) = default;
    }

    public override void Update(Frame f) {
      var d = f.Unsafe.GetPointerSingleton<Diplomacy>();

      var rules = FactoryWarsUtil.Rules(f);

      // 무응답이거나 대상이 패배한 제안은 일부 환급하고, 제안자가 패배했으면 그냥 지운다.
      for (var i = 0; i < d->Proposals.Length; i++) {
        var proposal = d->Proposals.GetPointer(i);
        if (proposal->ExpireFrame == 0) {
          continue;
        }
        if (FactoryWarsUtil.Player(f, proposal->From)->Defeated) {
          ClearProposal(d, i);
        } else if (f.Number >= proposal->ExpireFrame || FactoryWarsUtil.Player(f, proposal->To)->Defeated) {
          RefundProposal(f, d, rules, i);
        }
      }

      // 파기 유예가 끝나면 적대로 전환.
      for (var a = 0; a < f.PlayerCount; a++) {
        for (var b = a + 1; b < f.PlayerCount; b++) {
          PlayerRef playerA = a;
          PlayerRef playerB = b;
          var cell = Cell(playerA, playerB);
          if (d->Relations[cell] == Relation.BreakPending && f.Number >= d->BreakExpireFrames[cell]) {
            SetRelation(f, d, playerA, playerB, Relation.Enemy);
          }
        }
      }
    }
  }

  /// <summary>패배·승리 판정. 사령부와 생산 건물을 모두 잃으면 패배.</summary>
  public unsafe class VictorySystem : SystemMainThread {
    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }

      var buildingMask = 0;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->ConstructionRemaining <= FP._0 && f.FindAsset(building->Spec).IsCoreBuilding) {
          buildingMask |= 1 << (int)building->Owner;
        }
      }

      var activeCount = 0;
      var aliveMask = 0;
      for (var i = 0; i < f.PlayerCount; i++) {
        var state = f.Global->Players.GetPointer(i);
        if (state->Active == false) {
          continue;
        }
        activeCount++;
        if (state->Defeated) {
          continue;
        }
        if ((buildingMask & (1 << i)) == 0) {
          state->Defeated = true;
          f.Events.PlayerDefeated(i);
          continue;
        }
        aliveMask |= 1 << i;
      }

      // 혼자 있는 세션(테스트)에서는 승패를 내지 않는다.
      if (activeCount < 2) {
        return;
      }

      var alive = 0;
      for (var i = 0; i < f.PlayerCount; i++) {
        if ((aliveMask & (1 << i)) != 0) {
          alive++;
        }
      }

      if (alive <= 1) {
        End(f, aliveMask);
        return;
      }

      var d = f.Unsafe.GetPointerSingleton<Diplomacy>();
      if (AllAllied(f, aliveMask) == false) {
        d->JointVictoryFrame = 0;
        return;
      }

      // 팀전은 상대 팀이 전멸하면 바로 끝난다. 시작 시 같은 팀만 합류한 상태는 승리가 아니다.
      if (d->TeamMode) {
        if (alive < activeCount) {
          End(f, aliveMask);
        }
        return;
      }

      if (d->JointVictory == false) {
        return;
      }
      if (d->JointVictoryFrame == 0) {
        d->JointVictoryFrame = f.Number + FactoryWarsUtil.SecondsToFrames(f, FactoryWarsUtil.Rules(f).JointVictorySeconds);
      }
      if (f.Number >= d->JointVictoryFrame) {
        End(f, aliveMask);
      }
    }

    static void End(Frame f, int aliveMask) {
      f.Global->GameEnded = true;
      f.Global->EndFrame = f.Number;
      f.Global->WinnerMask = aliveMask;
      f.Events.GameOver(aliveMask);
    }

    static bool AllAllied(Frame f, int aliveMask) {
      for (var a = 0; a < f.PlayerCount; a++) {
        for (var b = a + 1; b < f.PlayerCount; b++) {
          var bothAlive = (aliveMask & (1 << a)) != 0 && (aliveMask & (1 << b)) != 0;
          if (bothAlive && DiplomacySystem.GetRelation(f, a, b) != Relation.Allied) {
            return false;
          }
        }
      }
      return true;
    }
  }
}
