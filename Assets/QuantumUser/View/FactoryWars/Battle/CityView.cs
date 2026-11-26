namespace FactoryWars.View {
  using System.Collections.Generic;
  using Photon.Deterministic;
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
      /// <summary>Kenney 건물(항목 19B·19C). 있으면 단계 0·1·2는 이것을 보이고 절차 몸체는 숨긴다.</summary>
      public GameObject Dressed;
      public Vector3 BaseScale = Vector3.one;
      public GameObject Ruins;
      public GameObject InteriorFurniture;
    }

    static DressKind DressOf(CityBuildingPlacement placement) => placement.Topples ? DressKind.CityTower : placement.Garrison ? DressKind.CityGarrison : placement.Kind == 0 ? DressKind.CitySmall : placement.Kind == 1 ? DressKind.CityMedium : placement.Kind == 2 ? DressKind.CityIndustrial : DressKind.CityTower;

    /// <summary>단계에 맞춰 Kenney 건물과 절차 몸체 중 하나를 보인다. 단계 2는 높이를 낮추고 폐허 조각을 켠다.</summary>
    static void ShowDressed(Block block, CityBuildingPlacement placement, int stage) {
      if (block.Ruins != null) {
        block.Ruins.SetActive(stage >= 2 || block.Rubble);
      }
      if (block.InteriorFurniture != null) {
        block.InteriorFurniture.SetActive((stage >= 2 || block.Rubble) && GraphicsQuality.Current == QualityTier.High);
      }
      if (block.Dressed == null) {
        return;
      }
      var keep = block.Rubble == false || placement.Topples;
      block.Dressed.SetActive(keep);
      var hasSyntyRuins = block.Ruins != null && block.Ruins.transform.childCount > 0;
      block.Renderer.enabled = keep == false && hasSyntyRuins == false;
      if (keep && placement.Topples == false) {
        block.Dressed.transform.localScale = new Vector3(block.BaseScale.x, block.BaseScale.y * (stage >= 2 ? 0.72f : 1f), block.BaseScale.z);
      }
      DressingSpawn.Soot(block.Dressed, stage <= 0 ? 0f : stage == 1 ? 0.35f : 0.62f);
    }

    readonly Transform _root;
    readonly MapSpec _map;
    readonly bool _urban;
    readonly bool _desert;
    readonly List<Block> _blocks = new List<Block>();
    static readonly Dictionary<(int Kind, int Stage), Mesh> Meshes = new Dictionary<(int, int), Mesh>();
    static Mesh _rubble;
    static Mesh _stripes;
    static Mesh _pad;
    public BattleFx Fx { get; set; }

    public int BlockCount => _blocks.Count;
    public int RubbleCount => _blocks.FindAll(b => b.Rubble).Count;

    public CityView(Transform parent, MapSpec map) {
      _root = new GameObject("City").transform;
      _root.SetParent(parent, false);
      _map = map;
      _urban = TerrainKit.BiomeOf(map) == TerrainKit.Biome.Urban;
      _desert = TerrainKit.BiomeOf(map) == TerrainKit.Biome.Desert;
      Dressing.Begin();
      for (var i = 0; map.CityBuildings != null && i < map.CityBuildings.Length; i++) {
        _blocks.Add(Create(i, map.CityBuildings[i]));
      }
      Dressing.End("city_props");
      BuildBoundary();
      BuildPads();
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
      kit.Shading = MeshKit.ShadeMode.Object;
      var mesh = kit.ToMesh($"city_{kind}_{stage}");
      Meshes[(kind, stage)] = mesh;
      return mesh;
    }

    static Mesh RubbleMesh() {
      if (_rubble == null) {
        var kit = new MeshKit();
        kit.Paint(Swatch.Concrete).Box(new Vector3(0, 0.04f, 0), new Vector3(0.94f, 0.08f, 0.94f));
        kit.Paint(Swatch.ConcreteDark).Box(new Vector3(0, 0.07f, 0), new Vector3(0.82f, 0.06f, 0.82f));
        kit.ShadeFaces(91, Swatch.ConcreteDark, Swatch.Concrete, Swatch.Boulder);
        for (var i = 0; i < 6; i++) {
          var a = i * 1.05f + 0.35f;
          var r = 0.14f + 0.22f * ((i * 29) % 10) / 10f;
          kit.Box(new Vector3(Mathf.Cos(a) * r, 0.11f, Mathf.Sin(a) * r), new Vector3(0.18f, 0.12f, 0.14f));
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

    /// <summary>건물 부지 패드를 메쉬 하나로 합친다(항목 25-1: 건물마다 렌더러 하나씩이던 것을 하나로). 패드는 무너져도 남는다.</summary>
    void BuildPads() {
      // 야전 룩: 도시 건물 밑 부지 판을 그리지 않는다(건물이 땅에 바로 선다).
      if (VisualProfile.Current.FieldLook) {
        return;
      }
      var kit = new MeshKit();
      for (var i = 0; _map.CityBuildings != null && i < _map.CityBuildings.Length; i++) {
        var shape = _map.CityBuildings[i].Shape;
        kit.Push(shape.Center.ToUnityVector3(), new Vector3(0, shape.Yaw.AsFloat, 0), new Vector3(shape.HalfLength.AsFloat * 2, 1, shape.HalfWidth.AsFloat * 2));
        kit.Paint(Swatch.Asphalt).Box(new Vector3(0, 0.04f, 0), new Vector3(1.04f, 0.08f, 1.04f));
        kit.Paint(Swatch.ConcreteDark).Box(new Vector3(0, 0.09f, 0), new Vector3(0.98f, 0.1f, 0.98f));
        kit.Pop();
      }
      if (kit.TriangleCount > 0) {
        Item(_root, "Pads", kit.ToMesh("city_pads"), false);
      }
    }

    static Mesh PadMesh() {
      if (_pad == null) {
        var kit = new MeshKit();
        kit.Paint(Swatch.Asphalt).Box(new Vector3(0, 0.04f, 0), new Vector3(1.04f, 0.08f, 1.04f));
        kit.Paint(Swatch.ConcreteDark).Box(new Vector3(0, 0.09f, 0), new Vector3(0.98f, 0.1f, 0.98f));
        _pad = kit.ToMesh("city_pad");
      }
      return _pad;
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
      var length = shape.HalfLength.AsFloat * 2;
      var width = shape.HalfWidth.AsFloat * 2;
      var seed = 4001 + index * 13;
      var kind = DressOf(placement);
      if (_desert) {
        var style = placement.Topples ? "Radar" : placement.Garrison ? "CamoYard" : index % 3 == 0 ? "Fuel" : "DesertTent";
        var height = placement.Topples ? 4.6f : Mathf.Clamp(width * 0.34f, 1.2f, 3.2f);
        block.Dressed = MapPolishArt.Place(style, pivot, Vector3.zero, Quaternion.identity,
          new Vector3(length * 0.84f, height, width * 0.84f));
        kind = placement.Garrison ? DressKind.TraderNet : DressKind.TraderDish;
      }
      if (_urban && kind == DressKind.CitySmall && index % 2 == 0) {
        block.Dressed = DressingSpawn.Fitted(DressKind.CityCafe, pivot, length, width, 0f, seed);
      }
      if (block.Dressed == null) {
        block.Dressed = DressingSpawn.Fitted(kind, pivot, length, width, Height(placement.Kind), seed);
      }
      if (block.Dressed != null) {
        block.BaseScale = block.Dressed.transform.localScale;
      }
      var ruins = new GameObject("Ruins").transform;
      ruins.SetParent(root, false);
      // 자연스러운 도심 콘크리트 기초 패드(어두운 원형 크레이터·갈색 흙판 대체) + Synty 폐허/잔해 군집.
      var ruinPad = Item(ruins, "RuinFoundation", PadMesh(), false);
      ruinPad.transform.localScale = new Vector3(shape.HalfLength.AsFloat * 1.88f, 0.7f, shape.HalfWidth.AsFloat * 1.88f);
      var coreRuin = DressingSpawn.Place(DressKind.Ruin, ruins, new Vector3(0f, 0.06f, 0f), (index * 90f) % 360f, 7001 + index * 17);
      if (coreRuin != null) {
        coreRuin.name = "RuinCore";
        coreRuin.transform.localScale *= Mathf.Clamp(Mathf.Min(length, width) / 8.5f, 0.72f, 1.15f);
        DressingSpawn.Soot(coreRuin, 0.24f);
      }
      for (var r = 0; r < 3; r++) {
        var angle = (index * 1.7f + r * 2.09f) % (Mathf.PI * 2f);
        var offset = new Vector3(Mathf.Cos(angle) * shape.HalfLength.AsFloat * 0.44f, 0.06f, Mathf.Sin(angle) * shape.HalfWidth.AsFloat * 0.44f);
        var part = DressingSpawn.Place(r == 0 ? DressKind.Ruin : DressKind.Debris, ruins, offset, r * 90f + index * 29f, 7051 + index * 19 + r);
        if (part != null) {
          part.name = "RuinPart";
          part.transform.localScale *= (r == 0 ? 0.78f : 0.85f);
          DressingSpawn.Soot(part, 0.26f);
        }
      }
      block.Ruins = ruins.gameObject;
      // 무너진 건물 내부 가구 잔해(furniture-kit, high 품질에서만 활성 — 항목 19D).
      var interior = new GameObject("InteriorFurniture").transform;
      interior.SetParent(root, false);
      for (var f = 0; f < 2; f++) {
        var fx = (f == 0 ? -0.32f : 0.32f) * shape.HalfLength.AsFloat;
        var fz = (f == 0 ? 0.25f : -0.25f) * shape.HalfWidth.AsFloat;
        var item = DressingSpawn.Place(DressKind.Furniture, interior, new Vector3(fx, 0.12f, fz), index * 37f + f * 115f, 7401 + index * 19 + f);
        if (item != null) {
          item.name = "FurniturePart";
          DressingSpawn.Soot(item, 0.65f);
        }
      }
      block.InteriorFurniture = interior.gameObject;
      var worldRot = Quaternion.Euler(0, shape.Yaw.AsFloat, 0);
      var worldPos = shape.Center.ToUnityVector3();
      if (!_desert && placement.Kind == 0 && Dressing.Collecting) {
        var spot = worldPos + worldRot * new Vector3(shape.HalfLength.AsFloat * 0.68f, 0.02f, -shape.HalfWidth.AsFloat * 0.68f);
        Dressing.Add(DressKind.SuburbanProp, spot, 7601 + index * 11, null, shape.Yaw.AsFloat + 90f);
      } else if (!_desert && placement.Kind == 1 && (index == 2 || index == 11) && Dressing.Collecting) {
        var spot = worldPos + worldRot * new Vector3(-shape.HalfLength.AsFloat * 0.7f, 0.04f, -shape.HalfWidth.AsFloat * 0.72f);
        Dressing.Add(DressKind.StreetMachine, spot, 7801 + index, null, shape.Yaw.AsFloat);
      }
      ShowDressed(block, placement, 0);
      if (placement.Garrison) {
        // 들어갈 수 있다는 표시: 문 앞 모래주머니와 창·문 판자(building-kit).
        var bags = new MeshKit().Paint(Swatch.Sand);
        var front = shape.HalfWidth.AsFloat + 0.6f;
        for (var i = -1; i <= 1; i++) {
          bags.Box(new Vector3(i * 1.1f, 0.35f, -front), new Vector3(1f, 0.7f, 0.6f));
        }
        bags.Paint(Swatch.Black).Box(new Vector3(0, 1.2f, -shape.HalfWidth.AsFloat - 0.02f), new Vector3(1.4f, 2.2f, 0.06f));
        Item(root, "Sandbags", bags.ToMesh("city_sandbags"), true);
        for (var b = -1; b <= 1; b += 2) {
          var board = DressingSpawn.Place(DressKind.GarrisonBoard, pivot, new Vector3(b * shape.HalfLength.AsFloat * 0.45f, 0.9f, -shape.HalfWidth.AsFloat * 0.78f), 0f, 5501 + index * 7 + b);
          if (board != null) {
            board.name = "Board";
          }
        }
        block.Marker = Item(root, "Occupants", null, false);
        block.Marker.transform.localPosition = Vector3.up * ((_desert ? Mathf.Clamp(width * 0.34f, 1.2f, 3.2f) : Height(placement.Kind)) + MarkerLift);
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
      // 주둔 방패/갈매기형 휘장 + 주둔 수 표시.
      kit.Team().Box(new Vector3(0f, 0.25f, 0f), new Vector3(1.1f, 0.9f, 0.22f));
      kit.Team().Box(new Vector3(0f, -0.32f, 0f), new Vector3(0.65f, 0.35f, 0.22f));
      kit.Paint(Swatch.White).Box(new Vector3(-0.22f, 0.28f, -0.13f), new Vector3(0.36f, 0.16f, 0.06f));
      kit.Paint(Swatch.White).Box(new Vector3(0.22f, 0.28f, -0.13f), new Vector3(0.36f, 0.16f, 0.06f));
      for (var i = 0; i < count; i++) {
        kit.Team().Box(new Vector3((i - (count - 1) / 2f) * 0.48f, 0.92f, 0f), new Vector3(0.34f, 0.34f, 0.22f));
      }
      return kit.ToMesh("city_occupants");
    }

    /// <summary>맵 경계 밖을 파괴 불가 고층 건물 벽으로 막는다. 경계 안은 그대로다.</summary>
    void BuildBoundary() {
      if (_map.SealedPerimeter) return;
      if (_desert || TerrainKit.BiomeOf(_map) == TerrainKit.Biome.Archipelago) return;
      // 항목 36: 시가지 모드는 맵 가장자리를 건물 외벽 열(UrbanDecor.Boundary)로 닫는다. 도시 건물 소품 칸이 비어 있으면 예전 경계를 쓴다.
      if (VisualProfile.Current.UrbanDecor && Dressing.Has(DressKind.CityTower)) {
        return;
      }
      if (VisualProfile.Current.Boundary == 1) {
        BuildRockBoundary();
        return;
      }
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

    /// <summary>
    /// 맵 경계를 층진 바위 절벽과 숲으로 마감한다(aow3-ref 방향). 판정은 그대로 경계 밖이다.
    /// 카메라 쪽(남쪽)은 낮게, 먼 쪽은 높게 둔다. 절벽 위에는 침엽수를 듬성듬성 심는다.
    /// </summary>
    void BuildRockBoundary() {
      // 외곽 수로가 있으면 절벽은 수로 바깥에 선다(항목 24).
      var channel = TerrainKit.Channel(_map) > 0f ? TerrainKit.Channel(_map) + 4f : 0f;
      var minX = _map.BoundsMin.X.AsFloat - channel;
      var maxX = _map.BoundsMax.X.AsFloat + channel;
      var minZ = _map.BoundsMin.Y.AsFloat - channel;
      var maxZ = _map.BoundsMax.Y.AsFloat + channel;
      const float Depth = 14f;
      const float Segment = 22f;
      var rock = new MeshKit { Shading = MeshKit.ShadeMode.Ground };
      Dressing.Begin();
      var trees = new MeshKit { Shading = MeshKit.ShadeMode.Object };
      var index = 0;
      void Side(Vector2 from, Vector2 to, Vector2 outward, float low, float high) {
        var length = (to - from).magnitude;
        var count = Mathf.Max(1, Mathf.RoundToInt(length / Segment));
        var along = (to - from) / count;
        for (var i = 0; i < count; i++, index++) {
          var mid = from + along * (i + 0.5f) + outward * (Depth * 0.5f);
          var yaw = -Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
          var height = Mathf.Lerp(low, high, MeshKit.Hash(71, index));
          var shape = new MapShape {
            Center = new FPVector3(FP.FromFloat_UNSAFE(mid.x), FP._0, FP.FromFloat_UNSAFE(mid.y)),
            HalfLength = FP.FromFloat_UNSAFE(along.magnitude * 0.5f + 3f),
            HalfWidth = FP.FromFloat_UNSAFE(Depth * 0.5f + 1f),
            Yaw = FP.FromFloat_UNSAFE(yaw),
          };
          TerrainKit.Plateau(rock, shape, 300 + index, height, 3, 0.9f, false);
          for (var t = 0; t < 5 && VisualProfile.Current.UrbanDecor == false; t++) {
            if (MeshKit.Hash(index, 40 + t) < 0.35f) {
              continue;
            }
            var spot = mid + along.normalized * ((MeshKit.Hash(index, 50 + t) - 0.5f) * along.magnitude * 0.8f) + outward * ((MeshKit.Hash(index, 60 + t) - 0.3f) * Depth * 0.5f);
            var origin = new Vector3(spot.x, height * 0.97f, spot.y);
            if (ForestKit.DressTree(origin, index * 17 + t, ForestKit.TreeState.Full) == false) {
              ForestKit.Tree(trees, origin, index * 17 + t, ForestKit.TreeState.Full);
            }
          }
        }
      }
      Side(new Vector2(minX - Depth, minZ), new Vector2(maxX + Depth, minZ), Vector2.down, 2.5f, 4f);
      Side(new Vector2(minX - Depth, maxZ), new Vector2(maxX + Depth, maxZ), Vector2.up, 9f, 14f);
      Side(new Vector2(minX, minZ), new Vector2(minX, maxZ), Vector2.left, 5f, 9f);
      Side(new Vector2(maxX, minZ), new Vector2(maxX, maxZ), Vector2.right, 5f, 9f);
      Dressing.End("boundary");
      Item(_root, "Boundary", rock.ToMesh("city_boundary_rock"), false);
      Item(_root, "BoundaryTrees", trees.ToMesh("city_boundary_trees"), true);
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
            ShowDressed(block, placement, stage);
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
      Fx?.CollapseBurst(block.Root.transform.position + Vector3.up * 1.3f, 1.35f);
      block.Filter.sharedMesh = RubbleMesh();
      block.Pivot.localRotation = Quaternion.identity;
      // 붕괴 시 진지용 모래주머니·문 판자가 공중에 떠 남지 않도록 함께 숨긴다.
      foreach (Transform child in block.Root.transform) {
        if (child.name == "Sandbags") child.gameObject.SetActive(false);
      }
      foreach (Transform child in block.Pivot) {
        if (child.name == "Board") child.gameObject.SetActive(false);
      }
      ShowDressed(block, placement, 3);
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
