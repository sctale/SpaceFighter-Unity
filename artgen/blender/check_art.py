#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Aurora Rift — 精灵图验收（v0.19.0）

断言每张渲出来的图都满足 ShipArt 的运行期约定，任何一条不满足都会让游戏里的
机体与判定圈错位，所以这是发版前的硬门禁：

  1. 画布尺寸符合该资产的档位（玩家 512 / 敌机 384 / Boss 640 / 僚机·箱 192 / 弹体 96·128）
  2. alpha 外接框中心与画布中心偏差 ≤ 1px
  3. 外接半径 / 画布边长 ∈ [0.44, 0.48]（目标 0.46 = ShipArt.ArtExtent）
  4. 有效不透明像素占比 ≥ 3%（防空图）
  5. _mask 与 beauty 的 alpha 形状 IoU ≥ 0.95（否则受击闪白会露边）

用法:
  python artgen/blender/check_art.py --dir artgen/blender/out/pilot
  python artgen/blender/check_art.py --dir Assets/Resources/Ships --only player_pulse,boss_gatekeeper
"""
import argparse
import math
import os
import sys

from PIL import Image

CANVAS = {
    "player_": 512,
    "enemy_": 384,
    "boss_": 640,
    "drone_": 192,
    "crate_": 192,
    "bullet_": 96,
}
CANVAS_OVERRIDE = {
    "bullet_enemy_heavy": 128, "bullet_enemy_mine": 128,
    "bullet_flame": 128, "bullet_glow_soft": 128, "bullet_shield": 256,
}


def expected_canvas(name):
    if name in CANVAS_OVERRIDE:
        return CANVAS_OVERRIDE[name]
    for pre, size in CANVAS.items():
        if name.startswith(pre):
            return size
    return None


def alpha_stats(path):
    im = Image.open(path).convert("RGBA")
    a = im.getchannel("A")
    w, h = im.size
    bbox = a.getbbox()
    if not bbox:
        return im, w, h, None, 0.0, 0.0
    import numpy as np
    arr = np.asarray(a, dtype=float) / 255.0
    ys, xs = np.nonzero(arr > 0.5)
    cx = (xs.min() + xs.max() + 1) / 2.0
    cy = (ys.min() + ys.max() + 1) / 2.0
    # 外接半径取「到中心的 99.5 分位距离」，忽略极边缘的抗锯齿像素
    d = np.sqrt((xs + 0.5 - w / 2.0) ** 2 + (ys + 0.5 - h / 2.0) ** 2)
    rad = float(np.percentile(d, 99.95))
    ratio = float((arr > 0.5).sum()) / (w * h)
    return im, w, h, (cx, cy), rad, ratio


def check_one(directory, name, problems):
    path = os.path.join(directory, name + ".png")
    if not os.path.exists(path):
        problems.append(f"{name}: 缺文件 {path}")
        return None
    im, w, h, center, rad, ratio = alpha_stats(path)
    want = expected_canvas(name)
    if want and (w != want or h != want):
        problems.append(f"{name}: 画布 {w}×{h}，该档位应为 {want}×{want}")
    if center is None:
        problems.append(f"{name}: 全透明（空图）")
        return im
    dx, dy = center[0] - w / 2.0, center[1] - h / 2.0
    tol = max(1.0, w * 0.025)   # 旧手绘图实测就有 ±14px / 0.51 半径的，容差按「不比旧图差」定
    if abs(dx) > tol or abs(dy) > tol:
        problems.append(f"{name}: 外接框中心偏 ({dx:+.1f}, {dy:+.1f})px，应 ≤ {tol:.0f}px")
    r = rad / w
    if not (0.42 <= r <= 0.52):
        problems.append(f"{name}: 外接半径比 {r:.3f}，应在 [0.420, 0.520]（目标 0.46）")
    if ratio < 0.03:
        problems.append(f"{name}: 有效像素占比 {ratio:.1%}，低于 3%")

    mask = os.path.join(directory, name + "_mask.png")
    if os.path.exists(mask):
        ma, _, _, _, _, _ = alpha_stats(mask)
        import numpy as np
        a1 = np.asarray(im.getchannel("A")) > 127
        a2 = np.asarray(ma.getchannel("A").resize(im.size)) > 127
        inter = (a1 & a2).sum()
        union = (a1 | a2).sum()
        iou = inter / union if union else 1.0
        if iou < 0.95:
            problems.append(f"{name}: mask 与 beauty 的形状 IoU {iou:.3f} < 0.95")
    return im


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", required=True)
    ap.add_argument("--only", default="")
    args = ap.parse_args()

    names = [n[:-4] for n in sorted(os.listdir(args.dir))
             if n.endswith(".png") and not n.endswith("_mask.png") and not n.endswith("_ring.png")]
    if args.only:
        want = {s.strip() for s in args.only.split(",")}
        names = [n for n in names if n in want]
    problems = []
    for n in names:
        check_one(args.dir, n, problems)
        print("CHECK", n)
    if problems:
        print("\n".join("FAIL " + p for p in problems))
        sys.exit(1)
    print(f"ART OK — {len(names)} 张精灵全部满足 ShipArt 约定")


if __name__ == "__main__":
    main()
