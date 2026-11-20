namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// MapSpec에서 바닥·지형·자원·다리를 만들고 시뮬레이션 상태를 따라 바꾼다. 상태는 읽기만 한다.
  /// </summary>
  public unsafe class WorldView {
    const int ForestSteps = 10;
    const float MineModelRadius = 3f;
    const float BarrierSeconds = 1.7f;
    const float SlowBlink = 1.6f;
    const float FastBlink = 0.32f;
    const float FastBlinkFrom = 10f;

    class Forest {
      public MapShape Shape;
      public int Seed;
      public float Amount;
      public int Step = -1;
      public float ClearHalfExtent;
      public MeshFilter Filter;
      /// <summary>숲 자리의 전장 손상 단계(0 멀쩡, 1 그을림, 2 부러짐). 바뀌면 나무를 다시 놓는다(항목 27 K-2).</summary>
      public int ScarBand;
    }

    int _scarVersion = -1;
    float _scarCheckAt;

    /// <summary>손상 지도가 바뀌었으면 숲마다 손상 단계를 다시 보고, 바뀐 숲은 다음 갱신에서 다시 만든다. 1초에 한 번.</summary>
    void CheckForestScars() {
      var scars = BattleScars.Active;
      if (scars == null || scars.Version == _scarVersion || Time.time < _scarCheckAt) {
        return;
      }
      _scarVersion = scars.Version;
      _scarCheckAt = Time.time + 1f;
      foreach (var forest in _forests.Values) {
        var radius = forest.Shape.IsCircle ? forest.Shape.Radius.AsFloat : Mathf.Max(forest.Shape.HalfLength.AsFloat, forest.Shape.HalfWidth.AsFloat);
        var level = scars.Max(forest.Shape.Center.ToUnityVector3(), radius);
        var band = level >= 0.7f ? 2 : level >= 0.4f ? 1 : 0;
        if (band != forest.ScarBand) {
          forest.ScarBand = band;
          forest.Step = -1;
        }
      }
    }

    class Barrier {
      public GameObject Root;
      public MeshRenderer Lamps;
      public List<Vector3> Dust;
    }

    readonly List<Barrier> _barriers = new List<Barrier>();
    float _barrierDrop = -1f;
    /// <summary>항목 36 수문: 시뮬레이션 수문 영역마다 조립 프리팹 하나. 개방(7분 휴전 끝 또는 빈 본진)이면 가운데 문이 내려간다.</summary>
    public class GateView {
      public WaterGateRig Rig;
      public int Seat;
      public float Drop;
      public float OpenFor;
    }
    readonly List<GateView> _gates = new List<GateView>();
    bool _gatesBuilt;
    public IReadOnlyList<GateView> Gates => _gates;
    readonly Transform _root;
    readonly MapSpec _map;
    readonly ViewCatalog _catalog;
    readonly Dictionary<EntityRef, Forest> _forests = new Dictionary<EntityRef, Forest>();
    readonly Dictionary<EntityRef, GameObject> _crystals = new Dictionary<EntityRef, GameObject>();
    readonly Dictionary<EntityRef, GameObject> _mineRoots = new Dictionary<EntityRef, GameObject>();
    readonly Dictionary<EntityRef, float> _mountedNodes = new Dictionary<EntityRef, float>();
    readonly Dictionary<EntityRef, List<Dressing.Item>> _coveredMineDressing = new Dictionary<EntityRef, List<Dressing.Item>>();
    public BattleFx Fx { get; set; }
    public int BarrierCount => _barriers.Count;
    public float BarrierDrop => Mathf.Max(_barrierDrop, 0f);
    public bool BarrierVisible => _barriers.Exists(b => b.Root.activeSelf);
    public int ForestCount => _forests.Count;
    GameObject _noBuild;
    class BridgeVisual {
      public GameObject Intact;
      public GameObject Broken;
      public GameObject Pontoon;
      public int State = -1; // 0=intact, 1=destroyed, 2=pontoon
    }

    readonly List<BridgeVisual> _bridges = new List<BridgeVisual>();
    public int BridgeCount => _bridges.Count;

    public bool NoBuildShown => _noBuild == null || _noBuild.activeSelf;

    /// <summary>건설 위치를 고를 때만 금지 표시를 켠다.</summary>
    public void ShowNoBuild(bool show) {
      if (_noBuild != null && _noBuild.activeSelf != show) {
        _noBuild.SetActive(show);
      }
    }
    public int MineCount => _crystals.Count;

    public MapSpec Map => _map;

    public WorldView(Transform parent, MapSpec map, ViewCatalog catalog) {
      _root = new GameObject("World").transform;
      _root.SetParent(parent, false);
      _map = map;
      _catalog = catalog;
      BuildStatic();
    }

    static Material _ground;
    static Material _desertGround;

    /// <summary>바닥 전용 공유 머티리얼: 공용 셰이더에 정점 색을 켜고 접촉 AO·높이 그라데이션은 끈다.</summary>
    /// <summary>바닥·도로 공용 머티리얼(정점 색 + 전장 손상). 색 맞추기 비교 장면도 쓴다.</summary>
    public static Material SharedGroundMaterial => GroundMaterial();

    static Material _road;
    public const string AsphaltNormalPath = "FactoryWars/Art/Asphalt02Normal";
    static Texture2D _asphaltNormal;

    /// <summary>
    /// 길 전용 머티리얼: 바닥과 같지만 전장 손상(_ScarStrength)을 약하게 해, 교전이 잦은 교차로에서 아스팔트가 흙색으로 바뀌어
    /// 흰 차선만 떠 보이던 문제를 없앤다. 깊이 오프셋으로 바닥에 묻히지 않게 한다.
    /// </summary>
    static Material RoadMaterial() {
      if (_road == null && GroundMaterial() != null) {
        _road = new Material(GroundMaterial()) { name = "RoadVertexColor" };
        _road.SetFloat("_ScarStrength", VisualProfile.Current.FieldLook ? 0.18f : 1f);
        _road.SetFloat("_GridStrength", 0f);
        _road.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 10;
        _asphaltNormal = Resources.Load<Texture2D>(AsphaltNormalPath);
        if (_asphaltNormal != null) _road.SetTexture("_AsphaltNormalMap", _asphaltNormal);
      }
      RefreshSurfaceDetail();
      return _road;
    }

    static Material GroundMaterial() {
      if (_ground == null) {
        var shader = Shader.Find("FactoryWars/KenneyLit");
        if (shader == null) {
          return null;
        }
        _ground = new Material(shader) { name = "GroundVertexColor" };
        _ground.SetFloat("_UseVertexColor", 1f);
        _ground.SetFloat("_Desaturate", 0f);
        _ground.SetFloat("_Brightness", 1f);
        _ground.SetFloat("_AoStrength", 0f);
        _ground.SetFloat("_GradientStrength", 0f);
        // 항목 36 시각 교정: 단색 플라스틱 느낌을 없애는 은은한 명암 노이즈와, 밝은 회색 포장면에만 2m 타일 줄눈.
        _ground.SetFloat("_NoiseStrength", VisualProfile.Current.UrbanDecor ? 0.1f : 0.05f);
        _ground.SetFloat("_NoiseScale", VisualProfile.Current.UrbanDecor ? 0.09f : 0.05f);
        _ground.SetFloat("_GridStrength", VisualProfile.Current.UrbanDecor && VisualProfile.Current.FieldLook == false ? 0.16f : 0f);
        _ground.SetFloat("_GridSize", 2f);
        _ground.SetFloat("_ScarStrength", 1f);
      }
      RefreshSurfaceDetail();
      return _ground;
    }

    static Material DesertGroundMaterial() {
      if (_desertGround == null) {
        var normal = Resources.Load<Texture2D>("FactoryWarsBundle/SandNormal");
        if (normal == null) return GroundMaterial();
        _desertGround = new Material(GroundMaterial()) { name = "DesertSandVertexColor" };
        // The ground shader and road shader share the same world-space detail normal slot.
        _desertGround.SetTexture("_AsphaltNormalMap", normal);
        _desertGround.SetFloat("_GridStrength", 0f);
      }
      RefreshSurfaceDetail();
      return _desertGround;
    }

    public static void RefreshSurfaceDetail() {
      var relief = GraphicsQuality.Current == QualityTier.Low ? 0f : VisualProfile.Current.SurfaceRelief;
      if (_ground != null) _ground.SetFloat("_SurfaceRelief", relief);
      if (_desertGround != null) {
        _desertGround.SetFloat("_SurfaceRelief", relief);
        _desertGround.SetFloat("_AsphaltNormalStrength", Mathf.Min(relief, 0.35f));
      }
      if (_road != null) {
        _road.SetFloat("_SurfaceRelief", relief * 0.6f);
        _road.SetFloat("_AsphaltNormalStrength", _asphaltNormal != null ? Mathf.Min(relief * 1.8f, 0.55f) : 0f);
      }
    }

    static GameObject MeshObject(Transform parent, string name, Mesh mesh, bool casts) {
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(parent, false);
      item.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = item.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = ArtLook.NeutralMaterial;
      renderer.shadowCastingMode = casts ? ShadowCastingMode.On : ShadowCastingMode.Off;
      return item;
    }

    void BuildStatic() {
      var biome = TerrainKit.BiomeOf(_map);
      UrbanDecor.ApplyWaterBiome(_map);
      var ground = new MeshKit { Shading = MeshKit.ShadeMode.Ground };
      TerrainKit.Ground(ground, _map, 3);
      var groundMesh = ground.ToMesh("world_ground");
      var groundObject = MeshObject(_root, "Ground", groundMesh, false);
      if (VisualProfile.Current.GroundVertexColors && GroundMaterial() != null) {
        // 정점 색 바닥(항목 25-1): 정점 위치마다 색을 골라 부드럽게 섞는다. 머티리얼은 하나를 같이 쓴다.
        var vertices = groundMesh.vertices;
        var colors = new Color[vertices.Length];
        var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        for (var v = 0; v < vertices.Length; v++) {
          var tint = TerrainKit.GroundTint(_map, vertices[v].x, vertices[v].z, 3);
          colors[v] = linear ? tint.linear : tint;
        }
        groundMesh.colors = colors;
        groundObject.GetComponent<MeshRenderer>().sharedMaterial =
          biome == TerrainKit.Biome.Desert ? DesertGroundMaterial() : GroundMaterial();
      }
      // 도로는 따로 그려 바닥과 같은 머티리얼(정점 색 + 전장 손상)을 쓴다(항목 27 K-2: 균열·패임·그을음).
      if (GroundMaterial() != null) {
        var roads = new MeshKit();
        Dressing.RoadMask = _map;
        TerrainKit.Roads(roads, _map);
        UrbanDecor.Crosswalks(roads, _map);
        UrbanDecor.Plazas(roads, _map);
        UrbanDecor.Sidewalks(roads, _map);
        UrbanDecor.QuayApron(roads, _map);
        UrbanDecor.BridgeLinks(roads, _map);
        if (roads.TriangleCount > 0) {
          var roadMesh = roads.ToMesh("world_roads");
          var uvs = roadMesh.uv;
          var roadColors = new Color[uvs.Length];
          var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
          for (var v = 0; v < uvs.Length; v++) {
            var swatch = (Swatch)Mathf.Clamp(Mathf.FloorToInt(uvs[v].x * ArtPalette.Size), 0, ArtPalette.CellCount - 1);
            Color tint;
            if (biome == TerrainKit.Biome.Desert) {
              tint = swatch == Swatch.Asphalt ? new Color(0.62f, 0.51f, 0.36f)
                : swatch == Swatch.Concrete || swatch == Swatch.ConcreteDark ? new Color(0.79f, 0.69f, 0.51f)
                : swatch == Swatch.RockDark ? new Color(0.72f, 0.58f, 0.39f)
                : swatch == Swatch.LaneWhite ? new Color(0.95f, 0.90f, 0.78f)
                : ArtPalette.ColorOf(swatch);
            } else if (biome == TerrainKit.Biome.Urban) {
              tint = swatch == Swatch.Asphalt ? new Color(0.22f, 0.24f, 0.28f)
                : swatch == Swatch.Concrete || swatch == Swatch.ConcreteDark ? new Color(0.50f, 0.54f, 0.59f)
                : ArtPalette.ColorOf(swatch);
            } else {
              tint = swatch == Swatch.Concrete || swatch == Swatch.ConcreteDark ? VisualProfile.Current.GroundPlaza
                : swatch == Swatch.Asphalt && VisualProfile.Current.RoadColor.a > 0f ? VisualProfile.Current.RoadColor : ArtPalette.ColorOf(swatch);
            }
            var color = linear ? tint.linear : tint;
            color.a = swatch == Swatch.Asphalt && biome != TerrainKit.Biome.Desert ? 1f : 0f;
            roadColors[v] = color;
          }
          roadMesh.colors = roadColors;
          roadMesh.RecalculateBounds();
          MeshObject(_root, "Roads", roadMesh, false).GetComponent<MeshRenderer>().sharedMaterial = RoadMaterial();
        }
      }
      var features = new MeshKit { Shading = MeshKit.ShadeMode.Ground };
      // 항목 36: 시가지 모드에서는 물 면을 따로 모아 물 셰이더로 그린다(우리 셰이더가 없으면 지금처럼 한 메쉬).
      var water = VisualProfile.Current.UrbanDecor && UrbanDecor.WaterMaterial() != null ? new MeshKit() : null;
      Dressing.Begin();
      Dressing.RoadMask = _map;
      TerrainKit.Lots.Clear();
      TerrainKit.Features(features, _map, GroundMaterial() == null, water);
      Dressing.End("world");
      if (water != null && water.TriangleCount > 0) {
        var waterMesh = water.ToMesh("world_water");
        var uvs = waterMesh.uv;
        var waterColors = new Color[uvs.Length];
        for (var v = 0; v < uvs.Length; v++) {
          var swatch = (Swatch)Mathf.Clamp(Mathf.FloorToInt(uvs[v].x * ArtPalette.Size), 0, ArtPalette.CellCount - 1);
          waterColors[v] = swatch == Swatch.White ? new Color(1f, 0f, 0f, 1f) : new Color(0f, 0f, 0f, 1f);
        }
        waterMesh.colors = waterColors;
        MeshObject(_root, "Water", waterMesh, false).GetComponent<MeshRenderer>().sharedMaterial = UrbanDecor.WaterMaterial();
      }
      // 다도해·해협: 수면 아래 연속 바다 기저판(WaterBase)을 깔아 만·라군·외해·교차 수로에 구멍/이음새가 전혀 없도록 보장한다.
      if ((biome == TerrainKit.Biome.Archipelago || biome == TerrainKit.Biome.Strait) && UrbanDecor.WaterMaterial() != null) {
        var extent = biome == TerrainKit.Biome.Archipelago ? 540f : 260f;
        var wy = TerrainKit.WaterLevel - 0.04f;
        var baseMesh = new Mesh { name = "world_water_base" };
        baseMesh.vertices = new[] {
          new Vector3(-extent, wy, -extent), new Vector3(extent, wy, -extent),
          new Vector3(extent, wy, extent), new Vector3(-extent, wy, extent),
        };
        baseMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        baseMesh.colors = new[] { new Color(0f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 1f) };
        baseMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        baseMesh.RecalculateBounds();
        MeshObject(_root, "WaterBase", baseMesh, false).GetComponent<MeshRenderer>().sharedMaterial = UrbanDecor.WaterMaterial();
      }
      Dressing.Begin();
      UrbanDecor.Build(_map);
      Dressing.End("urban");
      BuildBiomeProps(biome);
      Dressing.Begin();
      Scatter.Fill(_map, ClearOfBiomeProps);
      Dressing.End("scatter");
      if (features.TriangleCount > 0) {
        MeshObject(_root, "Features", features.ToMesh("world_features"), true);
      }
      if (VisualProfile.Current.NoBuildAlways == false) {
        var marks = new MeshKit();
        TerrainKit.NoBuildMarks(marks, _map);
        if (marks.TriangleCount > 0) {
          _noBuild = MeshObject(_root, "NoBuild", marks.ToMesh("world_nobuild"), false);
          for (var z = 0; _map.NoBuildZones != null && z < _map.NoBuildZones.Length; z++) {
            var center = _map.NoBuildZones[z].Center.ToUnityVector3();
            var ind = DressingSpawn.Place(DressKind.BuildIndicator, _noBuild.transform, center + Vector3.up * 0.04f, z * 90f, 8100 + z);
            if (ind != null) {
              ind.name = "BuildIndicator";
            }
          }
          _noBuild.SetActive(false);
        }
      }
      foreach (var shape in _map.Bridges ?? new MapShape[0]) {
        var rect = TerrainKit.Extent(shape);
        var holder = new GameObject("BridgeSlot");
        holder.transform.SetParent(_root, false);
        holder.transform.localPosition = new Vector3(rect.center.x, 0, rect.center.y);
        var span = Mathf.Max(rect.width, rect.height);
        var width = Mathf.Min(rect.width, rect.height);
        var longStrait = span > 20f;
        var themedBridge = NewBundleArt.UsesBridges(biome);
        var yaw = longStrait ? (rect.width >= rect.height ? 90f : 0f) : (rect.width < rect.height ? 90f : 0f);
        // Base crossings traverse the short axis of the rectangular gap in the canal.
        if (themedBridge && longStrait == false) (span, width) = (width, span);
        holder.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        GameObject intactGo;
        if (longStrait || themedBridge) {
          // 해협 54m 장대 전략 교량: 전체 교각·상판·아스팔트 차선·중앙선·양측 콘크리트 난간을 빈틈없이 연결한다.
          intactGo = new GameObject(themedBridge ? "BundleBridgeDeck" : "StraitBridgeDeck", typeof(MeshFilter), typeof(MeshRenderer));
          intactGo.transform.SetParent(holder.transform, false);
          var sk = new MeshKit();
          sk.Paint(Swatch.ConcreteDark).Box(new Vector3(0f, -0.18f, 0f), new Vector3(width + 0.8f, 0.55f, span + 2.4f));
          sk.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.14f, 0f), new Vector3(width - 0.8f, 0.10f, span + 2.2f));
          if (themedBridge == false) {
            sk.Paint(Swatch.Concrete).Box(new Vector3(-(width * 0.5f + 0.1f), 0.52f, 0f), new Vector3(0.65f, 0.95f, span + 2.2f));
            sk.Paint(Swatch.Concrete).Box(new Vector3(width * 0.5f + 0.1f, 0.52f, 0f), new Vector3(0.65f, 0.95f, span + 2.2f));
          }
          sk.Paint(Swatch.LaneWhite).Box(new Vector3(-(width * 0.5f - 0.85f), 0.21f, 0f), new Vector3(0.22f, 0.02f, span + 1.8f));
          sk.Paint(Swatch.LaneWhite).Box(new Vector3(width * 0.5f - 0.85f, 0.21f, 0f), new Vector3(0.22f, 0.02f, span + 1.8f));
          sk.Paint(Swatch.LaneYellow);
          for (var z = -span * 0.48f; z < span * 0.48f; z += 5.5f) {
            sk.Box(new Vector3(0f, 0.215f, z + 1.3f), new Vector3(0.24f, 0.02f, 2.8f));
          }
          if (themedBridge) {
            NewBundleArt.AddBridge(intactGo.transform, span, width, longStrait);
          } else {
            sk.Paint(Swatch.MetalDark);
            for (var z = -span * 0.42f; z <= span * 0.42f + 0.1f; z += span * 0.21f) {
              sk.Box(new Vector3(-(width * 0.5f + 0.15f), 1.65f, z), new Vector3(0.32f, 2.2f, 0.32f));
              sk.Box(new Vector3(width * 0.5f + 0.15f, 1.65f, z), new Vector3(0.32f, 2.2f, 0.32f));
            }
          }
          intactGo.GetComponent<MeshFilter>().sharedMesh = sk.ToMesh("strait_bridge");
          intactGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
        } else {
          var view = ArtView.Create(_catalog, "bridge", holder.transform, 0);
          // 29: 갈색 나무판자 다리를 city-kit-roads의 화이트/라이트그레이 난간 교량 FBX로 교체
          if (Dressing.Collecting && Dressing.Has(DressKind.BridgeDeck)) {
            var dressed = DressingSpawn.Place(DressKind.BridgeDeck, view.transform, Vector3.zero, 0f, 9800);
            if (dressed != null) {
              foreach (var r in view.GetComponentsInChildren<Renderer>()) {
                if (r.transform.IsChildOf(dressed.transform) == false) {
                  r.enabled = false;
                }
              }
            }
          }
          intactGo = view.gameObject;
        }

        // 파괴된 교량 양단 교각/잔해.
        var broken = new GameObject("BrokenBridge", typeof(MeshFilter), typeof(MeshRenderer));
        broken.transform.SetParent(holder.transform, false);
        var bkit = new MeshKit();
        bkit.Paint(Swatch.ConcreteDark);
        bkit.Box(new Vector3(0, 0.25f, -span * 0.42f), new Vector3(width, 0.7f, 1.8f));
        bkit.Box(new Vector3(0, 0.25f, span * 0.42f), new Vector3(width, 0.7f, 1.8f));
        bkit.Paint(Swatch.DirtDark);
        bkit.Box(new Vector3(-0.4f, 0.1f, -span * 0.34f), new Vector3(1.5f, 0.4f, 1.4f));
        bkit.Box(new Vector3(0.4f, 0.1f, span * 0.34f), new Vector3(1.5f, 0.4f, 1.4f));
        broken.GetComponent<MeshFilter>().sharedMesh = bkit.ToMesh("broken_bridge");
        broken.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
        broken.SetActive(false);

        // 공병 가교(폰툰 철판 + 부표).
        var pontoon = new GameObject("PontoonBridge", typeof(MeshFilter), typeof(MeshRenderer));
        pontoon.transform.SetParent(holder.transform, false);
        var pkit = new MeshKit();
        pkit.Paint(Swatch.MetalDark).Box(new Vector3(0, 0.18f, 0), new Vector3(width * 0.9f, 0.22f, span));
        pkit.Paint(Swatch.OliveDark);
        for (var p = -2; p <= 2; p++) {
          pkit.Box(new Vector3(0, -0.1f, p * (span * 0.18f)), new Vector3(width * 1.05f, 0.4f, 1.1f));
        }
        pkit.Paint(Swatch.Hazard);
        pkit.Box(new Vector3(-width * 0.42f, 0.34f, 0), new Vector3(0.18f, 0.14f, span));
        pkit.Box(new Vector3(width * 0.42f, 0.34f, 0), new Vector3(0.18f, 0.14f, span));
        pontoon.GetComponent<MeshFilter>().sharedMesh = pkit.ToMesh("pontoon_bridge");
        pontoon.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
        pontoon.SetActive(false);

        _bridges.Add(new BridgeVisual { Intact = intactGo, Broken = broken, Pontoon = pontoon, State = 0 });
      }
    }

    readonly List<(GameObject Object, GroundFootprint Footprint)> _biomeProps = new List<(GameObject, GroundFootprint)>();

    public IEnumerable<GroundFootprint> BiomePropFootprints {
      get {
        foreach (var item in _biomeProps) yield return item.Footprint;
      }
    }

    bool ClearOfBiomeProps(GroundFootprint footprint) => _biomeProps.TrueForAll(item => footprint.Overlaps(item.Footprint, 0.4f) == false);

    public void ClearBiomeProps(System.Predicate<GroundFootprint> overlaps) {
      for (var i = _biomeProps.Count - 1; i >= 0; i--) {
        var item = _biomeProps[i];
        if (overlaps(item.Footprint) == false) continue;
        item.Object.SetActive(false);
        Object.Destroy(item.Object);
        _biomeProps.RemoveAt(i);
      }
    }

    /// <summary>바이옴별 Synty 소품을 이미 배치된 도로·건물·갓길 소품과 겹치지 않는 곳에 놓는다.</summary>
    void BuildBiomeProps(TerrainKit.Biome biome) {
      if (biome == TerrainKit.Biome.Standard) {
        return;
      }
      var holder = new GameObject($"BiomeProps_{biome}").transform;
      holder.SetParent(_root, false);
      GameObject LoadFx(string fxName) => VisualCatalog.TryFilled(VisualCatalog.FxId(fxName), out var s) ? s.Prefab : null;
      var measured = new Dictionary<GameObject, Bounds>();
      bool Place(GameObject prefab, Vector3 spot, float yaw, float scale) {
        if (prefab == null) return false;
        if (measured.TryGetValue(prefab, out var bounds) == false) {
          bounds = VisualCatalog.LocalBounds(prefab);
          measured[prefab] = bounds;
        }
        var rotation = Quaternion.Euler(0f, yaw, 0f);
        var footprint = new GroundFootprint(spot + rotation * (bounds.center * scale), bounds.extents * scale, rotation);
        if (UrbanDecor.Fits(_map, footprint) == false || ClearOfBiomeProps(footprint) == false) return false;
        var go = Object.Instantiate(prefab, spot, rotation, holder);
        go.transform.localScale = Vector3.one * scale;
        _biomeProps.Add((go, footprint));
        return true;
      }

      if (biome == TerrainKit.Biome.Desert) {
        var ridge = NewBundleArt.Prefab("SandRidge");
        var canyon = NewBundleArt.Prefab("CanyonRock");
        var boulder = NewBundleArt.Prefab("DesertBoulder");
        var dunes = new[] { ridge ?? LoadFx("desert_dune_1"), LoadFx("desert_dune_2"), ridge ?? LoadFx("desert_dune_3") };
        var cacti = new[] { LoadFx("desert_cactus_1"), LoadFx("desert_cactus_2"), LoadFx("desert_cactus_3") };
        var rocks = new[] { boulder ?? LoadFx("desert_rock_1"), canyon ?? LoadFx("desert_rock_2"), LoadFx("desert_rock_3"), canyon ?? LoadFx("desert_rock_arch"), LoadFx("desert_ruin_1") };
        var outposts = new[] { LoadFx("desert_outpost_1"), LoadFx("desert_outpost_2"), LoadFx("desert_outpost_3") };
        var deadTree = LoadFx("desert_tree");
        var oasis = LoadFx("desert_oasis");
        var idx = 0;
        var placed = 0;
        for (var x = _map.BoundsMin.X.AsFloat + 10f; x <= _map.BoundsMax.X.AsFloat - 10f; x += 15f) {
          for (var z = _map.BoundsMin.Y.AsFloat + 10f; z <= _map.BoundsMax.Y.AsFloat - 10f; z += 15f) {
            idx++;
            var jx = (MeshKit.Hash(idx, 11) - 0.5f) * 10f;
            var jz = (MeshKit.Hash(idx, 13) - 0.5f) * 10f;
            var spot = new Vector3(x + jx, 0f, z + jz);
            if (UrbanDecor.Allowed(_map, spot) == false || Dressing.IsOnAnyRoad(spot, 5.2f) || Dressing.InTraderClear(spot)) {
              continue;
            }
            var roll = MeshKit.Hash(idx, 17);
            GameObject prefab = null;
            var scale = 1f;
            if (roll < 0.14f && Dressing.IsOnAnyRoad(spot, 15f) == false) {
              prefab = dunes[idx % dunes.Length];
              scale = 0.45f + MeshKit.Hash(idx, 19) * 0.35f;
            } else if (roll < 0.22f && Dressing.IsOnAnyRoad(spot, 9.5f) == false) {
              prefab = outposts[idx % outposts.Length];
              scale = 0.85f;
            } else if (roll < 0.56f) {
              prefab = cacti[idx % cacti.Length];
              scale = 1.45f + MeshKit.Hash(idx, 19) * 0.85f;
            } else if (roll < 0.80f) {
              prefab = rocks[idx % rocks.Length];
              scale = 0.65f + MeshKit.Hash(idx, 19) * 0.55f;
            } else if (roll < 0.92f && deadTree != null) {
              prefab = deadTree;
              scale = 0.85f + MeshKit.Hash(idx, 19) * 0.45f;
            } else if (roll < 0.96f && oasis != null && spot.magnitude > 52f && Dressing.IsOnAnyRoad(spot, 12f) == false) {
              prefab = oasis;
              scale = 0.42f;
            }
            if (prefab != null && (prefab == ridge || prefab == canyon || prefab == boulder)) {
              var bounds = NewBundleArt.BoundsOf(prefab);
              var width = prefab == boulder ? 4.5f : 10f;
              scale *= width / Mathf.Max(0.01f, bounds.size.x, bounds.size.z);
            }
            if (Place(prefab, spot, MeshKit.Hash(idx, 23) * 360f, scale) == false) continue;
            placed++;
            if (placed >= 240) return;
          }
        }
        return;
      }

      if (biome == TerrainKit.Biome.Archipelago || biome == TerrainKit.Biome.Strait) {
        var wh = LoadFx("waterfront_warehouse");
        var crane = LoadFx("waterfront_crane");
        var cont = LoadFx("waterfront_container");
        var pipes = LoadFx("waterfront_pipes");
        var waterfrontProps = new[] {
          (wh, 0.58f), (crane, 0.32f), (cont, 0.92f), (pipes, 0.95f),
        };
        var paths = TerrainKit.RiverPaths(_map);
        var idx = 0;
        var placed = 0;
        for (var x = _map.BoundsMin.X.AsFloat + 16f; x <= _map.BoundsMax.X.AsFloat - 16f; x += 22f) {
          for (var z = _map.BoundsMin.Y.AsFloat + 16f; z <= _map.BoundsMax.Y.AsFloat - 16f; z += 22f) {
            idx++;
            var spot = new Vector3(x, 0f, z);
            if (biome == TerrainKit.Biome.Strait && Mathf.Abs(spot.x) < 34f) continue;
            var riverDist = TerrainKit.RiverDistance(paths, new Vector2(spot.x, spot.z));
            // 해안/해협 연안 육지(물가 8~24m 안쪽)에만 산업 항만·부두 창고·크레인 배치(수상·도로 침범 0건).
            if (riverDist < 8.5f || riverDist > 26f) continue;
            if (UrbanDecor.Allowed(_map, spot) == false || Dressing.IsOnAnyRoad(spot, 7.0f) || Dressing.InTraderClear(spot)) continue;
            var p2 = new Vector2(spot.x, spot.z);
            var blocked = false;
            for (var h = 0; _map.HighGrounds != null && h < _map.HighGrounds.Length; h++) {
              var hc = _map.HighGrounds[h].Center;
              var hr = _map.HighGrounds[h].Radius.AsFloat + 8f;
              if ((new Vector2(hc.X.AsFloat, hc.Z.AsFloat) - p2).sqrMagnitude < hr * hr) { blocked = true; break; }
            }
            if (blocked) continue;
            var (prefab, sc) = waterfrontProps[idx % waterfrontProps.Length];
            var yaw = Mathf.FloorToInt(MeshKit.Hash(idx, 53) * 4f) * 90f;
            if (Place(prefab, spot, yaw, sc) == false) continue;
            placed++;
            if (placed >= 32) return;
          }
        }
        return;
      }

      if (biome == TerrainKit.Biome.Urban) {
        var blds = new[] {
          LoadFx("urban_bld_1"), LoadFx("urban_bld_2"), LoadFx("urban_bld_3"),
          LoadFx("urban_bld_4"), LoadFx("urban_bld_5"), LoadFx("urban_bld_6"),
        };
        var idx = 0;
        var placed = 0;
        for (var x = _map.BoundsMin.X.AsFloat + 12f; x <= _map.BoundsMax.X.AsFloat - 12f; x += 13.5f) {
          for (var z = _map.BoundsMin.Y.AsFloat + 12f; z <= _map.BoundsMax.Y.AsFloat - 12f; z += 13.5f) {
            idx++;
            var spot = new Vector3(x + (MeshKit.Hash(idx, 31) - 0.5f) * 3.5f, 0f, z + (MeshKit.Hash(idx, 33) - 0.5f) * 3.5f);
            if (UrbanDecor.Allowed(_map, spot) == false || Dressing.IsOnAnyRoad(spot, 6.2f) || Dressing.InTraderClear(spot)) {
              continue;
            }
            var onHighGround = false;
            var p2 = new Vector2(spot.x, spot.z);
            for (var h = 0; _map.HighGrounds != null && h < _map.HighGrounds.Length; h++) {
              var hc = _map.HighGrounds[h].Center;
              var hr = _map.HighGrounds[h].Radius.AsFloat + 9.5f;
              if ((new Vector2(hc.X.AsFloat, hc.Z.AsFloat) - p2).sqrMagnitude < hr * hr) {
                onHighGround = true;
                break;
              }
            }
            if (onHighGround) continue;
            var tooClose = false;
            for (var c = 0; _map.CityBuildings != null && c < _map.CityBuildings.Length; c++) {
              var cc = _map.CityBuildings[c].Shape.Center;
              if ((new Vector2(cc.X.AsFloat, cc.Z.AsFloat) - p2).sqrMagnitude < 10.2f * 10.2f) {
                tooClose = true;
                break;
              }
            }
            if (tooClose) continue;
            var prefab = blds[idx % blds.Length];
            var yaw = Mathf.FloorToInt(MeshKit.Hash(idx, 37) * 4f) * 90f;
            var sc = 0.78f + MeshKit.Hash(idx, 41) * 0.28f;
            if (Place(prefab, spot, yaw, sc) == false) continue;
            placed++;
            if (placed >= 150) return;
          }
        }
      }
    }

    /// <summary>바이옴별 태양광·환경광·배경색·안개 분위기를 적용해 5개 맵의 첫인상을 1초 만에 구분되게 한다.</summary>
    public static void ApplyBiomeAtmosphere(MapSpec map, Camera camera) {
      if (map == null) {
        return;
      }
      var biome = TerrainKit.BiomeOf(map);
      if (biome == TerrainKit.Biome.Standard) {
        return;
      }
      var sun = RenderSettings.sun;
      if (sun == null) {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.InstanceID)) {
          if (l.type == LightType.Directional) {
            sun = l;
            break;
          }
        }
      }
      switch (biome) {
        case TerrainKit.Biome.Desert:
          if (sun != null) {
            sun.color = new Color(1f, 0.87f, 0.64f);
            sun.intensity = 1.42f;
            sun.transform.rotation = Quaternion.Euler(46f, 38f, 0f);
          }
          RenderSettings.ambientSkyColor = new Color(0.92f, 0.81f, 0.62f);
          RenderSettings.ambientEquatorColor = new Color(0.84f, 0.69f, 0.46f);
          RenderSettings.ambientGroundColor = new Color(0.62f, 0.47f, 0.28f);
          if (camera != null) camera.backgroundColor = new Color(0.76f, 0.62f, 0.41f);
          break;
        case TerrainKit.Biome.Urban:
          if (sun != null) {
            sun.color = new Color(0.95f, 0.94f, 0.92f);
            sun.intensity = 1.12f;
            sun.transform.rotation = Quaternion.Euler(54f, 25f, 0f);
          }
          RenderSettings.ambientSkyColor = new Color(0.56f, 0.58f, 0.61f);
          RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.46f);
          RenderSettings.ambientGroundColor = new Color(0.26f, 0.27f, 0.29f);
          if (camera != null) camera.backgroundColor = new Color(0.18f, 0.19f, 0.21f);
          break;
        case TerrainKit.Biome.Archipelago:
          if (sun != null) {
            sun.color = new Color(1f, 0.98f, 0.90f);
            sun.intensity = 1.34f;
            sun.transform.rotation = Quaternion.Euler(52f, 32f, 0f);
          }
          RenderSettings.ambientSkyColor = new Color(0.56f, 0.84f, 0.94f);
          RenderSettings.ambientEquatorColor = new Color(0.42f, 0.72f, 0.84f);
          RenderSettings.ambientGroundColor = new Color(0.28f, 0.52f, 0.48f);
          if (camera != null) {
            camera.backgroundColor = new Color(0.05f, 0.49f, 0.66f);
            camera.farClipPlane = 1200f;
          }
          break;
        case TerrainKit.Biome.Strait:
          if (sun != null) {
            sun.color = new Color(0.88f, 0.94f, 1f);
            sun.intensity = 1.18f;
            sun.transform.rotation = Quaternion.Euler(48f, 42f, 0f);
          }
          RenderSettings.ambientSkyColor = new Color(0.54f, 0.67f, 0.80f);
          RenderSettings.ambientEquatorColor = new Color(0.39f, 0.49f, 0.60f);
          RenderSettings.ambientGroundColor = new Color(0.24f, 0.31f, 0.38f);
          if (camera != null) {
            camera.backgroundColor = new Color(0.04f, 0.22f, 0.42f);
            camera.farClipPlane = 1200f;
          }
          break;
      }
      var profile = VisualProfile.Current;
      var weight = profile.BiomeLightingWeight;
      if (sun != null) {
        sun.color = Color.Lerp(profile.SunColor, sun.color, weight);
        sun.intensity = Mathf.Lerp(profile.SunIntensity, sun.intensity, weight);
      }
      RenderSettings.ambientSkyColor = Color.Lerp(profile.AmbientSky, RenderSettings.ambientSkyColor, weight);
      RenderSettings.ambientEquatorColor = Color.Lerp(profile.AmbientEquator, RenderSettings.ambientEquatorColor, weight);
      RenderSettings.ambientGroundColor = Color.Lerp(profile.AmbientGround, RenderSettings.ambientGroundColor, weight);
      DynamicGI.UpdateEnvironment();
    }

    public void UpdateBridges(Frame frame) {
      if (frame == null || _bridges.Count == 0) {
        return;
      }
      var bridges = frame.Filter<Quantum.Bridge>();
      while (bridges.NextUnsafe(out _, out var bridge)) {
        if (bridge->Index < 0 || bridge->Index >= _bridges.Count) {
          continue;
        }
        var visual = _bridges[bridge->Index];
        var next = bridge->Destroyed ? 1 : (bridge->Pontoon ? 2 : 0);
        if (visual.State == next) {
          continue;
        }
        visual.State = next;
        visual.Intact.SetActive(next == 0);
        visual.Broken.SetActive(next == 1);
        visual.Pontoon.SetActive(next == 2);
      }
    }

    /// <summary>임시 도형을 끈다. NavMesh를 굽는 데 쓴 물체라 지우지 않는다.</summary>
    public static void HideBakedScene() {
      var names = new HashSet<string> { "Terrain", "Ground", "Water", "Hill", "Canal", "River", "City", "Rock", "Bridge", "WaterGate", "Channel", "Fall", "Wood", "Iron" };
      foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)) {
        if (names.Contains(go.name) && (go.transform.root.name != "Stage" && go.transform.root.name != "Canvas")) {
          foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) {
            renderer.enabled = false;
          }
        }
      }
    }

    /// <summary>갤러리와 같은 빛을 쓴다.</summary>
    public static void ApplyLighting(Camera camera) {
      var profile = VisualProfile.Current;
      RenderSettings.ambientMode = AmbientMode.Trilight;
      RenderSettings.ambientSkyColor = profile.AmbientSky;
      RenderSettings.ambientEquatorColor = profile.AmbientEquator;
      RenderSettings.ambientGroundColor = profile.AmbientGround;
      // 실행 중에 환경광 색을 바꾸면 환경 프로브를 다시 계산해야 반영된다(항목 27 K-0).
      DynamicGI.UpdateEnvironment();
      RenderSettings.fog = false;
      var sun = RenderSettings.sun;
      if (sun == null) {
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.InstanceID)) {
          if (light.type == LightType.Directional) {
            sun = light;
            break;
          }
        }
      }
      if (sun == null) {
        sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
      }
      RenderSettings.sun = sun;
      sun.color = profile.SunColor;
      sun.intensity = profile.SunIntensity;
      sun.shadows = GraphicsQuality.Current == QualityTier.Low ? LightShadows.Hard : LightShadows.Soft;
      sun.shadowStrength = profile.ShadowStrength;
      sun.transform.rotation = Quaternion.Euler(profile.SunElevation, profile.SunAzimuth, 0);
      // 맵 밖 처리: 안개는 멀리 있는 경계 밖을 어둡게 감싼다.
      RenderSettings.fog = profile.Outside == OutsideMode.Fog;
      RenderSettings.fogMode = FogMode.ExponentialSquared;
      RenderSettings.fogDensity = 0.0022f;
      RenderSettings.fogColor = new Color(0.22f, 0.24f, 0.26f);
      if (camera != null) {
        camera.clearFlags = CameraClearFlags.SolidColor;
        // 맵 밖은 어둡게 끊는다. 안개면 안개 색과 맞춘다.
        camera.backgroundColor = profile.Outside == OutsideMode.Fog ? RenderSettings.fogColor : profile.BackgroundColor;
        camera.allowHDR = true;
        camera.farClipPlane = 700f;
      }
    }

    int NodeIndex(ResourceNode* node) {
      var nodes = _map.ResourceNodes;
      for (var i = 0; nodes != null && i < nodes.Length; i++) {
        if (nodes[i].Shape.Center == node->Shape.Center) {
          return i;
        }
      }
      return 0;
    }

    void AddNode(EntityRef entity, ResourceNode* node) {
      var index = NodeIndex(node);
      var center = new Vector3(node->Shape.Center.X.AsFloat, 0, node->Shape.Center.Z.AsFloat);
      if (node->Kind == ResourceKind.Iron) {
        var view = ArtView.Create(_catalog, "iron_mine", _root, 0);
        view.transform.position = center;
        view.Model.transform.localScale = Vector3.one * (node->Shape.Radius.AsFloat / MineModelRadius);
        view.SetYaw(index * 57f);
        _crystals[entity] = view.Model.transform.Find("Crystals").gameObject;
        _mineRoots[entity] = view.gameObject;
        // 고철·광산 설비 느낌: 둘레에 호퍼·고철 하치장(skip-rocks)·크레인·작업 차량(Kenney, 판정 없음 — 항목 19C·19D).
        // 야전 룩: 중앙 거점 안의 광산은 둘레 설비(컨테이너·호퍼 등)를 두지 않아 보병이 설 자리를 비운다.
        if (VisualProfile.Current.FieldLook && center.magnitude < 14f) {
          return;
        }
        Dressing.Begin();
        var radius = node->Shape.Radius.AsFloat;
        // 작업 차량(삼각형 2,700개)은 성능 예산 때문에 컨테이너로 바꿨다(항목 25-1).
        DressKind[] kinds = { DressKind.Hopper, DressKind.ScrapSkip, DressKind.Crane, DressKind.Container };
        var occupied = new List<GroundFootprint>();
        var mine = new Vector2(center.x, center.z);
        for (var i = 0; i < kinds.Length; i++) {
          var angle = (index * 1.7f + i * (Mathf.PI * 2f / kinds.Length)) % (Mathf.PI * 2f);
          var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
          var yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
          var spot = center + direction * (radius + 2.3f + MeshKit.Hash(index, 40 + i) * 0.9f);
          Dressing.Add(kinds[i], spot, index * 97 + i, yaw: yaw, fits: footprint => {
            if (footprint.Overlaps(mine, radius + 0.4f) || UrbanDecor.Fits(_map, footprint) == false
                || ClearOfBiomeProps(footprint) == false || occupied.Exists(other => footprint.Overlaps(other, 0.4f))) return false;
            occupied.Add(footprint);
            return true;
          });
        }
        Dressing.End($"iron{index}");
        return;
      }
      var item = MeshObject(_root, "Forest", null, true);
      item.transform.SetPositionAndRotation(center, Quaternion.Euler(0, node->Shape.Yaw.AsFloat, 0));
      var amount = _map.ResourceNodes != null && index < _map.ResourceNodes.Length ? _map.ResourceNodes[index].Amount.AsFloat : node->Remaining.AsFloat;
      _forests[entity] = new Forest { Shape = node->Shape, Seed = 11 + index * 7, Amount = Mathf.Max(amount, 1f), Filter = item.GetComponent<MeshFilter>() };
    }

    void UpdateForest(Forest forest, float remaining, float clearHalfExtent) {
      var step = Mathf.CeilToInt(Mathf.Clamp01(remaining / forest.Amount) * ForestSteps);
      if (step == forest.Step && clearHalfExtent == forest.ClearHalfExtent) {
        return;
      }
      forest.Step = step;
      forest.ClearHalfExtent = clearHalfExtent;
      var kit = new MeshKit { Shading = MeshKit.ShadeMode.Object };
      // Kenney 나무가 있으면 나무는 인스턴싱 그룹으로 가고 kit에는 바닥 판만 남는다. 채집 단계가 바뀌면 그룹을 바꿔 끼운다.
      var toWorld = forest.Filter.transform.localToWorldMatrix;
      Dressing.Begin();
      if (NewBundleArt.HasWoodland(_map)) {
        NewBundleArt.AddWoodland(_map, forest.Shape, forest.Seed, step / (float)ForestSteps, clearHalfExtent, forest.ScarBand >= 2);
      } else if (forest.Shape.IsCircle) {
        ForestKit.Grove(kit, forest.Shape.Radius.AsFloat, forest.Seed, step / (float)ForestSteps, toWorld, clearHalfExtent);
      } else {
        ForestKit.Forest(kit, forest.Shape.HalfLength.AsFloat, forest.Shape.HalfWidth.AsFloat, forest.Seed, step / (float)ForestSteps, toWorld, clearHalfExtent);
      }
      Dressing.End($"forest{forest.Seed}");
      if (forest.Filter.sharedMesh != null) {
        Object.Destroy(forest.Filter.sharedMesh);
      }
      forest.Filter.sharedMesh = kit.ToMesh("forest");
    }

    /// <summary>경고등 깜빡임 주기. 남은 시간이 10초 이하면 빠르다.</summary>
    public static float BlinkPeriod(float remainingSeconds) => remainingSeconds <= FastBlinkFrom ? FastBlink : SlowBlink;

    int NearestBase(Vector3 position) {
      var best = 0;
      var bestDistance = float.MaxValue;
      for (var i = 0; _map.BaseAreas != null && i < _map.BaseAreas.Length; i++) {
        var center = _map.BaseAreas[i].Center.ToUnityVector3();
        var distance = (center - position).sqrMagnitude;
        if (distance < bestDistance) {
          best = i;
          bestDistance = distance;
        }
      }
      return best;
    }

    /// <summary>다리마다 울타리 하나. 빈 본진 자리의 다리는 시작부터 열려 있으므로 울타리를 세우지 않는다.</summary>
    void BuildBarriers(Frame frame) {
      for (var i = 0; _map.Bridges != null && i < _map.Bridges.Length; i++) {
        var ownerSeat = NearestBase(_map.Bridges[i].Center.ToUnityVector3());
        var owner = Quantum.FactoryWars.FactoryWarsUtil.PlayerAtSeat(frame, ownerSeat);
        if (owner < 0) {
          continue;
        }
        var body = new MeshKit();
        var lamps = new MeshKit();
        var pieces = TerrainKit.BarrierPieces(_map, i);
        var dust = new List<Vector3>();
        foreach (var piece in pieces) {
          TerrainKit.Fence(body, lamps, piece);
          dust.Add((piece.From + piece.To) / 2);
        }
        var item = MeshObject(_root, "Barrier", body.ToMesh("barrier"), true);
        var state = owner < frame.PlayerCount ? frame.Global->Players.GetPointer(owner) : null;
        if (state != null && state->Active) {
          item.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.Team(state->ColorIndex, true);
        }
        var light = MeshObject(item.transform, "Lamps", lamps.ToMesh("barrier_lamps"), false);
        // 29: 7분 휴전 빨간 가시 울타리를 깔끔한 블록형 바리케이드/게이트 FBX로 교체
        if (Dressing.Collecting && Dressing.Has(DressKind.Barrier)) {
          for (var p = 0; p < pieces.Count; p++) {
            var mid = (pieces[p].From + pieces[p].To) * 0.5f;
            var dir = pieces[p].To - pieces[p].From;
            var yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            DressingSpawn.Place(DressKind.Barrier, item.transform, mid, yaw, 8600 + i * 31 + p);
          }
          item.GetComponent<MeshRenderer>().enabled = false;
        }
        _barriers.Add(new Barrier { Root = item, Lamps = light.GetComponent<MeshRenderer>(), Dust = dust });
      }
    }

    /// <summary>수문 영역(MapSpec.WaterGates)마다 조립 프리팹을 세운다. 칸(`dress/WaterGate`)이 비었거나 시가지 장식이 꺼져 있으면 아무것도 세우지 않는다.</summary>
    void BuildGates(Frame frame) {
      _gatesBuilt = true;
      var gates = _map.WaterGates;
      var slot = VisualCatalog.Current != null ? VisualCatalog.Current.Get(VisualCatalog.DressId(DressKind.WaterGate)) : null;
      if (gates == null || gates.Length == 0 || slot == null || slot.Prefab == null || VisualProfile.Current.UrbanDecor == false) {
        return;
      }
      var seats = Mathf.Max(1, _map.BaseAreas != null ? _map.BaseAreas.Length : 1);
      var perSeat = Mathf.Max(1, gates.Length / seats);
      var profile = VisualProfile.Current;
      for (var i = 0; i < gates.Length; i++) {
        var shape = gates[i];
        var holder = new GameObject("WaterGate" + i).transform;
        holder.SetParent(_root, false);
        var along = shape.HalfLength >= shape.HalfWidth;
        // 문이 가로지르는 폭 = 사각형의 긴 변(물길을 가로지르는 쪽).
        var span = 2f * (along ? shape.HalfLength : shape.HalfWidth).AsFloat;
        holder.position = new Vector3(shape.Center.X.AsFloat, 0f, shape.Center.Z.AsFloat);
        holder.rotation = Quaternion.Euler(0f, shape.Yaw.AsFloat + (along ? 0f : 90f), 0f);
        var model = Object.Instantiate(slot.Prefab, holder);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        var rig = model.GetComponent<WaterGateRig>();
        if (rig == null) {
          Object.Destroy(holder.gameObject);
          continue;
        }
        rig.Layout(span, profile.GatePostHeight, profile.GateDoorHeight);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) {
          if (renderer is ParticleSystemRenderer == false) {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
          }
        }
        _gates.Add(new GateView { Rig = rig, Seat = i / perSeat });
      }
    }

    /// <summary>수문은 울타리(방어 울타리)와 같이 열린다: 휴전이 끝났거나 그 본진 자리가 비어 있으면 열림. 시뮬레이션 상태만 읽는다.</summary>
    void UpdateGates(Frame frame, float deltaTime) {
      if (_gatesBuilt == false) {
        BuildGates(frame);
      }
      var truceOpen = (bool)frame.Global->BarrierOpen;
      var seconds = Mathf.Max(0.1f, VisualProfile.Current.GateDropSeconds);
      foreach (var gate in _gates) {
        var open = truceOpen || Quantum.FactoryWars.FactoryWarsUtil.PlayerAtSeat(frame, gate.Seat) < 0;
        if (open && gate.Drop <= 0f && gate.OpenFor <= 0f && truceOpen == false) {
          // 처음부터 비어 있는 자리: 연출 없이 열린 채로 시작.
          gate.Drop = 1f;
          gate.Rig.SetDrop(1f);
          continue;
        }
        if (open == false) {
          continue;
        }
        if (gate.Drop < 1f) {
          if (gate.Drop <= 0f) {
            gate.Rig.PlayFoam();
          }
          gate.Drop = Mathf.MoveTowards(gate.Drop, 1f, deltaTime / seconds);
          gate.Rig.SetDrop(gate.Drop);
        } else {
          gate.OpenFor += deltaTime;
          if (gate.OpenFor > 4f) {
            gate.Rig.StopFoam();
          }
        }
      }
    }

    void UpdateBarriers(Frame frame, float deltaTime) {
      var open = (bool)frame.Global->BarrierOpen;
      if (_barrierDrop < 0f) {
        BuildBarriers(frame);
        _barrierDrop = open ? 1f : 0f;
        if (open) {
          foreach (var barrier in _barriers) {
            barrier.Root.SetActive(false);
          }
        }
      }
      if (open == false) {
        var remaining = (frame.Global->TruceEndFrame - frame.Number) / (float)frame.SessionConfig.UpdateFPS;
        var period = BlinkPeriod(remaining);
        var lit = Mathf.Repeat(Time.time, period) < period / 2;
        foreach (var barrier in _barriers) {
          barrier.Lamps.enabled = lit;
        }
        return;
      }
      if (_barrierDrop >= 1f) {
        return;
      }
      if (_barrierDrop <= 0f) {
        foreach (var barrier in _barriers) {
          foreach (var point in barrier.Dust) {
            Fx?.GroundDust(point);
          }
        }
      }
      _barrierDrop = Mathf.MoveTowards(_barrierDrop, 1f, deltaTime / BarrierSeconds);
      var eased = _barrierDrop * _barrierDrop * (3f - 2f * _barrierDrop);
      foreach (var barrier in _barriers) {
        barrier.Root.transform.localPosition = Vector3.down * (TerrainKit.FenceHeight * eased);
        if (_barrierDrop >= 1f) {
          barrier.Root.SetActive(false);
        }
      }
    }

    /// <summary>고갈되면 결정만 사라진다. 바위는 남아 건설 검사와 맞는다.</summary>
    public static void ShowCrystals(GameObject crystals, float remaining) {
      var alive = remaining > 0f;
      if (crystals.activeSelf != alive) {
        crystals.SetActive(alive);
      }
    }

    public void Update(Frame frame, float deltaTime, PlayerRef viewer = default) {
      if (frame == null) {
        return;
      }
      UpdateBarriers(frame, deltaTime);
      UpdateGates(frame, deltaTime);
      CheckForestScars();
      _mountedNodes.Clear();
      var buildings = frame.Filter<Building>();
      while (buildings.NextUnsafe(out var facility, out var building)) {
        if (building->ExtractNode != EntityRef.None && building->Health > Photon.Deterministic.FP._0
            && (viewer.IsValid == false || FactoryWarsUtil.IsVisibleTo(frame, viewer, facility))) {
          _mountedNodes[building->ExtractNode] = frame.FindAsset(building->Spec).FootprintRadius.AsFloat;
        }
      }
      var nodes = frame.Filter<ResourceNode>();
      while (nodes.NextUnsafe(out var entity, out var node)) {
        var mounted = _mountedNodes.TryGetValue(entity, out var clearHalfExtent);
        if (_forests.ContainsKey(entity) == false && _crystals.ContainsKey(entity) == false) {
          AddNode(entity, node);
        }
        if (_forests.TryGetValue(entity, out var forest)) {
          UpdateForest(forest, node->Remaining.AsFloat, clearHalfExtent);
        } else if (_crystals.TryGetValue(entity, out var crystals)) {
          var mineRoot = _mineRoots[entity];
          if (mineRoot.activeSelf == mounted) {
            mineRoot.SetActive(mounted == false);
            var dressing = DressingRenderer.Active;
            if (dressing != null) {
              var group = $"iron{NodeIndex(node)}";
              if (mounted) {
                _coveredMineDressing[entity] = new List<Dressing.Item>(dressing.Group(group));
                dressing.Set(group, new List<Dressing.Item>());
              } else if (_coveredMineDressing.TryGetValue(entity, out var items)) {
                dressing.Set(group, items);
                _coveredMineDressing.Remove(entity);
              }
            }
          }
          ShowCrystals(crystals, node->Remaining.AsFloat);
        }
      }
    }
  }
}
