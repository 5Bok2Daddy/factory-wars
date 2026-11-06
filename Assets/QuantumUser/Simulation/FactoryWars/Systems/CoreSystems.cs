namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  public static unsafe class FactoryWarsUtil {
    public static GameRulesSpec Rules(Frame f) => f.FindAsset(f.RuntimeConfig.Rules);

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
    }

    public void OnPlayerAdded(Frame f, PlayerRef player, bool firstTime) {
      if (firstTime == false) {
        return;
      }
      var data = f.GetPlayerData(player);
      var state = FactoryWarsUtil.Player(f, player);
      var faction = f.FindAsset(data.Faction);
      if (state == null || faction == null) {
        Log.Error($"FactoryWars: 플레이어 {player} 초기화 실패 (진영 없음)");
        return;
      }
      var rules = FactoryWarsUtil.Rules(f);
      state->Faction = data.Faction;
      state->Funds = rules.StartingFunds;
      state->Team = data.Team;
      state->Active = true;

      if (f.RuntimeConfig.TeamMode) {
        DiplomacySystem.ApplyTeam(f, player);
      }

      var start = rules.StartPositions[(int)player % rules.StartPositions.Length];
      var headquarters = ProductionSystem.CreateBuilding(f, player, faction.Headquarters, start);
      f.Unsafe.GetPointer<Building>(headquarters)->ConstructionRemaining = FP._0;
      for (var i = 0; i < rules.StartingEngineers; i++) {
        ProductionSystem.SpawnUnit(f, player, faction.Engineer, start, i, null);
      }
      if (faction.StartingUnits != null) {
        for (var i = 0; i < faction.StartingUnits.Length; i++) {
          ProductionSystem.SpawnUnit(f, player, faction.StartingUnits[i], start, rules.StartingEngineers + i, null);
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

    public static void Handle(Frame f, PlayerRef sender, PlayerRef target, DiplomacyAction action) {
      var d = f.Unsafe.GetPointerSingleton<Diplomacy>();
      var targetState = FactoryWarsUtil.Player(f, target);
      if (d->TeamMode || target == sender || targetState == null || targetState->Active == false || targetState->Defeated) {
        return;
      }
      var cell = Cell(sender, target);
      var relation = d->Relations[cell];
      var rules = FactoryWarsUtil.Rules(f);

      switch (action) {
        case DiplomacyAction.Propose:
          if (relation == Relation.Enemy && f.Number >= d->RealliableFrames[cell] && FindProposal(d, sender, target) < 0) {
            AddProposal(d, sender, target, f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.ProposalSeconds));
          }
          break;

        case DiplomacyAction.Accept: {
          var index = FindProposal(d, target, sender);
          if (index >= 0 && relation == Relation.Enemy) {
            ClearProposal(d, index);
            SetRelation(f, d, sender, target, Relation.Allied);
          }
          break;
        }

        case DiplomacyAction.Reject: {
          var index = FindProposal(d, target, sender);
          if (index >= 0) {
            ClearProposal(d, index);
          }
          break;
        }

        case DiplomacyAction.Break:
          if (relation == Relation.Allied) {
            var grace = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.BreakGraceSeconds);
            var ban = f.Number + FactoryWarsUtil.SecondsToFrames(f, rules.RealliableSeconds);
            d->BreakExpireFrames[cell] = grace;
            d->BreakExpireFrames[Cell(target, sender)] = grace;
            d->RealliableFrames[cell] = ban;
            d->RealliableFrames[Cell(target, sender)] = ban;
            SetRelation(f, d, sender, target, Relation.BreakPending);
          }
          break;
      }
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

      // 무응답 제안은 자동 거절.
      for (var i = 0; i < d->Proposals.Length; i++) {
        var expire = d->Proposals.GetPointer(i)->ExpireFrame;
        if (expire > 0 && f.Number >= expire) {
          ClearProposal(d, i);
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

  /// <summary>패배·승리 판정. 건물이 하나도 없으면 패배.</summary>
  public unsafe class VictorySystem : SystemMainThread {
    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }

      var buildingMask = 0;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        buildingMask |= 1 << (int)building->Owner;
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

      if (aliveMask == 0 || IsWinningGroup(f, aliveMask)) {
        f.Global->GameEnded = true;
        f.Events.GameOver(aliveMask);
      }
    }

    static bool IsWinningGroup(Frame f, int aliveMask) {
      var alive = 0;
      for (var i = 0; i < f.PlayerCount; i++) {
        if ((aliveMask & (1 << i)) != 0) {
          alive++;
        }
      }
      if (alive == 1) {
        return true;
      }
      if (f.Unsafe.GetPointerSingleton<Diplomacy>()->JointVictory == false) {
        return false;
      }
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
