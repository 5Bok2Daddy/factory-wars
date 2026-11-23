namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// Task 39 (C2): drawbridge animation from the simulation state (Bridge.Lift / LiftT, read only). Each drawbridge is drawn as two
  /// bascule leaves hinged at the deck ends (GridPaint/GridDress leave these deck cells out), a barrier arm at each road end that is
  /// down whenever the deck is not fully down, and red warning lights that blink while the bridge moves or stands open.
  /// </summary>
  public class DrawbridgeView {
    const float MaxAngle = 76f;
    readonly Transform _root;
    bool _built;

    class Span {
      public int Index;
      public readonly List<(Transform Pivot, float Sign)> Leaves = new List<(Transform, float)>();
      public readonly List<Transform> Arms = new List<Transform>();
      public readonly List<GameObject> Lights = new List<GameObject>();
      public float Angle;
      public float Arm;
    }
    readonly List<Span> _spans = new List<Span>();

    public int SpanCount => _spans.Count;
    public float LeafAngle(int i) => i < _spans.Count ? _spans[i].Angle : 0f;

    public DrawbridgeView(Transform root) {
      _root = root;
    }

    public static bool InDrawbridge(MapSpec map, Vector3 point) {
      if (map.Drawbridges == null || map.Bridges == null) return false;
      foreach (var i in map.Drawbridges) {
        if (i >= 0 && i < map.Bridges.Length && map.Bridges[i].Distance(point.ToFPVector3()).AsFloat <= 0.01f) return true;
      }
      return false;
    }

    static GameObject Part(string name, Transform parent, Mesh mesh) {
      var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      g.transform.SetParent(parent, false);
      g.GetComponent<MeshFilter>().sharedMesh = mesh;
      g.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
      return g;
    }

    void Build(MapSpec map) {
      _built = true;
      if (map.Drawbridges == null) return;
      var root = new GameObject("Drawbridges").transform;
      root.SetParent(_root, false);
      foreach (var index in map.Drawbridges) {
        if (index < 0 || index >= map.Bridges.Length) continue;
        var shape = map.Bridges[index];
        var c = shape.Center.ToUnityVector3();
        var alongX = shape.HalfLength >= shape.HalfWidth;
        var half = (alongX ? shape.HalfLength : shape.HalfWidth).AsFloat;
        var width = (alongX ? shape.HalfWidth : shape.HalfLength).AsFloat * 2f - 0.3f;
        var yaw = shape.Yaw.AsFloat + (alongX ? 0f : 90f);
        var frame = new GameObject($"Drawbridge{index}").transform;
        frame.SetParent(root, false);
        frame.position = new Vector3(c.x, 0f, c.z);
        frame.rotation = Quaternion.Euler(0f, yaw, 0f);
        var span = new Span { Index = index };
        // Leaf mesh: local +X from the hinge to the middle of the span, deck top at y 0.05.
        var kit = new MeshKit();
        kit.Paint(Swatch.Asphalt); kit.Box(new Vector3(half / 2f, -0.3f, 0f), new Vector3(half - 0.08f, 0.7f, width));
        kit.Paint(Swatch.LaneYellow); kit.Box(new Vector3(half / 2f, 0.06f, 0f), new Vector3(half - 0.6f, 0.02f, 0.18f));
        kit.Paint(Swatch.MetalDark);
        kit.Box(new Vector3(half / 2f, 0.55f, width / 2f - 0.1f), new Vector3(half - 0.2f, 0.9f, 0.12f));
        kit.Box(new Vector3(half / 2f, 0.55f, -width / 2f + 0.1f), new Vector3(half - 0.2f, 0.9f, 0.12f));
        kit.Paint(Swatch.StripeYellow); kit.Box(new Vector3(half - 0.2f, 0.07f, 0f), new Vector3(0.3f, 0.03f, width - 0.4f));
        var leaf = kit.ToMesh($"drawbridge_leaf_{index}");
        // Counterweight housing at each hinge.
        var house = new MeshKit();
        house.Paint(Swatch.ConcreteDark); house.Box(new Vector3(-1.4f, 1.1f, 0f), new Vector3(2.6f, 2.6f, width + 1.2f));
        house.Paint(Swatch.Hazard); house.Box(new Vector3(-0.1f, 2.45f, 0f), new Vector3(0.25f, 0.2f, width + 1.2f));
        var houseMesh = house.ToMesh($"drawbridge_house_{index}");
        var armKit = new MeshKit();
        armKit.Paint(Swatch.White); armKit.Box(new Vector3(0f, 0f, width * 0.45f), new Vector3(0.18f, 0.18f, width * 0.9f));
        armKit.Paint(Swatch.GlowRed); for (var k = 1; k < 5; k++) armKit.Box(new Vector3(0f, 0f, width * 0.18f * k), new Vector3(0.2f, 0.2f, width * 0.08f));
        var armMesh = armKit.ToMesh($"drawbridge_arm_{index}");
        var lightKit = new MeshKit();
        lightKit.Glow(Swatch.GlowRed); lightKit.Box(Vector3.zero, new Vector3(0.4f, 0.4f, 0.4f));
        var lightMesh = lightKit.ToMesh($"drawbridge_light_{index}");
        foreach (var sign in new[] { -1f, 1f }) {
          var pivot = new GameObject(sign < 0 ? "LeafWest" : "LeafEast").transform;
          pivot.SetParent(frame, false);
          pivot.localPosition = new Vector3(sign * half, 0f, 0f);
          pivot.localRotation = Quaternion.Euler(0f, sign < 0 ? 0f : 180f, 0f);
          Part("Leaf", pivot, leaf);
          var h = Part("Counterweight", frame, houseMesh).transform;
          h.localPosition = new Vector3(sign * half, 0f, 0f);
          h.localRotation = Quaternion.Euler(0f, sign < 0 ? 0f : 180f, 0f);
          span.Leaves.Add((pivot, sign));
          // Barrier arm on the road beyond the hinge (pivot at the road edge), light on its post.
          var post = new GameObject("BarrierPost").transform;
          post.SetParent(frame, false);
          post.localPosition = new Vector3(sign * (half + 4.2f), 1.1f, -width / 2f - 0.2f);
          var arm = Part("Arm", post, armMesh).transform;
          span.Arms.Add(arm);
          var light = Part("WarningLight", post, lightMesh);
          light.transform.localPosition = new Vector3(0f, 0.9f, 0f);
          span.Lights.Add(light);
        }
        _spans.Add(span);
      }
    }

    public unsafe void Update(Frame frame, float dt) {
      var map = FactoryWarsUtil.MapInfo(frame);
      if (map == null) return;
      if (_built == false) {
        if (FactoryWarsUtil.Rules(frame).DrawbridgeEnabled == false) { _built = true; return; }
        Build(map);
      }
      if (_spans.Count == 0) return;
      foreach (var pair in frame.GetComponentIterator<Bridge>()) {
        var bridge = pair.Component;
        foreach (var span in _spans) {
          if (span.Index != bridge.Index) continue;
          var target = bridge.Destroyed ? 0f : bridge.LiftT.AsFloat * MaxAngle;
          span.Angle = Mathf.MoveTowards(span.Angle, target, Mathf.Max(8f, Mathf.Abs(target - span.Angle) * 6f) * dt);
          foreach (var (pivot, sign) in span.Leaves) pivot.localRotation = Quaternion.Euler(0f, sign < 0 ? 0f : 180f, span.Angle);
          var closed = bridge.Lift != BridgeTraffic.Down;
          span.Arm = Mathf.MoveTowards(span.Arm, closed ? 0f : 85f, 120f * dt);
          foreach (var arm in span.Arms) arm.localRotation = Quaternion.Euler(-span.Arm, 0f, 0f);
          var on = closed && Mathf.Repeat(Time.time, 0.8f) < 0.4f;
          foreach (var light in span.Lights) light.SetActive(on);
        }
      }
    }
  }
}
