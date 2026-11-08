namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// 도시 건물의 화면. 종류·손상 단계별 메쉬를 한 번 만들어 공유하고, 단계가 바뀌면 메쉬만 바꾼다.
  /// 무너진 건물은 잔해, 쓰러진 건물은 1.5초 동안 기울어진 뒤 누운 잔해가 된다. 시뮬레이션 상태를 읽기만 한다.
  /// </summary>
  public unsafe class CityView {
    const float ToppleSeconds = 1.5f;
    const float WarnTilt = 4f;
    const float FloorHeight = 3f;
    const float MarkerLift = 1.2f;

    class Block {
      public int Index;
      public GameObject Root;
      public Transform Pivot;
      public MeshFilter Filter;
      public MeshRenderer Renderer;
      public GameObject Warning;
      public GameObject Marker;
      public MeshFilter MarkerFilter;
      public int Stage = -1;
      public int Count = -1;
      public int Color = -2;
      public float Fall = -1f;
      public bool Rubble;
    }

    readonly Transform _root;
    readonly MapSpec _map;
    readonly List<Block> _blocks = new List<Block>();
    static readonly Dictionary<(int Kind, int Stage), Mesh> Meshes = new Dictionary<(int, int), Mesh>();
    static Mesh _rubble;
    static Mesh _stripes;
    public BattleFx Fx { get; set; }

    public int BlockCount => _blocks.Count;
    public int RubbleCount => _blocks.FindAll(b => b.Rubble).Count;

    public CityView(Transform parent, MapSpec map) {
      _root = new GameObject("City").transform;
      _root.SetParent(parent, false);
      _map = map;
      for (var i = 0; map.CityBuildings != null && i < map.CityBuildings.Length; i++) {
        _blocks.Add(Create(i, map.CityBuildings[i]));
      }
      BuildBoundary();
    }

    static GameObject Item(Transform parent, string name, Mesh mesh, bool casts) {
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(parent, false);
      item.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = item.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = ArtLook.NeutralMaterial;
      renderer.shadowCastingMode = casts ? ShadowCastingMode.On : ShadowCastingMode.Off;
      return item;
    }

    static float Height(int kind) => kind == 0 ? 7f : kind == 1 ? 12f : kind == 2 ? 9f : 18f;

    /// <summary>단계 0 멀쩡함, 1 금 가고 그을림, 2 위층이 무너짐. 크기는 1×1이고 칸 크기로 늘린다.</summary>
    public static Mesh BlockMesh(int kind, int stage) {
      if (Meshes.TryGetValue((kind, stage), out var cached)) {
        return cached;
      }
      var kit = new MeshKit();
      var height = Height(kind) * (stage == 2 ? 0.62f : 1f);
      var wall = stage == 0 ? (kind == 2 ? Swatch.Metal : Swatch.Concrete) : Swatch.ConcreteDark;
      kit.Paint(Swatch.ConcreteDark).Box(new Vector3(0, 0.25f, 0), new Vector3(1.04f, 0.5f, 1.04f));
      kit.Paint(wall).Box(new Vector3(0, height / 2, 0), new Vector3(0.96f, height, 0.96f));
      // 창문 줄: 층마다 네 면에 어두운 띠.
      var floors = Mathf.Max(1, Mathf.FloorToInt(height / FloorHeight));
      for (var f = 0; f < floors; f++) {
        var y = 1.6f + f * FloorHeight;
        if (y > height - 0.6f) {
          break;
        }
        kit.Paint(stage == 0 ? Swatch.Window : Swatch.Black);
        kit.Box(new Vector3(0, y, 0), new Vector3(0.98f, 0.9f, 0.8f));
        kit.Box(new Vector3(0, y, 0), new Vector3(0.8f, 0.9f, 0.98f));
      }
      kit.Paint(stage == 0 ? Swatch.MetalDark : Swatch.Black).Box(new Vector3(0, height + 0.15f, 0), new Vector3(1f, 0.3f, 1f));
      if (stage == 0 && kind != 3) {
        kit.Paint(Swatch.Metal).Box(new Vector3(0.2f, height + 0.6f, -0.15f), new Vector3(0.22f, 0.6f, 0.22f));
      }
      if (stage == 2) {
        kit.ShadeFaces(kind * 7 + 3, Swatch.BoulderDark, Swatch.ConcreteDark, Swatch.Concrete);
        for (var i = 0; i < 5; i++) {
          var a = i * 1.3f + kind;
          kit.Rock(new Vector3(Mathf.Cos(a) * 0.3f, height, Mathf.Sin(a) * 0.3f), 0.18f, 0.25f, kind * 11 + i);
        }
        kit.EndShade();
      }
      var mesh = kit.ToMesh($"city_{kind}_{stage}");
      Meshes[(kind, stage)] = mesh;
      return mesh;
    }

    static Mesh RubbleMesh() {
      if (_rubble == null) {
        var kit = new MeshKit();
        kit.Paint(Swatch.Dirt).Box(new Vector3(0, 0.05f, 0), new Vector3(1.05f, 0.1f, 1.05f));
        kit.ShadeFaces(91, Swatch.BoulderDark, Swatch.ConcreteDark, Swatch.Concrete);
        for (var i = 0; i < 9; i++) {
          var a = i * 2.4f;
          var r = 0.12f + 0.3f * ((i * 37) % 10) / 10f;
          kit.Rock(new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r), 0.12f + 0.06f * (i % 3), 0.14f, 200 + i);
        }
        kit.EndShade();
        _rubble = kit.ToMesh("city_rubble");
      }
      return _rubble;
    }

    /// <summary>낙하 구역 예고: 붉은 빗금. 크기는 1×1이고 구역 크기로 늘린다.</summary>
    static Mesh StripeMesh() {
      if (_stripes == null) {
        var kit = new MeshKit();
        const int Count = 8;
        for (var i = 0; i < Count; i++) {
          kit.Paint(i % 2 == 0 ? Swatch.GlowRed : Swatch.Black);
          var x0 = -0.5f + i / (float)Count;
          var x1 = x0 + 1f / Count;
          kit.Quad(new Vector3(x0, 0.06f, -0.5f), new Vector3(x0, 0.06f, 0.5f), new Vector3(x1, 0.06f, 0.5f), new Vector3(x1, 0.06f, -0.5f), Vector3.up);
        }
        _stripes = kit.ToMesh("city_fall_warning");
      }
      return _stripes;
    }

    Block Create(int index, CityBuildingPlacement placement) {
      var shape = placement.Shape;
      var root = new GameObject($"City{index}").transform;
      root.SetParent(_root, false);
      root.localPosition = shape.Center.ToUnityVector3();
      root.localRotation = Quaternion.Euler(0, shape.Yaw.AsFloat, 0);
      var pivot = new GameObject("Pivot").transform;
      pivot.SetParent(root, false);
      var body = Item(pivot, "Body", BlockMesh(placement.Kind, 0), true);
      body.transform.localScale = new Vector3(shape.HalfLength.AsFloat * 2, 1, shape.HalfWidth.AsFloat * 2);
      var block = new Block { Index = index, Root = root.gameObject, Pivot = pivot, Filter = body.GetComponent<MeshFilter>(), Renderer = body.GetComponent<MeshRenderer>() };
      if (placement.Garrison) {
        // 들어갈 수 있다는 표시: 문 앞 모래주머니.
        var bags = new MeshKit().Paint(Swatch.Sand);
        var front = shape.HalfWidth.AsFloat + 0.6f;
        for (var i = -1; i <= 1; i++) {
          bags.Box(new Vector3(i * 1.1f, 0.35f, -front), new Vector3(1f, 0.7f, 0.6f));
        }
        bags.Paint(Swatch.Black).Box(new Vector3(0, 1.2f, -shape.HalfWidth.AsFloat - 0.02f), new Vector3(1.4f, 2.2f, 0.06f));
        Item(root, "Sandbags", bags.ToMesh("city_sandbags"), true);
        block.Marker = Item(root, "Occupants", null, false);
        block.Marker.transform.localPosition = Vector3.up * (Height(placement.Kind) + MarkerLift);
        block.MarkerFilter = block.Marker.GetComponent<MeshFilter>();
      }
      if (placement.Topples) {
        var zone = placement.FallZone;
        block.Warning = Item(_root, $"Fall{index}", StripeMesh(), false);
        block.Warning.transform.localPosition = zone.Center.ToUnityVector3();
        block.Warning.transform.localRotation = Quaternion.Euler(0, zone.Yaw.AsFloat, 0);
        block.Warning.transform.localScale = new Vector3(zone.HalfLength.AsFloat * 2, 1, zone.HalfWidth.AsFloat * 2);
        block.Warning.SetActive(false);
      }
      return block;
    }

    /// <summary>들어간 분대 수만큼 팀 색 칸. 0이면 숨긴다.</summary>
    static Mesh OccupantMesh(int count) {
      var kit = new MeshKit();
      for (var i = 0; i < count; i++) {
        kit.Team().Box(new Vector3((i - (count - 1) / 2f) * 0.9f, 0, 0), new Vector3(0.7f, 0.7f, 0.2f));
      }
      return kit.ToMesh("city_occupants");
    }

    /// <summary>맵 경계 밖을 파괴 불가 고층 건물 벽으로 막는다. 경계 안은 그대로다.</summary>
    void BuildBoundary() {
      var min = _map.BoundsMin.X.AsFloat;
      var max = _map.BoundsMax.X.AsFloat;
      var kit = new MeshKit();
      const float Depth = 14f;
      var step = 12f;
      var index = 0;
      for (var t = min - Depth; t < max + Depth; t += step, index++) {
        foreach (var side in new[] { 0, 1, 2, 3 }) {
          // side 0은 남쪽(카메라 쪽)이다. 화면 아래를 가리지 않게 낮은 담과 창고 줄로 둔다.
          var near = side == 0;
          var far = side == 1;
          var h = near ? 2.5f + 2f * MeshKit.Hash(31 + side, index) : far ? 12f + 8f * MeshKit.Hash(31 + side, index) : 5f + 6f * MeshKit.Hash(31 + side, index);
          var along = t + step / 2;
          var across = (side % 2 == 0 ? min - Depth / 2 : max + Depth / 2);
          var center = side < 2 ? new Vector3(along, h / 2, across) : new Vector3(across, h / 2, along);
          kit.Paint(MeshKit.Hash(53 + side, index) < 0.5f ? Swatch.ConcreteDark : Swatch.MetalDark);
          kit.Box(center, side < 2 ? new Vector3(step - 0.6f, h, Depth) : new Vector3(Depth, h, step - 0.6f));
          kit.Paint(Swatch.Black);
          kit.Box(center + Vector3.up * (h / 2 - 1.2f), side < 2 ? new Vector3(step - 0.4f, 0.6f, Depth + 0.1f) : new Vector3(Depth + 0.1f, 0.6f, step - 0.4f));
        }
      }
      Item(_root, "Boundary", kit.ToMesh("city_boundary"), false);
    }

    public void Update(Frame frame, PlayerRef viewer, float deltaTime) {
      var occupants = new Dictionary<EntityRef, int>();
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        if (pair.Component.Garrison != EntityRef.None) {
          occupants[pair.Component.Garrison] = occupants.TryGetValue(pair.Component.Garrison, out var n) ? n + 1 : 1;
        }
      }
      var alive = new Dictionary<int, (EntityRef Entity, CityBuilding City)>();
      foreach (var pair in frame.GetComponentIterator<CityBuilding>()) {
        alive[pair.Component.Index] = (pair.Entity, pair.Component);
      }
      foreach (var block in _blocks) {
        var placement = _map.CityBuildings[block.Index];
        if (alive.TryGetValue(block.Index, out var entry)) {
          var stage = Mathf.Min(entry.City.Stage, 2);
          if (stage != block.Stage) {
            if (stage > block.Stage && block.Stage >= 0) {
              Fx?.Explosion(block.Root.transform.position + Vector3.up * Height(placement.Kind) * 0.6f, 1.2f);
            }
            block.Stage = stage;
            block.Filter.sharedMesh = BlockMesh(placement.Kind, stage);
          }
          if (placement.Topples) {
            var warn = stage >= 2;
            block.Warning.SetActive(warn);
            block.Pivot.localRotation = warn ? Quaternion.AngleAxis(WarnTilt, FallAxis(placement)) : Quaternion.identity;
          }
          if (block.Marker != null) {
            var count = occupants.TryGetValue(entry.Entity, out var n) ? n : 0;
            var color = entry.City.Occupant.IsValid && (int)entry.City.Occupant < frame.PlayerCount ? frame.Global->Players.GetPointer(entry.City.Occupant)->ColorIndex : -1;
            if (count != block.Count || color != block.Color) {
              block.Count = count;
              block.Color = color;
              block.MarkerFilter.sharedMesh = count > 0 ? OccupantMesh(count) : null;
              block.Marker.GetComponent<MeshRenderer>().sharedMaterial = color >= 0 ? ArtLook.Team(color, true) : ArtLook.NeutralMaterial;
            }
            if (Camera.main != null) {
              block.Marker.transform.rotation = Quaternion.Euler(VisualProfile.Current.Pitch, 0, 0);
            }
          }
          continue;
        }
        if (block.Rubble) {
          continue;
        }
        if (placement.Topples) {
          Topple(block, placement, deltaTime);
        } else {
          Crumble(block, placement);
        }
      }
    }

    /// <summary>쓰러지는 방향을 축으로 바꾼다. 낙하 구역의 긴 변 방향으로 넘어간다.</summary>
    static Vector3 FallAxis(CityBuildingPlacement placement) {
      var yaw = placement.FallZone.Yaw.AsFloat * Mathf.Deg2Rad;
      var direction = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
      var local = Quaternion.Inverse(Quaternion.Euler(0, placement.Shape.Yaw.AsFloat, 0)) * direction;
      return Vector3.Cross(Vector3.up, local).normalized;
    }

    void Crumble(Block block, CityBuildingPlacement placement) {
      block.Rubble = true;
      Fx?.Explosion(block.Root.transform.position + Vector3.up * 2f, 2.4f);
      Fx?.GroundDust(block.Root.transform.position);
      block.Filter.sharedMesh = RubbleMesh();
      block.Pivot.localRotation = Quaternion.identity;
      if (block.Marker != null) {
        block.Marker.SetActive(false);
      }
    }

    void Topple(Block block, CityBuildingPlacement placement, float deltaTime) {
      if (block.Fall < 0f) {
        block.Fall = 0f;
        Fx?.GroundDust(block.Root.transform.position);
        if (block.Warning != null) {
          block.Warning.SetActive(false);
        }
      }
      block.Fall = Mathf.MoveTowards(block.Fall, 1f, deltaTime / ToppleSeconds);
      var eased = block.Fall * block.Fall;
      block.Pivot.localRotation = Quaternion.AngleAxis(Mathf.Lerp(WarnTilt, 90f, eased), FallAxis(placement));
      if (block.Fall < 1f) {
        return;
      }
      block.Rubble = true;
      Fx?.Explosion(placement.FallZone.Center.ToUnityVector3() + Vector3.up, 2.8f);
      foreach (var t in new[] { 0.25f, 0.5f, 0.75f }) {
        Fx?.GroundDust(Vector3.Lerp(block.Root.transform.position, placement.FallZone.Center.ToUnityVector3() * 2f - block.Root.transform.position, t));
      }
    }
  }
}
