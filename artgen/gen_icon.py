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

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter

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


def nebula(size):
    """低透明度长云带：让图标背景有体积，而不是一块渐变。"""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    bands = [
        (0.16, 0.28, 0.62, 0.10, TEAL, 38),
        (0.48, 0.62, 0.72, 0.12, VIOLET, 34),
        (0.70, 0.18, 0.52, 0.08, GOLD, 22),
        (0.30, 0.84, 0.58, 0.09, TEAL, 24),
    ]
    for cx, cy, w, h, color, alpha in bands:
        d.ellipse([size * (cx - w * 0.5), size * (cy - h * 0.5),
                   size * (cx + w * 0.5), size * (cy + h * 0.5)], fill=color + (alpha,))
    return layer.filter(ImageFilter.GaussianBlur(size * 0.06))


def flares(size, n=5):
    """少量四芒星：小尺寸下比更大的圆点更像"亮星"。"""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    rnd = 4242
    for i in range(n):
        rnd = (rnd * 1103515245 + 12345) & 0x7FFFFFFF
        x = (rnd >> 8) % size
        rnd = (rnd * 1103515245 + 12345) & 0x7FFFFFFF
        y = (rnd >> 8) % size
        c = TEAL if i % 2 == 0 else PAPER
        length = size * (0.020 + 0.012 * (i % 3))
        w = max(1, SS)
        d.line([x - length, y, x + length, y], fill=c + (105,), width=w)
        d.line([x, y - length, x, y + length], fill=c + (105,), width=w)
    return layer.filter(ImageFilter.GaussianBlur(SS * 1.2))


def background():
    bg = radial(S, INK2, INK0).convert("RGBA")
    bg = Image.alpha_composite(bg, glow_layer(S, TEAL, S * 0.18, S * 0.15, S * 0.56, 36))
    bg = Image.alpha_composite(bg, glow_layer(S, VIOLET, S * 0.86, S * 0.88, S * 0.58, 42))
    bg = Image.alpha_composite(bg, nebula(S))
    bg = Image.alpha_composite(bg, stars(S))
    bg = Image.alpha_composite(bg, flares(S))
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
    # 24 个刻度（每 90° 加长）+ 四段金色弧：小尺寸下也能读出"瞄准环"
    for i in range(24):
        a = i * math.pi / 12
        inner, outer = rr - 8 * SS, rr + 8 * SS
        if i % 6 == 0:
            inner, outer = rr - 16 * SS, rr + 16 * SS
        rd.line([cx + math.cos(a) * inner, cy + math.sin(a) * inner,
                 cx + math.cos(a) * outer, cy + math.sin(a) * outer],
                fill=TEAL + (135 if i % 6 == 0 else 62,), width=2 * SS)
    for i in range(4):
        start = 45 + i * 90
        rd.arc([cx - (rr + 18 * SS), cy - (rr + 18 * SS),
                cx + (rr + 18 * SS), cy + (rr + 18 * SS)],
               start, start + 16, fill=GOLD + (210,), width=4 * SS)
    # 四个方位的菱形标记
    for a in (0, math.pi * 0.5, math.pi, math.pi * 1.5):
        mx, my = cx + math.cos(a) * rr, cy + math.sin(a) * rr
        r = 7 * SS
        rd.polygon([(mx, my - r), (mx + r * 0.7, my), (mx, my + r), (mx - r * 0.7, my)],
                   fill=VIOLET + (190,))
    ring = ring.filter(ImageFilter.GaussianBlur(SS * 0.6))
    layer = Image.alpha_composite(layer, ring)

    # 舰船背后的中央柔光，把主体从背景里托起来
    layer = Image.alpha_composite(layer, glow_layer(S, TEAL, cx, cy, S * 0.34, 34, 0.05))

    # 舰船投影 + 外发光 + 边缘光 + 舰船
    hx = int(cx - ship.width / 2)
    hy = int(cy - ship.height / 2)
    a = ship.getchannel("A")

    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    black = Image.new("L", ship.size, 0)
    shadow.alpha_composite(Image.merge("RGBA", (
        black, black, black, a.point(lambda v: int(v * 0.55)))),
        (hx, hy + int(S * 0.020)))
    shadow = shadow.filter(ImageFilter.GaussianBlur(S * 0.012))
    layer = Image.alpha_composite(layer, shadow)

    ship_halo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ship_halo.alpha_composite(Image.merge("RGBA", (
        Image.new("L", ship.size, TEAL[0]), Image.new("L", ship.size, TEAL[1]),
        Image.new("L", ship.size, TEAL[2]), a.point(lambda v: int(v * 0.48)))), (hx, hy))
    ship_halo = ship_halo.filter(ImageFilter.GaussianBlur(S * 0.014))
    layer = Image.alpha_composite(layer, ship_halo)

    # 边缘光：把 alpha 膨胀后减去原图，得到一圈 6–10px 的青色轮廓
    dil = a
    for _ in range(2):
        dil = dil.filter(ImageFilter.MaxFilter(9))
    rim = ImageChops.subtract(dil, a).filter(ImageFilter.GaussianBlur(SS * 1.4))
    rim_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rim_layer.alpha_composite(Image.merge("RGBA", (
        Image.new("L", ship.size, TEAL[0]), Image.new("L", ship.size, TEAL[1]),
        Image.new("L", ship.size, TEAL[2]), rim.point(lambda v: int(v * 0.9)))), (hx, hy))
    layer = Image.alpha_composite(layer, rim_layer)

    layer.alpha_composite(ship, (hx, hy))

    # 尾喷暖金余晖（压在舰船下缘，小尺寸下提供第二个焦点）
    for fx in (0.38, 0.62):
        ex = hx + int(ship.width * fx)
        ey = hy + int(ship.height * 0.92)
        layer = Image.alpha_composite(layer, glow_layer(S, GOLD, ex, ey, S * 0.055, 95, 0.012))
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
