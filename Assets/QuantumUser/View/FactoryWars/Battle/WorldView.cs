namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// MapSpec에서 바닥·지형·자원·다리를 만들고 시뮬레이션 상태를 따라 바꾼다. 상태는 읽기만 한다.
  /// </summary>
  public unsafe class WorldView {
    const int ForestSteps = 10;
    const float BridgeRaised = -78f;
    const float BridgeSeconds = 1.8f;
    const float BridgeY = -0.54f;
    const float MineModelRadius = 3f;

    class Forest {
      public MapShape Shape;
      public int Seed;
      public float Amount;
      public int Step = -1;
      public MeshFilter Filter;
    }

    readonly Transform _root;
    readonly MapSpec _map;
    readonly ViewCatalog _catalog;
    readonly Dictionary<EntityRef, Forest> _forests = new Dictionary<EntityRef, Forest>();
    readonly Dictionary<EntityRef, GameObject> _crystals = new Dictionary<EntityRef, GameObject>();
    readonly List<Transform> _decks = new List<Transform>();
    float _bridgeOpen = -1f;

    public float BridgeOpen => _bridgeOpen;
    public int ForestCount => _forests.Count;
    public int MineCount => _crystals.Count;

    public WorldView(Transform parent, MapSpec map, ViewCatalog catalog) {
      _root = new GameObject("World").transform;
      _root.SetParent(parent, false);
      _map = map;
      _catalog = catalog;
      BuildStatic();
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
      var ground = new MeshKit();
      TerrainKit.Ground(ground, _map, 3);
      MeshObject(_root, "Ground", ground.ToMesh("world_ground"), false);
      var features = new MeshKit();
      TerrainKit.Features(features, _map);
      if (features.TriangleCount > 0) {
        MeshObject(_root, "Features", features.ToMesh("world_features"), true);
      }
      foreach (var shape in _map.Bridges ?? new MapShape[0]) {
        var rect = TerrainKit.Extent(shape);
        var view = ArtView.Create(_catalog, "bridge", _root, 0);
        view.transform.localPosition = new Vector3(rect.center.x, BridgeY, rect.center.y);
        // 프리팹의 다리는 Z축으로 건넌다. 사각형의 짧은 변이 건너는 방향이다.
        view.SetYaw(rect.width < rect.height ? 90f : 0f);
        _decks.Add(view.Model.transform.Find("Deck"));
      }
    }

    /// <summary>임시 도형을 끈다. NavMesh를 굽는 데 쓴 물체라 지우지 않는다.</summary>
    public static void HideBakedScene() {
      foreach (var name in new[] { "Terrain", "Ground" }) {
        var item = GameObject.Find(name);
        if (item == null) {
          continue;
        }
        foreach (var renderer in item.GetComponentsInChildren<Renderer>(true)) {
          renderer.enabled = false;
        }
      }
    }

    /// <summary>갤러리와 같은 빛을 쓴다.</summary>
    public static void ApplyLighting(Camera camera) {
      RenderSettings.ambientMode = AmbientMode.Trilight;
      RenderSettings.ambientSkyColor = new Color(0.56f, 0.59f, 0.64f);
      RenderSettings.ambientEquatorColor = new Color(0.44f, 0.44f, 0.42f);
      RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.24f);
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
      sun.color = new Color(1f, 0.96f, 0.88f);
      sun.intensity = 0.98f;
      sun.shadows = LightShadows.Soft;
      sun.transform.rotation = Quaternion.Euler(52, 20, 0);
      if (camera != null) {
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.36f, 0.42f, 0.3f);
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
        return;
      }
      var item = MeshObject(_root, "Forest", null, true);
      item.transform.SetPositionAndRotation(center, Quaternion.Euler(0, node->Shape.Yaw.AsFloat, 0));
      var amount = _map.ResourceNodes != null && index < _map.ResourceNodes.Length ? _map.ResourceNodes[index].Amount.AsFloat : node->Remaining.AsFloat;
      _forests[entity] = new Forest { Shape = node->Shape, Seed = 11 + index * 7, Amount = Mathf.Max(amount, 1f), Filter = item.GetComponent<MeshFilter>() };
    }

    static void UpdateForest(Forest forest, float remaining) {
      var step = Mathf.CeilToInt(Mathf.Clamp01(remaining / forest.Amount) * ForestSteps);
      if (step == forest.Step) {
        return;
      }
      forest.Step = step;
      var kit = new MeshKit();
      ForestKit.Forest(kit, forest.Shape.HalfLength.AsFloat, forest.Shape.HalfWidth.AsFloat, forest.Seed, step / (float)ForestSteps);
      if (forest.Filter.sharedMesh != null) {
        Object.Destroy(forest.Filter.sharedMesh);
      }
      forest.Filter.sharedMesh = kit.ToMesh("forest");
    }

    /// <summary>고갈되면 결정만 사라진다. 바위는 남아 건설 검사와 맞는다.</summary>
    public static void ShowCrystals(GameObject crystals, float remaining) {
      var alive = remaining > 0f;
      if (crystals.activeSelf != alive) {
        crystals.SetActive(alive);
      }
    }

    public void Update(Frame frame, float deltaTime) {
      if (frame == null) {
        return;
      }
      var nodes = frame.Filter<ResourceNode>();
      while (nodes.NextUnsafe(out var entity, out var node)) {
        if (_forests.TryGetValue(entity, out var forest)) {
          UpdateForest(forest, node->Remaining.AsFloat);
        } else if (_crystals.TryGetValue(entity, out var crystals)) {
          ShowCrystals(crystals, node->Remaining.AsFloat);
        } else {
          AddNode(entity, node);
        }
      }

      var open = frame.Global->BridgesOpen ? 1f : 0f;
      _bridgeOpen = _bridgeOpen < 0f ? open : Mathf.MoveTowards(_bridgeOpen, open, deltaTime / BridgeSeconds);
      var angle = Mathf.Lerp(BridgeRaised, 0f, _bridgeOpen * _bridgeOpen * (3f - 2f * _bridgeOpen));
      foreach (var deck in _decks) {
        deck.localRotation = Quaternion.Euler(angle, 0, 0);
      }
    }
  }
}
