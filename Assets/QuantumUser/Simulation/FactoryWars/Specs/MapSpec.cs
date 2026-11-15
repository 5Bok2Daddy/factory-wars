namespace Quantum {
  using System;
  using Photon.Deterministic;

  [Serializable]
  public struct ResourceNodePlacement {
    public ResourceKind Kind;
    public FP Amount;
    public MapShape Shape;
  }

  /// <summary>지상 통과와 건설을 막는 지형. Unity NavMesh에 구워진다.</summary>
  [Serializable]
  public struct TerrainFeature {
    public TerrainKind Kind;
    public MapShape Shape;
  }

  /// <summary>점령지. 공격 대상이 아니고, 반경 안의 지상 전투 유닛으로 점령한다.</summary>
  [Serializable]
  public struct CaptureSitePlacement {
    public CaptureKind Kind;
    public FPVector3 Center;
    public FP Radius;
  }

  /// <summary>뒷길 틈을 막는 바리케이드. 틈은 NavMesh 영역 Region으로 구워져 있고 경기 시작 때 닫힌다.</summary>
  [Serializable]
  public struct BarricadePlacement {
    public MapShape Shape;
    public string Region;
  }

  /// <summary>보급 상자 자리. Seat가 0 이상이면 그 자리의 본진 상자, -1이면 투하 후보지다. Group이 같은 투하 후보지는 대칭 쌍이다.</summary>
  [Serializable]
  public struct CrateSpot {
    public FPVector3 Position;
    public int Seat;
    public int Group;
  }

  /// <summary>
  /// 도시 건물 하나. 바닥은 NavMesh 영역 Region으로 구워져 처음에 닫힌다. 쓰러지는 건물은 낙하 구역 FallRegion이 따로 있고 처음에 열려 있다.
  /// Kind는 GameRulesSpec.CityKinds의 번호다.
  /// </summary>
  [Serializable]
  public struct CityBuildingPlacement {
    public MapShape Shape;
    public int Kind;
    public bool Garrison;
    public bool Shortcut;
    public bool Topples;
    public MapShape FallZone;
    public string Region;
    public string FallRegion;
  }

  /// <summary>AI가 확장을 고르는 자리. SeatMask는 이 자리에 인접한 플레이어 자리의 비트다.</summary>
  [Serializable]
  public struct ExpansionSite {
    public ExpansionKind Kind;
    public FPVector3 Position;
    public int SeatMask;
  }

  /// <summary>맵 하나의 크기·시작 위치·자원 노드. Map.UserAsset으로 맵과 연결한다.</summary>
  public partial class MapSpec : AssetObject {
    public int MaxPlayers;
    /// <summary>항목 35 맵 프리셋: 테마 이름, 보급 낙하 간격 배율, 보급 보상 배율(0 이하는 1로 본다). 광산 양은 광산 데이터에 이미 반영한다.</summary>
    public string Theme;
    public FP SupplyIntervalMultiplier = 1;
    public FP SupplyRewardMultiplier = 1;
    /// <summary>항목 37 기상: 첫 발동 시각, 지속, 주기(초). 0이면 기상 없음. 기상 중 기본 시야 배율.</summary>
    /// <summary>중앙 암시장(NPC 무기상)이 있는지와 그 자리.</summary>
    public bool HasTrader;
    public FPVector3 TraderPosition;
    public int WeatherFirstSeconds;
    public int WeatherDurationSeconds;
    public int WeatherPeriodSeconds;
    public FP WeatherSightMultiplier = 1;
    /// <summary>카메라 이동 범위·미니맵용 경계(XZ).</summary>
    public FPVector2 BoundsMin;
    public FPVector2 BoundsMax;
    public FPVector3[] StartPositions;
    public ResourceCost StartingResources;
    public int StartingEngineers;
    /// <summary>시작 기지 단계(항목 31). 0이면 1. 시험 맵은 3으로 모든 건물·유닛을 바로 쓴다.</summary>
    public int StartingTier;
    /// <summary>true면 진영의 시작 전투 유닛도 배치한다. 테스트 맵에서만 쓴다.</summary>
    public bool SpawnFactionUnits;
    public ResourceNodePlacement[] ResourceNodes;
    public TerrainFeature[] Terrain;
    /// <summary>길목이 건물로 막히지 않게 건설을 금지하는 구역. 협곡 안과 본진 입구.</summary>
    public MapShape[] NoBuildZones;
    /// <summary>본진 입구의 다리. 입구의 울타리는 NavMesh 영역 하나로 구워 한 번에 연다.</summary>
    public MapShape[] Bridges;
    /// <summary>강줄기의 조각을 물길 순서대로 담는다. 다리 밑 조각도 있다. 막는 것은 Terrain의 River 항목이고 이 배열은 화면용이다.</summary>
    public MapShape[] River;
    /// <summary>자리별 본진 안쪽. 휴전 중에는 여기서만 채집·건설한다. 비어 있으면 제한이 없다.</summary>
    public MapShape[] BaseAreas;
    public CaptureSitePlacement[] CaptureSites;
    public BarricadePlacement[] Barricades;
    public CrateSpot[] CrateSpots;
    /// <summary>절벽에 둘러싸인 평지. 위에 있는지는 이 원 안인지로 판정한다.</summary>
    public MapShape[] HighGrounds;
    /// <summary>고지대로 오르는 비탈. 통과할 수 있다.</summary>
    public MapShape[] Ramps;
    /// <summary>도로의 짧은 조각들. 모양의 합집합이 도로다.</summary>
    public MapShape[] Roads;
    /// <summary>얕은 개울. 통과할 수 있다.</summary>
    public MapShape[] Fords;
    public ExpansionSite[] Expansions;
    /// <summary>도로·여울 판정 격자. 유닛마다 프레임마다 모양을 검사하지 않도록 맵을 구울 때 칠한다. 0 일반, 1 도로, 2 여울.</summary>
    public byte[] GroundGrid;
    public int GroundGridSize;
    public FP GroundGridCell;

    public CityBuildingPlacement[] CityBuildings;

    /// <summary>항목 24 외곽 수로: 땅(Bounds) 바깥을 두르는 물길 띠. 배만 다닌다. 너비는 ChannelWidth다.</summary>
    public MapShape[] Channel;
    public FP ChannelWidth;
    /// <summary>본진 운하와 외곽 수로가 만나는 수문. 자리 s의 수문은 수상 NavMesh 영역 "{WaterGateRegion}{s}"이다. 자리마다 2개, 자리 순서대로다.</summary>
    public MapShape[] WaterGates;
    /// <summary>GroundGrid와 같은 칸의 도시 건물 번호 + 1. 0이면 건물이 없다. 붕괴한 칸은 잔해라 느리다.</summary>
    public short[] CityGrid;

    public const int GroundRoad = 1;
    public const int GroundFord = 2;

    public int GroundAt(FPVector3 position) {
      if (GroundGrid == null || GroundGrid.Length == 0) {
        return 0;
      }
      var x = FPMath.FloorToInt((position.X - BoundsMin.X) / GroundGridCell);
      var z = FPMath.FloorToInt((position.Z - BoundsMin.Y) / GroundGridCell);
      return x < 0 || z < 0 || x >= GroundGridSize || z >= GroundGridSize ? 0 : GroundGrid[z * GroundGridSize + x];
    }

    /// <summary>위치의 도시 건물 번호. 없으면 -1이다.</summary>
    public int CityAt(FPVector3 position) {
      if (CityGrid == null || CityGrid.Length == 0) {
        return -1;
      }
      var x = FPMath.FloorToInt((position.X - BoundsMin.X) / GroundGridCell);
      var z = FPMath.FloorToInt((position.Z - BoundsMin.Y) / GroundGridCell);
      return x < 0 || z < 0 || x >= GroundGridSize || z >= GroundGridSize ? -1 : CityGrid[z * GroundGridSize + x] - 1;
    }

    /// <summary>위치가 들어 있는 고지대 번호. 없으면 -1이다.</summary>
    public int HighGroundAt(FPVector3 position) {
      for (var i = 0; HighGrounds != null && i < HighGrounds.Length; i++) {
        if (HighGrounds[i].Distance(position) <= FP._0) {
          return i;
        }
      }
      return -1;
    }
  }
}
