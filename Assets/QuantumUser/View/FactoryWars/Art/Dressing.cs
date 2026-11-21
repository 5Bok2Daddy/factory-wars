namespace FactoryWars.View {
  using System.Collections.Generic;
  using System.Linq;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering;

  public readonly struct GroundFootprint {
    public readonly Vector2 Center;
    public readonly Vector2 HalfSize;
    readonly Vector2 _along;
    readonly Vector2 _across;

    public GroundFootprint(Vector3 position, Vector3 halfSize, Quaternion rotation) {
      Center = new Vector2(position.x, position.z);
      HalfSize = new Vector2(halfSize.x, halfSize.z);
      var axis = rotation * Vector3.right;
      _along = new Vector2(axis.x, axis.z);
      _across = new Vector2(-axis.z, axis.x);
    }

    float Reach(Vector2 axis) =>
      Mathf.Abs(Vector2.Dot(_along, axis)) * HalfSize.x + Mathf.Abs(Vector2.Dot(_across, axis)) * HalfSize.y;

    bool Separates(Vector2 axis, GroundFootprint other, float gap) =>
      Mathf.Abs(Vector2.Dot(other.Center - Center, axis)) >= Reach(axis) + other.Reach(axis) + gap;

    public bool Overlaps(GroundFootprint other, float gap = 0f) =>
      Separates(_along, other, gap) == false && Separates(_across, other, gap) == false
      && Separates(other._along, other, gap) == false && Separates(other._across, other, gap) == false;

    public bool Overlaps(Vector2 point, float radius) {
      var delta = point - Center;
      var u = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(delta, _along)) - HalfSize.x);
      var v = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(delta, _across)) - HalfSize.y);
      return u * u + v * v <= radius * radius;
    }

    public bool Overlaps(MapShape shape, float gap) {
      var center = shape.Center.ToUnityVector3();
      return shape.IsCircle
        ? Overlaps(new Vector2(center.x, center.z), shape.Radius.AsFloat + gap)
        : Overlaps(new GroundFootprint(center, new Vector3(shape.HalfLength.AsFloat, 0f, shape.HalfWidth.AsFloat),
          Quaternion.Euler(0f, shape.Yaw.AsFloat, 0f)), gap);
    }
  }

  /// <summary>
  /// Kenney 모델 배치(항목 19B). 화면 코드가 Begin(그룹) → Add(...) → End()로 모은 개체를 DressingRenderer가 GPU 인스턴싱으로 그린다.
  /// 맵(VisualProfile.Dressing)이 없거나 그 종류가 비어 있으면 Has가 false이고, 호출한 쪽은 절차 메쉬를 그대로 쓴다.
  /// </summary>
  public static class Dressing {
    public struct Item {
      public DressKind Kind;
      public GameObject Prefab;
      public Matrix4x4 World;
      public GroundFootprint Footprint;
      public bool Shadows;
      public bool PreserveMaterial;
      /// <summary>0~1 해시. 품질 낮음에서는 이 값이 LowKeep보다 작은 것만 남긴다.</summary>
      public float Keep;
      public float LowKeep;
      public float MediumKeep;
      /// <summary>합친 메쉬의 정점 색에 곱하는 색(절벽 단 밝기, 그을린 나무 등). 기본 흰색.</summary>
      public Color Tint;
    }

    static List<Item> _collector;

    public static KenneyAssetMap Map => VisualProfile.Current.Dressing;

    public static bool Has(DressKind kind) => Map != null && Map.Has(kind);

    public static bool Collecting => _collector != null;

    public static void AddBundle(GameObject prefab, Vector3 position, float width, int seed,
        System.Predicate<GroundFootprint> fits) {
      if (_collector == null || prefab == null) return;
      var bounds = NewBundleArt.BoundsOf(prefab);
      var factor = width / Mathf.Max(0.01f, bounds.size.x, bounds.size.z);
      var rotation = Quaternion.Euler(0f, MeshKit.Hash(seed, 3) * 360f, 0f);
      AddPrefab(prefab, position, rotation, Vector3.one * factor, fits, MeshKit.Hash(seed, 77), 0.55f, 0.85f);
    }

    public static bool AddPrefab(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale,
        System.Predicate<GroundFootprint> fits, float keep = 0f, float lowKeep = 1f, float mediumKeep = 1f) {
      if (_collector == null || prefab == null) return false;
      var bounds = NewBundleArt.BoundsOf(prefab);
      var footprint = new GroundFootprint(position, Vector3.Scale(bounds.extents, scale), rotation);
      if (fits(footprint) == false) return false;
      var pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
      _collector.Add(new Item {
        Prefab = prefab, Kind = DressKind.Conifer, Footprint = footprint, Shadows = true, PreserveMaterial = true,
        World = Matrix4x4.TRS(position, rotation, scale) * Matrix4x4.Translate(-pivot),
        Keep = keep, LowKeep = lowKeep, MediumKeep = mediumKeep, Tint = Color.white,
      });
      return true;
    }

    public static void Begin() {
      _collector = new List<Item>();
    }

    /// <summary>모은 개체를 그룹 이름으로 넘긴다. 같은 이름이면 바꿔 끼운다.</summary>
    public static void End(string group) {
      var items = _collector;
      _collector = null;
      if (items != null && DressingRenderer.Active != null) {
        DressingRenderer.Active.Set(group, items);
      }
    }

    /// <summary>
    /// 종류 하나를 놓는다. scale은 엔트리 배율에 곱하는 축별 배율이다. yaw가 null이면 엔트리 범위에서 해시로 고른다.
    /// 놓았으면 true. 모으는 중이 아니거나 종류가 비어 있으면 false다.
    /// </summary>
    public static bool Add(DressKind kind, Vector3 position, int seed, Vector3? scale = null, float? yaw = null,
        System.Predicate<GroundFootprint> fits = null) => Add(kind, position, seed, Color.white, scale, yaw, fits);

    /// <summary>틴트를 곱해 놓는다(합친 메쉬에서만 보인다).</summary>
    /// <summary>야전 룩 길 비우기: 지금 그리는 맵(WorldView가 넣는다). 길 폭 + RoadClearance 안에는 건물·소품·바위를 놓지 않는다.</summary>
    public static MapSpec RoadMask;
    public const float RoadClearance = 3.6f;
    /// <summary>검문소처럼 일부러 길 위에 두는 소품을 놓는 동안 true.</summary>
    public static bool AllowOnRoad;

    static bool Exempt(DressKind kind) => kind == DressKind.RoadProp || kind == DressKind.QuayPillar || kind == DressKind.QuayRail || kind == DressKind.QuayBollard
      || kind == DressKind.WaterGate || kind == DressKind.BridgeDeck || kind == DressKind.BridgePillar || kind == DressKind.Grass
      || kind == DressKind.RoadLight || kind == DressKind.RoadPole || kind == DressKind.RoadSign || kind == DressKind.RoadBin || kind == DressKind.RoadBusStop;

    /// <summary>모든 2차선 도로·교량 폴리곤 침범 검사(컨테이너·감시탑·방벽 도로 위 배치 절대 차단).</summary>
    public static bool IsOnAnyRoad(Vector3 position, float clearance = 3.5f) {
      if (RoadMask == null) {
        return false;
      }
      var p = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(position.x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(position.z));
      if (RoadMask.Roads != null) {
        foreach (var road in RoadMask.Roads) {
          if (road.Distance(p).AsFloat < clearance) {
            return true;
          }
        }
      }
      if (RoadMask.Bridges != null) {
        foreach (var bridge in RoadMask.Bridges) {
          if (bridge.Distance(p).AsFloat < clearance) {
            return true;
          }
        }
      }
      return false;
    }

    /// <summary>중앙 암시장 비움 반경: 이 안에는 건물·바위·잡풀 등 흩뿌림 개체를 하나도 놓지 않는다(주둔지만 남는다).</summary>
    public const float TraderClearRadius = 22f;

    public static bool InTraderClear(Vector3 position) {
      if (RoadMask == null || RoadMask.HasTrader == false) {
        return false;
      }
      var dx = position.x - RoadMask.TraderPosition.X.AsFloat;
      var dz = position.z - RoadMask.TraderPosition.Z.AsFloat;
      return dx * dx + dz * dz < TraderClearRadius * TraderClearRadius;
    }

    /// <summary>이 자리가 2차선 도로 폴리곤(+ RoadClearance) 안이라 소품·건물·컨테이너를 놓으면 안 되는가.</summary>
    public static bool OnRoad(DressKind kind, Vector3 position, float radius = 0f) {
      if (AllowOnRoad || RoadMask == null || RoadMask.Roads == null || Exempt(kind)) {
        return false;
      }
      return IsOnAnyRoad(position, RoadClearance + radius);
    }

    public static bool Add(DressKind kind, Vector3 position, int seed, Color tint, Vector3? scale = null, float? yaw = null,
        System.Predicate<GroundFootprint> fits = null) {
      if (_collector == null || Map == null || OnRoad(kind, position) || InTraderClear(position)) {
        return false;
      }
      var entry = Map.Get(kind);
      if (entry == null) {
        return false;
      }
      var (variant, rotation, size) = KenneyAssetMap.Place(entry, seed);
      if (yaw.HasValue) {
        rotation = Quaternion.Euler(0f, yaw.Value, 0f);
      }
      var footprint = new GroundFootprint(position, Vector3.Scale(variant.Size, (scale ?? Vector3.one) * size) * 0.5f, rotation);
      if (fits != null && fits(footprint) == false) {
        return false;
      }
      _collector.Add(new Item {
        Kind = kind,
        Prefab = variant.Prefab,
        World = KenneyAssetMap.Matrix(variant, position, rotation, (scale ?? Vector3.one) * size),
        Footprint = footprint,
        Shadows = entry.Shadows,
        Keep = MeshKit.Hash(seed, 77),
        LowKeep = entry.LowKeep,
        MediumKeep = entry.MediumKeep,
        Tint = tint,
      });
      return true;
    }

    /// <summary>시가지 장식용: 품질별 남김 비율을 직접 정해 놓는다(낮음·보통에서 해시가 이 값보다 크면 그리지 않는다).</summary>
    public static bool AddKept(DressKind kind, Vector3 position, int seed, float yaw, float lowKeep, float mediumKeep,
        System.Predicate<GroundFootprint> fits) {
      if (InTraderClear(position)) {
        return false;
      }
      if (_collector == null || Map == null) {
        return false;
      }
      var entry = Map.Get(kind);
      if (entry == null) {
        return false;
      }
      var (variant, rotation, size) = KenneyAssetMap.Place(entry, seed);
      rotation = Quaternion.Euler(0f, yaw, 0f);
      var footprint = new GroundFootprint(position, variant.Size * (size * 0.5f), rotation);
      if (fits != null && fits(footprint) == false) {
        return false;
      }
      _collector.Add(new Item {
        Kind = kind,
        Prefab = variant.Prefab,
        World = KenneyAssetMap.Matrix(variant, position, rotation, Vector3.one * size),
        Footprint = footprint,
        Shadows = entry.Shadows,
        Keep = MeshKit.Hash(seed, 77),
        LowKeep = lowKeep,
        MediumKeep = mediumKeep,
        Tint = Color.white,
      });
      return true;
    }

    /// <summary>정해진 칸(월드 길이 length × width, 회전 yaw)에 맞춰 놓는다. 건물 부지처럼 밖으로 튀어나오면 안 되는 곳에 쓴다.</summary>
    public static bool AddFitted(DressKind kind, Vector3 position, float yaw, float length, float width, int seed, float margin = 0.94f) {
      if (_collector == null || Map == null || OnRoad(kind, position, Mathf.Max(length, width) * 0.5f)) {
        return false;
      }
      var entry = Map.Get(kind);
      if (entry == null) {
        return false;
      }
      var variant = KenneyAssetMap.Pick(entry, MeshKit.Hash(seed, 1));
      var turned = MeshKit.Hash(seed, 2) < 0.5f;
      if (VisualProfile.Current.FieldLook) {
        variant = DressingSpawn.BestFit(entry, length, width, turned, variant);
      }
      var fit = KenneyAssetMap.FitScale(variant, length, width, turned) * margin * entry.Scale;
      if (VisualProfile.Current.FieldLook) {
        // 균일 배율만: 칸보다 크면 균일하게 줄이고(늘이지 않음), 키우는 것은 1.15배까지.
        fit = Mathf.Min(fit, 1.15f);
      }
      var rotation = Quaternion.Euler(0f, yaw + (turned ? 90f : 0f), 0f);
      _collector.Add(new Item { Kind = kind, Prefab = variant.Prefab, World = KenneyAssetMap.Matrix(variant, position, rotation, Vector3.one * fit),
        Footprint = new GroundFootprint(position, variant.Size * (fit * 0.5f), rotation), Shadows = entry.Shadows, Keep = 0f, LowKeep = 1f, MediumKeep = 1f });
      return true;
    }
  }

  /// <summary>
  /// 빈 땅 흩뿌리기(항목 19C): 풀포기·작은 바위·덤불을 5m 격자에 해시로 놓는다.
  /// 도로·다리·강·본진·건설 금지 구역·지형·자원·도시 건물·점령지 위는 피한다. 모두 통과 가능한 작은 장식이다.
  /// </summary>
  public static class Scatter {
    const float Step = 5f;

    static bool Near(MapShape[] shapes, Vector3 point, float margin) {
      if (shapes == null) {
        return false;
      }
      var p = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(point.x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(point.z));
      foreach (var shape in shapes) {
        if (shape.Distance(p).AsFloat <= margin) {
          return true;
        }
      }
      return false;
    }

    public static int Fill(MapSpec map, System.Predicate<GroundFootprint> clearOfBiome = null) {
      if (Dressing.Collecting == false || Dressing.Has(DressKind.Grass) == false) {
        return 0;
      }
      var biome = TerrainKit.BiomeOf(map);
      // 사막(초록 잡풀 0% — 사구·선인장·사막 바위는 WorldView.BuildBiomeProps에서 실제 Synty 프리팹으로 배치)과 시가전(콘크리트 도심)은 초록 들판 흩뿌리기를 건너뛴다.
      if (biome == TerrainKit.Biome.Desert || biome == TerrainKit.Biome.Urban) {
        return 0;
      }
      var urban = VisualProfile.Current.UrbanDecor;
      var paths = TerrainKit.RiverPaths(map);
      var terrain = map.Terrain?.Select(t => t.Shape).ToArray();
      var nodes = map.ResourceNodes?.Select(n => n.Shape).ToArray();
      var city = map.CityBuildings?.Select(c => c.Shape).ToArray();
      var sites = map.CaptureSites?.Select(c => new MapShape { Center = c.Center, Radius = c.Radius }).ToArray();
      var count = 0;
      var index = 0;
      for (var x = map.BoundsMin.X.AsFloat + Step / 2; x < map.BoundsMax.X.AsFloat; x += Step) {
        for (var z = map.BoundsMin.Y.AsFloat + Step / 2; z < map.BoundsMax.Y.AsFloat; z += Step) {
          index++;
          var spot = new Vector3(x + (MeshKit.Hash(index, 1) - 0.5f) * Step * 0.8f, 0f, z + (MeshKit.Hash(index, 2) - 0.5f) * Step * 0.8f);
          // 야전 룩: 넓은 들판도 길가만큼 채운다(남김 비율 0.7 → 0.945, 약 35% 증가). 모두 통과 가능한 작은 장식이다.
          if (MeshKit.Hash(index, 3) > (VisualProfile.Current.FieldLook ? 0.945f : 0.7f)) {
            continue;
          }
          if (TerrainKit.RiverDistance(paths, new Vector2(spot.x, spot.z)) < 3f || Near(map.Roads, spot, 1.5f) || Near(map.Bridges, spot, 2f)
              || Near(map.BaseAreas, spot, 0f) || Near(map.NoBuildZones, spot, 0f) || Near(terrain, spot, 0.5f) || Near(nodes, spot, 1.5f)
              || Near(city, spot, 1.5f) || Near(sites, spot, 1f) || Near(map.HighGrounds, spot, 1f) || Near(map.Fords, spot, 1f)) {
            continue;
          }
          var pick = MeshKit.Hash(index, 4);
          // 시가지: 잔디 구역(보도 바깥)에만 낮은 풀·덤불. 자갈·쓰레기는 두지 않는다.
          if (urban) {
            if (TerrainKit.IsPavedGround(map, spot.x, spot.z) && pick > 0.25f) {
              continue;
            }
            spot.y = TerrainKit.SurfaceHeight(map, spot.x, spot.z);
            // 배틀로얄 데모 씬 구성: 풀 덤불 패치가 대부분, 작은 돌·덤불·바위를 섞는다.
            // 큰 바위는 걸어 다니는 들판에 두지 않는다(판정이 없어 보병이 바위 속을 지나가 보였다). 큰 바위는 막힌 지형(바위 지형) 안에만.
            var field = pick < 0.6f ? DressKind.Grass : pick < 0.78f ? DressKind.RockSmall : DressKind.Bush;
            count += Dressing.Add(field, spot, 9000 + index, fits: Fits) ? 1 : 0;
            continue;
          }
          var kind = pick < 0.48f ? DressKind.Grass : pick < 0.6f && Dressing.Has(DressKind.GroundPatch) ? DressKind.GroundPatch : pick < 0.85f ? DressKind.RockSmall : DressKind.Bush;
          // 도시 건물 둘레(폐허 주변)는 차량 부품·타이어 같은 잔해와 길가 버려진 가구(high 전용)를 섞는다. 작아서 통과해도 어색하지 않다.
          if (Near(city, spot, 5f) && pick < 0.6f) {
            kind = pick < 0.45f || Dressing.Has(DressKind.Furniture) == false ? DressKind.Debris : DressKind.Furniture;
          }
          spot.y = TerrainKit.SurfaceHeight(map, spot.x, spot.z);
          count += Dressing.Add(kind, spot, 9000 + index, fits: Fits) ? 1 : 0;
        }
      }
      return count;

      bool Fits(GroundFootprint footprint) => UrbanDecor.ClearOfProps(footprint)
        && (clearOfBiome == null || clearOfBiome(footprint));
    }
  }

  public static class DressingSpawn {
    /// <summary>
    /// 움직이거나 단계가 바뀌는 것(도시 건물)은 인스턴싱 대신 실제 오브젝트로 만든다. 칸(length × width)에 맞추고,
    /// 높이는 targetHeight 쪽으로 0.8~1.3배까지 늘이거나 줄인다. 만들었으면 감싼 오브젝트, 아니면 null.
    /// </summary>
    public static GameObject Fitted(DressKind kind, Transform parent, float length, float width, float targetHeight, int seed) {
      var map = Dressing.Map;
      var entry = map != null ? map.Get(kind) : null;
      if (entry == null) {
        return null;
      }
      var variant = KenneyAssetMap.Pick(entry, MeshKit.Hash(seed, 1));
      var turned = MeshKit.Hash(seed, 2) < 0.5f;
      if (VisualProfile.Current.FieldLook) {
        // 야전 룩: 칸에 크기가 가장 맞는 변종을 고르고(늘이지 않음) 균일 배율 0.85~1.15로만 맞춘다. 판정 칸 크기와 무관하게 창·벽 디테일이 그대로다.
        variant = BestFit(entry, length, width, turned, variant);
      }
      var fit = KenneyAssetMap.FitScale(variant, length, width, turned) * 0.86f * entry.Scale;
      if (VisualProfile.Current.FieldLook) {
        // 균일 배율만: 칸보다 크면 균일하게 줄이고(늘이지 않음), 키우는 것은 1.15배까지.
        fit = Mathf.Min(fit, 1.15f);
      }
      if (kind == DressKind.CityTower) {
        fit = Mathf.Min(fit, 3.2f);
      }
      var stretch = targetHeight > 0f && VisualProfile.Current.FieldLook == false ? Mathf.Clamp(targetHeight / Mathf.Max(0.01f, variant.Size.y * fit), 0.8f, 1.3f) : 1f;
      var wrapper = new GameObject("Kenney").transform;
      wrapper.SetParent(parent, false);
      wrapper.localRotation = Quaternion.Euler(0f, turned ? 90f : 0f, 0f);
      wrapper.localScale = new Vector3(fit, fit * stretch, fit);
      var model = Object.Instantiate(variant.Prefab, wrapper);
      model.transform.localPosition += variant.Offset;
      foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>()) {
        renderer.sharedMaterials = KenneyLook.Resolve(renderer.sharedMaterials);
        // 품질 낮음에서는 도시 건물 그림자를 끈다(그림자 그리기 호출 절약, 항목 25-1).
        renderer.shadowCastingMode = entry.Shadows && GraphicsQuality.Current != QualityTier.Low ? ShadowCastingMode.On : ShadowCastingMode.Off;
      }
      return wrapper.gameObject;
    }

    /// <summary>칸(length×width)에 균일 배율 1에 가장 가깝게 맞는 변종.</summary>
    public static KenneyAssetMap.Variant BestFit(KenneyAssetMap.Entry entry, float length, float width, bool turned, KenneyAssetMap.Variant fallback) {
      var best = fallback;
      var bestError = float.MaxValue;
      foreach (var candidate in entry.Variants) {
        if (candidate.Prefab == null) {
          continue;
        }
        var error = Mathf.Abs(Mathf.Log(Mathf.Max(0.01f, KenneyAssetMap.FitScale(candidate, length, width, turned) * 0.86f)));
        if (error < bestError) {
          best = candidate;
          bestError = error;
        }
      }
      return best;
    }

    /// <summary>사라지거나 움직이는 것에 붙이는 Kenney 소품 하나(실제 오브젝트). localPosition은 parent 기준이다.</summary>
    public static GameObject Place(DressKind kind, Transform parent, Vector3 localPosition, float yaw, int seed,
        System.Predicate<GroundFootprint> fits = null) {
      var map = Dressing.Map;
      var entry = map != null ? map.Get(kind) : null;
      if (entry == null) {
        return null;
      }
      // 품질별 남김 비율(항목 25-1): 인스턴싱 개체와 같은 규칙을 실제 오브젝트 소품에도 적용한다(낮음·보통에서 렌더러 수를 줄인다).
      var keep = MeshKit.Hash(seed, 77);
      if ((GraphicsQuality.Current == QualityTier.Low && keep > entry.LowKeep) || (GraphicsQuality.Current == QualityTier.Medium && keep > entry.MediumKeep)) {
        return null;
      }
      var (variant, _, scale) = KenneyAssetMap.Place(entry, seed);
      var rotation = Quaternion.Euler(0f, yaw, 0f);
      if (fits != null && fits(new GroundFootprint(parent.TransformPoint(localPosition),
          Vector3.Scale(variant.Size, parent.lossyScale) * (scale * 0.5f), parent.rotation * rotation)) == false) return null;
      var wrapper = new GameObject("Kenney").transform;
      wrapper.SetParent(parent, false);
      wrapper.localPosition = localPosition;
      wrapper.localRotation = rotation;
      wrapper.localScale = Vector3.one * scale;
      var model = Object.Instantiate(variant.Prefab, wrapper);
      model.transform.localPosition += variant.Offset;
      foreach (var renderer in model.GetComponentsInChildren<Renderer>()) {
        renderer.sharedMaterials = KenneyLook.Resolve(renderer.sharedMaterials);
        renderer.shadowCastingMode = entry.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
      }
      return wrapper.gameObject;
    }

    static MaterialPropertyBlock _block;

    /// <summary>손상 단계의 그을음(공용 셰이더 _Soot). 0이면 블록을 지워 SRP Batcher로 돌아간다.</summary>
    public static void Soot(GameObject root, float amount) {
      _block ??= new MaterialPropertyBlock();
      foreach (var renderer in root.GetComponentsInChildren<Renderer>()) {
        if (amount <= 0f) {
          renderer.SetPropertyBlock(null);
          continue;
        }
        _block.Clear();
        _block.SetFloat("_Soot", amount);
        renderer.SetPropertyBlock(_block);
      }
    }
  }

  /// <summary>
  /// 모은 Kenney 개체를 그린다. 두 방식이다.
  /// - 읽기 가능한 메쉬(nature-kit): 그룹 × 120m 칸마다 한 메쉬로 합치고, 원래 머티리얼 색은 정점 색으로 굽는다. 칸 하나가 그리기 호출 하나다.
  ///   그룹(숲 하나, 지형, 경계)마다 따로 합치므로 숲이 벌목돼도 그 숲만 다시 합친다.
  /// - 나머지(색 아틀라스 키트): (메쉬, 머티리얼, 서브메쉬)별 Graphics.RenderMeshInstanced.
  /// 테스트가 코루틴에서 카메라를 직접 그리므로 LateUpdate가 아니라 Update에서 낸다.
  /// </summary>
  public class DressingRenderer : MonoBehaviour {
    public static DressingRenderer Active { get; private set; }

    const float CellSize = 120f;
    const int MaxPerBatch = 1000;

    struct Part {
      public Mesh Mesh;
      public Material[] Materials;
      public Matrix4x4 Local;
    }

    class Batch {
      public Mesh Mesh;
      public Material Material;
      public int Submesh;
      public bool Shadows;
      public Bounds Bounds;
      public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
    }

    class Merged {
      public Mesh Mesh;
      public bool Shadows;
    }

    static readonly Dictionary<GameObject, Part[]> Parts = new Dictionary<GameObject, Part[]>();
    readonly Dictionary<string, List<Dressing.Item>> _groups = new Dictionary<string, List<Dressing.Item>>();
    readonly Dictionary<string, List<Merged>> _merged = new Dictionary<string, List<Merged>>();
    readonly HashSet<string> _stale = new HashSet<string>();
    readonly List<GroundFootprint> _clearSites = new List<GroundFootprint>();
    readonly List<Batch> _batches = new List<Batch>();
    Material _vertexColor;
    bool _instancedDirty;

    public int InstanceCount { get; private set; }

    void Awake() {
      Active = this;
      // 프로필이 바뀌었을 수 있다(원본 ↔ 군용). 부품 캐시를 비운다.
      Parts.Clear();
    }

    void OnDestroy() {
      if (Active == this) {
        Active = null;
      }
      foreach (var list in _merged.Values) {
        foreach (var merged in list) {
          Destroy(merged.Mesh);
        }
      }
      if (_vertexColor != null) {
        Destroy(_vertexColor);
      }
    }

    public void Set(string group, List<Dressing.Item> items) {
      if (group.StartsWith("iron", System.StringComparison.Ordinal) && _clearSites.Count > 0)
        items.RemoveAll(item => _clearSites.Exists(site => site.Overlaps(item.Footprint)));
      _groups[group] = items;
      _stale.Add(group);
      _instancedDirty = true;
    }

    public IEnumerable<string> GroupNames => _groups.Keys;

    /// <summary>
    /// 플레이어 건물이 들어선 자리의 길가·흩뿌림 장식(컨테이너·방벽·잡풀 등)을 반경 안에서 걷어낸다. 지운 개수를 돌려준다.
    /// 지도 장식은 시작할 때 한 번 깔려 나중에 지어지는 건물을 모르므로, 건물이 생길 때 화면에서 비운다(판정과 무관).
    /// </summary>
    public int ClearAround(Vector3 center, float radius, params string[] groups) {
      var point = new Vector2(center.x, center.z);
      return ClearWhere(item => item.Footprint.Overlaps(point, radius), groups);
    }

    public int ClearSite(GroundFootprint site, params string[] groups) =>
      ClearWhere(item => item.Footprint.Overlaps(site), groups);

    public void ReserveSite(GroundFootprint site) {
      _clearSites.Add(site);
      var groups = _groups.Keys.Where(name => name == "urban" || name == "scatter" || name.StartsWith("iron", System.StringComparison.Ordinal)).ToArray();
      ClearSite(site, groups);
    }

    int ClearWhere(System.Predicate<Dressing.Item> overlaps, string[] groups) {
      var removed = 0;
      foreach (var name in groups) {
        if (_groups.TryGetValue(name, out var items) == false) {
          continue;
        }
        var kept = new List<Dressing.Item>(items.Count);
        foreach (var item in items) {
          if (overlaps(item)) {
            removed++;
            continue;
          }
          kept.Add(item);
        }
        if (kept.Count != items.Count) {
          Set(name, kept);
        }
      }
      return removed;
    }

    public IReadOnlyList<Dressing.Item> Group(string group) => _groups.TryGetValue(group, out var list) ? list : System.Array.Empty<Dressing.Item>();

    public void RemoveBlastBarriers(Vector3 point, float radius, System.Action<Dressing.Item> animate) {
      if (_groups.TryGetValue("urban", out var items) == false) return;
      var center = new Vector2(point.x, point.z);
      var removed = items.RemoveAll(item => {
        if (item.Kind != DressKind.Barrier && item.Kind != DressKind.OutpostWall && item.Kind != DressKind.RoadCover) return false;
        if (item.Footprint.Overlaps(center, radius) == false) return false;
        if (Skip(item) == false) animate(item);
        return true;
      });
      if (removed > 0) Set("urban", items);
    }

    /// <summary>품질이 바뀌면(낮음은 나무 수를 줄인다) 모두 다시 만든다.</summary>
    public void MarkDirty() {
      foreach (var group in _groups.Keys) {
        _stale.Add(group);
      }
      _instancedDirty = true;
    }

    static readonly Dictionary<Material, Material> InstancedCopies = new Dictionary<Material, Material>();

    /// <summary>인스턴싱이 켜진 머티리얼. 이미 켜져 있으면 그대로, 아니면 원본마다 복사본 하나를 만들어 같이 쓴다.</summary>
    static Material[] Instanced(Material[] sources) {
      var result = new Material[sources.Length];
      for (var i = 0; i < sources.Length; i++) {
        var source = sources[i];
        if (source == null || source.enableInstancing) {
          result[i] = source;
          continue;
        }
        if (InstancedCopies.TryGetValue(source, out var copy) == false || copy == null) {
          copy = new Material(source) { name = source.name + "_instanced", enableInstancing = true };
          InstancedCopies[source] = copy;
        }
        result[i] = copy;
      }
      return result;
    }

    static Part[] PartsOf(GameObject prefab, bool preserveMaterial = false) {
      if (Parts.TryGetValue(prefab, out var parts)) {
        return parts;
      }
      var list = new List<Part>();
      // 프리팹 에셋의 변환은 부모가 없으므로 localToWorld가 곧 루트 기준이다(루트 자신의 회전·배율 포함). 바닥 보정도 같은 기준으로 쟀다.
      foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true)) {
        var renderer = filter.GetComponent<MeshRenderer>();
        if (filter.sharedMesh == null || renderer == null) {
          continue;
        }
        var materials = KenneyLook.Resolve(renderer.sharedMaterials);
        // 읽을 수 없는 메쉬(구입한 팩)는 GPU 인스턴싱으로 그린다. 팩 머티리얼은 인스턴싱이 꺼져 있으므로 메모리 안에서 복사본을 만들어 쓴다(팩 파일은 그대로).
        if (preserveMaterial || filter.sharedMesh.isReadable == false) {
          materials = Instanced(materials);
        }
        list.Add(new Part { Mesh = filter.sharedMesh, Materials = materials, Local = filter.transform.localToWorldMatrix });
      }
      parts = list.ToArray();
      Parts[prefab] = parts;
      return parts;
    }

    static bool Skip(Dressing.Item item) => item.Prefab == null
      || (GraphicsQuality.Current == QualityTier.Low && (item.LowKeep <= 0f || item.Keep > item.LowKeep))
      || (GraphicsQuality.Current == QualityTier.Medium && (item.MediumKeep <= 0f || item.Keep > item.MediumKeep));

    /// <summary>합친 메쉬용 머티리얼: 공용 셰이더, 흰 바탕, 정점 색 사용. 다른 값은 원본 머티리얼에서 가져온다.</summary>
    Material VertexColorMaterial(Material source) {
      if (_vertexColor == null) {
        _vertexColor = new Material(source) { name = "KenneyMerged", enableInstancing = false };
        _vertexColor.SetColor("_BaseColor", Color.white);
        _vertexColor.SetTexture("_BaseMap", Texture2D.whiteTexture);
        _vertexColor.SetFloat("_UseVertexColor", 1f);
        _vertexColor.SetFloat("_Desaturate", 0f);
        _vertexColor.SetFloat("_Brightness", 1f);
        _vertexColor.SetColor("_Tint", Color.white);
      }
      return _vertexColor;
    }

    void MergeGroup(string group) {
      if (_merged.TryGetValue(group, out var old)) {
        foreach (var merged in old) {
          Destroy(merged.Mesh);
        }
      }
      var cells = new Dictionary<(int, int, bool), (List<Vector3> V, List<Vector3> N, List<Color> C, List<int> T)>();
      var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
      foreach (var item in _groups[group]) {
        if (Skip(item) || item.PreserveMaterial) {
          continue;
        }
        var position = item.World.GetColumn(3);
        var key = (Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.z / CellSize), item.Shadows);
        foreach (var part in PartsOf(item.Prefab)) {
          if (part.Mesh.isReadable == false) {
            continue;
          }
          if (cells.TryGetValue(key, out var cell) == false) {
            cell = (new List<Vector3>(), new List<Vector3>(), new List<Color>(), new List<int>());
            cells[key] = cell;
          }
          var world = item.World * part.Local;
          var normalMatrix = world.inverse.transpose;
          var vertices = part.Mesh.vertices;
          var normals = part.Mesh.normals;
          for (var s = 0; s < part.Mesh.subMeshCount && s < part.Materials.Length; s++) {
            var material = part.Materials[s];
            if (material == null) {
              continue;
            }
            VertexColorMaterial(material);
            var source = KenneyLook.Resolve(material);
            var color = (source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white) * (item.Tint.a > 0f ? item.Tint : Color.white);
            if (linear) {
              color = color.linear;
            }
            var indices = part.Mesh.GetTriangles(s);
            var remap = new Dictionary<int, int>();
            foreach (var index in indices) {
              if (remap.TryGetValue(index, out var mapped) == false) {
                mapped = cell.V.Count;
                remap[index] = mapped;
                cell.V.Add(world.MultiplyPoint3x4(vertices[index]));
                cell.N.Add(normals.Length > index ? normalMatrix.MultiplyVector(normals[index]).normalized : Vector3.up);
                cell.C.Add(color);
              }
              cell.T.Add(mapped);
            }
          }
        }
      }
      var list = new List<Merged>();
      foreach (var pair in cells) {
        if (pair.Value.T.Count == 0) {
          continue;
        }
        var mesh = new Mesh { name = $"kenney_{group}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(pair.Value.V);
        mesh.SetNormals(pair.Value.N);
        mesh.SetColors(pair.Value.C);
        mesh.SetTriangles(pair.Value.T, 0);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        list.Add(new Merged { Mesh = mesh, Shadows = pair.Key.Item3 });
      }
      _merged[group] = list;
    }

    void RebuildInstanced() {
      _instancedDirty = false;
      _batches.Clear();
      InstanceCount = 0;
      var index = new Dictionary<(Mesh, Material, int, bool, int, int, int), Batch>();
      foreach (var group in _groups.Values) {
        foreach (var item in group) {
          if (Skip(item)) {
            continue;
          }
          InstanceCount++;
          foreach (var part in PartsOf(item.Prefab, item.PreserveMaterial)) {
            if (part.Mesh.isReadable && item.PreserveMaterial == false) {
              continue;
            }
            var world = item.World * part.Local;
            for (var s = 0; s < part.Mesh.subMeshCount && s < part.Materials.Length; s++) {
              var material = part.Materials[s];
              if (material == null) {
                continue;
              }
              var page = 0;
              Batch batch;
              // 칸(60m)마다 묶음을 나눈다: RenderMeshInstanced는 묶음 전체를 한 번에 그리므로, 맵 전체를 덮는 묶음은 카메라 밖 개체도 전부 그린다.
              var cellX = Mathf.FloorToInt(world.m03 / 60f);
              var cellZ = Mathf.FloorToInt(world.m23 / 60f);
              while (true) {
                var key = (part.Mesh, material, s, item.Shadows, page, cellX, cellZ);
                if (index.TryGetValue(key, out batch) == false) {
                  batch = new Batch { Mesh = part.Mesh, Material = material, Submesh = s, Shadows = item.Shadows, Bounds = new Bounds(world.MultiplyPoint3x4(part.Mesh.bounds.center), Vector3.zero) };
                  index[key] = batch;
                  _batches.Add(batch);
                  break;
                }
                if (batch.Matrices.Count < MaxPerBatch) {
                  break;
                }
                page++;
              }
              batch.Matrices.Add(world);
              var b = part.Mesh.bounds;
              for (var c = 0; c < 8; c++) {
                batch.Bounds.Encapsulate(world.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
              }
            }
          }
        }
      }
    }

    void Refresh() {
      foreach (var group in _stale) {
        MergeGroup(group);
      }
      _stale.Clear();
      if (_instancedDirty) {
        RebuildInstanced();
      }
    }

    bool Casts(bool shadows) => shadows && GraphicsQuality.UnitShadows;

    void Update() {
      Refresh();
      foreach (var list in _merged.Values) {
        foreach (var merged in list) {
          var parameters = new RenderParams(_vertexColor) { worldBounds = merged.Mesh.bounds, shadowCastingMode = Casts(merged.Shadows) ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, layer = gameObject.layer };
          Graphics.RenderMesh(parameters, merged.Mesh, 0, Matrix4x4.identity);
        }
      }
      foreach (var batch in _batches) {
        var parameters = new RenderParams(batch.Material) { worldBounds = batch.Bounds, shadowCastingMode = Casts(batch.Shadows) ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, layer = gameObject.layer };
        Graphics.RenderMeshInstanced(parameters, batch.Mesh, batch.Submesh, batch.Matrices);
      }
    }

    /// <summary>카메라에 걸리는 묶음 수·삼각형 수·그림자 묶음 수. 비교 도구의 그리기 호출 추정에 더한다.</summary>
    public (int Batches, int Triangles, int Casters) Count(Plane[] planes) {
      Refresh();
      int batches = 0, triangles = 0, casters = 0;
      foreach (var list in _merged.Values) {
        foreach (var merged in list) {
          if (GeometryUtility.TestPlanesAABB(planes, merged.Mesh.bounds)) {
            batches++;
            triangles += (int)(merged.Mesh.GetIndexCount(0) / 3);
            casters += Casts(merged.Shadows) ? 1 : 0;
          }
        }
      }
      foreach (var batch in _batches) {
        if (GeometryUtility.TestPlanesAABB(planes, batch.Bounds) == false) {
          continue;
        }
        batches++;
        triangles += (int)(batch.Mesh.GetIndexCount(batch.Submesh) / 3) * batch.Matrices.Count;
        casters += Casts(batch.Shadows) ? 1 : 0;
      }
      return (batches, triangles, casters);
    }
  }
}
