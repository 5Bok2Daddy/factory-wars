namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// 휴전 중 할 일 안내와 재미 요소 첫 안내(항목 30). 휴전 팁은 매 판, 재미 요소 안내는 기기마다 한 번만 보인다.
  /// 시간표는 경기 시간(초) 기준이고, 화면 위 알림 줄을 쓴다. 시뮬레이션은 읽기만 한다.
  /// </summary>
  public static unsafe class TruceTips {
    public const string HintPrefix = "fw_hint_";

    public struct Tip {
      public int Second;
      public string Key;
      public string Text;
      /// <summary>true면 기기마다 한 번만(재미 요소 첫 안내).</summary>
      public bool Once;
      /// <summary>true면 휴전 중에만.</summary>
      public bool TruceOnly;
    }

    /// <summary>첫 3분 추천 빌드와 휴전 할 일, 그 뒤 재미 요소 첫 안내.</summary>
    public static readonly Tip[] Schedule = {
      new Tip { Second = 3, Key = "truce_start", Text = "휴전 중입니다. 공병으로 철광산·숲을 채집하고 기지를 키우세요", TruceOnly = true },
      new Tip { Second = 25, Key = "build_depot", Text = "추천: 보급창 → 병영 → 발전소 → 가공소 순서로 지으세요", TruceOnly = true },
      new Tip { Second = 70, Key = "crates", Text = "본진 근처 보급 상자를 유닛으로 주우면 자원을 받습니다", TruceOnly = true },
      new Tip { Second = 120, Key = "factory", Text = "가공소로 강철을 만들고 군수공장에서 차량을 생산하세요", TruceOnly = true },
      new Tip { Second = 170, Key = "groups", Text = "부대 지정으로 군대를 1~3부대에 묶어 두면 빨리 고를 수 있습니다", TruceOnly = true },
      new Tip { Second = 240, Key = "capture_hint", Text = "지도 가운데 통신소·발전 중계소를 점령하면 자원이 계속 들어옵니다", Once = true },
      new Tip { Second = 300, Key = "highground_hint", Text = "고지대 위 유닛은 사거리가 길고 아래에서 잘 안 보입니다", Once = true },
      new Tip { Second = 360, Key = "garrison_hint", Text = "보병을 도시 건물에 넣으면 진지가 됩니다. 건물이 무너지면 튀어나옵니다", Once = true },
      new Tip { Second = 400, Key = "naval_hint", Text = "물가에 조선소를 지으면 휴전 뒤 수문으로 배를 내보낼 수 있습니다", Once = true },
    };

    static readonly HashSet<string> Shown = new HashSet<string>();
    static object _match;

    public static bool WasShown(string key) => Shown.Contains(key);

    public static void Reset() => Shown.Clear();

    public static void Update(MatchHud hud, Frame frame, PlayerState* state) {
      if (_match != (object)hud) {
        _match = hud;
        Shown.Clear();
      }
      var second = frame.Number / frame.SessionConfig.UpdateFPS;
      var truce = FactoryWarsUtil.InTruce(frame);
      foreach (var tip in Schedule) {
        if (second < tip.Second || Shown.Contains(tip.Key) || (tip.TruceOnly && truce == false)) {
          continue;
        }
        Shown.Add(tip.Key);
        if (tip.Once) {
          if (PlayerPrefs.GetInt(HintPrefix + tip.Key, 0) == 1) {
            continue;
          }
          PlayerPrefs.SetInt(HintPrefix + tip.Key, 1);
        }
        hud.ShowAlert(tip.Text);
        return;
      }
    }
  }
}
