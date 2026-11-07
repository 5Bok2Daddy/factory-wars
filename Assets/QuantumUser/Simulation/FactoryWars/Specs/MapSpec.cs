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

  /// <summary>맵 하나의 크기·시작 위치·자원 노드. Map.UserAsset으로 맵과 연결한다.</summary>
  public partial class MapSpec : AssetObject {
    public int MaxPlayers;
    /// <summary>카메라 이동 범위·미니맵용 경계(XZ).</summary>
    public FPVector2 BoundsMin;
    public FPVector2 BoundsMax;
    public FPVector3[] StartPositions;
    public ResourceCost StartingResources;
    public int StartingEngineers;
    /// <summary>true면 진영의 시작 전투 유닛도 배치한다. 테스트 맵에서만 쓴다.</summary>
    public bool SpawnFactionUnits;
    public ResourceNodePlacement[] ResourceNodes;
    public TerrainFeature[] Terrain;
    /// <summary>길목이 건물로 막히지 않게 건설을 금지하는 구역. 협곡 안과 본진 입구.</summary>
    public MapShape[] NoBuildZones;
    /// <summary>본진 입구의 다리. 전부 NavMesh 영역 하나로 구워 한 번에 연다.</summary>
    public MapShape[] Bridges;
    /// <summary>자리별 본진 안쪽. 휴전 중에는 여기서만 채집·건설한다. 비어 있으면 제한이 없다.</summary>
    public MapShape[] BaseAreas;
  }
}
