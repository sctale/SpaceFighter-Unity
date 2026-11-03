#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
给渲好的机体精灵烘一圈同色外发光（v0.19.0 定稿风格：3D + 外发光）。

为什么单独一步：Blender 自带的 Python 没有 PIL，而外发光本质是 2D 的 alpha 模糊，
放在这里做既不用依赖 Blender 的合成器，也能单独重跑、单独调参。

约定：光晕 alpha 上限压在 0.45，`check_art.py` 用 alpha>0.5 判轮廓时不会被带偏，
机体仍然是 ShipArt 的 0.46 外接半径。

用法:
  python artgen/blender/post_glow.py --dir artgen/blender/out/batch
"""
import argparse
import os
import sys

from PIL import Image, ImageFilter

ACCENT = {
    "player_pulse": (53, 224, 200),
    "player_prism": (255, 92, 138),
    "player_swarm": (255, 194, 75),
    "boss_gatekeeper": (255, 111, 145),
    "boss_weaver": (155, 140, 255),
    "boss_helios": (255, 210, 122),
    "enemy_drifter": (255, 111, 145),
    "enemy_wasp": (255, 154, 180),
    "enemy_lancer": (165, 151, 255),
    "enemy_orbiter": (155, 140, 255),
    "enemy_splitter": (255, 111, 145),
    "enemy_bulwark": (195, 203, 224),
    "enemy_rock": (170, 182, 204),
    "enemy_sniper": (105, 245, 220),
    "enemy_minekeeper": (255, 210, 122),
    "enemy_sidewinder": (155, 140, 255),
    "enemy_carrier": (143, 164, 201),
    "enemy_shardling": (255, 154, 180),
    "bullet_enemy_needle": (140, 247, 234),
}

# 大块机体光晕弱一点、小而快的敌机强一点，避免满屏糊成一片
STRENGTH = {"boss_": 0.42, "player_": 0.5, "enemy_": 0.78}


def strength_for(name):
    for pre, v in STRENGTH.items():
        if name.startswith(pre):
            return v
    return 0.55


def bake(path, tint, strength, radius_frac=0.026, lift=None):
    if lift is None:
        lift = 0.14 if os.path.basename(path).startswith("enemy_") else 0.08
    im = Image.open(path).convert("RGBA")
    w, _ = im.size
    a = im.getchannel("A")
    r, g, b, _ = im.split()
    if lift > 0:
        f = lambda v: min(255, int(v + 255 * lift))
        r, g, b = r.point(f), g.point(f), b.point(f)
    body = Image.merge("RGBA", (r, g, b, a))

    blur = a.filter(ImageFilter.GaussianBlur(max(2, int(w * radius_frac))))
    cap = int(0.45 * 255)
    glow = Image.new("RGBA", im.size, (tint[0], tint[1], tint[2], 0))
    glow.putalpha(blur.point(lambda v: min(cap, int(v * strength))))
    Image.alpha_composite(glow, body).save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", required=True)
    args = ap.parse_args()
    names = [n[:-4] for n in sorted(os.listdir(args.dir))
             if n.endswith(".png") and not n.endswith("_mask.png") and not n.endswith("_ring.png")]
    done = 0
    for n in names:
        tint = ACCENT.get(n)
        if not tint:
            print("SKIP 无配色:", n)
            continue
        bake(os.path.join(args.dir, n + ".png"), tint, strength_for(n))
        done += 1
    print(f"GLOW OK — {done} 张")


if __name__ == "__main__":
    main()
