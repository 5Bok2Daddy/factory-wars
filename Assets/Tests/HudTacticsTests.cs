namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Quantum;
  using Quantum.FactoryWars;
  using FactoryWars.View;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>34·37 화면 조작: 저격수 은신·교란 장치 버튼과 탭, 비행장 격납고 메뉴에서 출격, 도크린 카드 패널, 작전기 모델.</summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest]
    public IEnumerator Hud_DoctrineCardsShownUntilChosen() {
      yield return StartMatch();
      yield return WaitForHud();
      yield return WaitFrames(2);
      var panel = GameObject.Find("DoctrinePanel");
      Assert.IsNotNull(panel, "도크린 카드 패널");
      Assert.IsTrue(panel.activeSelf, "시작 직후 보임");
      Assert.AreEqual(3, panel.GetComponentsInChildren<UnityEngine.UI.Button>(true).Count(b => b.name.StartsWith("Doctrine")), "카드 3장");
      panel.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(b => b.name == "Doctrine2").onClick.Invoke();
      yield return WaitFor(() => DoctrineLockedOf(0), 10f, "선택 반영");
      Assert.AreEqual(2, DoctrineOf(0));
      yield return WaitFrames(2);
      Assert.IsFalse(panel.activeInHierarchy, "고르면 닫힘");
    }

    [UnityTest]
    public IEnumerator Hud_SniperStealthAndJammerButtons() {
      yield return StartMatch(units: new[] { (0, "ff_sniper", -30, 8) });
      yield return WaitForHud();
      var sniper = Sniper(0, "ff_sniper");
      var hud = Controller().Hud;
      Controller().Input.Selection.SelectUnit(sniper);
      hud.Refresh(Controller().Frame);
      var stealth = hud.Entries.First(e => e.Label.StartsWith("은신"));
      Assert.IsTrue(stealth.Enabled, "은신 가능");
      Assert.IsTrue(hud.Entries.Any(e => e.Label.Contains("교란") && e.Enabled == false), "은신 전 교란 장치 잠김");
      stealth.Action();
      yield return WaitFor(() => Verified.Get<Unit>(sniper).StealthUntilFrame > Verified.Number, 5f, "은신");
      hud.Refresh(Controller().Frame);
      var jammer = hud.Entries.First(e => e.Label.Contains("교란"));
      Assert.IsTrue(jammer.Enabled, "은신 중 교란 장치");
      jammer.Action();
      Assert.AreEqual(MatchInput.TapMode.Jammer, Controller().Input.Mode);
      var command = Controller().Input.Tap(Controller().Frame, 0, new Vector3(-30, 0, 14));
      Assert.IsInstanceOf<PlaceJammerCommand>(command, "노드를 누르면 설치 명령");
    }

    [UnityTest]
    public IEnumerator Hud_HangarMenuOrdersSortieAndModelFlies() {
      yield return StartMatch(stock: SortieStock, buildings: new[] { (0, "ff_airfield", -20, 10), (0, "ff_power_plant", -30, 0), (0, "ff_power_plant", -30, 20) });
      yield return WaitForHud();
      var airfield = OwnBuilding(0, "ff_airfield");
      yield return StoreAircraft(airfield, "ff_sortie_fighter");
      var hud = Controller().Hud;
      Controller().Input.Selection.SelectBuilding(airfield);
      hud.Refresh(Controller().Frame);
      var open = hud.Entries.First(e => e.Label.StartsWith("격납고"));
      open.Action();
      hud.Refresh(Controller().Frame);
      var slot = hud.Entries.First(e => e.Label.Contains(Texts.NameOf("ff_sortie_fighter")));
      Assert.IsTrue(slot.Enabled, "출격 가능 칸");
      slot.Action();
      Assert.AreEqual(MatchInput.TapMode.Sortie, Controller().Input.Mode);
      var command = Controller().Input.Tap(Controller().Frame, 0, new Vector3(10, 0, 10));
      Assert.IsInstanceOf<OrderSortieCommand>(command);
      yield return Send(0, command);
      yield return WaitFor(() => Verified.ComponentCount<Sortie>() == 1, 5f, "출격");
      yield return WaitFrames(Fps);
      Assert.IsNotNull(Object.FindObjectsByType<ArtView>(FindObjectsSortMode.None).FirstOrDefault(v => v.name.Contains("sortie")), "작전기 모델이 그려진다");
    }
  }
}
