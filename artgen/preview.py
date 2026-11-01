#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 Assets/Resources/Ships 里的精灵拼到深色星空上，检查实际观感。"""
import glob
import os
import random

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHIPS = os.path.join(ROOT, "Assets", "Resources", "Ships")
OUT = os.path.join(ROOT, "artgen", "preview.png")


def font(size):
    for name in ("msyh.ttc", "segoeui.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            pass
    return ImageFont.load_default()


def backdrop(w, h):
    img = Image.new("RGB", (w, h), (4, 6, 14))
    px = img.load()
    for y in range(h):
        t = y / h
        for x in range(0, w, 2):
            px[x, y] = (int(5 + 5 * (1 - t)), int(7 + 7 * (1 - t)), int(16 + 14 * (1 - t)))
            px[x + 1, y] = px[x, y]
    rnd = random.Random(7)
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(340):
        x, y = rnd.randrange(w), rnd.randrange(h)
        r = rnd.choice((1, 1, 1, 2))
        a = rnd.randrange(40, 150)
        d.ellipse((x, y, x + r, y + r), fill=(190, 225, 255, a))
    for cx, cy, r, col in ((w * 0.18, h * 0.28, w * 0.22, (30, 120, 140, 22)),
                           (w * 0.82, h * 0.55, w * 0.3, (80, 60, 160, 18)),
                           (w * 0.5, h * 0.85, w * 0.32, (120, 70, 50, 12))):
        for i in range(8, 0, -1):
            rr = r * i / 8
            d.ellipse((cx - rr, cy - rr * 0.6, cx + rr, cy + rr * 0.6),
                      fill=(col[0], col[1], col[2], max(2, col[3] // i)))
    return img


def paste_scaled(bg, path, center, height):
    im = Image.open(path).convert("RGBA")
    scale = height / im.height
    nw = max(1, int(im.width * scale))
    im = im.resize((nw, max(1, int(im.height * scale))), Image.LANCZOS)
    x = int(center[0] - nw / 2)
    y = int(center[1] - im.height / 2)
    bg.alpha_composite(im, (x, y))
    return y, im.height


def label(bg, text, center, y, size=14, color=(150, 190, 210, 230)):
    d = ImageDraw.Draw(bg)
    f = font(size)
    tw = d.textlength(text, font=f)
    d.text((center[0] - tw / 2, y), text, font=f, fill=color)


def section(bg, title, y, names, cols, cw, ch, size, big=()):
    d = ImageDraw.Draw(bg)
    d.text((30, y), title, font=font(22), fill=(200, 230, 245, 255))
    y += 34
    for i, name in enumerate(names):
        p = os.path.join(SHIPS, name + ".png")
        if not os.path.exists(p):
            continue
        cx = 30 + cw * (i % cols) + cw / 2
        cy = y + ch * (i // cols) + ch / 2
        s = size if name not in big else big[name]
        ly, hh = paste_scaled(bg, p, (cx, cy), s)
        label(bg, name.replace("enemy_", "").replace("bullet_", "").replace("boss_", ""),
              (cx, cy), ly + hh + 4, 13)


def main():
    W, H = 1700, 1700
    bg = backdrop(W, H).convert("RGBA")
    d = ImageDraw.Draw(bg)
    d.text((30, 18), "Aurora Rift — art sheet", font=font(32), fill=(220, 240, 255, 255))
    d.text((30, 58), "暗底观感 / 大图细节", font=font(18), fill=(120, 160, 185, 255))

    section(bg, "PLAYER  (line variants)",
            94, ["player_pulse", "player_prism", "player_swarm",
                 "drone_seeker", "drone_cannon", "drone_support"],
            cols=6, cw=270, ch=310, size=260,
            big={"drone_seeker": 110, "drone_cannon": 110, "drone_support": 110})

    # 实际游戏尺寸（1080 宽手机：世界宽 10 单位 → 108 像素/单位）
    px = 108
    y0 = 445
    d.text((30, y0), "IN-GAME SIZE  (1080×1920, 108 px/unit)", font=font(22),
           fill=(200, 230, 245, 255))
    actual = [("player_pulse", 1.40), ("player_prism", 1.40), ("player_swarm", 1.40),
              ("enemy_drifter", 0.92), ("enemy_wasp", 0.82), ("enemy_lancer", 1.24),
              ("drone_seeker", 0.52)]
    for i, (name, units) in enumerate(actual):
        p = os.path.join(SHIPS, name + ".png")
        if not os.path.exists(p):
            continue
        cx = 140 + i * 180
        cy = y0 + 110
        h = int(units * px)
        ly, hh = paste_scaled(bg, p, (cx, cy), h)
        label(bg, name.split("_")[1], (cx, cy), ly + hh + 4, 12)

    section(bg, "ENEMIES",
            610, ["enemy_drifter", "enemy_wasp", "enemy_lancer", "enemy_orbiter",
                  "enemy_splitter", "enemy_bulwark", "enemy_rock", "enemy_sniper",
                  "enemy_minekeeper", "enemy_sidewinder", "enemy_carrier", "enemy_shardling"],
            cols=6, cw=275, ch=220, size=150)

    section(bg, "BOSSES  +  ROTATING RINGS", 1115,
            ["boss_gatekeeper", "boss_weaver", "boss_helios",
             "boss_gatekeeper_ring", "boss_weaver_ring", "boss_helios_ring"],
            cols=6, cw=275, ch=230, size=170)

    section(bg, "BULLETS / FX / CRATES", 1460,
            ["bullet_player_bolt", "bullet_player_lance", "bullet_player_micro",
             "bullet_enemy_orb", "bullet_enemy_needle", "bullet_enemy_heavy",
             "bullet_enemy_mine", "bullet_flame", "bullet_shield",
             "bullet_glow_soft", "crate_weapon", "crate_shield"],
            cols=12, cw=138, ch=160, size=80,
            big={"bullet_shield": 105, "bullet_glow_soft": 105})

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bg.convert("RGB").save(OUT, quality=92)
    print("->", OUT)


if __name__ == "__main__":
    main()
