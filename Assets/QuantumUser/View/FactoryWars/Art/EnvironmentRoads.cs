namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;

  public static class EnvironmentRoads {
    struct RoadPiece {
      public Vector2 From;
      public Vector2 To;
      public Vector2 Along;
      public float HalfWidth;
    }

    public static void Build(MeshKit kit, MapSpec map) {
      var pieces = new List<RoadPiece>(map.Roads?.Length ?? 0);
      foreach (var shape in map.Roads ?? System.Array.Empty<MapShape>()) {
        var radians = shape.Yaw.AsFloat * Mathf.Deg2Rad;
        var forward = new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
        foreach (var (center, half) in TerrainKit.RoadPieces(map, shape)) {
          var point = new Vector2(center.x, center.z);
          var span = Mathf.Max(0.15f, half - 0.14f);
          pieces.Add(new RoadPiece { From = point - forward * span, To = point + forward * span,
            Along = forward, HalfWidth = shape.HalfWidth.AsFloat });
        }
      }
      var biome = TerrainKit.BiomeOf(map);
      for (var i = 0; i < pieces.Count; i++) {
        var piece = pieces[i];
        var (from, normalA, joinedA) = End(pieces, i, piece.From, true);
        var (to, normalB, joinedB) = End(pieces, i, piece.To, false);
        var mid = (from + to) * 0.5f;
        var service = piece.HalfWidth < 3.15f;
        var dirt = biome == TerrainKit.Biome.Desert || (!service && biome == TerrainKit.Biome.Standard
          && Mathf.Max(Mathf.Abs(mid.x), Mathf.Abs(mid.y)) > 136f);
        var half = piece.HalfWidth;
        var layer = service ? 0.113f : dirt ? 0.074f : 0.097f;
        Vector3 Ground(Vector2 p, float lift) => new Vector3(p.x, TerrainKit.SurfaceHeight(map, p.x, p.y) + lift, p.y);
        void Ribbon(float left, float right, float lift, Swatch swatch, float start = 0f, float finish = 1f) {
          var a = Vector2.Lerp(from, to, start);
          var b = Vector2.Lerp(from, to, finish);
          var na = Vector2.Lerp(normalA, normalB, start);
          var nb = Vector2.Lerp(normalA, normalB, finish);
          kit.Paint(swatch);
          kit.Quad(Ground(a + na * left, lift), Ground(b + nb * left, lift), Ground(b + nb * right, lift), Ground(a + na * right, lift), Vector3.up);
        }
        var surface = service ? Swatch.Concrete : dirt ? Swatch.Dirt : Swatch.Asphalt;
        Ribbon(-half, half, layer, surface);
        Ribbon(-half - 1.35f, -half, layer - 0.018f, Swatch.RockDark);
        Ribbon(half, half + 1.35f, layer - 0.018f, Swatch.RockDark);
        void Cap(Vector2 center, Vector2 normal, Vector2 outward) {
          kit.Paint(surface);
          var last = center - normal * half;
          for (var arc = 1; arc <= 8; arc++) {
            var angle = (float)arc / 8f * Mathf.PI;
            var next = center - normal * (Mathf.Cos(angle) * half) + outward * (Mathf.Sin(angle) * 1.1f);
            kit.Tri(Ground(center, layer), Ground(last, layer), Ground(next, layer), Vector3.up);
            last = next;
          }
        }
        if (!joinedA) Cap(from, normalA, -piece.Along);
        if (!joinedB) Cap(to, normalB, piece.Along);
        if (!Junction(pieces, piece, i, mid)) {
          if (dirt) {
            Ribbon(-1.11f, -0.89f, layer + 0.008f, Swatch.DirtDark);
            Ribbon(0.89f, 1.11f, layer + 0.008f, Swatch.DirtDark);
          } else if (!service) {
            Ribbon(-half + 0.40f, -half + 0.54f, layer + 0.010f, Swatch.LaneWhite);
            Ribbon(half - 0.54f, half - 0.40f, layer + 0.010f, Swatch.LaneWhite);
            if (MeshKit.Hash(i, 361) > 0.33f) {
              Ribbon(-0.075f, 0.075f, layer + 0.011f, Swatch.LaneWhite, 0.12f, 0.66f);
            }
          }
        }
      }
    }

    static (Vector2 Position, Vector2 Normal, bool Joined) End(List<RoadPiece> pieces, int index, Vector2 point, bool first) {
      var piece = pieces[index];
      var axis = piece.Along;
      var normal = new Vector2(-axis.y, axis.x);
      var closest = 0.55f * 0.55f;
      var end = point;
      var neighborAlong = axis;
      var found = false;
      for (var i = 0; i < pieces.Count; i++) {
        if (i == index || Mathf.Abs(piece.HalfWidth - pieces[i].HalfWidth) > 0.1f) continue;
        var other = pieces[i];
        var dot = Vector2.Dot(other.Along, axis);
        if (Mathf.Abs(dot) < 0.56f) continue;
        var endpoint = first == (dot > 0f) ? other.To : other.From;
        var distance = (endpoint - point).sqrMagnitude;
        if (distance >= closest) continue;
        closest = distance;
        end = (point + endpoint) * 0.5f;
        neighborAlong = other.Along * Mathf.Sign(dot);
        found = true;
      }
      var joined = (axis + neighborAlong).normalized;
      var edge = new Vector2(-joined.y, joined.x);
      return (end, edge / Mathf.Max(0.92f, Vector2.Dot(edge, normal)), found);
    }

    static bool Junction(List<RoadPiece> pieces, RoadPiece piece, int index, Vector2 point) {
      for (var i = 0; i < pieces.Count; i++) {
        if (i == index || Mathf.Abs(Vector2.Dot(piece.Along, pieces[i].Along)) > 0.88f) continue;
        var other = pieces[i];
        var offset = point - other.From;
        if (Mathf.Abs(offset.x) > 14f || Mathf.Abs(offset.y) > 14f) continue;
        var along = Vector2.Dot(offset, other.Along);
        if (along >= -0.8f && along <= Vector2.Distance(other.From, other.To) + 0.8f
            && Mathf.Abs(Vector2.Dot(offset, new Vector2(-other.Along.y, other.Along.x))) <= other.HalfWidth) return true;
      }
      return false;
    }
  }
}
