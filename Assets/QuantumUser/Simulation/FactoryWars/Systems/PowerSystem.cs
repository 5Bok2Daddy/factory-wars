namespace Quantum.FactoryWars {
  using Photon.Deterministic;

  /// <summary>전기 축적, 저장 한도, 가동 유지비, 가공소, 정전. 건물은 엔티티 순서대로 처리한다.</summary>
  public unsafe class PowerSystem : SystemMainThread {
    public override void Update(Frame f) {
      if (f.Global->GameEnded) {
        return;
      }
      Accumulate(f);
      var shortage = PayUpkeep(f);
      UpdateOutages(f, shortage);
    }

    static void Accumulate(Frame f) {
      for (var i = 0; i < f.PlayerCount; i++) {
        var state = f.Global->Players.GetPointer(i);
        state->PowerCap = FP._0;
        state->PowerRate = FP._0;
      }
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out _, out var building)) {
        if (building->ConstructionRemaining <= FP._0) {
          var spec = f.FindAsset(building->Spec);
          var state = FactoryWarsUtil.Player(f, building->Owner);
          state->PowerCap += spec.PowerStorage;
          state->PowerRate += spec.PowerOutput;
        }
      }
      for (var i = 0; i < f.PlayerCount; i++) {
        var state = f.Global->Players.GetPointer(i);
        var before = state->Stock.Power;
        // 한도가 줄면 넘치는 전기는 바로 사라진다.
        state->Stock.Power = FPMath.Min(before + state->PowerRate * f.DeltaTime, state->PowerCap);
        state->Earned.Power += FPMath.Max(FP._0, state->Stock.Power - before);
      }
    }

    static bool WantsPower(Frame f, Building* building, BuildingSpec spec, PlayerState* state) {
      switch (spec.Upkeep) {
        case UpkeepMode.Always: return true;
        case UpkeepMode.WhileProducing: return ProductionSystem.IsProducing(f, building, state);
        // 항목 31: 연구소는 연구 중에만 전기를 쓴다.
        case UpkeepMode.WhileResearching: return building->Research > 0;
        default: return false;
      }
    }

    static void SetPaused(Frame f, EntityRef entity, Building* building, PauseReason reason) {
      if (building->Paused != reason) {
        building->Paused = reason;
        f.Events.BuildingPaused(entity, reason);
      }
    }

    /// <summary>재료가 다 있을 때만 한 번에 변환한다. 전기가 모자라 못 바꿨으면 true.</summary>
    static bool Process(Frame f, EntityRef entity, Building* building, BuildingSpec spec, PlayerState* state) {
      if (building->ProcessorOff) {
        SetPaused(f, entity, building, PauseReason.None);
        return false;
      }
      if (state->Outage) {
        SetPaused(f, entity, building, PauseReason.Outage);
        return false;
      }
      SetPaused(f, entity, building, PauseReason.None);

      if (state->Stock.Iron >= spec.ProcessInput.Iron) {
        state->PowerRate -= spec.ProcessInput.Power / spec.ProcessSeconds;
      }
      building->ProcessTimer += f.DeltaTime;
      if (building->ProcessTimer < spec.ProcessSeconds) {
        return false;
      }
      if (state->Stock.Covers(spec.ProcessInput) == false) {
        // 재료가 생기면 바로 변환하도록 다 찬 채로 기다린다.
        building->ProcessTimer = spec.ProcessSeconds;
        if (state->Stock.Iron < spec.ProcessInput.Iron) {
          return false;
        }
        SetPaused(f, entity, building, PauseReason.Outage);
        return true;
      }
      building->ProcessTimer -= spec.ProcessSeconds;
      state->Stock -= spec.ProcessInput;
      state->Stock.Steel += spec.ProcessSteel;
      state->Earned.Steel += spec.ProcessSteel;
      return false;
    }

    /// <summary>유지비를 걷는다. 전기를 못 낸 건물이 있는 플레이어를 비트로 돌려준다.</summary>
    static int PayUpkeep(Frame f) {
      var shortage = 0;
      var buildings = f.Filter<Building>();
      while (buildings.NextUnsafe(out var entity, out var building)) {
        var spec = f.FindAsset(building->Spec);
        var state = FactoryWarsUtil.Player(f, building->Owner);
        if (building->ConstructionRemaining <= FP._0 && spec.ProcessSeconds > FP._0) {
          if (Process(f, entity, building, spec, state)) {
            shortage |= 1 << (int)building->Owner;
          }
          continue;
        }
        if (building->ConstructionRemaining > FP._0 || WantsPower(f, building, spec, state) == false) {
          SetPaused(f, entity, building, PauseReason.None);
          continue;
        }
        var amount = spec.PowerUpkeep * f.DeltaTime;
        if (state->Outage) {
          SetPaused(f, entity, building, PauseReason.Outage);
        } else if (state->Stock.Power < amount) {
          SetPaused(f, entity, building, PauseReason.Outage);
          shortage |= 1 << (int)building->Owner;
        } else {
          state->Stock.Power -= amount;
          state->PowerRate -= spec.PowerUpkeep;
          SetPaused(f, entity, building, PauseReason.None);
        }
      }
      return shortage;
    }

    static void UpdateOutages(Frame f, int shortage) {
      var resumeLevel = FactoryWarsUtil.Rules(f).PowerResumeLevel;
      for (var i = 0; i < f.PlayerCount; i++) {
        var state = f.Global->Players.GetPointer(i);
        if (state->Outage == false && (shortage & (1 << i)) != 0) {
          state->Outage = true;
          state->OutageCount++;
          f.Events.PowerOutage(i, true);
        } else if (state->Outage && state->Stock.Power >= resumeLevel) {
          state->Outage = false;
          f.Events.PowerOutage(i, false);
        }
        if (state->Outage) {
          state->OutageSeconds += f.DeltaTime;
        }
      }
    }
  }
}
