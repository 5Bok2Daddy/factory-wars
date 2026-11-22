"""Quick orthographic preview of an OBJ (flat shaded, both sides) for checking conversions without Unity.
usage: python -I preview_obj.py in.obj out.png [yaw_deg] [pitch_deg]"""
import sys, numpy as np, trimesh, matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.collections import PolyCollection
from PIL import Image
m = trimesh.load(sys.argv[1], force='mesh', process=False)
yaw = np.radians(float(sys.argv[3]) if len(sys.argv) > 3 else 35); pitch = np.radians(float(sys.argv[4]) if len(sys.argv) > 4 else 30)
V = np.asarray(m.vertices); F = np.asarray(m.faces)
cols = None
try:
    tex = np.asarray(m.visual.material.image.convert('RGB')); uv = m.visual.uv
    h, w = tex.shape[:2]; fuv = uv[F].mean(1)
    cols = tex[np.clip(((1 - fuv[:, 1]) * h).astype(int), 0, h - 1), np.clip((fuv[:, 0] * w).astype(int), 0, w - 1)] / 255.0
except Exception as e:
    cols = np.full((len(F), 3), 0.7)
Ry = np.array([[np.cos(yaw), 0, np.sin(yaw)], [0, 1, 0], [-np.sin(yaw), 0, np.cos(yaw)]])
Rx = np.array([[1, 0, 0], [0, np.cos(pitch), -np.sin(pitch)], [0, np.sin(pitch), np.cos(pitch)]])
P = V @ Ry.T @ Rx.T
tri = P[F]
n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]); n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-9
shade = 0.45 + 0.55 * np.abs(n @ np.array([0.3, 0.8, -0.5]) / np.linalg.norm([0.3, 0.8, -0.5]))
order = np.argsort(tri[:, :, 2].mean(1))[::-1]
fig = plt.figure(figsize=(12, 6), dpi=110); ax = fig.add_axes([0, 0, 1, 1]); ax.set_facecolor('#1c2230')
ax.add_collection(PolyCollection(tri[order][:, :, :2], facecolors=np.clip(cols[order] * shade[order, None], 0, 1), edgecolors='none'))
ax.set_xlim(P[:, 0].min() - 1, P[:, 0].max() + 1); ax.set_ylim(P[:, 1].min() - 1, P[:, 1].max() + 1); ax.set_aspect('equal'); ax.axis('off')
fig.savefig(sys.argv[2], facecolor='#1c2230')
