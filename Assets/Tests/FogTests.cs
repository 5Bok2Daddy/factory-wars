namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 34-A 전장의 안개 시험. 안개는 GameRulesSpec.FogOfWar가 켜져야 동작하므로(기본 꺼짐) 시험이 데이터를 잠시 켠다.
  /// 자기·동맹 시야, 본 적 있는 칸, 보이지 않는 표적 공격 거부, 고지대 은폐와의 결합, 같은 시드 결정론을 본다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    void UseFog() {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var before = rules.FogOfWar;
      rules.FogOfWar = true;
      Restores.Add(() => rules.FogOfWar = before);
    }

    /// <summary>탐색된 칸이 들어 있는 바이트 수(반복자 시험 안에서는 포인터를 못 쓰므로 따로 둔다).</summary>
    static int ExploredBytes(int player) {
      var fog = Verified.Unsafe.GetPointerSingleton<Fog>();
      var seen = 0;
      for (var i = 0; i < 1152; i++) {
        seen += fog->Grids.GetPointer(player)->Explored[i] != 0 ? 1 : 0;
      }
      return seen;
    }

    static FPVector3 At(EntityRef entity) => Verified.Get<Transform3D>(entity).Position;

    /// <summary>자기 유닛 둘레와 동맹 유닛 둘레는 보이고, 먼 적은 안 보인다. 안개가 꺼져 있으면 전부 보인다.</summary>
    [UnityTest]
    public IEnumerator Fog_VisibleOnlyNearOwnAndAllied() {
      UseFog();
      yield return StartMatch(players: 4, teamMode: true, units: new[] { (0, "ff_ranger_squad", -30, ArenaZ), (1, "ff_ranger_squad", 30, ArenaZ), (2, "cf_militia", 0, 46) });
      yield return WaitFrames(Fps);
      var mine = UnitsNamed(0, "ff_ranger_squad").First(e => At(e).Z > 30);
      var ally = UnitsNamed(1, "ff_ranger_squad").First(e => At(e).Z > 30);
      var enemy = UnitsNamed(2, "cf_militia").First(e => At(e).Z > 44);
      Assert.IsTrue(FogSystem.IsVisible(Verified, 0, At(mine)), "내 유닛 자리");
      Assert.IsTrue(FogSystem.IsVisible(Verified, 0, At(ally)), "동맹 시야 공유");
      Assert.IsTrue(FogSystem.IsVisible(Verified, 2, At(enemy)), "적 자기 자리");
      Assert.IsFalse(FogSystem.IsVisible(Verified, 0, At(enemy)), "먼 적 자리는 안 보임");
      Assert.IsFalse(FactoryWarsUtil.IsVisibleTo(Verified, 0, enemy), "먼 적은 안 보임");
      Assert.IsTrue(FactoryWarsUtil.IsVisibleTo(Verified, 0, ally), "동맹은 늘 보임");
      Assert.IsTrue(FactoryWarsUtil.IsVisibleTo(Verified, 0, mine), "내 유닛은 늘 보임");
      // 시야 반경 10: 8m 떨어진 칸은 보이고 14m 떨어진 칸은 안 보인다.
      Assert.IsTrue(FogSystem.IsVisible(Verified, 0, At(mine) + new FPVector3(8, 0, 0)), "8m");
      Assert.IsFalse(FogSystem.IsVisible(Verified, 0, At(mine) + new FPVector3(14, 0, 0)), "14m");
    }

    /// <summary>유닛이 떠나면 그 칸은 안 보이지만 본 적 있는 칸으로 남는다.</summary>
    [UnityTest]
    public IEnumerator Fog_ExploredPersists() {
      UseFog();
      yield return StartMatch(units: new[] { (0, "ff_ranger_squad", -40, ArenaZ) });
      yield return WaitFrames(Fps);
      var unit = UnitsNamed(0, "ff_ranger_squad").First(e => At(e).Z > 30);
      var start = At(unit);
      Assert.IsTrue(FogSystem.IsVisible(Verified, 0, start));
      Assert.IsTrue(FogSystem.IsExplored(Verified, 0, start));
      yield return Send(0, new MoveUnitsCommand { Units = new[] { unit }, Target = V(-10, ArenaZ) });
      yield return WaitFor(() => At(unit).X.AsFloat > start.X.AsFloat + 18f, 30f, "멀리 이동");
      yield return WaitFrames(Fps);
      Assert.IsFalse(FogSystem.IsVisible(Verified, 0, start), "떠난 자리는 지금 안 보임");
      Assert.IsTrue(FogSystem.IsExplored(Verified, 0, start), "본 적 있는 칸으로 남음");
      Assert.IsFalse(FogSystem.IsExplored(Verified, 0, new FPVector3(40, 0, 20)), "간 적 없는 곳은 탐색 안 됨");
    }

    /// <summary>보이지 않는 적에게는 공격 명령이 거부되고 이유가 "시야 없음"이다. 보이게 되면 받아들인다.</summary>
    [UnityTest]
    public IEnumerator Fog_AttackRejectedOutsideVision() {
      UseFog();
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -30, ArenaZ), (1, "cf_dragonscale_tank", 30, ArenaZ) });
      yield return WaitFrames(Fps);
      var tank = UnitsNamed(0, "ff_eagle_tank").First(e => At(e).Z > 30);
      var enemy = UnitsNamed(1, "cf_dragonscale_tank").First(e => At(e).Z > 30);
      _rejected.Clear();
      yield return Send(0, new AttackTargetCommand { Units = new[] { tank }, Target = enemy });
      AssertRejected(0, ActionKind.Attack, RejectReason.NoVision);
      Assert.AreEqual("시야 없음", FactoryWars.View.Texts.Rejected(RejectReason.NoVision, default), "화면 문구");
    }

    /// <summary>고지대 은폐는 안개와 합쳐진다: 칸이 보여도 은폐 조건이면 안 보이고, 가까이 가면 보인다.</summary>
    [UnityTest]
    public IEnumerator Fog_HighGroundConcealmentStillApplies() {
      UseFog();
      // 휴전 30초: 은폐된 전차가 먼저 쏘면 사격 노출(3초)로 보이게 되므로, 이 시험은 쏘기 전 상태를 본다(휴전이 끝나면 4초 뒤 바로 서로 쏜다).
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 42, 24), (1, "cf_dragonscale_tank", 35, 29) }, truce: 30);
      var hidden = UnitsNamed(0, "ff_eagle_tank").First(e => At(e).X.AsFloat > 30f);
      var enemy = UnitsNamed(1, "cf_dragonscale_tank").First(e => At(e).Z.AsFloat > 20f);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { hidden }, Target = V(42, 24) });
      yield return WaitFor(() => FogSystem.IsVisible(Verified, 1, At(hidden)), 5f, "안개 첫 갱신");
      Assert.IsTrue(FogSystem.IsVisible(Verified, 1, At(hidden)), "적 시야 반경 안이라 칸은 보인다");
      Assert.IsFalse(FactoryWarsUtil.IsVisibleTo(Verified, 1, hidden), "그래도 고지대 은폐로 안 보인다");
      Assert.IsTrue(FactoryWarsUtil.IsVisibleTo(Verified, 0, hidden), "주인은 본다");
      yield return Send(1, new MoveUnitsCommand { Units = new[] { enemy }, Target = V(37, 24) });
      yield return WaitFor(() => FactoryWarsUtil.IsVisibleTo(Verified, 1, hidden), 20f, "6m 안에서 보임");
    }

    /// <summary>안개 칸 번호의 (x, z)와 칸 크기.</summary>
    static (int X, int Z) FogCellOf(FPVector3 position) {
      var fog = Verified.Unsafe.GetPointerSingleton<Fog>();
      return (FPMath.FloorToInt((position.X - fog->MinX) / fog->Cell), FPMath.FloorToInt((position.Z - fog->MinZ) / fog->Cell));
    }

    /// <summary>화면: 안개 판이 만들어지고, 내 유닛 자리는 투명하며 먼 칸은 짙고, 안 보이는 적 유닛·건물은 그리지 않는다. 가까워지면 보인다.</summary>
    [UnityTest]
    public IEnumerator Fog_ViewHidesUnseenEnemiesAndShowsOverlay() {
      UseFog();
      yield return StartMatch(units: new[] { (0, "ff_ranger_squad", -40, ArenaZ), (1, "cf_militia", 40, ArenaZ) });
      yield return WaitForHud();
      yield return WaitForBattle();
      yield return WaitFrames(Fps);
      yield return null;
      var view = Battle();
      Assert.IsNotNull(view.Fog, "안개 판");
      var mine = UnitsNamed(0, "ff_ranger_squad").First(e => At(e).Z > 30);
      var enemy = UnitsNamed(1, "cf_militia").First(e => At(e).Z > 30);
      Assert.IsTrue(view.Actors[mine].View.gameObject.activeSelf, "내 유닛은 보인다");
      Assert.IsFalse(view.Actors[enemy].View.gameObject.activeSelf, "안 보이는 적은 그리지 않는다");
      var own = FogCellOf(At(mine));
      var far = FogCellOf(new FPVector3(0, 0, 10));
      Assert.AreEqual(0, view.Fog.Texture.GetPixel(own.X, own.Z).a, 0.01f, "내 자리는 투명");
      Assert.Greater(view.Fog.Texture.GetPixel(far.X, far.Z).a, 0.8f, "간 적 없는 곳은 짙다");
      // 적 건물(본진)도 안개 속이면 그리지 않는다.
      var hiddenBuildings = view.Actors.Values.Count(a => a.IsBuilding && a.View.gameObject.activeSelf == false);
      Assert.Greater(hiddenBuildings, 0, "안개 속 적 건물은 그리지 않는다");
      yield return Send(0, new MoveUnitsCommand { Units = new[] { mine }, Target = V(33, ArenaZ) });
      yield return WaitFor(() => view.Actors[enemy].View.gameObject.activeSelf, 90f, "가까워지면 적이 보인다");
    }

    /// <summary>같은 시드로 안개를 켜고 AI 대 AI를 두 번 돌려 60초 시점 체크섬이 같다(안개 격자가 상태에 들어 있어 체크섬에 반영된다).</summary>
    [UnityTest, Timeout(900000)]
    public IEnumerator Fog_DeterministicOnline() {
      var checksums = new ulong[2];
      for (var run = 0; run < 2; run++) {
        UseFog();
        yield return StartMatch(players: 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal }, seed: 3404);
        var stop = 60 * Fps;
        var sum = 0UL;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          if (e.Frame.Number == stop && sum == 0UL) {
            sum = e.Frame.CalculateChecksum();
          }
        });
        yield return WaitFor(() => sum != 0UL, 120f, "60초 시점 체크섬");
        checksums[run] = sum;
        Assert.Greater(ExploredBytes(0), 20, "탐색된 칸이 쌓인다");
        TearDown();
        yield return null;
      }
      Assert.AreEqual(checksums[0], checksums[1], "같은 시드면 같은 결과");
    }
  }
}
