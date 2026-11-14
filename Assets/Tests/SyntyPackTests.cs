namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Security.Cryptography;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 32 에셋 팩 적용 시험(사용자 결정 2026-11-14). 팩이 없는 PC에서는 "팩 없음"으로 통과한다(칸은 지금 모델 그대로).
  /// 원본 무결성·칸 연결·애니메이션·포탑 조준·회전 부품을 본다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static string PackFolder => Path.Combine(Application.dataPath, "ExternalAssets", "Synty");
    static bool PackPresent => Directory.Exists(Path.Combine(PackFolder, "PolygonMilitary"));

    /// <summary>
    /// 절차 모델 확인용: 팩 칸을 이 시험 동안 끈다(TearDown이 되돌린다). 팀 머티리얼·정전 머티리얼처럼
    /// 절차 모델에만 있는 동작을 시험하는 곳에서 쓴다.
    /// </summary>
    void UseProceduralModels() {
      var catalog = VisualCatalog.Current;
      if (catalog == null) {
        return;
      }
      var was = catalog.Enabled;
      catalog.Enabled = false;
      catalog.Invalidate();
      Restores.Add(() => {
        catalog.Enabled = was;
        catalog.Invalidate();
      });
    }

    /// <summary>
    /// 구입한 팩 원본(모델·메쉬·텍스처·머티리얼·프리팹·애니메이션·.meta)이 바뀌지 않았다.
    /// 목록은 `python Tools/pack_hashes.py write`로 들여온 직후 한 번 만든다(Game-logs/assets/pack-hashes.csv).
    /// </summary>
    [Test, Timeout(600000)]
    public void Assets_PackFilesUnchanged() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음(이 PC에는 들여오지 않음)");
      }
      var list = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "pack-hashes.csv"));
      Assert.IsTrue(File.Exists(list), "해시 목록 없음: python Tools/pack_hashes.py write");
      var saved = new Dictionary<string, (long Size, string Hash)>();
      foreach (var line in File.ReadAllLines(list).Skip(1)) {
        var second = line.LastIndexOf(',');
        var first = line.LastIndexOf(',', second - 1);
        saved[line.Substring(0, first).Trim('"')] = (long.Parse(line.Substring(first + 1, second - first - 1)), line.Substring(second + 1));
      }
      var problems = new List<string>();
      var seen = new HashSet<string>();
      using (var sha = SHA256.Create()) {
        foreach (var file in Directory.EnumerateFiles(PackFolder, "*", SearchOption.AllDirectories)) {
          var relative = file.Substring(PackFolder.Length + 1).Replace('\\', '/');
          seen.Add(relative);
          if (saved.TryGetValue(relative, out var expected) == false) {
            problems.Add("추가됨: " + relative);
            continue;
          }
          if (new FileInfo(file).Length != expected.Size) {
            problems.Add("크기 달라짐: " + relative);
            continue;
          }
          using (var stream = File.OpenRead(file)) {
            var hash = string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            if (hash != expected.Hash) {
              problems.Add("내용 달라짐: " + relative);
            }
          }
        }
      }
      problems.AddRange(saved.Keys.Where(k => seen.Contains(k) == false).Select(k => "없어짐: " + k));
      Assert.IsEmpty(problems, $"팩 원본이 바뀜 {problems.Count}개 (앞 20개)\n" + string.Join("\n", problems.Take(20)));
    }

    /// <summary>팩 칸이 연결되어 있고(유닛 22·건물 40 이상), 프리팹이 렌더러·지원되는 셰이더를 가지며, 팀 색 칠하기가 꺼져 있다(원본 머티리얼을 쓴다).</summary>
    [Test]
    public void Synty_SlotsConnectedWithSupportedShaders() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음");
      }
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      var pack = catalog.Slots.Where(s => s.IsFilled && s.Notes != null && s.Notes.StartsWith("Synty")).ToArray();
      Assert.GreaterOrEqual(pack.Count(s => s.Category == VisualCategory.Unit), 22, "유닛 칸");
      Assert.GreaterOrEqual(pack.Count(s => s.Category == VisualCategory.Building), 40, "건물 칸");
      var problems = new List<string>();
      foreach (var slot in pack) {
        if (slot.TeamColor != TeamColorMode.None) {
          problems.Add($"{slot.Id}: 팀 색 칠하기 켜짐({slot.TeamColor}) — 사용자 결정으로 금지");
        }
        var renderers = slot.Prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) {
          problems.Add($"{slot.Id}: 렌더러 없음");
        }
        foreach (var material in renderers.SelectMany(r => r.sharedMaterials)) {
          if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader" || material.shader.isSupported == false) {
            problems.Add($"{slot.Id}: 분홍색 위험 머티리얼 {(material != null ? material.name : "null")}");
            break;
          }
        }
      }
      Assert.IsEmpty(problems, string.Join("\n", problems));
    }

    /// <summary>보병 분대: 분대원마다 Animator가 있고, 속도에 따라 걷기·달리기를 섞고, 엄폐이면서 멈추면 웅크려 대기한다. 분대원끼리 시작 위치가 다르다.</summary>
    [UnityTest]
    public IEnumerator Synty_InfantryAnimationFollowsSpeedAndCover() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음");
      }
      Time.timeScale = 1f;
      // 앞선 시험이 품질을 낮춰 두면 분대원이 1명만 켜진다(MembersByQuality). 높음으로 맞추고 끝나면 되돌린다.
      var tier = GraphicsQuality.Current;
      GraphicsQuality.Apply(QualityTier.High);
      Restores.Add(() => GraphicsQuality.Apply(tier));
      var parent = new GameObject("Stage").transform;
      var view = ArtView.Create(Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath), "ff_ranger_squad", parent, 0);
      Assert.IsNotNull(view);
      Assert.IsNotNull(view.Animation, "팩 캐릭터의 Animator 연결");
      Assert.AreEqual(3, view.Animation.All.Count, "분대원 3명");
      var phases = new HashSet<int>();
      // 상태 기계 동작을 보는 시험이라 화면 밖 컬링(실제 게임 설정)은 끈다. 다른 시험 뒤에는 카메라가 이 무대를 보지 않을 수 있다.
      foreach (var animator in view.Animation.All) {
        Assert.IsTrue(animator.isActiveAndEnabled, "높음 품질에서는 분대원 3명이 모두 켜진다");
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
      }
      view.Animation.Set(UnitAnimState.Move, 4f);
      var until = Time.realtimeSinceStartup + 1.2f;
      while (Time.realtimeSinceStartup < until) {
        view.Animation.Set(UnitAnimState.Move, 4f);
        yield return null;
      }
      foreach (var animator in view.Animation.All) {
        var clips = animator.GetCurrentAnimatorClipInfo(0);
        Assert.IsTrue(clips.Any(c => c.weight > 0.5f && c.clip.name.Contains("Run")), $"속도 4에서 달리기(지금: {string.Join(",", clips.Select(c => c.clip.name + ":" + c.weight.ToString("0.0")))}) 컨트롤러 {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "없음")} 초기화 {animator.isInitialized} 켜짐 {animator.isActiveAndEnabled} 속도 {animator.speed} Speed {animator.GetFloat("Speed")} 시간배율 {Time.timeScale} dt {Time.deltaTime} 갱신 {animator.updateMode} 아바타 {(animator.avatar != null ? animator.avatar.name + animator.avatar.isHuman : "없음")} 상태 {animator.GetCurrentAnimatorStateInfo(0).normalizedTime}");
        phases.Add(Mathf.RoundToInt((animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f) * 10f));
      }
      Assert.GreaterOrEqual(phases.Count, 2, "분대원마다 걸음 위치가 다름");
      until = Time.realtimeSinceStartup + 1.2f;
      while (Time.realtimeSinceStartup < until) {
        view.Animation.Set(UnitAnimState.Idle, 0f);
        yield return null;
      }
      Assert.IsTrue(view.Animation.All[0].GetCurrentAnimatorClipInfo(0).Any(c => c.weight > 0.5f && c.clip.name.Contains("Idle")), "멈추면 대기");
      view.Animation.Cover = true;
      until = Time.realtimeSinceStartup + 1.0f;
      while (Time.realtimeSinceStartup < until) {
        view.Animation.Set(UnitAnimState.Idle, 0f);
        yield return null;
      }
      Assert.IsTrue(view.Animation.All[0].GetCurrentAnimatorStateInfo(0).IsName("CoverIdle"), "엄폐 중 웅크림");
      Object.Destroy(parent.gameObject);
    }

    /// <summary>전차는 포탑이 목표를 향해 돌고 포신이 반동하며, 헬기·레이더 차량의 회전 부품이 실제로 돈다(팩 파일은 고치지 않고 이름으로 찾는다).</summary>
    [UnityTest]
    public IEnumerator Synty_TurretAimsAndSpinPartsMove() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음");
      }
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -4, ArenaZ), (1, "cf_militia", 4, ArenaZ), (0, "ff_hawk_gunship", -10, ArenaZ - 8), (1, "cf_thunder_aa", 14, ArenaZ - 8) });
      yield return WaitForBattle();
      var tank = ArenaUnit(0, "ff_eagle_tank");
      var heli = ArenaUnit(0, "ff_hawk_gunship");
      var radar = ArenaUnit(1, "cf_thunder_aa");
      var actors = Battle().Actors;
      Assert.IsTrue(actors[tank].View.Slot != null && actors[tank].View.Slot.Notes.StartsWith("Synty"), "전차가 팩 모델");
      Assert.IsNotNull(actors[tank].Turret, "포탑을 이름으로 찾음");
      Assert.IsNotNull(actors[tank].Barrel, "포신을 이름으로 찾음");
      Assert.IsTrue(actors[tank].Muzzle != actors[tank].View.Model.transform, "포구는 포신");
      Assert.GreaterOrEqual(actors[heli].Spinners.Count, 2, "헬기 주 회전날개·꼬리 회전날개");
      Assert.GreaterOrEqual(actors[radar].Spinners.Count, 1, "레이더 접시");
      var rotor = actors[heli].Spinners[0].Part;
      var aimed = false;
      var recoiled = false;
      yield return WaitFor(() => {
        aimed |= Mathf.Abs(actors[tank].TurretYaw) > 5f;
        recoiled |= actors[tank].Recoil > 0f;
        return aimed && recoiled;
      }, 40f, "포탑 조준과 반동");
      var first = rotor.localRotation;
      yield return WaitFor(() => Quaternion.Angle(first, rotor.localRotation) > 5f, 5f, "헬기 회전날개가 돈다");
    }

    /// <summary>
    /// 팩 모델은 원본 머티리얼 그대로 그린다(팀 머티리얼 ArtP*로 바꾸지 않음). 소유자는 발밑 팀 색 고리로 알아본다.
    /// </summary>
    [UnityTest]
    public IEnumerator Synty_PackModelsKeepOriginalMaterialsAndShowOwnerRing() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음");
      }
      yield return StartMatch(units: new[] { (0, "ff_ranger_squad", -4, ArenaZ), (1, "cf_militia", 4, ArenaZ), (0, "ff_eagle_tank", -10, ArenaZ - 6) });
      yield return WaitForBattle();
      yield return WaitFrames(Fps);
      var checkedActors = 0;
      foreach (var actor in Battle().Actors.Values) {
        if (actor.View.Slot == null || actor.View.Slot.Notes == null || actor.View.Slot.Notes.StartsWith("Synty") == false) {
          continue;
        }
        checkedActors++;
        foreach (var renderer in actor.View.Model.GetComponentsInChildren<Renderer>(true)) {
          foreach (var material in renderer.sharedMaterials) {
            Assert.IsFalse(material != null && material.name.StartsWith(ArtLook.TeamPrefix), $"{actor.View.name}: 팩 모델에 팀 머티리얼이 입혀짐");
          }
        }
        if (actor.IsBuilding) {
          continue;
        }
        var ring = actor.View.transform.Find("OwnerRing");
        Assert.IsNotNull(ring, $"{actor.View.name}: 소유자 고리");
        Assert.IsTrue(ring.GetComponent<MeshRenderer>().sharedMaterial.name.StartsWith(ArtLook.TeamPrefix), "고리는 팀 색 머티리얼");
      }
      Assert.GreaterOrEqual(checkedActors, 3, "팩 유닛 3기 확인");
    }
  }
}
