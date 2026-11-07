namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// 한 사분면의 강을 짧은 회전 사각 조각의 띠로 만든다. 맵을 굽는 에디터에서만 돌고 결과는 MapSpec에 저장된다.
  /// 기준선은 본진 사각형의 경계다. 강가가 언제나 기준선 바깥이라 본진을 침범하지 않는다.
  /// </summary>
  public static class RiverBuilder {
    public const float MinHalfWidth = 3f;
    public const float MaxHalfWidth = 5f;
    // 지시는 10m다. 경계 조각이 폭 평균을 내므로 2m 더 잡아야 10m 안쪽이 정확히 직선이다.
    public const float StraightReach = 12f;
    const float BlendReach = 10f;
    const float Spacing = 2.4f;
    const float BridgeGap = 7.2f;
    const float Overlap = 1.2f;
    const float CornerRadius = 8f;
    // 화면의 땅 끝(맵 경계 밖 80m)을 넘어가야 강 끝이 보이지 않는다.
    const float Overrun = 90f;
    const float CornerBulge = 3f;

    const float CornerMargin = 5f;
    const int SmoothPasses = 8;

    /// <summary>강폭 절반만큼 본진 사각형에서 떨어지게 민다. 본진은 corner의 왼쪽 아래 사분면이다.</summary>
    static Vector2 KeepOutOfBase(Vector2 center, Vector2 corner, float half) {
      var v = center - corner;
      if (v.x >= 0f && v.y >= 0f) {
        return v.magnitude >= half ? center : corner + v.normalized * half;
      }
      if (v.y < 0f) {
        center.x = Mathf.Max(center.x, corner.x + half);
      } else {
        center.y = Mathf.Max(center.y, corner.y + half);
      }
      return center;
    }

    public struct Piece {
      public Vector2 Center;
      public float HalfLength;
      public float HalfWidth;
      public float Yaw;
      public bool UnderBridge;
    }

    /// <param name="corner">본진 사각형에서 맵 중심 쪽 모서리.</param>
    /// <param name="edge">맵 경계 좌표. 음수다.</param>
    /// <param name="bridges">다리 중심. 앞은 x가 모서리 쪽 기둥, 뒤는 z가 모서리 쪽 기둥이다.</param>
    public static List<Piece> Build(Vector2 corner, float edge, Vector2[] bridges, float amplitude, int seed) {
      var start = edge - Overrun;
      var lineA = corner.y - start;
      var arc = CornerRadius * Mathf.PI / 2;
      var total = lineA + arc + (corner.x - start);

      Vector2 Reference(float s, out Vector2 normal) {
        if (s <= lineA) {
          normal = Vector2.right;
          return new Vector2(corner.x, start + s);
        }
        if (s <= lineA + arc) {
          var angle = (s - lineA) / CornerRadius;
          normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
          return corner;
        }
        normal = Vector2.up;
        return new Vector2(corner.x - (s - lineA - arc), corner.y);
      }

      // 다리 위치를 기준선 거리로 바꾼다.
      var bridgeAt = bridges.Select(b => Mathf.Abs(b.x - corner.x) < Mathf.Abs(b.y - corner.y) + 0.01f && b.x > b.y
        ? b.y - start
        : lineA + arc + (corner.x - b.x)).ToArray();

      float Bend(float s) {
        var distance = bridgeAt.Min(b => Mathf.Abs(s - b));
        var t = Mathf.Clamp01((distance - StraightReach) / BlendReach);
        return t * t * (3f - 2f * t);
      }
      // 고정 시드 사인파에 정수 해시 노이즈를 더한다.
      float Noise(float s) {
        var cell = Mathf.FloorToInt(s / 7f);
        var t = s / 7f - cell;
        return Mathf.Lerp(FactoryWars.View.MeshKit.Hash(seed, cell), FactoryWars.View.MeshKit.Hash(seed, cell + 1), t * t * (3f - 2f * t));
      }
      // 본진 모서리를 돌 때는 바깥으로 불룩하게 돌아 곡률 반경을 키운다. 대신 폭과 굽이를 줄여 근처 자원을 덮지 않는다.
      float Corner(float s) {
        var middle = lineA + arc / 2;
        var reach = Mathf.Max(bridgeAt.Min(b => Mathf.Abs(middle - b)) - StraightReach, 1f);
        var t = Mathf.Clamp01(1f - Mathf.Abs(s - middle) / reach);
        return t * t * (3f - 2f * t);
      }
      float Half(float s) => MinHalfWidth + Bend(s) * (1f - Corner(s)) * (MaxHalfWidth - MinHalfWidth) * (0.5f + 0.3f * Mathf.Sin(s * 0.33f + seed) + 0.2f * (Noise(s + 31f) * 2f - 1f));
      float Offset(float s) => Bend(s) * (1f - 0.85f * Corner(s)) * amplitude * (0.5f + 0.32f * Mathf.Sin(s * 0.19f + seed * 0.7f) + 0.18f * (Noise(s) * 2f - 1f)) + Corner(s) * CornerBulge;
      Vector2 Center(float s) {
        var point = Reference(s, out var normal);
        return point + normal * (Half(s) + Offset(s));
      }

      var stations = new List<float>();
      for (var s = 0f; s < total; s += Spacing) {
        if (bridgeAt.All(b => Mathf.Abs(Mathf.Abs(s - b) - BridgeGap) > Spacing * 0.4f && Mathf.Abs(s - b) > BridgeGap)) {
          stations.Add(s);
        }
      }
      foreach (var bridge in bridgeAt) {
        stations.Add(bridge - BridgeGap);
        stations.Add(bridge + BridgeGap);
      }
      // 모서리는 조각을 촘촘하게 둔다.
      for (var s = lineA - CornerMargin; s < lineA + arc + CornerMargin; s += Spacing) {
        var fine = s + Spacing / 2;
        if (bridgeAt.All(b => Mathf.Abs(fine - b) > BridgeGap + Spacing * 0.4f)) {
          stations.Add(fine);
        }
      }
      stations.Add(total);
      stations.Sort();

      // 다리 앞뒤를 뺀 곳의 중심선을 다듬어 꺾임을 없앤다.
      var centers = stations.Select(Center).ToArray();
      for (var pass = 0; pass < SmoothPasses; pass++) {
        var previous = (Vector2[])centers.Clone();
        for (var i = 1; i + 1 < centers.Length; i++) {
          var smooth = (previous[i - 1] + previous[i + 1]) / 2;
          centers[i] = KeepOutOfBase(Vector2.Lerp(previous[i], smooth, 0.5f * Bend(stations[i])), corner, Half(stations[i]));
        }
      }

      var pieces = new List<Piece>();
      for (var i = 0; i + 1 < stations.Count; i++) {
        var a = centers[i];
        var b = centers[i + 1];
        var delta = b - a;
        var middle = (stations[i] + stations[i + 1]) / 2;
        pieces.Add(new Piece {
          Center = (a + b) / 2,
          HalfLength = delta.magnitude / 2 + Overlap,
          HalfWidth = (Half(stations[i]) + Half(stations[i + 1])) / 2,
          // MapShape의 긴 변은 (cos, -sin) 방향이다.
          Yaw = Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg,
          UnderBridge = bridgeAt.Any(s => Mathf.Abs(middle - s) < BridgeGap),
        });
      }
      return pieces;
    }
  }
}
