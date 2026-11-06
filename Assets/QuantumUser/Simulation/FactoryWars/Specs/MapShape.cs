namespace Quantum {
  using System;
  using Photon.Deterministic;

  /// <summary>
  /// 철광산·숲·지형의 모양. 사각형의 긴 변은 Yaw가 0일 때 X축과 나란하고,
  /// Yaw는 Unity의 Y축 회전과 같은 방향이다. 필드는 FactoryWars.qtn에 있다.
  /// </summary>
  [Serializable]
  public partial struct MapShape {
    public bool IsCircle => Radius > FP._0;

    void Axes(out FPVector2 along, out FPVector2 across) {
      var angle = Yaw * FP.Deg2Rad;
      var cos = FPMath.Cos(angle);
      var sin = FPMath.Sin(angle);
      along = new FPVector2(cos, -sin);
      across = new FPVector2(sin, cos);
    }

    /// <summary>XZ 평면에서 모양 가장자리까지의 거리. 안쪽이면 0 이하.</summary>
    public FP Distance(FPVector3 point) {
      var offset = new FPVector2(point.X - Center.X, point.Z - Center.Z);
      if (IsCircle) {
        return offset.Magnitude - Radius;
      }
      Axes(out var along, out var across);
      var x = FPMath.Abs(FPVector2.Dot(offset, along)) - HalfLength;
      var z = FPMath.Abs(FPVector2.Dot(offset, across)) - HalfWidth;
      if (x <= FP._0 && z <= FP._0) {
        return FPMath.Max(x, z);
      }
      return new FPVector2(FPMath.Max(x, FP._0), FPMath.Max(z, FP._0)).Magnitude;
    }

    /// <summary>중심에서 가장 먼 점까지의 거리 이내로 어림잡는다. 정밀 검사를 건너뛰는 데 쓴다.</summary>
    public FP Reach => IsCircle ? Radius : HalfLength + HalfWidth;

    /// <summary>작업 지점 수. 원은 둘레에 고르게, 사각형은 긴 변 양쪽에 간격을 두고 놓는다.</summary>
    public int SpotCount(GameRulesSpec rules) {
      return IsCircle ? rules.MineSpots : 2 * SpotsPerSide(rules);
    }

    int SpotsPerSide(GameRulesSpec rules) {
      return System.Math.Max(1, FPMath.FloorToInt(HalfLength * 2 / rules.ForestSpotSpacing));
    }

    public FPVector3 Spot(int index, GameRulesSpec rules) {
      if (IsCircle) {
        var angle = FP.PiTimes2 * index / rules.MineSpots;
        var distance = Radius + rules.SpotOffset;
        return Center + new FPVector3(FPMath.Cos(angle) * distance, FP._0, FPMath.Sin(angle) * distance);
      }
      Axes(out var along, out var across);
      var perSide = SpotsPerSide(rules);
      var side = index < perSide ? FP._1 : -FP._1;
      var step = HalfLength * 2 / perSide;
      var t = step * (index % perSide) + step / 2 - HalfLength;
      var s = side * (HalfWidth + rules.SpotOffset);
      return Center + new FPVector3(along.X * t + across.X * s, FP._0, along.Y * t + across.Y * s);
    }
  }
}
