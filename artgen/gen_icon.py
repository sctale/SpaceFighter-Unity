#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成应用图标（1024 母图 + 自适应图标的前景/背景层）。

构图沿用游戏里的品牌标记：双层菱形环 + 金色核心，前面压一架玩家舰船。
- 母图：Assets/Icons/icon_1024.png（构建时交给 PlayerSettings.SetIcons）
- 自适应：icon_fg_1024.png（透明前景，内容缩到 66% 安全区）+ icon_bg_1024.png（底）

用法：python artgen/gen_icon.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHIP = os.path.join(ROOT, "Assets", "Resources", "Ships", "player_pulse.png")
OUT_DIR = os.path.join(ROOT, "Assets", "Icons")
N = 1024

INK0 = (4, 6, 15)
INK2 = (11, 18, 38)
TEAL = (105, 245, 220)
VIOLET = (155, 140, 255)
GOLD = (255, 210, 122)
PAPER = (243, 248, 255)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def radial(size, inner, outer, cx=0.5, cy=0.42, radius=0.62):
    """径向渐变底：中心到边缘 inner→outer。"""
    img = Image.new("RGB", (size, size), outer)
    px = img.load()
    for y in range(size):
        for x in range(size):
            d = math.hypot((x / size - cx) * 1.05, (y / size - cy))
            t = min(1.0, d / radius)
            t = t * t * (3 - 2 * t)
            px[x, y] = lerp(inner, outer, t)
    return img


def glow_layer(size, color, cx, cy, r, alpha):
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    steps = 26
    for i in range(steps, 0, -1):
        t = i / steps
        rr = r * t
        a = int(alpha * (1 - t) ** 2)
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=color + (a,))
    return layer.filter(ImageFilter.GaussianBlur(size * 0.02))


def diamond(draw, cx, cy, half, color, width):
    pts = [(cx, cy - half), (cx + half, cy), (cx, cy + half), (cx - half, cy)]
    draw.line(pts + [pts[0]], fill=color, width=width, joint="curve")


def build():
    bg = radial(N, INK2, INK0, 0.5, 0.40, 0.70)

    # 两侧极光
    bg = bg.convert("RGBA")
    bg = Image.alpha_composite(bg, glow_layer(N, TEAL, N * 0.22, N * 0.20, N * 0.55, 46))
    bg = Image.alpha_composite(bg, glow_layer(N, VIOLET, N * 0.82, N * 0.84, N * 0.60, 52))

    # 星点
    star = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    sd = ImageDraw.Draw(star)
    rnd = 20261102
    for i in range(70):
        rnd = (rnd * 1103515245 + 12345) & 0x7FFFFFFF
        x = (rnd >> 8) % N
        rnd = (rnd * 1103515245 + 12345) & 0x7FFFFFFF
        y = (rnd >> 8) % N
        rnd = (rnd * 1103515245 + 12345) & 0x7FFFFFFF
        s = 1 + (rnd >> 16) % 3
        a = 40 + (rnd >> 12) % 90
        c = TEAL if i % 5 == 0 else GOLD if i % 7 == 0 else PAPER
        sd.ellipse([x, y, x + s, y + s], fill=c + (a,))
    bg = Image.alpha_composite(bg, star)

    # 菱形环（品牌标记）
    rings = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rings)
    cx, cy = N * 0.5, N * 0.46
    diamond(rd, cx, cy, N * 0.325, TEAL + (215,), 10)
    diamond(rd, cx, cy, N * 0.245, VIOLET + (200,), 8)
    rings = rings.filter(ImageFilter.GaussianBlur(1.2))
    bg = Image.alpha_composite(bg, glow_layer(N, TEAL, cx, cy, N * 0.34, 26))
    bg = Image.alpha_composite(bg, rings)

    # 金色核心（在舰船后面）
    core = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    cd = ImageDraw.Draw(core)
    h = N * 0.085
    cd.polygon([(cx, cy - h * 1.5), (cx + h * 1.5, cy), (cx, cy + h * 1.5), (cx - h * 1.5, cy)],
               fill=GOLD + (240,))
    core = core.filter(ImageFilter.GaussianBlur(1.0))
    bg = Image.alpha_composite(bg, glow_layer(N, GOLD, cx, cy, N * 0.20, 60))
    bg = Image.alpha_composite(bg, core)

    # 舰船
    ship = Image.open(SHIP).convert("RGBA")
    # 3D 机体的装甲很暗，压在暗底板上小尺寸会像"挖了个洞"：图标里单独提亮一点
    sr, sg, sb, sa = ship.split()
    lift = lambda v: min(255, int(v * 1.16 + 10))
    ship = Image.merge("RGBA", (sr.point(lift), sg.point(lift), sb.point(lift), sa))
    target_h = int(N * 0.60)
    scale = target_h / ship.height
    ship = ship.resize((int(ship.width * scale), target_h), Image.LANCZOS)
    sx = int(cx - ship.width / 2)
    sy = int(cy - ship.height * 0.52)

    halo = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    hd = ImageDraw.Draw(halo)
    hd.ellipse([sx + ship.width * 0.16, sy + ship.height * 0.18,
                sx + ship.width * 0.84, sy + ship.height * 0.90], fill=TEAL + (70,))
    halo = halo.filter(ImageFilter.GaussianBlur(N * 0.035))
    bg = Image.alpha_composite(bg, halo)
    bg.alpha_composite(ship, (sx, sy))

    # 暗角
    vig = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    vd = ImageDraw.Draw(vig)
    for i in range(46):
        t = i / 46
        pad = int(N * t * 0.5)
        vd.rectangle([pad, pad, N - pad, N - pad], outline=(0, 0, 0, 10))
    bg = Image.alpha_composite(bg, vig.filter(ImageFilter.GaussianBlur(8)))

    os.makedirs(OUT_DIR, exist_ok=True)
    icon = bg.convert("RGB")
    icon.save(os.path.join(OUT_DIR, "icon_1024.png"))

    # 自适应图标：背景只保留底 + 极光 + 星点（去掉标记），前景是标记 + 舰船缩到安全区
    bg_layer = radial(N, INK2, INK0, 0.5, 0.40, 0.70).convert("RGBA")
    bg_layer = Image.alpha_composite(bg_layer, glow_layer(N, TEAL, N * 0.22, N * 0.20, N * 0.55, 46))
    bg_layer = Image.alpha_composite(bg_layer, glow_layer(N, VIOLET, N * 0.82, N * 0.84, N * 0.60, 52))
    bg_layer = Image.alpha_composite(bg_layer, star)
    bg_layer.convert("RGB").save(os.path.join(OUT_DIR, "icon_bg_1024.png"))

    fg = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    fg.alpha_composite(bg.crop((0, 0, N, N)), (0, 0))
    # 把整张图里"非背景"的部分当前景不现实，直接重画一遍标记层
    fg = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    scale_fg = 0.66
    layer = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    rd2 = ImageDraw.Draw(layer)
    diamond(rd2, cx, cy, N * 0.325, TEAL + (230,), 11)
    diamond(rd2, cx, cy, N * 0.245, VIOLET + (215,), 9)
    layer = layer.filter(ImageFilter.GaussianBlur(1.2))
    layer = Image.alpha_composite(glow_layer(N, GOLD, cx, cy, N * 0.20, 70), layer)
    cd2 = ImageDraw.Draw(layer)
    cd2.polygon([(cx, cy - h * 1.5), (cx + h * 1.5, cy), (cx, cy + h * 1.5), (cx - h * 1.5, cy)],
                fill=GOLD + (245,))
    layer.alpha_composite(ship, (sx, sy))
    small = layer.resize((int(N * scale_fg), int(N * scale_fg)), Image.LANCZOS)
    fg.alpha_composite(small, ((N - small.width) // 2, (N - small.height) // 2))
    fg.save(os.path.join(OUT_DIR, "icon_fg_1024.png"))

    # 顺带出一张 192px 预览，方便肉眼检查小尺寸
    icon.resize((192, 192), Image.LANCZOS).save(os.path.join(ROOT, "artgen", "icon_preview_192.png"))
    icon.resize((512, 512), Image.LANCZOS).save(os.path.join(ROOT, "artgen", "icon_preview.png"))
    print("icon: %s" % os.path.join(OUT_DIR, "icon_1024.png"))


if __name__ == "__main__":
    build()
