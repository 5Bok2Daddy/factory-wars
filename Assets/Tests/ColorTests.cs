namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>플레이어 색 선택.</summary>
  public unsafe partial class LocalMatchTests {
    // Machado 2009 색각 이상 시뮬레이션 행렬. 심각도 1.0. 일반, 적색맹, 녹색맹, 청색맹 순서다.
    static readonly float[][] VisionModes = {
      new[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f },
      new[] { 0.152286f, 1.052583f, -0.204868f, 0.114503f, 0.786281f, 0.099216f, -0.003882f, -0.048116f, 1.051998f },
      new[] { 0.367322f, 0.860646f, -0.227968f, 0.280085f, 0.672501f, 0.047413f, -0.011820f, 0.042940f, 0.968881f },
      new[] { 1.255528f, -0.076749f, -0.178779f, -0.078411f, 0.930809f, 0.147602f, 0.004733f, 0.691367f, 0.303900f },
    };

    static float Linear(byte value) {
      var c = value / 255f;
      return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    static Vector3 Lab(Color32 color, float[] m) {
      float r = Linear(color.r), g = Linear(color.g), b = Linear(color.b);
      var sr = Mathf.Clamp01(m[0] * r + m[1] * g + m[2] * b);
      var sg = Mathf.Clamp01(m[3] * r + m[4] * g + m[5] * b);
      var sb = Mathf.Clamp01(m[6] * r + m[7] * g + m[8] * b);
      float x = 0.4124f * sr + 0.3576f * sg + 0.1805f * sb, y = 0.2126f * sr + 0.7152f * sg + 0.0722f * sb, z = 0.0193f * sr + 0.1192f * sg + 0.9505f * sb;
      float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
      float fx = F(x / 0.9505f), fy = F(y), fz = F(z / 1.089f);
      return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }

    [Test]
    public void Palette_EightColorsStayDistinctForColorBlindness() {
      Assert.AreEqual(8, Palette.Colors.Length);
      foreach (var mode in VisionModes) {
        for (var a = 0; a < Palette.Count; a++) {
          for (var b = a + 1; b < Palette.Count; b++) {
            var distance = (Lab(Palette.Colors[a], mode) - Lab(Palette.Colors[b], mode)).magnitude;
            Assert.Greater(distance, 15f, $"{Palette.Names[a]}·{Palette.Names[b]}");
          }
        }
      }
    }

    [Test]
    public void Palette_KeepsFactionRecommendations() {
      Assert.AreEqual(new Color32(0x1F, 0x3A, 0x6B, 0xFF), Palette.Colors[Palette.Recommended(0)], "자유 연방 남색");
      Assert.AreEqual(new Color32(0xA3, 0x22, 0x2B, 0xFF), Palette.Colors[Palette.Recommended(1)], "대륙 연방 진홍");
    }

    static int ColorOf(int player) => StateOf(player).ColorIndex;

    [UnityTest]
    public IEnumerator Color_DuplicateChoiceMovesToNextFree() {
      // 테스트가 추가한 자리는 색을 고르지 않아 모두 0번을 원한다.
      yield return StartMatch(players: 4);
      yield return WaitFrames(CommandFrames);
      var colors = Enumerable.Range(0, 4).Select(ColorOf).ToArray();
      Assert.AreEqual(4, colors.Distinct().Count(), string.Join(",", colors));
    }

    [UnityTest]
    public IEnumerator Color_ChosenInMenuReachesMatchAndBotsAvoidIt() {
      yield return OpenMenu();
      var menu = Object.FindAnyObjectByType<MenuController>();
      menu.Show(MenuController.Screen.Options);
      menu.PickFaction(1);
      Assert.AreEqual(1, menu.Settings.Color, "대륙 연방 기본 진홍");
      menu.NextMode();
      menu.NextColor();
      menu.NextColor();
      Assert.AreEqual(3, menu.Settings.Color);
      menu.Begin();
      yield return WaitFor(() => Game?.Frames?.Verified != null && Verified.ComponentCount<Building>() >= 4, 30f, "대전 시작");
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual(3, ColorOf(0), "고른 색");
      var colors = Enumerable.Range(0, 4).Select(ColorOf).ToArray();
      Assert.AreEqual(4, colors.Distinct().Count(), string.Join(",", colors));
      Assert.AreEqual(Palette.Colors[3], Palette.Of(Verified, 0));
    }

    [UnityTest]
    public IEnumerator Color_ColorIsNotTiedToFaction() {
      yield return OpenMenu();
      var menu = Object.FindAnyObjectByType<MenuController>();
      menu.PickFaction(0);
      for (var i = 0; i < 5; i++) {
        menu.NextColor();
      }
      menu.Begin();
      yield return WaitFor(() => Game?.Frames?.Verified != null && Verified.ComponentCount<Building>() >= 2, 30f, "대전 시작");
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual("free_federation", FactionOf(0).FactionId);
      Assert.AreEqual(5, ColorOf(0), "자유 연방이 노랑");
    }
  }
}
