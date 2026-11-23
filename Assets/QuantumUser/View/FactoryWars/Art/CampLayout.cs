namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>
  /// Task 39 (fix 2): shared layout of the PMC camp (camp centre = map centre). The editor bakes the static parts (CampKit, compound trim
  /// paving) and MercCampView fills the dynamic parts (display line that follows the shop stock, troops) from the same numbers.
  /// Roads: one 6 m loop around the trading post (rectangle ±LoopX × ±LoopZ) and one road from the loop to the south gate.
  /// </summary>
  public static class CampLayout {
    public const float LoopX = 16f, LoopZ = 13f, RoadWidth = 6f, GateRoadHalf = 3f, GateReach = 48f;
    public const float RingMid = LoopX;

    public enum Bay { Tank, Mrap, Drone, Heli, ScoutHeli }

    /// <summary>Display bays ("shop window"): position, facing yaw and what is parked there. Order inside a kind = stock order.</summary>
    public static readonly (Vector3 Position, float Yaw, Bay Kind)[] Bays = {
      // West (real scale, fix 4): six black tanks (9.5 x 4.75 m) with 1.5 m gaps, three attack helicopters (16 x 11 m, rotors
      // cannot fold - single mesh - so three fit) with 1.5 m gaps, and the light helicopter.
      (new Vector3(-42f, 0f, 15.625f), 90f, Bay.Tank),
      (new Vector3(-42f, 0f, 9.375f), 90f, Bay.Tank),
      (new Vector3(-42f, 0f, 3.125f), 90f, Bay.Tank),
      (new Vector3(-42f, 0f, -3.125f), 90f, Bay.Tank),
      (new Vector3(-42f, 0f, -9.375f), 90f, Bay.Tank),
      (new Vector3(-42f, 0f, -15.625f), 90f, Bay.Tank),
      (new Vector3(-27f, 0f, 12.5f), 90f, Bay.Heli),
      (new Vector3(-27f, 0f, 0f), 90f, Bay.Heli),
      (new Vector3(-27f, 0f, -12.5f), 90f, Bay.Heli),
      (new Vector3(-34f, 0f, 24.5f), 90f, Bay.ScoutHeli),
      // South-west: four APCs (7.5 m) in a row, two drone pads.
      (new Vector3(-8.5f, 0f, -29f), 0f, Bay.Mrap),
      (new Vector3(-14.5f, 0f, -29f), 0f, Bay.Mrap),
      (new Vector3(-20.5f, 0f, -29f), 0f, Bay.Mrap),
      (new Vector3(-26.5f, 0f, -29f), 0f, Bay.Mrap),
      (new Vector3(-9f, 0f, -37f), 0f, Bay.Drone),
      (new Vector3(-15f, 0f, -37f), 0f, Bay.Drone),
    };

    /// <summary>Dark display pads (x0, z0, x1, z1): tank/helicopter columns and the APC row.</summary>
    public static readonly Vector4 DisplayPad = new Vector4(-46.5f, -21f, -19.5f, 21f);
    public static readonly Vector4 ApcPad = new Vector4(-30f, -33.5f, -5f, -24.5f);
    public static readonly Vector3 MainGate = new Vector3(0f, 0f, -48f);

    /// <summary>Loop road point at distance d (counter-clockwise from the south-west corner) with its heading; lane offsets outward.</summary>
    public static (Vector3 Position, float Yaw) Loop(float d, float lane = 0f) {
      var x = LoopX + lane;
      var z = LoopZ + lane;
      var t = Mathf.Repeat(d, 4f * (x + z));
      if (t < 2f * x) return (new Vector3(-x + t, 0f, -z), 90f);
      t -= 2f * x;
      if (t < 2f * z) return (new Vector3(x, 0f, -z + t), 0f);
      t -= 2f * z;
      if (t < 2f * x) return (new Vector3(x - t, 0f, z), 270f);
      t -= 2f * x;
      return (new Vector3(-x, 0f, z - t), 180f);
    }

    /// <summary>Training yard jogging loop (south-east), firing line positions.</summary>
    public static readonly Vector3 TrainingCentre = new Vector3(19f, 0f, -31.5f);
    public static readonly Vector2 TrainingHalf = new Vector2(9f, 4f);
    public static readonly Vector3[] FiringLine = { new Vector3(10f, 0f, -24.3f), new Vector3(16f, 0f, -24.3f), new Vector3(22f, 0f, -24.3f), new Vector3(28f, 0f, -24.3f) };

    /// <summary>Guards at the gate and the corner towers.</summary>
    public static readonly Vector3[] Sentries = { new Vector3(-5f, 0f, -44f), new Vector3(5f, 0f, -44f), new Vector3(-27f, 0f, 27f), new Vector3(27f, 0f, -27f), new Vector3(27f, 0f, 27f) };
    /// <summary>East drill squad (3 x 3) between the barracks rows.</summary>
    public static readonly Vector3 DrillCentre = new Vector3(32.5f, 0f, 1f);
    /// <summary>Mechanics at the display vehicles.</summary>
    public static readonly Vector3[] Mechanics = { new Vector3(-36.1f, 0f, 6.2f), new Vector3(-36.1f, 0f, -12.4f), new Vector3(-17.5f, 0f, -25.5f), new Vector3(23.5f, 0f, -16.8f), new Vector3(-6.2f, 0f, -17.6f), new Vector3(21.5f, 0f, 22.5f) };
    /// <summary>Trading-post counter: vendor behind it, customers in front.</summary>
    public static readonly Vector3[] Counter = { new Vector3(0f, 0f, -7.2f), new Vector3(-2.2f, 0f, -9.6f), new Vector3(1.6f, 0f, -9.9f), new Vector3(3.6f, 0f, -9.2f) };
  }
}
