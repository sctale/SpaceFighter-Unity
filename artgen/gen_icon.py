#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成应用图标（1024 母图 + 自适应图标的前景/背景层）。

v0.21.3 起改用「3/4 英雄视角」：Polyfork interceptor 抬头 52°、绕 Z 转 160°，
从机背后上方看，两台尾喷改成暖金自发光；背景只留一圈青色瞄准环 + 紫内环，
不再用两层菱形把舰船框成徽章。

前置：3/4 机体由
  blender --background --factory-startup --python artgen/blender/polyfork_icon.py
渲到 artgen/blender/out/icon/hero.png，再复制为 artgen/icon_src/interceptor_hero_3q.png。
（源模型在 D:/V-Coding/.blender-setup/polyfork/interceptor-9d2289.fbx，不进仓库。）

用法：python artgen/gen_icon.py
产物：
  Assets/Icons/icon_1024.png       构建给 PlayerSettings.SetIcons 的母图
  Assets/Icons/icon_fg_1024.png    自适应前景（透明）
  Assets/Icons/icon_bg_1024.png    自适应背景（满幅）
  artgen/icon_preview*.png         192 / 512 肉眼检查用
"""
import math
import os

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HERO = os.path.join(ROOT, "artgen", "icon_src", "interceptor_hero_3q.png")
OUT_DIR = os.path.join(ROOT, "Assets", "Icons")
N = 1024
SS = 2
S = N * SS

INK0 = (3, 5, 12)
INK2 = (18, 30, 58)
TEAL = (105, 245, 220)
VIOLET = (155, 140, 255)
GOLD = (255, 210, 122)
PAPER = (243, 248, 255)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def radial(size, inner, outer, cx=0.5, cy=0.43, radius=0.74):
    """径向渐变底：中心到边缘 inner→outer。"""
    img = Image.new("RGB", (size, size), outer)
    px = img.load()
    for y in range(size):
        for x in range(size):
            d = math.hypot((x / size - cx) * 1.02, (y / size - cy))
            t = min(1.0, d / radius)
            t = t * t * (3 - 2 * t)
            px[x, y] = lerp(inner, outer, t)
    return img


def glow_layer(size, color, cx, cy, r, alpha, blur=0.02):
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    steps = 30
    for i in range(steps, 0, -1):
        t = i / steps
        rr = r * t
        a = int(alpha * (1 - t) ** 2)
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=color + (a,))
    return layer.filter(ImageFilter.GaussianBlur(size * blur))


def stars(size, n=70):
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    rnd = 20261104
    for i in range(n):
        rnd = (rnd * 1103515245 + 12345) & 0x7FFFFFFF
        x = (rnd >> 8) % size
        rnd = (rnd * 1103515245 + 12345) & 0x7FFFFFFF
        y = (rnd >> 8) % size
        rnd = (rnd * 1103515245 + 12345) & 0x7FFFFFFF
        s = SS + (rnd >> 16) % (3 * SS)
        a = 35 + (rnd >> 12) % 85
        c = TEAL if i % 6 == 0 else GOLD if i % 9 == 0 else PAPER
        d.ellipse([x, y, x + s, y + s], fill=c + (a,))
    return layer


def background():
    bg = radial(S, INK2, INK0).convert("RGBA")
    bg = Image.alpha_composite(bg, glow_layer(S, TEAL, S * 0.18, S * 0.15, S * 0.56, 36))
    bg = Image.alpha_composite(bg, glow_layer(S, VIOLET, S * 0.86, S * 0.88, S * 0.58, 42))
    bg = Image.alpha_composite(bg, stars(S))
    return bg


def ship_layer():
    im = Image.open(HERO).convert("RGBA")
    im = im.crop(im.getchannel("A").getbbox())
    target = S * 0.86
    k = target / max(im.width, im.height)
    im = im.resize((int(im.width * k), int(im.height * k)), Image.LANCZOS)
    # 3/4 视角的侧面本来就暗，缩到 48dp 会糊成一团：提亮 + 加饱和
    im = ImageEnhance.Brightness(im).enhance(1.16)
    im = ImageEnhance.Color(im).enhance(1.28)
    im = ImageEnhance.Contrast(im).enhance(1.06)
    return im


def content_layer(ship):
    """瞄准环 + 舰船（透明底）。"""
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    cx = cy = S * 0.5

    # 环外发光（小尺寸下 1px 的线会消失，靠这一层留住"环"）
    rr = S * 0.425
    halo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(halo)
    hd.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], outline=TEAL + (120,), width=8 * SS)
    layer = Image.alpha_composite(layer, halo.filter(ImageFilter.GaussianBlur(10 * SS)))

    ring = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rd = ImageDraw.Draw(ring)
    rd.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], outline=TEAL + (225,), width=6 * SS)
    rd.ellipse([cx - rr * 0.80, cy - rr * 0.80, cx + rr * 0.80, cy + rr * 0.80],
               outline=VIOLET + (75,), width=2 * SS)
    ring = ring.filter(ImageFilter.GaussianBlur(SS * 0.6))
    layer = Image.alpha_composite(layer, ring)

    # 舰船外发光 + 舰船
    hx = int(cx - ship.width / 2)
    hy = int(cy - ship.height / 2)
    a = ship.getchannel("A")
    ship_halo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ship_halo.alpha_composite(Image.merge("RGBA", (
        Image.new("L", ship.size, TEAL[0]), Image.new("L", ship.size, TEAL[1]),
        Image.new("L", ship.size, TEAL[2]), a.point(lambda v: int(v * 0.48)))), (hx, hy))
    ship_halo = ship_halo.filter(ImageFilter.GaussianBlur(S * 0.014))
    layer = Image.alpha_composite(layer, ship_halo)
    layer.alpha_composite(ship, (hx, hy))
    return layer


def vignette(img):
    vig = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    vd = ImageDraw.Draw(vig)
    for i in range(46):
        t = i / 46
        pad = int(S * t * 0.5)
        vd.rectangle([pad, pad, S - pad, S - pad], outline=(0, 0, 0, 10))
    return Image.alpha_composite(img, vig.filter(ImageFilter.GaussianBlur(8 * SS)))


def build():
    ship = ship_layer()
    content = content_layer(ship)

    full = Image.alpha_composite(background(), content)
    full = vignette(full)

    os.makedirs(OUT_DIR, exist_ok=True)
    icon = full.resize((N, N), Image.LANCZOS).convert("RGB")
    icon.save(os.path.join(OUT_DIR, "icon_1024.png"))

    # 自适应背景：满幅渐变 + 极光 + 星点
    background().resize((N, N), Image.LANCZOS).convert("RGB").save(
        os.path.join(OUT_DIR, "icon_bg_1024.png"))

    # 自适应前景：整体缩到 74% 安全区（环可以轻微出血，舰船保持在中心）
    fg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    small = content.resize((int(S * 0.74), int(S * 0.74)), Image.LANCZOS)
    fg.alpha_composite(small, ((S - small.width) // 2, (S - small.height) // 2))
    fg.resize((N, N), Image.LANCZOS).save(os.path.join(OUT_DIR, "icon_fg_1024.png"))

    # 小尺寸预览
    icon.resize((512, 512), Image.LANCZOS).save(os.path.join(ROOT, "artgen", "icon_preview.png"))
    icon.resize((192, 192), Image.LANCZOS).save(os.path.join(ROOT, "artgen", "icon_preview_192.png"))
    icon.resize((96, 96), Image.LANCZOS).save(os.path.join(ROOT, "artgen", "icon_preview_96.png"))
    icon.resize((48, 48), Image.LANCZOS).save(os.path.join(ROOT, "artgen", "icon_preview_48.png"))
    print("icon: %s" % os.path.join(OUT_DIR, "icon_1024.png"))


if __name__ == "__main__":
    build()
