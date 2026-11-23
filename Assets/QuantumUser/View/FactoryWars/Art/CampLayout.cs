namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>
  /// Task 39 card 4: shared layout of the PMC camp (camp centre = map centre). The editor bakes the static parts (CampKit, compound trim
  /// paving) and MercCampView fills the dynamic parts (display line that follows the shop stock, ambient troops) from the same numbers.
  /// </summary>
  public static class CampLayout {
    public const float RingMid = 17f, RingWidth = 6f, AxisHalf = 3.5f, GateReach = 48f;

    public enum Bay { Tank, Mrap, Drone, Heli }

    /// <summary>Display bays ("shop window"): position, facing yaw and what is parked there. Order inside a kind = stock order.</summary>
    public static readonly (Vector3 Position, float Yaw, Bay Kind)[] Bays = {
      // West display pad (target v2): a column of black tanks facing the road and a column of attack helicopters.
      (new Vector3(-39f, 0f, 19f), 90f, Bay.Tank),
      (new Vector3(-39f, 0f, 11f), 90f, Bay.Tank),
      (new Vector3(-39f, 0f, -11f), 90f, Bay.Tank),
      (new Vector3(-39f, 0f, -19f), 90f, Bay.Tank),
      (new Vector3(-27f, 0f, 17f), 90f, Bay.Heli),
      (new Vector3(-27f, 0f, 8f), 90f, Bay.Heli),
      (new Vector3(-27f, 0f, -8f), 90f, Bay.Heli),
      (new Vector3(-27f, 0f, -17f), 90f, Bay.Heli),
      // South-west: armoured SUVs and drone pads.
      (new Vector3(-9f, 0f, -29f), 0f, Bay.Mrap),
      (new Vector3(-15f, 0f, -29f), 0f, Bay.Mrap),
      (new Vector3(-24f, 0f, -30f), 0f, Bay.Drone),
      (new Vector3(-30f, 0f, -30f), 0f, Bay.Drone),
    };

    /// <summary>Dark display pad under the tank and helicopter columns (x0, z0, x1, z1).</summary>
    public static readonly Vector4 DisplayPad = new Vector4(-44f, -23f, -21.5f, 23f);
    public static readonly Vector3 MainGate = new Vector3(0f, 0f, -48f);

    /// <summary>Road loop (square) point at distance d, counter-clockwise from the south-west corner; with its heading.</summary>
    public static (Vector3 Position, float Yaw) Loop(float d, float lane = 0f) {
      var m = RingMid + lane;
      var side = m * 2f;
      var t = Mathf.Repeat(d, side * 4f);
      var leg = Mathf.FloorToInt(t / side);
      var s = t - leg * side;
      switch (leg) {
        case 0: return (new Vector3(-m + s, 0f, -m), 90f);
        case 1: return (new Vector3(m, 0f, -m + s), 0f);
        case 2: return (new Vector3(m - s, 0f, m), 270f);
        default: return (new Vector3(-m, 0f, m - s), 180f);
      }
    }

    /// <summary>Training yard jogging loop (south-east), firing line positions and target line.</summary>
    public static readonly Vector3 TrainingCentre = new Vector3(19f, 0f, -31.5f);
    public static readonly Vector2 TrainingHalf = new Vector2(10f, 4.5f);
    public static readonly Vector3[] FiringLine = { new Vector3(10f, 0f, -24.3f), new Vector3(16f, 0f, -24.3f), new Vector3(22f, 0f, -24.3f), new Vector3(28f, 0f, -24.3f) };

    /// <summary>Guard posts at the towers and the main gate (ambient sentries).</summary>
    public static readonly Vector3[] Sentries = { new Vector3(-5.5f, 0f, -44f), new Vector3(5.5f, 0f, -44f), new Vector3(-5f, 0f, -9f), new Vector3(5f, 0f, -9f) };
    /// <summary>East drill squad (3 x 3) in front of the barracks.</summary>
    public static readonly Vector3 DrillCentre = new Vector3(29f, 0f, 9f);
    /// <summary>Mechanic spots next to the display bays and the workshop.</summary>
    public static readonly Vector3[] Mechanics = { new Vector3(-35.5f, 0f, 15f), new Vector3(-31.5f, 0f, -12.5f), new Vector3(-12f, 0f, -24.5f), new Vector3(32f, 0f, -12f) };
  }
}
