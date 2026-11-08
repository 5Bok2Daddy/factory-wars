namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering.Universal;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;
  using Object = UnityEngine.Object;

  /// <summary>
  /// 룩 개발 최종 비교(항목 19A). 후보 세 프로필을 같은 장면·시점 A~E로 찍어 final_compare.png로 붙이고,
  /// 1안은 폰 해상도(2400×1080, 렌더 배율 0.75)에서 낮음·보통·높음을 따로 찍는다. 수치는 Game-logs/lookdev/final.csv.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly int[] FinalScenes = { 180, 450, 660 };

    static VisualProfile Candidate(string name) {
      var path = name == "A" ? VisualProfile.ResourcePath : VisualProfile.ResourcePath + "_" + name;
      var profile = Resources.Load<VisualProfile>(path);
      Assert.IsNotNull(profile, "후보 프로필 에셋: " + path);
      return profile;
    }

    /// <summary>시점 E: 본진에서 유닛 하나를 고른 채 HUD까지 함께 찍는다.</summary>
    Texture2D ShootHud(Camera camera, int width, int height) {
      var canvas = Controller().Hud.Root.GetComponentInParent<Canvas>();
      var mode = canvas.renderMode;
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = camera;
      canvas.planeDistance = 2f;
      Canvas.ForceUpdateCanvases();
      var image = Shoot(camera, width, height);
      canvas.renderMode = mode;
      return image;
    }

    IEnumerator ShootCandidate(string name, List<(Texture2D, string)> cells, int width, int height, string label) {
      VisualProfile.Current = Candidate(name);
      Battle().Rebuild();
      yield return null;
      yield return null;
      GraphicsQuality.Apply(GraphicsQuality.Current);
      yield return null;
      var profile = VisualProfile.Current;
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var camera = Camera.main;
      foreach (var view in ViewPoints(map)) {
        Aim(camera, view.Focus, view.Height);
        var seen = Visible(camera, width, height);
        var tank = view.Name == "C" ? TankPixels(camera, 1920, 1080) : 0f;
        LookdevNote("final.csv", $"{label},{name},{GraphicsQuality.Current},{view.Name},{seen.Renderers},{seen.Triangles},{seen.Casters},{EstimateDrawCalls(seen)},{L(tank)}");
        cells.Add((Shoot(camera, width, height), $"{VisualPresets.Title(name)} [{view.Name}] draw~{EstimateDrawCalls(seen)} tris {seen.Triangles / 1000}k"));
      }
      var home = map.StartPositions[0].ToUnityVector3();
      var engineer = Engineers(0).FirstOrDefault();
      if (engineer != default) {
        Controller().Input.Selection.SelectUnit(engineer);
        Controller().Hud.Refresh(Controller().Frame);
      }
      yield return null;
      Aim(camera, home + new Vector3(8, 0, 6), profile.DefaultHeight);
      cells.Add((ShootHud(camera, width, height), $"{VisualPresets.Title(name)} [E] HUD"));
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(6000000)]
    public IEnumerator Lookdev_Final() {
      yield return FixedScene(FinalScenes[1], human: true);
      var basis = VisualProfile.Current;
      GraphicsQuality.Apply(QualityTier.Medium);
      var cells = new List<(Texture2D, string)>();
      foreach (var name in VisualPresets.Names) {
        yield return ShootCandidate(name, cells, CellWidth * 2, CellHeight * 2, "pc1080");
      }
      SaveSheet("final_compare", "최종 후보 비교: 1안 전술 디오라마 / 2안 근접 강조 / 3안 넓은 시야 (보통 품질, 7:30)", cells, 5);
      foreach (var (texture, _) in cells) {
        Object.Destroy(texture);
      }

      // 1안을 폰 해상도로: 2400×1080에 렌더 배율 0.75를 곱한 1800×810에 그린다.
      var tiers = new List<(Texture2D, string)>();
      foreach (var tier in new[] { QualityTier.Low, QualityTier.Medium, QualityTier.High }) {
        GraphicsQuality.Apply(tier);
        var phone = new List<(Texture2D, string)>();
        yield return ShootCandidate("A", phone, 1800, 810, $"phone_{tier}");
        foreach (var (texture, label) in phone) {
          if (label.Contains("[C]") || label.Contains("[E]")) {
            tiers.Add((texture, $"{tier} {label}"));
          } else {
            Object.Destroy(texture);
          }
        }
      }
      SaveSheet("final_tiers", "1안 폰 2400×1080 (렌더 0.75) 품질별: 중앙 교전 / HUD", tiers, 2);
      foreach (var (texture, _) in tiers) {
        Object.Destroy(texture);
      }
      VisualProfile.Current = basis;
      GraphicsQuality.Apply(QualityTier.Medium);
    }

    /// <summary>1안으로 3:00·7:30·11:00 세 장면의 중앙 교전을 찍는다.</summary>
    [UnityTest, Explicit, Category("Lookdev"), Timeout(6000000)]
    public IEnumerator Lookdev_Times([Values(0, 2)] int index) {
      yield return FixedScene(FinalScenes[index], human: true);
      GraphicsQuality.Apply(QualityTier.Medium);
      var cells = new List<(Texture2D, string)>();
      yield return ShootCandidate("A", cells, CellWidth * 2, CellHeight * 2, $"time{FinalScenes[index]}");
      SaveSheet($"final_time_{FinalScenes[index]}", $"1안 {FinalScenes[index] / 60}:{FinalScenes[index] % 60:00} 장면", cells, 5);
      foreach (var (texture, _) in cells) {
        Object.Destroy(texture);
      }
    }
  }
}
