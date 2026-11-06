namespace Quantum {
  using System;
  using Photon.Deterministic;

  [Serializable]
  public struct ResourceNodePlacement {
    public ResourceKind Kind;
    public FP Amount;
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
  }
}
