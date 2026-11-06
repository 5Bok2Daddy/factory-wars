namespace Quantum {
  using System;
  using Photon.Deterministic;

  [Serializable]
  public struct ResourceNodePlacement {
    public FPVector3 Position;
    public FP Funds;
  }

  /// <summary>맵 하나의 크기·시작 위치·자금 노드. Map.UserAsset으로 맵과 연결한다.</summary>
  public partial class MapSpec : AssetObject {
    public int MaxPlayers;
    /// <summary>카메라 이동 범위·미니맵용 경계(XZ).</summary>
    public FPVector2 BoundsMin;
    public FPVector2 BoundsMax;
    public FPVector3[] StartPositions;
    public ResourceNodePlacement[] ResourceNodes;
  }
}
