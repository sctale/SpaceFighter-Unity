#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Aurora Rift — 程序化舰船美术生成器

输出到 Assets/Resources/Ships/*.png。
设计语言：硬表面军武 + 霓虹能量线；顶光金属渐变、装甲倒角、面板刻线、
引擎/座舱自发光，轮廓外一圈同色柔光。所有坐标归一化到 [-1, 1]，
y 向下为正（玩家机头朝上，敌机机头朝下）。

运行：
    python artgen/gen_ships.py            # 生成全部
    python artgen/gen_ships.py --only player,enemy_drifter
    python artgen/preview.py              # 拼一张预览图
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "Ships")


# ---------------------------------------------------------------- 基础工具
def hx(s):
    s = s.lstrip("#")
    return np.array([int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


def mix(a, b, t):
    return np.array(a, np.float32) * (1 - t) + np.array(b, np.float32) * t


def clamp01(a):
    return np.clip(a, 0.0, 1.0)


def shift(a, dx, dy):
    out = np.zeros_like(a)
    ys0, ys1 = max(0, dy), min(a.shape[0], a.shape[0] + dy)
    xs0, xs1 = max(0, dx), min(a.shape[1], a.shape[1] + dx)
    if ys0 < ys1 and xs0 < xs1:
        out[ys0:ys1, xs0:xs1] = a[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
    return out


class Cv:
    """带 alpha 的 CPU 画布；所有绘制在 ss 倍超采样下进行，结束时按面积平均降采样。"""

    def __init__(self, px=512, ss=3, extent=0.46):
        self.px = px
        self.ss = ss
        self.S = px * ss
        self.unit = self.S * extent
        self.cx = self.S * 0.5
        self.cy = self.S * 0.5
        self.rgb = np.zeros((self.S, self.S, 3), np.float32)
        self.a = np.zeros((self.S, self.S), np.float32)
        self.hull = np.zeros((self.S, self.S), np.float32)
        self.name = ""

    # ---------------- 坐标 / 蒙版
    def xy(self, pts):
        return [(self.cx + x * self.unit, self.cy + y * self.unit) for x, y in pts]

    def _img(self):
        return Image.new("L", (self.S, self.S), 0)

    def mask_poly(self, pts, width=0.0):
        im = self._img()
        d = ImageDraw.Draw(im)
        p = self.xy(pts)
        if width > 0:
            # 源图会被缩到 100px 左右显示：给线宽设最终像素下限，否则夜战里轮廓全糊掉
            px = max(int(round(width * self.unit)), int(round(5.0 * self.ss)))
            d.line(p + [p[0]], fill=255, width=px, joint="curve")
        else:
            d.polygon(p, fill=255)
        return np.asarray(im, np.float32) / 255.0

    def mask_line(self, pts, width=0.006):
        im = self._img()
        d = ImageDraw.Draw(im)
        px = max(int(round(width * self.unit)), int(round(2.7 * self.ss)))
        d.line(self.xy(pts), fill=255, width=px, joint="curve")
        return np.asarray(im, np.float32) / 255.0

    def mask_ellipse(self, c, r, width=0.0):
        im = self._img()
        d = ImageDraw.Draw(im)
        cx, cy = self.cx + c[0] * self.unit, self.cy + c[1] * self.unit
        rx, ry = r[0] * self.unit, r[1] * self.unit
        box = [cx - rx, cy - ry, cx + rx, cy + ry]
        if width > 0:
            px = max(int(round(width * self.unit)), int(round(2.7 * self.ss)))
            d.ellipse(box, outline=255, width=px)
        else:
            d.ellipse(box, fill=255)
        return np.asarray(im, np.float32) / 255.0

    def mask_rect(self, p0, p1, width=0.0):
        im = self._img()
        d = ImageDraw.Draw(im)
        x0, y0 = self.cx + p0[0] * self.unit, self.cy + p0[1] * self.unit
        x1, y1 = self.cx + p1[0] * self.unit, self.cy + p1[1] * self.unit
        box = [min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)]
        if width > 0:
            d.rectangle(box, outline=255, width=max(1, int(round(width * self.unit))))
        else:
            d.rectangle(box, fill=255)
        return np.asarray(im, np.float32) / 255.0

    def mask_circle(self, c, r):
        return self.mask_ellipse(c, (r, r))

    def blur(self, m, radius_norm):
        r = max(1, int(round(radius_norm * self.unit)))
        im = Image.fromarray((clamp01(m) * 255).astype(np.uint8))
        return np.asarray(im.filter(ImageFilter.GaussianBlur(r)), np.float32) / 255.0

    # ---------------- 绘制
    def paint(self, m, color, alpha=1.0, op="over"):
        if alpha <= 0 or color is None:
            return
        m = clamp01(m) * alpha
        if np.isscalar(color):
            col = np.full((self.S, self.S, 3), float(color), np.float32)
        else:
            col = np.asarray(color, np.float32)
            if col.ndim == 1:
                col = np.broadcast_to(col, (self.S, self.S, 3))
        if op == "add":
            self.rgb = clamp01(self.rgb + col * m[..., None])
            self.a = clamp01(self.a + m * (1 - self.a))
            return
        na = m + self.a * (1 - m)
        safe = np.maximum(na, 1e-6)
        self.rgb = (col * m[..., None] + self.rgb * self.a[..., None] * (1 - m[..., None])) / safe[..., None]
        self.a = na

    def vgrad(self, top, bottom, gamma=1.0):
        t = np.linspace(0.0, 1.0, self.S, dtype=np.float32) ** gamma
        row = np.array(top, np.float32)[None, :] * (1 - t[:, None]) + np.array(bottom, np.float32)[None, :] * t[:, None]
        return np.repeat(row[:, None, :], self.S, axis=1)

    def hgrad(self, left, right):
        t = np.linspace(0.0, 1.0, self.S, dtype=np.float32)
        row = np.array(left, np.float32)[None, :] * (1 - t[:, None]) + np.array(right, np.float32)[None, :] * t[:, None]
        return np.repeat(row[None, :, :], self.S, axis=0)

    def radial(self, c, r, inner, outer):
        yy, xx = np.mgrid[0:self.S, 0:self.S].astype(np.float32)
        cx, cy = self.cx + c[0] * self.unit, self.cy + c[1] * self.unit
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / max(1e-5, r * self.unit)
        t = clamp01(d)[..., None]
        return np.array(inner, np.float32) * (1 - t) + np.array(outer, np.float32) * t

    def glow(self, pts_or_mask, color, radius=0.06, alpha=0.35):
        m = pts_or_mask if isinstance(pts_or_mask, np.ndarray) else self.mask_poly(pts_or_mask)
        g = self.blur(m, radius)
        self.paint(g, color, alpha)

    def thick_ring(self, m, width_norm=0.01):
        r = max(1, int(round(width_norm * self.unit)))
        im = Image.fromarray((clamp01(m) * 255).astype(np.uint8))
        dil = np.asarray(im.filter(ImageFilter.MaxFilter(2 * r + 1)), np.float32) / 255.0
        ero = np.asarray(im.filter(ImageFilter.MinFilter(2 * r + 1)), np.float32) / 255.0
        return clamp01(dil - ero)

    def bevel(self, m, hi, lo, width=0.012, a_hi=0.55, a_lo=0.45):
        w = max(int(round(width * self.unit)), int(round(2.4 * self.ss)))
        top = clamp01(m - shift(m, -w, -w))
        bot = clamp01(m - shift(m, w, w))
        self.paint(top, hi, a_hi)
        self.paint(bot, lo, a_lo)

    def inner_shadow(self, m, color, width=0.02, alpha=0.35):
        w = max(int(round(width * self.unit)), int(round(2.0 * self.ss)))
        edge = clamp01(m - shift(m, w, w))
        self.paint(edge * 0.7, color, alpha)

    def plate(self, pts, top, bottom, stroke=None, stroke_w=0.008,
              bevel_hi=None, bevel_lo=None, bevel_w=0.012, inner_lo=None,
              track=True, alpha=1.0):
        m = self.mask_poly(pts)
        if stroke is not None:
            self.paint(self.mask_poly(pts, width=stroke_w * 2), stroke, 1.0)
        col = self.vgrad(top, bottom) if np.ndim(bottom) == 1 else bottom
        self.paint(m, col, alpha)
        if bevel_hi is not None:
            self.bevel(m, bevel_hi, bevel_lo if bevel_lo is not None else (0, 0, 0),
                       bevel_w)
        if inner_lo is not None:
            self.inner_shadow(m, inner_lo)
        if track:
            self.hull = np.maximum(self.hull, m)
        return m

    def line(self, pts, color, width=0.006, alpha=1.0, clip=None):
        m = self.mask_line(pts, width)
        if clip is not None:
            m = m * clip
        self.paint(m, color, alpha)

    def ellipse(self, c, r, color, stroke=None, stroke_w=0.006, alpha=1.0, clip=None):
        m = self.mask_ellipse(c, r)
        if clip is not None:
            m = m * clip
        if stroke is not None:
            sm = self.mask_ellipse(c, r, width=stroke_w * 2)
            if clip is not None:
                sm = sm * clip
            self.paint(sm, stroke, alpha)
        if color is not None:
            self.paint(m, color, alpha)
        return m

    def dot(self, c, r, color, alpha=1.0):
        self.paint(self.mask_circle(c, r), color, alpha)

    def emissive(self, c, r, core, glow_col, glow_r=0.04, glow_a=0.5):
        self.glow(self.mask_circle(c, r * 1.05), glow_col, glow_r, glow_a)
        self.paint(self.mask_circle(c, r), core, 1.0)
        self.paint(self.mask_circle(c, r * 0.45), (1, 1, 1), 1.0)

    def panel(self, pts, hull, color, width=0.0035, alpha=0.45, hi=None):
        clip = hull if hull is not None else self.hull
        self.line(pts, color, width, alpha, clip=clip)
        if hi is not None:
            self.line([(x, y + 0.004) for x, y in pts], hi, width * 0.8, alpha * 0.6, clip=clip)

    def sheen(self, m, color=(1, 1, 1), alpha=0.09, p0=(-1.0, -0.55), p1=(1.0, 0.35), width=0.18):
        band = self.blur(self.mask_line([p0, p1], width), 0.025)
        self.paint(band * m, color, alpha)

    # ---------------- 输出
    def result(self):
        self.rgb = np.nan_to_num(self.rgb, nan=0.0, posinf=1.0, neginf=0.0)
        self.a = np.nan_to_num(self.a, nan=0.0, posinf=1.0, neginf=0.0)
        a = self.a.reshape(self.px, self.ss, self.px, self.ss).mean(axis=(1, 3))
        prem = (self.rgb * self.a[..., None]).reshape(
            self.px, self.ss, self.px, self.ss, 3).mean(axis=(1, 3))
        rgb = prem / np.maximum(a, 1e-5)[..., None]
        rgba = np.concatenate([rgb, a[..., None]], axis=2)
        return np.clip(rgba, 0, 1)

    def save(self, name=None):
        name = name or self.name
        os.makedirs(OUT, exist_ok=True)
        rgba = self.result()
        Image.fromarray((rgba * 255).astype(np.uint8), "RGBA").save(
            os.path.join(OUT, name + ".png"), optimize=True)
        # 命中闪白用的剪影：只保留实心部分，不要把柔光一起闪
        solid = clamp01((self.a - 0.55) * 5.0)
        solid = solid.reshape(self.px, self.ss, self.px, self.ss).mean(axis=(1, 3))
        white = np.ones((self.px, self.px, 3), np.float32)
        mask = np.concatenate([white, solid[..., None]], axis=2)
        Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8), "RGBA").save(
            os.path.join(OUT, name + "_mask.png"), optimize=True)
        print("  ->", name + ".png")


# ---------------------------------------------------------------- 调色板
WHITE = hx("F4FAFF")
SOFT = hx("BFD6E8")
STEEL = hx("8FA4C9")
INK = hx("05070F")
DARK = hx("101726")
DARK2 = hx("1A2438")
GOLD = hx("FFD27A")

PAL = {
    "teal": dict(main=hx("35E0C8"), light=hx("9BFFEE"), deep=hx("0A5C55"), dark=hx("0E2A33")),
    "rose": dict(main=hx("FF5C8A"), light=hx("FFC2D4"), deep=hx("6E1230"), dark=hx("2A1229")),
    "gold": dict(main=hx("FFC24B"), light=hx("FFE9B0"), deep=hx("6E4306"), dark=hx("2B2114")),
    "violet": dict(main=hx("A597FF"), light=hx("D9D2FF"), deep=hx("3B2C87"), dark=hx("181633")),
    "pink": dict(main=hx("FF9AB4"), light=hx("FFD7E1"), deep=hx("7A2440"), dark=hx("2A1020")),
    "cyan": dict(main=hx("69F5DC"), light=hx("D0FFF7"), deep=hx("0C5A54"), dark=hx("08221F")),
    "steel": dict(main=hx("C3CBE0"), light=hx("F2F6FF"), deep=hx("3A4A66"), dark=hx("141B2A")),
    "blue": dict(main=hx("8FA4C9"), light=hx("E4EDFF"), deep=hx("26344F"), dark=hx("101827")),
}


# ---------------------------------------------------------------- 玩家
def draw_player(cv, pal):
    cv.name = "player_" + pal["id"]
    main, light, deep, dark = pal["main"], pal["light"], pal["deep"], pal["dark"]

    silhouette = [
        (0, -1.02), (0.16, -0.74), (0.30, -0.46), (0.62, -0.20),
        (0.96, 0.10), (0.90, 0.22), (0.62, 0.24), (0.44, 0.32),
        (0.30, 0.68), (0.20, 0.90), (0.10, 0.72), (0, 0.76),
        (-0.10, 0.72), (-0.20, 0.90), (-0.30, 0.68), (-0.44, 0.32),
        (-0.62, 0.24), (-0.90, 0.22), (-0.96, 0.10), (-0.62, -0.20),
        (-0.30, -0.46), (-0.16, -0.74),
    ]
    cv.glow(silhouette, main, 0.085, 0.30)
    cv.glow([(0, -0.8), (0.45, 0.0), (0, 0.6), (-0.45, 0.0)], main, 0.05, 0.22)

    # 机腹阴影轮廓（比本体略大，形成深色描边）
    cv.paint(cv.mask_poly(silhouette), INK, 1.0)

    # ---- 外侧机翼
    wing = [(0.18, -0.44), (0.64, -0.18), (0.93, 0.10), (0.88, 0.19),
            (0.58, 0.20), (0.40, 0.27), (0.28, 0.44), (0.22, 0.18)]
    for sgn in (-1, 1):
        pts = [(sgn * x, y) for x, y in wing]
        m = cv.plate(pts, mix(dark, main, 0.10), mix(dark, deep, 0.75),
                     stroke=mix(light, main, 0.35), stroke_w=0.007,
                     bevel_hi=mix(light, WHITE, 0.4), bevel_lo=(0.02, 0.03, 0.05),
                     bevel_w=0.010, inner_lo=(0.01, 0.02, 0.03))
        # 内翼面：提亮一块，打破整片深色
        inner = [(sgn * 0.24, -0.32), (sgn * 0.48, -0.13), (sgn * 0.80, 0.08),
                 (sgn * 0.66, 0.16), (sgn * 0.40, 0.20), (sgn * 0.27, 0.31), (sgn * 0.23, 0.08)]
        m2 = cv.plate(inner, mix(dark, main, 0.42), mix(dark, deep, 0.55),
                      stroke=mix(light, main, 0.45), stroke_w=0.004,
                      bevel_hi=mix(light, WHITE, 0.5), bevel_lo=(0.01, 0.02, 0.04),
                      bevel_w=0.007, track=False)
        cv.sheen(m2, mix(light, WHITE, 0.3), 0.10, (-1.0, -0.2), (1.0, 0.15), 0.12)
        cv.sheen(m, (1, 1, 1), 0.05, (-1.0, -0.1), (1.0, 0.4), 0.22)
        # 金色前缘
        cv.line([(sgn * 0.23, -0.42), (sgn * 0.66, -0.16), (sgn * 0.92, 0.10)],
                mix(GOLD, main, 0.25), 0.006, 0.95, clip=m)
        # 翼面凹槽
        cv.line([(sgn * 0.33, 0.10), (sgn * 0.55, 0.06), (sgn * 0.80, 0.13)],
                (0.02, 0.03, 0.05), 0.005, 0.35, clip=m)
        cv.line([(sgn * 0.34, 0.26), (sgn * 0.52, 0.18), (sgn * 0.74, 0.15)],
                mix(light, main, 0.5), 0.004, 0.28, clip=m)
        # 翼下挂架
        cv.plate([(sgn * 0.48, 0.16), (sgn * 0.62, 0.17), (sgn * 0.61, 0.25), (sgn * 0.47, 0.24)],
                 mix(dark, main, 0.18), dark, stroke=mix(main, WHITE, 0.3),
                 stroke_w=0.004, bevel_w=0.006, track=False)
        # 翼尖灯
        cv.emissive((sgn * 0.90, 0.13), 0.016, mix(GOLD, WHITE, 0.4), GOLD, 0.03, 0.45)

    # ---- 尾部安定面
    for sgn in (-1, 1):
        cv.plate([(sgn * 0.20, 0.40), (sgn * 0.38, 0.64), (sgn * 0.32, 0.72), (sgn * 0.17, 0.52)],
                 mix(dark, main, 0.22), dark, stroke=mix(main, light, 0.2),
                 stroke_w=0.005, bevel_w=0.008, track=False)

    # ---- 中央机身（深色底再叠主装甲）
    body = [(0, -1.00), (0.10, -0.66), (0.155, -0.22), (0.145, 0.20),
            (0.095, 0.50), (0.045, 0.66), (0, 0.70), (-0.045, 0.66),
            (-0.095, 0.50), (-0.145, 0.20), (-0.155, -0.22), (-0.10, -0.66)]
    cv.plate(body, (0.02, 0.03, 0.06), (0.02, 0.03, 0.05), stroke=mix(main, WHITE, 0.5),
              stroke_w=0.009, bevel_w=0.008, track=True)
    hull = cv.mask_poly(body)
    cv.paint(hull, cv.vgrad(mix(WHITE, light, 0.15), mix(deep, dark, 0.35), 1.25))
    cv.bevel(hull, mix(light, WHITE, 0.6), mix(deep, INK, 0.35), 0.012, 0.6, 0.5)
    cv.inner_shadow(hull, (0.01, 0.02, 0.03), 0.008, 0.4)

    # 背脊高光
    cv.plate([(0, -0.94), (0.045, -0.60), (0.06, 0.08), (0.03, 0.42), (0, 0.48),
              (-0.03, 0.42), (-0.06, 0.08), (-0.045, -0.60)],
             mix(WHITE, light, 0.45), mix(main, deep, 0.55), stroke=None,
             bevel_hi=mix(WHITE, light, 0.3), bevel_lo=mix(deep, INK, 0.45),
             bevel_w=0.006, track=False, alpha=0.85)

    # ---- 座舱
    canopy = [(0, -0.48), (0.055, -0.38), (0.062, -0.22), (0, -0.12),
              (-0.062, -0.22), (-0.055, -0.38)]
    cmask = cv.mask_poly(canopy)
    cv.glow(cmask, main, 0.035, 0.4)
    cv.plate(canopy, mix(WHITE, light, 0.2), mix(main, deep, 0.5),
             stroke=mix(WHITE, main, 0.4), stroke_w=0.008,
             bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.006, track=False)
    cv.line([(0, -0.45), (0, -0.16)], mix(WHITE, light, 0.2), 0.004, 0.5)
    cv.ellipse((-0.018, -0.36), (0.016, 0.05), mix(WHITE, light, 0.15), alpha=0.9)
    cv.ellipse((0.02, -0.24), (0.010, 0.028), WHITE, alpha=0.65)
    cv.sheen(hull, mix(light, WHITE, 0.5), 0.07, (-1.0, -0.65), (1.0, 0.1), 0.16)

    # ---- 进气道 / 侧装甲
    for sgn in (-1, 1):
        cv.plate([(sgn * 0.15, -0.28), (sgn * 0.27, -0.12), (sgn * 0.25, 0.16), (sgn * 0.14, 0.30)],
                 mix(dark, main, 0.25), dark, stroke=mix(main, light, 0.25),
                 stroke_w=0.005, bevel_w=0.006, track=False)
        for i in range(3):
            y = -0.10 + i * 0.055
            cv.line([(sgn * 0.16, y), (sgn * 0.245, y + 0.01)], (0.01, 0.02, 0.03), 0.006, 0.65)

    # ---- 引擎喷口
    for sgn in (-1, 1):
        cv.plate([(sgn * 0.145, 0.48), (sgn * 0.245, 0.48), (sgn * 0.235, 0.64), (sgn * 0.155, 0.64)],
                 (0.02, 0.025, 0.035), (0.005, 0.007, 0.012),
                 stroke=mix(main, WHITE, 0.25), stroke_w=0.005, bevel_w=0.005, track=False)
        cv.ellipse((sgn * 0.195, 0.585), (0.030, 0.046), mix(main, WHITE, 0.25))
        cv.ellipse((sgn * 0.195, 0.59), (0.016, 0.030), WHITE)

    # ---- 面板刻线与铆点
    cv.panel([(0, -0.60), (0, 0.48)], hull, (0.01, 0.02, 0.03), 0.0035, 0.35,
             hi=mix(WHITE, main, 0.6))
    cv.panel([(0.05, -0.60), (0.10, -0.22), (0.085, 0.34)], hull, (0.01, 0.02, 0.03), 0.003, 0.30)
    cv.panel([(-0.05, -0.60), (-0.10, -0.22), (-0.085, 0.34)], hull, (0.01, 0.02, 0.03), 0.003, 0.30)
    for sgn in (-1, 1):
        for y in (-0.50, -0.34, -0.02, 0.20, 0.36):
            cv.dot((sgn * 0.115, y), 0.007, mix(light, WHITE, 0.4), 0.45)

    # ---- 机翼标识
    for sgn in (-1, 1):
        for i in range(2):
            x = sgn * (0.44 + i * 0.09)
            cv.line([(x, -0.02), (x + sgn * 0.055, 0.055)], mix(GOLD, WHITE, 0.35), 0.008, 0.85)

    # ---- 机头高光 + 传感器
    cv.line([(0, -0.98), (0, -0.78)], WHITE, 0.006, 0.9)
    cv.dot((0, -0.70), 0.014, mix(WHITE, light, 0.2), 1.0)


# ---------------------------------------------------------------- 僚机
def draw_drone(cv, kind):
    cv.name = "drone_" + kind
    pal = {"seeker": PAL["gold"], "cannon": PAL["rose"], "support": PAL["cyan"],
               "swarm": PAL["violet"]}[kind]
    main, light, deep, dark = pal["main"], pal["light"], pal["deep"], pal["dark"]
    neutral_hi = hx("DCE9F5")
    neutral = hx("7E93AD")
    body_dark = hx("141B2A")

    if kind == "seeker":
        sil = [(0, -1.0), (0.30, -0.35), (0.95, 0.25), (0.55, 0.38), (0.16, 0.30),
               (0, 0.75), (-0.16, 0.30), (-0.55, 0.38), (-0.95, 0.25), (-0.30, -0.35)]
        cv.glow(sil, main, 0.10, 0.4)
        cv.paint(cv.mask_poly(sil), INK, 1.0)
        for sgn in (-1, 1):
            cv.plate([(sgn * 0.10, -0.25), (sgn * 0.55, 0.05), (sgn * 0.92, 0.25),
                      (sgn * 0.55, 0.32), (sgn * 0.18, 0.22)],
                     mix(body_dark, main, 0.25), mix(body_dark, deep, 0.5),
                     stroke=mix(light, main, 0.2), stroke_w=0.014,
                     bevel_hi=light, bevel_lo=(0, 0, 0), bevel_w=0.02, track=False)
        cv.plate([(0, -0.98), (0.12, -0.45), (0.16, 0.20), (0.08, 0.48), (0, 0.62),
                  (-0.08, 0.48), (-0.16, 0.20), (-0.12, -0.45)],
                 mix(neutral_hi, main, 0.15), mix(deep, body_dark, 0.35),
                 stroke=mix(light, WHITE, 0.3), stroke_w=0.016,
                 bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.02)
        cv.emissive((0, -0.42), 0.085, mix(WHITE, light, 0.2), main, 0.12, 0.55)
        cv.ellipse((0, 0.05), (0.16, 0.16), mix(dark, main, 0.3), stroke=main, stroke_w=0.02)
        cv.ellipse((0, 0.05), (0.07, 0.07), WHITE)
        cv.line([(0, -0.9), (0, -0.55)], WHITE, 0.012, 0.9)

    elif kind == "cannon":
        sil = [(-0.55, -0.62), (0.55, -0.62), (0.80, -0.30), (0.72, 0.18),
               (0.34, 0.42), (0.14, 0.86), (-0.14, 0.86), (-0.34, 0.42),
               (-0.72, 0.18), (-0.80, -0.30)]
        cv.glow(sil, main, 0.10, 0.35)
        cv.paint(cv.mask_poly(sil), INK, 1.0)
        # 主炮舱
        cv.plate([(-0.48, -0.50), (0.48, -0.50), (0.60, -0.16), (0.44, 0.22),
                  (-0.44, 0.22), (-0.60, -0.16)],
                 mix(body_dark, main, 0.28), mix(body_dark, deep, 0.6),
                 stroke=mix(light, main, 0.2), stroke_w=0.014,
                 bevel_hi=light, bevel_lo=(0, 0, 0), bevel_w=0.02)
        # 侧翼
        for sgn in (-1, 1):
            cv.plate([(sgn * 0.46, -0.34), (sgn * 0.76, -0.26), (sgn * 0.78, 0.02),
                      (sgn * 0.40, 0.12)],
                     mix(neutral_hi, main, 0.35), mix(deep, body_dark, 0.35),
                     stroke=mix(light, WHITE, 0.25), stroke_w=0.013,
                     bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.016, track=False)
            cv.line([(sgn * 0.48, -0.26), (sgn * 0.74, -0.20)],
                    (0.02, 0.02, 0.03), 0.012, 0.55)
        # 单根重炮
        cv.plate([(-0.13, 0.10), (0.13, 0.10), (0.10, 0.84), (-0.10, 0.84)],
                 mix(neutral_hi, main, 0.3), mix(deep, body_dark, 0.45),
                 stroke=mix(light, WHITE, 0.2), stroke_w=0.012,
                 bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.014, track=False)
        for i in range(4):
            y = 0.26 + i * 0.14
            cv.line([(-0.13, y), (0.13, y)], (0.02, 0.03, 0.04), 0.012, 0.6)
        cv.ellipse((0, 0.84), (0.11, 0.06), mix(WHITE, main, 0.3))
        cv.ellipse((0, 0.855), (0.055, 0.032), WHITE)
        # 传感器
        cv.ellipse((0, -0.24), (0.15, 0.13), mix(dark, main, 0.3),
                   stroke=light, stroke_w=0.018)
        cv.ellipse((0, -0.24), (0.065, 0.055), WHITE)
        cv.glow(cv.mask_circle((0, -0.24), 0.10), main, 0.05, 0.5)
        # 尾部喷口
        cv.ellipse((0, -0.48), (0.12, 0.06), (0.03, 0.02, 0.02))
        cv.ellipse((0, -0.48), (0.065, 0.03), mix(WHITE, main, 0.4))

    elif kind == "swarm":
        # 蜂群巢：小六边形 + 三个发射口
        sil = [(0, -0.86), (0.52, -0.5), (0.62, 0.1), (0.30, 0.72), (-0.30, 0.72),
               (-0.62, 0.1), (-0.52, -0.5)]
        cv.glow(sil, main, 0.10, 0.38)
        cv.paint(cv.mask_poly(sil), INK, 1.0)
        cv.plate(sil, mix(neutral_hi, main, 0.30), mix(deep, body_dark, 0.4),
                 stroke=mix(light, WHITE, 0.25), stroke_w=0.014,
                 bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.02)
        for dx in (-0.26, 0.0, 0.26):
            cv.plate([(dx - 0.08, -0.10), (dx + 0.08, -0.10), (dx + 0.06, 0.34), (dx - 0.06, 0.34)],
                     mix(dark, main, 0.3), (0.02, 0.02, 0.03),
                     stroke=mix(light, main, 0.3), stroke_w=0.010, bevel_w=0.010, track=False)
            cv.ellipse((dx, 0.32), (0.045, 0.03), mix(WHITE, main, 0.3))
        cv.ellipse((0, -0.42), (0.13, 0.11), mix(dark, main, 0.35), stroke=light, stroke_w=0.016)
        cv.dot((0, -0.42), 0.05, WHITE)
    else:  # support
        sil = [(0, -0.72), (0.32, -0.55), (0.90, -0.22), (0.72, 0.10), (0.30, 0.20),
               (0.16, 0.55), (0, 0.85), (-0.16, 0.55), (-0.30, 0.20), (-0.72, 0.10),
               (-0.90, -0.22), (-0.32, -0.55)]
        cv.glow(sil, main, 0.10, 0.4)
        cv.paint(cv.mask_poly(sil), INK, 1.0)
        for sgn in (-1, 1):
            cv.plate([(sgn * 0.12, -0.45), (sgn * 0.55, -0.28), (sgn * 0.88, -0.18),
                      (sgn * 0.70, 0.05), (sgn * 0.25, 0.12)],
                     mix(neutral_hi, main, 0.25), mix(deep, body_dark, 0.4),
                     stroke=mix(light, WHITE, 0.25), stroke_w=0.014,
                     bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.02, track=False)
        cv.plate([(0, -0.62), (0.16, -0.25), (0.19, 0.25), (0, 0.78),
                  (-0.19, 0.25), (-0.16, -0.25)],
                 mix(WHITE, main, 0.25), mix(deep, body_dark, 0.3),
                 stroke=light, stroke_w=0.016, bevel_hi=WHITE, bevel_lo=deep,
                 bevel_w=0.02)
        cv.emissive((0, -0.26), 0.10, WHITE, main, 0.12, 0.55)
        cv.ellipse((0, 0.18), (0.16, 0.16), mix(dark, main, 0.25), stroke=light, stroke_w=0.018)
        cv.ellipse((0, 0.18), (0.07, 0.07), WHITE)
        for sgn in (-1, 1):
            cv.line([(sgn * 0.55, -0.22), (sgn * 0.68, -0.10)], light, 0.012, 0.7)
            cv.dot((sgn * 0.86, -0.20), 0.03, mix(WHITE, light, 0.4))


# ---------------------------------------------------------------- 敌机
def mirror(pts):
    return [(-x, y) for x, y in pts]


def enemy_glow(cv, sil, pal, strength=0.32):
    cv.glow(sil, pal["main"], 0.11, strength + 0.10)
    cv.glow(sil, mix(pal["light"], WHITE, 0.35), 0.035, 0.26)
    cv.paint(cv.mask_poly(sil), INK, 1.0)


def draw_enemy(cv, kind):
    cv.name = "enemy_" + kind
    fn = {
        "drifter": enemy_drifter, "wasp": enemy_wasp, "lancer": enemy_lancer,
        "orbiter": enemy_orbiter, "splitter": enemy_splitter, "bulwark": enemy_bulwark,
        "rock": enemy_rock, "sniper": enemy_sniper, "minekeeper": enemy_minekeeper,
        "sidewinder": enemy_sidewinder, "carrier": enemy_carrier,
        "shardling": enemy_shardling,
    }[kind]
    fn(cv)


def enemy_drifter(cv):
    main, light, deep, dark = PAL["rose"]["main"], PAL["rose"]["light"], PAL["rose"]["deep"], PAL["rose"]["dark"]
    sil = [(0, 0.96), (0.26, 0.48), (0.62, 0.24), (0.99, -0.02), (0.94, -0.20),
           (0.58, -0.12), (0.36, -0.22), (0.24, -0.62), (0.10, -0.86),
           (0, -0.70), (-0.10, -0.86), (-0.24, -0.62), (-0.36, -0.22),
           (-0.58, -0.12), (-0.94, -0.20), (-0.99, -0.02), (-0.62, 0.24),
           (-0.26, 0.48)]
    enemy_glow(cv, sil, PAL["rose"], 0.34)

    wing = [(0.14, 0.34), (0.58, 0.16), (0.96, -0.04), (0.90, -0.16),
            (0.55, -0.06), (0.32, -0.12), (0.25, -0.42), (0.17, -0.10)]
    for sgn in (-1, 1):
        pts = [(sgn * x, y) for x, y in wing]
        m = cv.plate(pts, mix(dark, main, 0.22), mix(dark, deep, 0.8),
                     stroke=mix(light, main, 0.25), stroke_w=0.007,
                     bevel_hi=light, bevel_lo=(0.02, 0.01, 0.03), bevel_w=0.010,
                     inner_lo=(0.01, 0.01, 0.02))
        for i in range(3):
            a = (0.20 + i * 0.10) * sgn
            cv.line([(a, 0.24), (a * 2.0, 0.10), (a * 3.4, -0.04)],
                    mix(deep, INK, 0.35), 0.006, 0.55, clip=m)
            cv.line([(a, 0.27), (a * 2.0, 0.13), (a * 3.4, -0.01)],
                    mix(main, WHITE, 0.2), 0.003, 0.35, clip=m)
        # 翼尖能量灯
        cv.emissive((sgn * 0.93, -0.08), 0.022, WHITE, main, 0.045, 0.55)

    # 中央躯体
    body = [(0, 0.95), (0.15, 0.55), (0.20, 0.10), (0.15, -0.30),
            (0.22, -0.54), (0.08, -0.70), (0, -0.62), (-0.08, -0.70),
            (-0.22, -0.54), (-0.15, -0.30), (-0.20, 0.10), (-0.15, 0.55)]
    cv.plate(body, (0.02, 0.02, 0.04), (0.02, 0.01, 0.03),
             stroke=mix(main, WHITE, 0.35), stroke_w=0.009,
             bevel_w=0.008, track=True)
    hull = cv.mask_poly(body)
    cv.paint(hull, cv.vgrad(mix(light, main, 0.35), mix(deep, dark, 0.45), 1.2))
    cv.bevel(hull, mix(light, WHITE, 0.5), mix(deep, INK, 0.3), 0.010, 0.55, 0.5)
    cv.inner_shadow(hull, (0.01, 0.0, 0.02), 0.008, 0.4)

    # 胸腔能量核
    cv.paint(cv.mask_ellipse((0, 0.16), (0.16, 0.19)), mix(dark, deep, 0.4), 1)
    cv.bevel(cv.mask_ellipse((0, 0.16), (0.16, 0.19)), mix(light, main, 0.4),
             (0.01, 0.01, 0.02), 0.008, 0.6, 0.5)
    cv.emissive((0, 0.16), 0.085, mix(WHITE, light, 0.25), main, 0.10, 0.65)
    cv.ellipse((0, 0.16), (0.14, 0.17), None, stroke=main, stroke_w=0.006)

    # 机头感应器
    cv.line([(0, 0.86), (0, 0.62)], WHITE, 0.005, 0.85)
    cv.dot((0, 0.58), 0.018, mix(WHITE, light, 0.3))
    cv.line([(0, 0.50), (0, 0.28)], (0.02, 0.01, 0.03), 0.004, 0.4)

    # 尾部推进器
    for sgn in (-1, 1):
        cv.plate([(sgn * 0.06, -0.50), (sgn * 0.16, -0.50), (sgn * 0.14, -0.66), (sgn * 0.05, -0.66)],
                 (0.03, 0.02, 0.04), (0.01, 0.01, 0.02), stroke=mix(main, WHITE, 0.2),
                 stroke_w=0.004, bevel_w=0.004, track=False)
        cv.ellipse((sgn * 0.10, -0.60), (0.020, 0.030), mix(WHITE, main, 0.45))
        cv.ellipse((sgn * 0.10, -0.60), (0.010, 0.018), WHITE)

    # 面板线
    cv.panel([(0, 0.55), (0, -0.45)], hull, (0.01, 0.0, 0.02), 0.003, 0.4)
    cv.panel([(0.08, 0.30), (0.12, -0.20), (0.10, -0.45)], hull, (0.01, 0.0, 0.02), 0.003, 0.3)
    cv.panel([(-0.08, 0.30), (-0.12, -0.20), (-0.10, -0.45)], hull, (0.01, 0.0, 0.02), 0.003, 0.3)
    for sgn in (-1, 1):
        cv.dot((sgn * 0.30, 0.20), 0.008, light, 0.5)


def enemy_wasp(cv):
    main, light, deep, dark = PAL["pink"]["main"], PAL["pink"]["light"], PAL["pink"]["deep"], PAL["pink"]["dark"]
    sil = [(0, 0.99), (0.16, 0.40), (0.48, 0.12), (0.92, -0.40), (0.98, -0.56),
           (0.58, -0.36), (0.30, -0.40), (0.20, -0.78), (0, -0.60),
           (-0.20, -0.78), (-0.30, -0.40), (-0.58, -0.36), (-0.98, -0.56),
           (-0.92, -0.40), (-0.48, 0.12), (-0.16, 0.40)]
    enemy_glow(cv, sil, PAL["pink"], 0.30)

    wing = [(0.10, 0.20), (0.42, 0.06), (0.90, -0.42), (0.94, -0.52),
            (0.54, -0.34), (0.26, -0.34), (0.16, -0.20)]
    for sgn in (-1, 1):
        pts = [(sgn * x, y) for x, y in wing]
        m = cv.plate(pts, mix(dark, main, 0.28), mix(dark, deep, 0.7),
                     stroke=mix(main, WHITE, 0.3), stroke_w=0.006,
                     bevel_hi=light, bevel_lo=(0.01, 0.0, 0.01), bevel_w=0.008)
        cv.line([(sgn * 0.20, 0.08), (sgn * 0.60, -0.28), (sgn * 0.92, -0.48)],
                mix(GOLD, main, 0.55), 0.005, 0.65, clip=m)

    body = [(0, 0.98), (0.11, 0.45), (0.15, 0.0), (0.11, -0.40),
            (0.06, -0.66), (0, -0.58), (-0.06, -0.66), (-0.11, -0.40),
            (-0.15, 0.0), (-0.11, 0.45)]
    cv.plate(body, (0.02, 0.01, 0.03), (0.01, 0.0, 0.02),
             stroke=mix(main, WHITE, 0.4), stroke_w=0.008, bevel_w=0.007)
    hull = cv.mask_poly(body)
    cv.paint(hull, cv.vgrad(mix(WHITE, light, 0.25), mix(deep, dark, 0.4), 1.25))
    cv.bevel(hull, mix(light, WHITE, 0.5), mix(deep, INK, 0.25), 0.009, 0.55, 0.45)

    # 中央能量条纹
    cv.paint(cv.mask_poly([(0, 0.78), (0.045, 0.30), (0.04, -0.30), (0.02, -0.52),
                           (-0.02, -0.52), (-0.04, -0.30), (-0.045, 0.30)]),
             mix(WHITE, main, 0.25), 0.95)
    cv.glow(cv.mask_poly([(0, 0.78), (0.045, 0.30), (0.04, -0.30), (0.02, -0.52),
                          (-0.02, -0.52), (-0.04, -0.30), (-0.045, 0.30)]),
            main, 0.05, 0.45)
    cv.dot((0, 0.86), 0.02, WHITE)

    # 头部座舱
    cv.ellipse((0, 0.52), (0.045, 0.075), mix(deep, dark, 0.5), stroke=main, stroke_w=0.005)
    cv.ellipse((0, 0.50), (0.022, 0.042), mix(WHITE, light, 0.2), alpha=0.9)

    # 尾喷
    for sgn in (-1, 1):
        cv.ellipse((sgn * 0.14, -0.42), (0.025, 0.045), (0.02, 0.01, 0.02))
        cv.ellipse((sgn * 0.14, -0.44), (0.013, 0.028), mix(WHITE, main, 0.45))

    cv.panel([(0.05, 0.35), (0.075, 0.0), (0.06, -0.35)], hull, (0.01, 0.0, 0.01), 0.0028, 0.3)
    cv.panel([(-0.05, 0.35), (-0.075, 0.0), (-0.06, -0.35)], hull, (0.01, 0.0, 0.01), 0.0028, 0.3)


def enemy_lancer(cv):
    main, light, deep, dark = PAL["violet"]["main"], PAL["violet"]["light"], PAL["violet"]["deep"], PAL["violet"]["dark"]
    sil = [(0, 0.90), (0.20, 0.60), (0.52, 0.72), (0.52, 0.98), (0.64, 0.98),
           (0.64, 0.55), (0.92, 0.18), (1.0, -0.16), (0.62, -0.34),
           (0.30, -0.52), (0.12, -0.70), (0, -0.62), (-0.12, -0.70),
           (-0.30, -0.52), (-0.62, -0.34), (-1.0, -0.16), (-0.92, 0.18),
           (-0.64, 0.55), (-0.64, 0.98), (-0.52, 0.98), (-0.52, 0.72), (-0.20, 0.60)]
    enemy_glow(cv, sil, PAL["violet"], 0.32)

    # 双主炮
    for sgn in (-1, 1):
        x = sgn * 0.58
        cv.plate([(x - 0.055, 0.58), (x + 0.055, 0.58), (x + 0.045, 0.99), (x - 0.045, 0.99)],
                 (0.03, 0.03, 0.05), (0.01, 0.01, 0.02), stroke=mix(main, WHITE, 0.25),
                 stroke_w=0.006, bevel_w=0.006, track=False)
        for i in range(4):
            y = 0.62 + i * 0.085
            cv.line([(x - 0.055, y), (x + 0.055, y)], mix(deep, INK, 0.3), 0.004, 0.55)
        cv.emissive((x, 0.94), 0.020, WHITE, main, 0.04, 0.6)

    # 机翼
    wing = [(0.14, 0.38), (0.46, 0.24), (0.90, -0.06), (0.96, -0.16),
            (0.58, -0.26), (0.26, -0.42), (0.16, -0.20)]
    for sgn in (-1, 1):
        pts = [(sgn * x, y) for x, y in wing]
        m = cv.plate(pts, mix(dark, main, 0.30), mix(dark, deep, 0.75),
                     stroke=mix(light, main, 0.2), stroke_w=0.007,
                     bevel_hi=light, bevel_lo=(0.01, 0.01, 0.03), bevel_w=0.009)
        cv.line([(sgn * 0.22, 0.30), (sgn * 0.58, 0.06), (sgn * 0.92, -0.10)],
                mix(GOLD, main, 0.35), 0.005, 0.7, clip=m)
        cv.line([(sgn * 0.30, 0.10), (sgn * 0.52, 0.00), (sgn * 0.78, -0.12)],
                (0.01, 0.01, 0.03), 0.004, 0.4, clip=m)

    body = [(0, 0.88), (0.14, 0.50), (0.20, 0.10), (0.18, -0.30),
            (0.10, -0.55), (0, -0.62), (-0.10, -0.55), (-0.18, -0.30),
            (-0.20, 0.10), (-0.14, 0.50)]
    cv.plate(body, (0.02, 0.02, 0.05), (0.01, 0.01, 0.03),
             stroke=mix(main, WHITE, 0.4), stroke_w=0.008, bevel_w=0.008)
    hull = cv.mask_poly(body)
    cv.paint(hull, cv.vgrad(mix(WHITE, light, 0.3), mix(deep, dark, 0.4), 1.2))
    cv.bevel(hull, mix(light, WHITE, 0.5), mix(deep, INK, 0.25), 0.009, 0.55, 0.45)

    # 中央核心和瞄准器
    cv.emissive((0, 0.26), 0.075, mix(WHITE, light, 0.3), main, 0.08, 0.6)
    cv.ellipse((0, 0.26), (0.13, 0.14), None, stroke=mix(main, light, 0.3), stroke_w=0.006)
    cv.dot((0, 0.70), 0.02, WHITE)
    cv.line([(0, 0.62), (0, 0.38)], mix(WHITE, light, 0.3), 0.004, 0.8)

    for sgn in (-1, 1):
        cv.plate([(sgn * 0.10, -0.30), (sgn * 0.22, -0.30), (sgn * 0.20, -0.55), (sgn * 0.08, -0.55)],
                 (0.02, 0.02, 0.04), (0.01, 0.01, 0.02), stroke=mix(main, WHITE, 0.2),
                 stroke_w=0.004, bevel_w=0.004, track=False)
        cv.ellipse((sgn * 0.15, -0.47), (0.022, 0.038), mix(WHITE, main, 0.45))

    cv.panel([(0, 0.55), (0, -0.45)], hull, (0.01, 0.01, 0.03), 0.003, 0.4)
    for sgn in (-1, 1):
        cv.dot((sgn * 0.10, 0.06), 0.007, light, 0.55)
        cv.dot((sgn * 0.10, -0.12), 0.007, light, 0.45)


def enemy_orbiter(cv):
    main, light, deep, dark = PAL["violet"]["main"], PAL["violet"]["light"], PAL["violet"]["deep"], PAL["violet"]["dark"]
    sil = [(0, 0.78), (0.40, 0.62), (0.72, 0.28), (0.80, 0), (0.72, -0.28),
           (0.40, -0.62), (0, -0.78), (-0.40, -0.62), (-0.72, -0.28),
           (-0.80, 0), (-0.72, 0.28), (-0.40, 0.62)]
    enemy_glow(cv, sil, PAL["violet"], 0.30)

    # 八边形主体
    cv.plate([(0, 0.72), (0.36, 0.56), (0.64, 0.22), (0.66, -0.22), (0.36, -0.56),
              (0, -0.68), (-0.36, -0.56), (-0.66, -0.22), (-0.64, 0.22), (-0.36, 0.56)],
             (0.03, 0.03, 0.06), (0.01, 0.01, 0.03),
             stroke=mix(light, main, 0.2), stroke_w=0.008, bevel_w=0.010)
    hull = cv.mask_poly([(0, 0.72), (0.36, 0.56), (0.64, 0.22), (0.66, -0.22), (0.36, -0.56),
                         (0, -0.68), (-0.36, -0.56), (-0.66, -0.22), (-0.64, 0.22), (-0.36, 0.56)])
    cv.paint(hull, cv.vgrad(mix(dark, main, 0.25), dark, 1.3))
    cv.bevel(hull, mix(light, WHITE, 0.3), (0.01, 0.01, 0.02), 0.012, 0.5, 0.5)

    # 装甲花辦
    for i in range(8):
        a = math.pi * 2 * i / 8 + math.pi / 8
        c = (math.cos(a) * 0.50, math.sin(a) * 0.50)
        cv.plate([(c[0], c[1] - 0.10), (c[0] + 0.10, c[1]), (c[0], c[1] + 0.10), (c[0] - 0.10, c[1])],
                 mix(dark, main, 0.35), mix(dark, deep, 0.6),
                 stroke=mix(main, light, 0.35), stroke_w=0.004, bevel_w=0.006, track=False)

    # 内环/核心
    cv.ellipse((0, 0), (0.40, 0.40), mix(dark, deep, 0.35), stroke=mix(main, light, 0.25), stroke_w=0.008)
    cv.ellipse((0, 0), (0.30, 0.30), None, stroke=mix(main, light, 0.2), stroke_w=0.005)
    for i in range(6):
        a = math.pi * 2 * i / 6
        cv.line([(math.cos(a) * 0.30, math.sin(a) * 0.30), (math.cos(a) * 0.62, math.sin(a) * 0.62)],
                mix(deep, INK, 0.2), 0.006, 0.6)
    cv.emissive((0, 0), 0.12, mix(WHITE, light, 0.2), main, 0.10, 0.65)
    cv.ellipse((0, 0), (0.18, 0.18), None, stroke=light, stroke_w=0.006)

    # 下方炮口
    cv.plate([(-0.10, 0.52), (0.10, 0.52), (0.08, 0.80), (-0.08, 0.80)],
             mix(deep, main, 0.2), (0.01, 0.01, 0.02), stroke=mix(light, main, 0.3),
             stroke_w=0.005, bevel_w=0.005, track=False)
    cv.ellipse((0, 0.78), (0.055, 0.035), mix(WHITE, main, 0.35))
    cv.ellipse((0, 0.79), (0.026, 0.018), WHITE)


def enemy_orbiter_ring(cv):
    """环绕炮台的旋转环，单独一张，游戏里慢慢转。"""
    main, light, deep = PAL["violet"]["main"], PAL["violet"]["light"], PAL["violet"]["deep"]
    cv.name = "enemy_orbiter_ring"
    for i in range(3):
        a0 = math.pi * 2 * i / 3 + 0.18
        pts = []
        for k in range(9):
            a = a0 + (k / 8) * 0.62
            pts.append((math.cos(a) * 1.00, math.sin(a) * 1.00))
        for k in range(9):
            a = a0 + 0.62 - (k / 8) * 0.62
            pts.append((math.cos(a) * 0.86, math.sin(a) * 0.86))
        cv.glow(pts, main, 0.03, 0.25)
        cv.plate(pts, mix(deep, main, 0.35), (0.01, 0.01, 0.03),
                 stroke=mix(light, main, 0.2), stroke_w=0.010,
                 bevel_hi=light, bevel_lo=(0, 0, 0), bevel_w=0.012, track=False)
        # 弧上的灯
        am = a0 + 0.31
        cv.emissive((math.cos(am) * 0.93, math.sin(am) * 0.93), 0.022, WHITE, main, 0.03, 0.5)


def enemy_splitter(cv):
    main, light, deep, dark = PAL["rose"]["main"], PAL["rose"]["light"], PAL["rose"]["deep"], PAL["rose"]["dark"]
    sil = [(0, 0.88), (0.22, 0.66), (0.62, 0.60), (0.92, 0.30), (0.96, -0.10),
           (0.72, -0.48), (0.40, -0.62), (0.22, -0.84), (0, -0.70), (-0.22, -0.84),
           (-0.40, -0.62), (-0.72, -0.48), (-0.96, -0.10), (-0.92, 0.30),
           (-0.62, 0.60), (-0.22, 0.66)]
    enemy_glow(cv, sil, PAL["rose"], 0.34)

    lobe = [(0.10, 0.55), (0.48, 0.50), (0.86, 0.24), (0.90, -0.06),
            (0.62, -0.40), (0.34, -0.56), (0.16, -0.30), (0.13, 0.10)]
    for sgn in (-1, 1):
        pts = [(sgn * x, y) for x, y in lobe]
        m = cv.plate(pts, mix(dark, main, 0.30), mix(dark, deep, 0.8),
                     stroke=mix(light, main, 0.2), stroke_w=0.007,
                     bevel_hi=light, bevel_lo=(0.01, 0.0, 0.01), bevel_w=0.010)
        for i in range(3):
            cv.line([(sgn * (0.26 + i * 0.12), 0.38 - i * 0.20),
                     (sgn * (0.44 + i * 0.14), 0.20 - i * 0.18)],
                    (0.01, 0.0, 0.01), 0.005, 0.45, clip=m)
        cv.line([(sgn * 0.28, 0.44), (sgn * 0.58, 0.26), (sgn * 0.82, 0.02)],
                mix(GOLD, main, 0.5), 0.004, 0.5, clip=m)
        # 外缘发光胞
        cv.emissive((sgn * 0.78, 0.18), 0.030, WHITE, main, 0.05, 0.5)

    # 中间脊柱与裂缝
    cv.plate([(0, 0.84), (0.10, 0.40), (0.09, -0.20), (0, -0.70),
              (-0.09, -0.20), (-0.10, 0.40)],
             mix(deep, dark, 0.3), (0.01, 0.0, 0.02), stroke=mix(main, WHITE, 0.3),
             stroke_w=0.007, bevel_w=0.006, track=True)
    cv.glow(cv.mask_poly([(0, 0.70), (0.022, 0.20), (0.018, -0.25), (0, -0.58),
                          (-0.018, -0.25), (-0.022, 0.20)]), main, 0.04, 0.6)
    cv.paint(cv.mask_poly([(0, 0.72), (0.026, 0.20), (0.022, -0.28), (0, -0.62),
                           (-0.022, -0.28), (-0.026, 0.20)]), mix(WHITE, main, 0.35), 1)
    cv.dot((0, 0.62), 0.026, WHITE)
    cv.dot((0, 0.10), 0.035, mix(WHITE, light, 0.2))
    cv.dot((0, -0.36), 0.022, mix(WHITE, main, 0.4))

    for sgn in (-1, 1):
        cv.plate([(sgn * 0.18, -0.48), (sgn * 0.30, -0.50), (sgn * 0.28, -0.70), (sgn * 0.16, -0.66)],
                 (0.02, 0.01, 0.03), (0.01, 0.0, 0.02), stroke=mix(main, WHITE, 0.2),
                 stroke_w=0.004, bevel_w=0.004, track=False)
        cv.ellipse((sgn * 0.23, -0.60), (0.018, 0.030), mix(WHITE, main, 0.4))


def enemy_bulwark(cv):
    main, light, deep, dark = PAL["steel"]["main"], PAL["steel"]["light"], PAL["steel"]["deep"], PAL["steel"]["dark"]
    orange = hx("FF8A4B")
    sil = [(0, 0.98), (0.40, 0.90), (0.62, 0.74), (0.94, 0.52), (1.00, 0.18),
           (0.96, -0.18), (0.84, -0.46), (0.56, -0.60), (0.30, -0.80),
           (0, -0.72), (-0.30, -0.80), (-0.56, -0.60), (-0.84, -0.46),
           (-0.96, -0.18), (-1.00, 0.18), (-0.94, 0.52), (-0.62, 0.74), (-0.40, 0.90)]
    enemy_glow(cv, sil, PAL["steel"], 0.24)

    # 深色底甲
    cv.plate([(0, 0.94), (0.42, 0.84), (0.64, 0.66), (0.92, 0.46), (0.96, 0.12),
              (0.90, -0.22), (0.76, -0.44), (0.50, -0.56), (0.28, -0.74),
              (0, -0.66), (-0.28, -0.74), (-0.50, -0.56), (-0.76, -0.44),
              (-0.90, -0.22), (-0.96, 0.12), (-0.92, 0.46), (-0.64, 0.66), (-0.42, 0.84)],
             mix(dark, main, 0.14), dark, stroke=mix(main, WHITE, 0.18),
             stroke_w=0.008, bevel_w=0.014)
    hull = cv.mask_poly([(0, 0.94), (0.42, 0.84), (0.64, 0.66), (0.92, 0.46), (0.96, 0.12),
                         (0.90, -0.22), (0.76, -0.44), (0.50, -0.56), (0.28, -0.74),
                         (0, -0.66), (-0.28, -0.74), (-0.50, -0.56), (-0.76, -0.44),
                         (-0.90, -0.22), (-0.96, 0.12), (-0.92, 0.46), (-0.64, 0.66), (-0.42, 0.84)])
    cv.paint(hull, cv.vgrad(mix(main, light, 0.28), mix(deep, dark, 0.55), 1.25))
    cv.bevel(hull, mix(light, WHITE, 0.45), mix(deep, INK, 0.3), 0.014, 0.55, 0.5)

    # 前部斜面装甲（大块亮钢）
    cv.plate([(0, 0.90), (0.34, 0.80), (0.48, 0.60), (0.30, 0.52), (0, 0.60),
              (-0.30, 0.52), (-0.48, 0.60), (-0.34, 0.80)],
             mix(main, light, 0.55), mix(deep, main, 0.35),
             stroke=mix(light, WHITE, 0.3), stroke_w=0.007,
             bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.010)
    cv.line([(0, 0.88), (0, 0.62)], (0.03, 0.04, 0.06), 0.012, 0.55)

    # 左右舷侧重炮舱
    for sgn in (-1, 1):
        sponson = [(sgn * 0.50, 0.50), (sgn * 0.88, 0.42), (sgn * 0.97, 0.16),
                   (sgn * 0.92, -0.20), (sgn * 0.72, -0.36), (sgn * 0.46, -0.28),
                   (sgn * 0.38, 0.06)]
        m = cv.plate(sponson, mix(dark, main, 0.30), mix(dark, deep, 0.55),
                     stroke=mix(light, main, 0.3), stroke_w=0.006,
                     bevel_hi=light, bevel_lo=(0.01, 0.01, 0.02), bevel_w=0.010)
        # 侧甲亮条
        cv.line([(sgn * 0.52, 0.40), (sgn * 0.86, 0.32), (sgn * 0.93, 0.08)],
                mix(light, WHITE, 0.35), 0.009, 0.8, clip=m)
        # 炮管
        cv.plate([(sgn * 0.68, 0.34), (sgn * 0.84, 0.34), (sgn * 0.82, 0.74), (sgn * 0.70, 0.74)],
                 mix(deep, dark, 0.5), (0.02, 0.02, 0.03),
                 stroke=mix(main, light, 0.25), stroke_w=0.005,
                 bevel_w=0.006, track=False)
        for i in range(3):
            y = 0.42 + i * 0.10
            cv.line([(sgn * 0.69, y), (sgn * 0.83, y)], (0.02, 0.02, 0.03), 0.006, 0.7)
        cv.ellipse((sgn * 0.76, 0.72), (0.048, 0.030), mix(WHITE, orange, 0.35))
        cv.ellipse((sgn * 0.76, 0.73), (0.022, 0.014), WHITE)
        # 警示斜纹
        for i in range(3):
            cv.line([(sgn * (0.58 + i * 0.07), 0.14), (sgn * (0.68 + i * 0.07), 0.32)],
                    mix(orange, light, 0.15), 0.014, 0.85, clip=m)
        # 上方副炮
        cv.plate([(sgn * 0.50, 0.30), (sgn * 0.62, 0.28), (sgn * 0.60, 0.14), (sgn * 0.48, 0.16)],
                 mix(main, light, 0.3), deep, stroke=mix(light, main, 0.3),
                 stroke_w=0.004, bevel_w=0.005, track=False)
        cv.dot((sgn * 0.55, 0.30), 0.018, mix(WHITE, orange, 0.3), 0.9)
        # 铆钉
        for i in range(3):
            cv.dot((sgn * (0.50 + i * 0.10), -0.16), 0.010, light, 0.5)

    # 中央指挥堡
    cv.plate([(0, 0.56), (0.24, 0.42), (0.30, 0.04), (0.22, -0.34), (0.10, -0.52),
              (0, -0.44), (-0.10, -0.52), (-0.22, -0.34), (-0.30, 0.04), (-0.24, 0.42)],
             mix(main, light, 0.35), mix(deep, dark, 0.45),
             stroke=mix(light, WHITE, 0.3), stroke_w=0.006,
             bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.008)
    hull2 = cv.mask_poly([(0, 0.56), (0.24, 0.42), (0.30, 0.04), (0.22, -0.34), (0.10, -0.52),
                          (0, -0.44), (-0.10, -0.52), (-0.22, -0.34), (-0.30, 0.04), (-0.24, 0.42)])
    cv.paint(hull2, cv.vgrad(mix(main, light, 0.5), mix(deep, dark, 0.35)))
    cv.bevel(hull2, mix(light, WHITE, 0.6), mix(deep, INK, 0.2), 0.008, 0.55, 0.45)
    # 舰桥灯带
    cv.ellipse((0, 0.12), (0.17, 0.06), (0.02, 0.03, 0.04), stroke=light, stroke_w=0.004)
    for i in range(3):
        cv.dot((-0.08 + i * 0.08, 0.12), 0.013, mix(WHITE, orange, 0.2), 0.9)
    cv.glow(cv.mask_ellipse((0, 0.12), (0.20, 0.07)), orange, 0.03, 0.3)
    # 顶部传感器
    cv.dot((0, 0.50), 0.022, WHITE, 0.9)
    cv.line([(0, 0.42), (0, 0.26)], (0.02, 0.02, 0.03), 0.010, 0.7)

    # 尾部引擎组
    for sgn in (-1, 1):
        cv.plate([(sgn * 0.18, -0.52), (sgn * 0.38, -0.56), (sgn * 0.34, -0.72), (sgn * 0.14, -0.64)],
                 mix(deep, main, 0.25), (0.02, 0.02, 0.03), stroke=mix(main, light, 0.2),
                 stroke_w=0.005, bevel_w=0.006, track=False)
        cv.ellipse((sgn * 0.26, -0.62), (0.035, 0.045), mix(WHITE, orange, 0.35))
        cv.ellipse((sgn * 0.26, -0.63), (0.018, 0.026), WHITE)
        cv.glow(cv.mask_ellipse((sgn * 0.26, -0.62), (0.05, 0.06)), orange, 0.03, 0.4)

    # 装甲刻线
    for sgn in (-1, 1):
        cv.panel([(sgn * 0.12, 0.42), (sgn * 0.14, 0.0), (sgn * 0.10, -0.36)], hull,
                 (0.02, 0.02, 0.03), 0.003, 0.4)
        cv.panel([(sgn * 0.36, 0.48), (sgn * 0.44, 0.12), (sgn * 0.38, -0.20)], hull,
                 (0.02, 0.02, 0.03), 0.003, 0.35)
        cv.dot((sgn * 0.24, 0.46), 0.007, light, 0.5)
        cv.dot((sgn * 0.42, -0.02), 0.007, light, 0.45)


def enemy_rock(cv):
    steel = PAL["steel"]
    main, light, deep = steel["main"], steel["light"], steel["deep"]
    rnd = np.random.RandomState(20261101)
    n = 13
    pts = []
    for i in range(n):
        a = math.pi * 2 * i / n
        r = 0.82 + rnd.uniform(-0.14, 0.12)
        pts.append((math.cos(a) * r, math.sin(a) * r * 0.96))
    cv.glow(pts, mix(deep, main, 0.4), 0.05, 0.18)
    cv.paint(cv.mask_poly(pts), (0.015, 0.02, 0.03), 1.0)
    m = cv.plate(pts, mix(deep, main, 0.55), (0.02, 0.025, 0.035),
                 stroke=mix(main, light, 0.15), stroke_w=0.006,
                 bevel_hi=mix(light, WHITE, 0.3), bevel_lo=(0, 0, 0), bevel_w=0.015)
    # 凹凸面
    for i in range(9):
        a = rnd.uniform(0, math.pi * 2)
        d = rnd.uniform(0.10, 0.62)
        c = (math.cos(a) * d, math.sin(a) * d * 0.9)
        rr = rnd.uniform(0.06, 0.16)
        shade = rnd.uniform(0.3, 0.8)
        cv.ellipse(c, (rr, rr * rnd.uniform(0.7, 1.0)),
                   mix(deep, (0.02, 0.02, 0.03), shade), alpha=0.8, clip=m)
        cv.ellipse((c[0] - rr * 0.15, c[1] - rr * 0.18), (rr * 0.75, rr * 0.7),
                   None, stroke=mix(light, main, 0.4), stroke_w=0.003, alpha=0.35, clip=m)
    # 裂纹
    for _ in range(5):
        a = rnd.uniform(0, math.pi * 2)
        p0 = (math.cos(a) * 0.2, math.sin(a) * 0.2)
        p1 = (p0[0] + rnd.uniform(-0.3, 0.3), p0[1] + rnd.uniform(-0.3, 0.3))
        p2 = (p1[0] + rnd.uniform(-0.3, 0.3), p1[1] + rnd.uniform(-0.3, 0.3))
        cv.line([p0, p1, p2], (0.01, 0.01, 0.02), 0.006, 0.6, clip=m)
        cv.line([(p0[0] + 0.006, p0[1] + 0.006), (p1[0] + 0.006, p1[1] + 0.006),
                 (p2[0] + 0.006, p2[1] + 0.006)], mix(light, main, 0.3), 0.0025, 0.25, clip=m)
    # 冷色矿脉
    for i in range(4):
        a = rnd.uniform(0, math.pi * 2)
        c = (math.cos(a) * rnd.uniform(0.2, 0.55), math.sin(a) * rnd.uniform(0.2, 0.55))
        cv.glow(cv.mask_circle(c, 0.035), hx("69F5DC"), 0.03, 0.22)
        cv.dot(c, 0.015, mix(WHITE, hx("69F5DC"), 0.4), 0.8)


def enemy_sniper(cv):
    main, light, deep, dark = PAL["cyan"]["main"], PAL["cyan"]["light"], PAL["cyan"]["deep"], PAL["cyan"]["dark"]
    sil = [(0, 1.00), (0.07, 0.66), (0.18, 0.50), (0.58, 0.24), (0.82, 0.06),
           (0.88, -0.12), (0.62, -0.22), (0.30, -0.24), (0.20, -0.46),
           (0.28, -0.68), (0, -0.60), (-0.28, -0.68), (-0.20, -0.46),
           (-0.30, -0.24), (-0.62, -0.22), (-0.88, -0.12), (-0.82, 0.06),
           (-0.58, 0.24), (-0.18, 0.50), (-0.07, 0.66)]
    enemy_glow(cv, sil, PAL["cyan"], 0.30)

    # 后掠翼
    wing = [(0.08, 0.28), (0.40, 0.16), (0.80, -0.02), (0.86, -0.13),
            (0.58, -0.18), (0.24, -0.18), (0.13, -0.32), (0.10, -0.06)]
    for sgn in (-1, 1):
        pts = [(sgn * x, y) for x, y in wing]
        m = cv.plate(pts, mix(dark, main, 0.22), mix(dark, deep, 0.6),
                     stroke=mix(light, main, 0.2), stroke_w=0.006,
                     bevel_hi=light, bevel_lo=(0.01, 0.01, 0.02), bevel_w=0.008)
        cv.line([(sgn * 0.16, 0.22), (sgn * 0.46, 0.10), (sgn * 0.82, -0.06)],
                mix(GOLD, main, 0.45), 0.004, 0.55, clip=m)
        cv.line([(sgn * 0.26, 0.04), (sgn * 0.54, -0.06), (sgn * 0.76, -0.14)],
                (0.01, 0.02, 0.02), 0.004, 0.4, clip=m)
        cv.emissive((sgn * 0.80, -0.06), 0.016, WHITE, main, 0.03, 0.5)

    # 细长机身
    body = [(0, 0.72), (0.10, 0.42), (0.13, 0.0), (0.10, -0.34),
            (0.05, -0.56), (0, -0.50), (-0.05, -0.56), (-0.10, -0.34),
            (-0.13, 0.0), (-0.10, 0.42)]
    cv.plate(body, (0.01, 0.04, 0.04), (0.01, 0.01, 0.02),
             stroke=mix(main, WHITE, 0.35), stroke_w=0.007, bevel_w=0.007)
    hull = cv.mask_poly(body)
    cv.paint(hull, cv.vgrad(mix(WHITE, light, 0.2), mix(deep, dark, 0.4), 1.2))
    cv.bevel(hull, mix(light, WHITE, 0.5), mix(deep, INK, 0.3), 0.009, 0.5, 0.45)

    # 主炮导轨（画在最上层）
    cv.plate([(-0.075, 0.28), (0.075, 0.28), (0.055, 0.93), (-0.055, 0.93)],
             mix(dark, deep, 0.65), (0.01, 0.02, 0.02),
             stroke=mix(main, light, 0.3), stroke_w=0.006,
             bevel_hi=light, bevel_lo=(0.01, 0.01, 0.02), bevel_w=0.006, track=False)
    for i in range(5):
        y = 0.38 + i * 0.11
        cv.line([(-0.078, y), (0.078, y)], mix(deep, INK, 0.35), 0.006, 0.75)
        cv.line([(-0.026, y + 0.012), (0.026, y + 0.012)], mix(WHITE, main, 0.25), 0.004, 0.7)
    cv.line([(0, 0.32), (0, 0.91)], mix(WHITE, main, 0.2), 0.014, 0.75)
    cv.glow(cv.mask_ellipse((0, 0.94), (0.06, 0.07)), main, 0.04, 0.55)
    cv.ellipse((0, 0.95), (0.038, 0.055), mix(WHITE, light, 0.25))
    cv.ellipse((0, 0.955), (0.016, 0.032), WHITE)

    # 瞄准镜
    cv.ellipse((0, 0.20), (0.095, 0.11), mix(dark, deep, 0.5), stroke=main, stroke_w=0.006)
    cv.ellipse((0, 0.20), (0.05, 0.062), mix(WHITE, main, 0.2))
    cv.dot((0, 0.20), 0.02, WHITE)
    cv.glow(cv.mask_circle((0, 0.20), 0.08), main, 0.04, 0.45)
    cv.line([(0, 0.31), (0, 0.28)], light, 0.004, 0.7)

    # 翼根稳定鳍
    for sgn in (-1, 1):
        cv.plate([(sgn * 0.11, -0.26), (sgn * 0.20, -0.34), (sgn * 0.18, -0.56), (sgn * 0.08, -0.46)],
                 mix(dark, main, 0.25), dark, stroke=mix(main, light, 0.25),
                 stroke_w=0.004, bevel_w=0.005, track=False)
        # 尾喷
        cv.ellipse((sgn * 0.10, -0.44), (0.024, 0.038), (0.02, 0.03, 0.03))
        cv.ellipse((sgn * 0.10, -0.45), (0.013, 0.024), mix(WHITE, main, 0.4))
        cv.glow(cv.mask_ellipse((sgn * 0.10, -0.45), (0.03, 0.045)), main, 0.03, 0.4)

    cv.panel([(0.05, 0.40), (0.07, 0.06), (0.05, -0.28)], hull, (0.01, 0.01, 0.02), 0.0028, 0.35)
    cv.panel([(-0.05, 0.40), (-0.07, 0.06), (-0.05, -0.28)], hull, (0.01, 0.01, 0.02), 0.0028, 0.35)


def enemy_minekeeper(cv):
    main, light, deep, dark = PAL["gold"]["main"], PAL["gold"]["light"], PAL["gold"]["deep"], PAL["gold"]["dark"]
    sil = [(0, 0.76), (0.30, 0.70), (0.52, 0.58), (0.86, 0.54), (1.02, 0.28),
           (0.98, -0.10), (0.82, -0.38), (0.52, -0.50), (0.28, -0.68),
           (0, -0.62), (-0.28, -0.68), (-0.52, -0.50), (-0.82, -0.38),
           (-0.98, -0.10), (-1.02, 0.28), (-0.86, 0.54), (-0.52, 0.58), (-0.30, 0.70)]
    enemy_glow(cv, sil, PAL["gold"], 0.32)

    # 主体（深黄装甲）
    cv.plate([(0, 0.70), (0.34, 0.60), (0.58, 0.42), (0.66, 0.10), (0.60, -0.28),
              (0.40, -0.48), (0.20, -0.58), (0, -0.52), (-0.20, -0.58), (-0.40, -0.48),
              (-0.60, -0.28), (-0.66, 0.10), (-0.58, 0.42), (-0.34, 0.60)],
             mix(dark, main, 0.28), mix(dark, deep, 0.6),
             stroke=mix(light, main, 0.25), stroke_w=0.008, bevel_w=0.012)
    hull = cv.mask_poly([(0, 0.70), (0.34, 0.60), (0.58, 0.42), (0.66, 0.10), (0.60, -0.28),
                         (0.40, -0.48), (0.20, -0.58), (0, -0.52), (-0.20, -0.58), (-0.40, -0.48),
                         (-0.60, -0.28), (-0.66, 0.10), (-0.58, 0.42), (-0.34, 0.60)])
    cv.paint(hull, cv.vgrad(mix(main, light, 0.30), mix(deep, dark, 0.5), 1.2))
    cv.bevel(hull, mix(light, WHITE, 0.4), mix(deep, INK, 0.3), 0.012, 0.55, 0.5)

    # 左右布雷鼓（暗色环 + 金色辐条 + 发光核心）
    for sgn in (-1, 1):
        c = (sgn * 0.70, 0.18)
        cv.glow(cv.mask_circle(c, 0.25), main, 0.045, 0.3)
        cv.ellipse(c, (0.29, 0.29), mix(dark, deep, 0.35), stroke=mix(light, main, 0.3), stroke_w=0.007)
        cv.paint(cv.mask_circle(c, 0.245), cv.radial(c, 0.245, mix(main, light, 0.25), mix(deep, dark, 0.5)), 0.9)
        for k in range(6):
            a = k * math.pi / 3
            cv.line([(c[0] + math.cos(a) * 0.08, c[1] + math.sin(a) * 0.08),
                     (c[0] + math.cos(a) * 0.21, c[1] + math.sin(a) * 0.21)],
                    mix(deep, INK, 0.25), 0.014, 0.8)
        cv.ellipse(c, (0.075, 0.075), mix(WHITE, main, 0.35))
        cv.dot(c, 0.035, WHITE)
        # 挂钩
        cv.line([(sgn * 0.50, 0.30), (sgn * 0.62, 0.38), (sgn * 0.68, 0.30)],
                mix(light, main, 0.3), 0.010, 0.8)
        # 侧舱灯
        cv.dot((sgn * 0.86, -0.10), 0.016, mix(WHITE, main, 0.4), 0.85)

    # 中央驾驶舱
    cv.ellipse((0, 0.28), (0.19, 0.22), mix(dark, deep, 0.4), stroke=light, stroke_w=0.007)
    cv.paint(cv.mask_ellipse((0, 0.28), (0.17, 0.20)),
             cv.radial((0, 0.28), 0.20, mix(WHITE, light, 0.4), mix(deep, dark, 0.4)), 1)
    cv.ellipse((0, 0.25), (0.075, 0.10), mix(WHITE, light, 0.2))
    cv.dot((0, 0.24), 0.034, WHITE)
    cv.glow(cv.mask_ellipse((0, 0.28), (0.18, 0.20)), main, 0.04, 0.35)

    # 底部投雷舱门
    cv.plate([(-0.20, -0.30), (0.20, -0.30), (0.16, -0.48), (-0.16, -0.48)],
             mix(deep, dark, 0.3), (0.02, 0.02, 0.03), stroke=mix(main, light, 0.3),
             stroke_w=0.005, bevel_w=0.005, track=False)
    for i in range(3):
        cv.line([(-0.16 + i * 0.05, -0.32), (-0.12 + i * 0.05, -0.46)],
                mix(WHITE, main, 0.35), 0.006, 0.7)
    cv.glow(cv.mask_rect((-0.20, -0.48), (0.20, -0.30)), main, 0.035, 0.3)

    # 顶部护架 / 天线
    for sgn in (-1, 1):
        cv.line([(sgn * 0.14, -0.44), (sgn * 0.28, -0.62), (sgn * 0.24, -0.74)],
                mix(main, light, 0.3), 0.010, 0.9)
    cv.line([(0, -0.52), (0, -0.80)], mix(main, light, 0.3), 0.010, 0.9)
    cv.dot((0, -0.82), 0.026, mix(WHITE, main, 0.4))
    cv.glow(cv.mask_circle((0, -0.82), 0.05), main, 0.03, 0.35)

    # 引擎
    for sgn in (-1, 1):
        cv.ellipse((sgn * 0.28, -0.52), (0.035, 0.05), (0.03, 0.02, 0.01))
        cv.ellipse((sgn * 0.28, -0.53), (0.017, 0.028), mix(WHITE, main, 0.45))
    cv.panel([(0, 0.16), (0, -0.18)], hull, (0.03, 0.02, 0.01), 0.003, 0.45)
    cv.panel([(0.14, 0.14), (0.20, -0.10), (0.16, -0.30)], hull, (0.03, 0.02, 0.01), 0.003, 0.35)
    cv.panel([(-0.14, 0.14), (-0.20, -0.10), (-0.16, -0.30)], hull, (0.03, 0.02, 0.01), 0.003, 0.35)


def enemy_sidewinder(cv):
    main, light, deep, dark = PAL["violet"]["main"], PAL["violet"]["light"], PAL["violet"]["deep"], PAL["violet"]["dark"]
    sil = [(0.10, 0.94), (0.52, 0.62), (0.92, 0.30), (1.00, -0.06),
           (0.82, -0.40), (0.46, -0.56), (0.22, -0.80), (0.04, -0.66),
           (-0.10, -0.44), (-0.34, -0.30), (-0.52, -0.06), (-0.40, 0.10),
           (-0.10, 0.06), (-0.16, 0.46)]
    enemy_glow(cv, sil, PAL["violet"], 0.32)

    # 主弯刀翼
    blade = [(0.12, 0.66), (0.48, 0.44), (0.86, 0.18), (0.98, -0.08),
             (0.80, -0.30), (0.46, -0.40), (0.22, -0.28), (0.18, 0.02)]
    m = cv.plate(blade, mix(dark, main, 0.32), mix(dark, deep, 0.8),
                 stroke=mix(light, main, 0.2), stroke_w=0.007,
                 bevel_hi=light, bevel_lo=(0.01, 0.01, 0.03), bevel_w=0.010)
    cv.line([(0.20, 0.52), (0.54, 0.30), (0.88, 0.06), (0.94, -0.14)],
            mix(GOLD, main, 0.4), 0.005, 0.7, clip=m)
    for i in range(3):
        cv.line([(0.26 + i * 0.10, 0.26 - i * 0.10), (0.50 + i * 0.10, 0.10 - i * 0.10)],
                (0.01, 0.01, 0.03), 0.004, 0.4, clip=m)

    # 小侧翼
    cv.plate([(-0.06, 0.18), (-0.40, 0.02), (-0.50, -0.08), (-0.32, -0.20),
              (-0.12, -0.24), (-0.04, -0.06)],
             mix(dark, main, 0.22), mix(dark, deep, 0.7),
             stroke=mix(main, light, 0.25), stroke_w=0.006,
             bevel_hi=light, bevel_lo=(0.01, 0.0, 0.02), bevel_w=0.008, track=False)

    # 机身
    body = [(0.10, 0.92), (0.26, 0.52), (0.28, 0.06), (0.20, -0.34),
            (0.08, -0.58), (-0.04, -0.48), (-0.08, -0.14), (-0.04, 0.24),
            (0.02, 0.62)]
    cv.plate(body, (0.02, 0.02, 0.05), (0.01, 0.01, 0.03),
             stroke=mix(main, WHITE, 0.35), stroke_w=0.007, bevel_w=0.008)
    hull = cv.mask_poly(body)
    cv.paint(hull, cv.vgrad(mix(WHITE, light, 0.3), mix(deep, dark, 0.4), 1.2))
    cv.bevel(hull, mix(light, WHITE, 0.5), mix(deep, INK, 0.3), 0.009, 0.55, 0.45)

    cv.emissive((0.12, 0.34), 0.06, mix(WHITE, light, 0.2), main, 0.07, 0.6)
    cv.dot((0.13, 0.74), 0.02, WHITE)
    cv.line([(0.12, 0.66), (0.12, 0.42)], mix(WHITE, light, 0.3), 0.004, 0.8)

    # 尾部大喷口
    cv.plate([(0.00, -0.42), (0.18, -0.38), (0.16, -0.58), (-0.02, -0.62)],
             (0.02, 0.02, 0.04), (0.01, 0.01, 0.02), stroke=mix(main, WHITE, 0.25),
             stroke_w=0.005, bevel_w=0.005, track=False)
    cv.ellipse((0.08, -0.48), (0.05, 0.07), mix(WHITE, main, 0.35))
    cv.ellipse((0.08, -0.49), (0.026, 0.042), WHITE)

    cv.panel([(0.16, 0.56), (0.21, 0.16), (0.15, -0.22)], hull, (0.01, 0.01, 0.03), 0.003, 0.35)
    cv.panel([(0.06, 0.30), (0.02, -0.02), (0.04, -0.30)], hull, (0.01, 0.01, 0.03), 0.003, 0.3)


def enemy_carrier(cv):
    main, light, deep, dark = PAL["blue"]["main"], PAL["blue"]["light"], PAL["blue"]["deep"], PAL["blue"]["dark"]
    amber = hx("FFB45C")
    sil = [(0, 0.80), (0.42, 0.78), (0.80, 0.60), (0.98, 0.24), (0.98, -0.36),
           (0.76, -0.62), (0.34, -0.72), (0, -0.80), (-0.34, -0.72),
           (-0.76, -0.62), (-0.98, -0.36), (-0.98, 0.24), (-0.80, 0.60), (-0.42, 0.78)]
    enemy_glow(cv, sil, PAL["blue"], 0.28)

    # 主舰体（矩形机库）
    cv.plate([(0, 0.68), (0.44, 0.66), (0.78, 0.50), (0.90, 0.18), (0.88, -0.30),
              (0.66, -0.54), (0.30, -0.62), (0, -0.70), (-0.30, -0.62), (-0.66, -0.54),
              (-0.88, -0.30), (-0.90, 0.18), (-0.78, 0.50), (-0.44, 0.66)],
             mix(dark, main, 0.22), mix(dark, deep, 0.6),
             stroke=mix(light, main, 0.25), stroke_w=0.008, bevel_w=0.012)
    hull = cv.mask_poly([(0, 0.68), (0.44, 0.66), (0.78, 0.50), (0.90, 0.18), (0.88, -0.30),
                         (0.66, -0.54), (0.30, -0.62), (0, -0.70), (-0.30, -0.62), (-0.66, -0.54),
                         (-0.88, -0.30), (-0.90, 0.18), (-0.78, 0.50), (-0.44, 0.66)])
    cv.paint(hull, cv.vgrad(mix(main, light, 0.32), mix(deep, dark, 0.55), 1.15))
    cv.bevel(hull, mix(light, WHITE, 0.4), mix(deep, INK, 0.3), 0.010, 0.5, 0.5)

    # 两条弹射跑道
    for sgn in (-1, 1):
        x0, x1 = sgn * 0.34, sgn * 0.12
        cv.plate([(x1 - 0.10, 0.42), (x1 + 0.10, 0.42), (x0 + 0.08, -0.28), (x0 - 0.08, -0.28)],
                 (0.01, 0.02, 0.03), (0.01, 0.01, 0.02),
                 stroke=mix(light, main, 0.3), stroke_w=0.006,
                 bevel_w=0.004, track=False)
        # 甲板灯
        for i in range(5):
            y = 0.34 - i * 0.13
            xx = x1 + (x0 - x1) * (i / 4)
            cv.dot((xx, y), 0.013, mix(WHITE, amber, 0.25), 0.9)
        # 机库发光口
        cv.plate([(sgn * 0.08, 0.56), (sgn * 0.30, 0.54), (sgn * 0.28, 0.40), (sgn * 0.08, 0.42)],
                 mix(amber, WHITE, 0.15), mix(amber, deep, 0.4), stroke=amber,
                 stroke_w=0.004, track=False)
        cv.glow(cv.mask_rect((sgn * 0.08, 0.40), (sgn * 0.30, 0.56)), amber, 0.035, 0.35)

    # 舰桥
    cv.plate([(0, 0.18), (0.18, 0.10), (0.20, -0.18), (0.10, -0.38), (0, -0.48),
              (-0.10, -0.38), (-0.20, -0.18), (-0.18, 0.10)],
             mix(main, light, 0.45), mix(deep, dark, 0.4),
             stroke=mix(light, WHITE, 0.3), stroke_w=0.006,
             bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.008)
    cv.ellipse((0, -0.10), (0.13, 0.07), mix(dark, main, 0.2), stroke=light, stroke_w=0.004)
    for i in range(3):
        cv.dot((-0.08 + i * 0.08, -0.10), 0.012, mix(WHITE, main, 0.3), 0.8)

    # 天线
    cv.line([(0, 0.10), (0, -0.30)], (0.01, 0.01, 0.02), 0.014, 0.8)
    cv.line([(0, 0.10), (0, -0.30)], mix(light, main, 0.3), 0.005, 0.8)
    cv.dot((0, -0.32), 0.025, mix(WHITE, amber, 0.2))

    # 侧舷炮
    for sgn in (-1, 1):
        for y in (0.16, -0.10, -0.34):
            cv.plate([(sgn * 0.72, y - 0.04), (sgn * 0.86, y - 0.04),
                      (sgn * 0.84, y + 0.06), (sgn * 0.74, y + 0.06)],
                     mix(deep, main, 0.3), (0.01, 0.01, 0.02),
                     stroke=mix(light, main, 0.2), stroke_w=0.003,
                     bevel_w=0.003, track=False)
            cv.dot((sgn * 0.80, y + 0.04), 0.012, mix(WHITE, amber, 0.3), 0.8)

    # 面板
    for sgn in (-1, 1):
        cv.panel([(sgn * 0.38, 0.56), (sgn * 0.44, 0.10), (sgn * 0.40, -0.42)], hull,
                 (0.01, 0.01, 0.02), 0.003, 0.35)
        cv.dot((sgn * 0.56, 0.42), 0.008, light, 0.5)
        cv.dot((sgn * 0.60, 0.30), 0.008, light, 0.4)


def enemy_shardling(cv):
    main, light, deep, dark = PAL["pink"]["main"], PAL["pink"]["light"], PAL["pink"]["deep"], PAL["pink"]["dark"]
    sil = [(0, 0.96), (0.20, 0.40), (0.72, -0.10), (0.30, -0.20),
           (0.10, -0.60), (0, -0.40), (-0.10, -0.60), (-0.30, -0.20),
           (-0.72, -0.10), (-0.20, 0.40)]
    enemy_glow(cv, sil, PAL["pink"], 0.30)
    cv.plate(sil, mix(dark, main, 0.25), mix(dark, deep, 0.65),
             stroke=mix(light, main, 0.3), stroke_w=0.008,
             bevel_hi=light, bevel_lo=(0.01, 0, 0.01), bevel_w=0.009)
    body = [(0, 0.90), (0.10, 0.40), (0.12, -0.10), (0.06, -0.44), (0, -0.36),
            (-0.06, -0.44), (-0.12, -0.10), (-0.10, 0.40)]
    cv.plate(body, mix(WHITE, light, 0.25), mix(deep, dark, 0.5),
             stroke=mix(light, WHITE, 0.2), stroke_w=0.006, bevel_w=0.006, track=False)
    cv.glow(cv.mask_circle((0, 0.12), 0.10), main, 0.06, 0.55)
    cv.dot((0, 0.12), 0.07, mix(WHITE, light, 0.2))
    cv.dot((0, 0.12), 0.03, WHITE)
    cv.line([(0, 0.82), (0, 0.52)], WHITE, 0.004, 0.7)
    cv.emissive((0, -0.30), 0.035, WHITE, main, 0.03, 0.4)


# ---------------------------------------------------------------- Boss
def rot_pts(pts, deg, scale=1.0):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return [( (x * c - y * s) * scale, (x * s + y * c) * scale) for x, y in pts]


def draw_boss(cv, kind):
    cv.name = "boss_" + kind
    if kind == "gatekeeper":
        boss_gatekeeper(cv)
    elif kind == "weaver":
        boss_weaver(cv)
    else:
        boss_helios(cv)


def boss_gatekeeper(cv):
    main, light, deep, dark = PAL["rose"]["main"], PAL["rose"]["light"], PAL["rose"]["deep"], PAL["rose"]["dark"]
    body_hex = [(math.cos(math.pi / 6 + i * math.pi / 3) * 0.72,
                 math.sin(math.pi / 6 + i * math.pi / 3) * 0.72) for i in range(6)]
    pod = [(0.36, -0.34), (0.80, -0.46), (1.06, -0.30), (1.12, 0.02),
           (0.94, 0.30), (0.62, 0.34), (0.38, 0.20)]
    pieces = [cv.mask_poly(body_hex)] + [cv.mask_poly([(s * x, y) for x, y in pod]) for s in (-1, 1)]
    sil = np.maximum.reduce(pieces)
    cv.glow(sil, main, 0.075, 0.34)
    cv.glow(sil, hx("FFB0C4"), 0.03, 0.22)
    cv.paint(sil, INK, 1.0)

    # 侧舷重炮舱
    for sgn in (-1, 1):
        pts = [(sgn * x, y) for x, y in pod]
        m = cv.plate(pts, mix(dark, main, 0.26), mix(dark, deep, 0.6),
                     stroke=mix(light, main, 0.25), stroke_w=0.010,
                     bevel_hi=light, bevel_lo=(0.01, 0.0, 0.02), bevel_w=0.016)
        # 外缘炮管
        cv.plate([(sgn * 0.94, -0.28), (sgn * 1.10, -0.32), (sgn * 1.12, 0.24), (sgn * 0.98, 0.26)],
                 mix(deep, dark, 0.4), (0.01, 0.0, 0.02), stroke=mix(main, WHITE, 0.3),
                 stroke_w=0.007, bevel_w=0.008, track=False)
        for i in range(3):
            y = -0.24 + i * 0.14
            cv.line([(sgn * 1.02, y), (sgn * 1.12, y)], mix(WHITE, main, 0.4), 0.008, 0.8)
            cv.glow(cv.mask_rect((sgn * 1.02, y - 0.012), (sgn * 1.13, y + 0.012)), main, 0.02, 0.5)
        # 装甲条纹
        cv.line([(sgn * 0.48, -0.24), (sgn * 0.78, -0.32), (sgn * 1.02, -0.24)],
                mix(GOLD, main, 0.3), 0.010, 0.75, clip=m)
        cv.line([(sgn * 0.48, 0.16), (sgn * 0.80, 0.22)], mix(light, main, 0.4), 0.006, 0.5, clip=m)
        for i in range(4):
            cv.dot((sgn * (0.55 + i * 0.10), 0.02), 0.012, light, 0.5)
        # 散热槽
        for i in range(3):
            yy = -0.06 + i * 0.07
            cv.line([(sgn * 0.56, yy), (sgn * 0.74, yy + 0.02)], (0.01, 0.0, 0.02), 0.012, 0.65)

    # 主堡垒（六边形）
    body = body_hex
    cv.plate(body, mix(dark, main, 0.2), mix(dark, deep, 0.65),
             stroke=mix(main, WHITE, 0.3), stroke_w=0.010,
             bevel_hi=light, bevel_lo=(0.01, 0.0, 0.02), bevel_w=0.016)
    hull = cv.mask_poly(body)
    cv.paint(hull, cv.vgrad(mix(dark, deep, 0.6), dark, 1.1))
    cv.bevel(hull, mix(light, WHITE, 0.35), (0.02, 0.0, 0.02), 0.014, 0.6, 0.55)

    # 内层六边形装甲
    inner = [(x * 0.82, y * 0.82) for x, y in body]
    cv.plate(inner, mix(dark, main, 0.30), mix(dark, deep, 0.7),
             stroke=mix(light, main, 0.3), stroke_w=0.007,
             bevel_hi=light, bevel_lo=(0.01, 0.0, 0.02), bevel_w=0.010, track=False)
    # 旋转内环纹理（静态）
    for i in range(3):
        a0 = i * 2.094 + 0.2
        pts = []
        for k in range(10):
            a = a0 + k / 9 * 0.8
            pts.append((math.cos(a) * 0.40, math.sin(a) * 0.40))
        cv.line(pts, mix(GOLD, main, 0.35), 0.008, 0.55)
    for i in range(6):
        a = i * math.pi / 3 + math.pi / 6
        cv.line([(math.cos(a) * 0.42, math.sin(a) * 0.42),
                 (math.cos(a) * 0.70, math.sin(a) * 0.70)], (0.02, 0.0, 0.02), 0.006, 0.45)

    # 中央巨眼
    socket = cv.mask_circle((0, 0), 0.30)
    cv.paint(socket, (0.01, 0.0, 0.02), 1)
    cv.bevel(socket, mix(light, WHITE, 0.3), (0.0, 0.0, 0.0), 0.012, 0.6, 0.6)
    cv.glow(cv.mask_circle((0, 0), 0.24), main, 0.06, 0.65)
    cv.ellipse((0, 0), (0.20, 0.25), cv.radial((0, 0), 0.26, mix(light, WHITE, 0.35), deep),
               stroke=mix(light, WHITE, 0.2), stroke_w=0.006)
    cv.ellipse((0, 0), (0.11, 0.15), mix(WHITE, main, 0.15))
    cv.ellipse((0, 0), (0.05, 0.075), WHITE)
    cv.ellipse((-0.03, -0.05), (0.025, 0.04), WHITE, alpha=0.9)

    # 外围金环与铆钉
    ring_pts = []
    for k in range(72):
        a = math.pi * 2 * k / 72
        ring_pts.append((math.cos(a) * 0.56, math.sin(a) * 0.56))
    cv.line(ring_pts + [ring_pts[0]], mix(GOLD, main, 0.35), 0.006, 0.6)
    for i in range(12):
        a = math.pi * 2 * i / 12
        cv.dot((math.cos(a) * 0.63, math.sin(a) * 0.63), 0.011, mix(WHITE, main, 0.5), 0.65)
    # 顶部天线
    cv.line([(0, -0.70), (0, -0.88)], mix(light, main, 0.3), 0.010, 0.8)
    cv.dot((0, -0.90), 0.022, WHITE, 0.9)
    # 底部炮口
    cv.plate([(-0.10, 0.62), (0.10, 0.62), (0.07, 0.80), (-0.07, 0.80)],
             mix(deep, dark, 0.3), (0.01, 0.0, 0.02), stroke=mix(main, WHITE, 0.3),
             stroke_w=0.005, bevel_w=0.005, track=False)
    cv.ellipse((0, 0.79), (0.06, 0.035), mix(WHITE, main, 0.3))
    cv.ellipse((0, 0.80), (0.03, 0.018), WHITE)


def boss_gatekeeper_ring(cv):
    cv.name = "boss_gatekeeper_ring"
    main, light, deep = PAL["rose"]["main"], PAL["rose"]["light"], PAL["rose"]["deep"]
    for i in range(3):
        pts = []
        a0 = i * math.pi * 2 / 3 + 0.15
        for k in range(16):
            a = a0 + k / 15 * 0.74
            pts.append((math.cos(a) * 1.14, math.sin(a) * 1.14))
        for k in range(16):
            a = a0 + 0.74 - k / 15 * 0.74
            pts.append((math.cos(a) * 1.06, math.sin(a) * 1.06))
        cv.glow(pts, main, 0.018, 0.28)
        cv.plate(pts, mix(deep, main, 0.45), (0.01, 0.0, 0.02),
                 stroke=mix(light, main, 0.25), stroke_w=0.006,
                 bevel_hi=light, bevel_lo=(0, 0, 0), bevel_w=0.008, track=False)
        am = a0 + 0.37
        cv.emissive((math.cos(am) * 1.10, math.sin(am) * 1.10), 0.020, WHITE, main, 0.025, 0.55)


def boss_weaver(cv):
    main, light, deep, dark = PAL["violet"]["main"], PAL["violet"]["light"], PAL["violet"]["deep"], PAL["violet"]["dark"]
    rose = hx("FF6F91")
    # 两片弯刀翼（用密集贝塞尔近似）
    wing = []
    for k in range(21):
        t = k / 20
        # 三次贝塞尔： (0.14,-0.62) -> (1.25,-0.86) -> (1.30,0.24) -> (0.30,0.64)
        x = (1 - t) ** 3 * 0.14 + 3 * (1 - t) ** 2 * t * 1.25 + 3 * (1 - t) * t ** 2 * 1.30 + t ** 3 * 0.30
        y = (1 - t) ** 3 * -0.62 + 3 * (1 - t) ** 2 * t * -0.86 + 3 * (1 - t) * t ** 2 * 0.24 + t ** 3 * 0.64
        wing.append((x, y))
    for k in range(21):
        t = 1 - k / 20
        x = (1 - t) ** 3 * 0.14 + 3 * (1 - t) ** 2 * t * 0.66 + 3 * (1 - t) * t ** 2 * 0.64 + t ** 3 * 0.22
        y = (1 - t) ** 3 * -0.62 + 3 * (1 - t) ** 2 * t * -0.34 + 3 * (1 - t) * t ** 2 * 0.06 + t ** 3 * 0.38
        wing.append((x, y))
    spindle = [(0, -0.78), (0.16, -0.30), (0.18, 0.20), (0.08, 0.66), (0, 0.78),
               (-0.08, 0.66), (-0.18, 0.20), (-0.16, -0.30)]
    shards = [(math.cos(i * math.pi * 2 / 3) * 0.92, math.sin(i * math.pi * 2 / 3) * 0.92) for i in range(3)]
    pieces = [cv.mask_poly(spindle)]
    for s in (-1, 1):
        pieces.append(cv.mask_poly([(s * x, y) for x, y in wing]))
    for c in shards:
        pieces.append(cv.mask_circle(c, 0.075))
    sil = np.maximum.reduce(pieces)
    cv.glow(sil, main, 0.07, 0.38)
    cv.glow(sil, hx("D8C8FF"), 0.025, 0.22)
    cv.paint(sil, INK, 1.0)

    for sgn in (-1, 1):
        pts = [(sgn * x, y) for x, y in wing]
        m = cv.plate(pts, mix(dark, main, 0.32), mix(dark, deep, 0.75),
                     stroke=mix(light, main, 0.25), stroke_w=0.008,
                     bevel_hi=light, bevel_lo=(0.01, 0.0, 0.03), bevel_w=0.014)
        # 金色翼脉
        for i in range(3):
            off = 0.10 + i * 0.09
            cv.line([(sgn * (0.34 + off * 0.3), -0.40 + off),
                     (sgn * (0.72 + off * 0.4), -0.06 + off * 0.5),
                     (sgn * (0.96 - off * 0.3), 0.34 - off * 0.4)],
                    mix(GOLD, main, 0.4), 0.006 - i * 0.001, 0.6, clip=m)
        # 翼上装甲接缝
        for i in range(4):
            cv.line([(sgn * (0.38 + i * 0.12), -0.30 + i * 0.14),
                     (sgn * (0.72 + i * 0.10), -0.10 + i * 0.12)],
                    (0.01, 0.0, 0.03), 0.004, 0.45, clip=m)
        # 外缘灯带
        edge = [(sgn * x, y) for x, y in wing[:8]]
        cv.line(edge, mix(light, WHITE, 0.2), 0.007, 0.55, clip=m)
        cv.emissive((sgn * 1.22, 0.02), 0.022, WHITE, main, 0.03, 0.55)

    # 中央纺锤
    cv.plate(spindle, mix(dark, main, 0.3), mix(dark, deep, 0.7),
             stroke=mix(light, main, 0.3), stroke_w=0.009,
             bevel_hi=light, bevel_lo=(0.01, 0.0, 0.03), bevel_w=0.012)
    hull = cv.mask_poly(spindle)
    cv.paint(hull, cv.vgrad(mix(light, main, 0.35), mix(deep, dark, 0.5), 1.25))
    cv.bevel(hull, mix(light, WHITE, 0.5), mix(deep, INK, 0.3), 0.010, 0.6, 0.5)
    # 核心眼
    cv.paint(cv.mask_circle((0, 0), 0.20), (0.01, 0.0, 0.02), 1)
    cv.glow(cv.mask_circle((0, 0), 0.17), rose, 0.05, 0.6)
    cv.ellipse((0, 0), (0.13, 0.16), cv.radial((0, 0), 0.16, mix(WHITE, rose, 0.2), rose))
    cv.ellipse((0, 0), (0.06, 0.08), WHITE)
    cv.ellipse((-0.02, -0.03), (0.02, 0.03), WHITE, alpha=0.9)
    # 纺锤刻线
    cv.panel([(0, -0.58), (0, -0.24)], hull, (0.01, 0.0, 0.03), 0.004, 0.5)
    cv.panel([(0, 0.28), (0, 0.60)], hull, (0.01, 0.0, 0.03), 0.004, 0.5)
    cv.panel([(0.10, -0.20), (0.13, 0.22)], hull, (0.01, 0.0, 0.03), 0.0035, 0.4)
    cv.panel([(-0.10, -0.20), (-0.13, 0.22)], hull, (0.01, 0.0, 0.03), 0.0035, 0.4)

    # 三枚轨道碎片（静态，游戏里整张再转）
    for i, c in enumerate(shards):
        cv.glow(cv.mask_circle(c, 0.06), main, 0.03, 0.35)
        cv.plate([(c[0], c[1] - 0.085), (c[0] + 0.055, c[1]),
                  (c[0], c[1] + 0.085), (c[0] - 0.055, c[1])],
                 mix(light, main, 0.3), mix(deep, main, 0.4),
                 stroke=mix(light, WHITE, 0.25), stroke_w=0.005,
                 bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.006, track=False)
        cv.dot(c, 0.018, WHITE, 0.9)


def boss_weaver_ring(cv):
    cv.name = "boss_weaver_ring"
    main, light, deep = PAL["violet"]["main"], PAL["violet"]["light"], PAL["violet"]["deep"]
    # 旋转的细环 + 三个光点
    for k in range(90):
        a0 = math.pi * 2 * k / 90
        pass
    arcs = [(0.2, 1.15), (2.3, 1.15), (4.4, 1.15)]
    for a0, r in arcs:
        pts = []
        for k in range(20):
            a = a0 + k / 19 * 0.62
            pts.append((math.cos(a) * r, math.sin(a) * r))
        cv.line(pts, mix(light, main, 0.3), 0.008, 0.5)
    for i in range(3):
        a = i * math.pi * 2 / 3 + 0.5
        cv.emissive((math.cos(a) * 1.02, math.sin(a) * 1.02), 0.022, WHITE, main, 0.03, 0.55)


def boss_helios(cv):
    main, light, deep, dark = PAL["gold"]["main"], PAL["gold"]["light"], PAL["gold"]["deep"], PAL["gold"]["dark"]
    rose = hx("FF6F91")
    # 四条幅臂
    arm = [(-0.16, -0.42), (0.16, -0.42), (0.30, -1.02), (0.16, -1.14),
           (0, -1.05), (-0.16, -1.14), (-0.30, -1.02)]
    pieces = [cv.mask_circle((0, 0), 0.58)]
    for i in range(4):
        pieces.append(cv.mask_poly(rot_pts(arm, i * 90)))
    sil = np.maximum.reduce(pieces)
    cv.glow(sil, main, 0.07, 0.38)
    cv.glow(sil, hx("FFE9B0"), 0.025, 0.25)
    cv.paint(sil, INK, 1.0)

    for i in range(4):
        pts = rot_pts(arm, i * 90)
        m = cv.plate(pts, mix(dark, main, 0.25), mix(dark, deep, 0.65),
                     stroke=mix(light, main, 0.3), stroke_w=0.008,
                     bevel_hi=light, bevel_lo=(0.01, 0.0, 0.01), bevel_w=0.012)
        # 臂上灯
        tip = rot_pts([(0, -0.86)], i * 90)[0]
        cv.emissive(tip, 0.030, WHITE, rose, 0.04, 0.6)
        mid = rot_pts([(0, -0.58)], i * 90)[0]
        cv.dot(mid, 0.025, mix(WHITE, main, 0.3), 0.8)
        # 金色刻线
        cv.line(rot_pts([(-0.12, -0.46), (-0.14, -0.98)], i * 90), mix(light, WHITE, 0.2),
                0.004, 0.5, clip=m)
        cv.line(rot_pts([(0.12, -0.46), (0.14, -0.98)], i * 90), (0.03, 0.02, 0.0),
                0.004, 0.45, clip=m)

    # 环状外壳
    for r, a0, a1, w, col in ((0.70, 0.15, 2.9, 0.018, mix(light, main, 0.3)),
                              (0.70, 3.25, 6.0, 0.018, mix(light, main, 0.3)),
                              (0.84, 0.6, 2.4, 0.010, mix(GOLD, main, 0.4)),
                              (0.84, 3.6, 5.8, 0.010, mix(GOLD, main, 0.4))):
        pts = []
        for k in range(40):
            a = a0 + (a1 - a0) * k / 39
            pts.append((math.cos(a) * r, math.sin(a) * r))
        cv.line(pts, col, w, 0.8)
    # 虚线环
    for k in range(24):
        a = k * math.pi / 12
        if 2.9 < (a % (math.pi * 2)) < 3.25:
            continue
        cv.dot((math.cos(a) * 0.90, math.sin(a) * 0.90), 0.007, mix(WHITE, main, 0.4), 0.5)

    # 核心
    core = cv.mask_circle((0, 0), 0.56)
    cv.paint(core, (0.02, 0.02, 0.03), 1)
    cv.paint(core, cv.radial((0, 0), 0.56, mix(WHITE, main, 0.2), mix(deep, dark, 0.4)))
    cv.bevel(core, mix(light, WHITE, 0.5), (0.01, 0.0, 0.01), 0.016, 0.6, 0.5)
    # 内核热斑
    cv.glow(cv.mask_circle((0, 0), 0.40), main, 0.05, 0.7)
    cv.ellipse((0, 0), (0.34, 0.34), cv.radial((0, 0), 0.34, WHITE, mix(main, deep, 0.5)))
    cv.ellipse((0, 0), (0.20, 0.20), None, stroke=mix(WHITE, main, 0.2), stroke_w=0.005)
    cv.ellipse((0, 0), (0.12, 0.12), WHITE)
    # 内旋纹
    for i in range(3):
        pts = []
        for k in range(16):
            a = i * 2.1 + k / 15 * 1.1
            pts.append((math.cos(a) * (0.22 + i * 0.05), math.sin(a) * (0.22 + i * 0.05)))
        cv.line(pts, mix(WHITE, main, 0.3), 0.005, 0.55)
    # 外圈铆点
    for i in range(8):
        a = i * math.pi / 4 + math.pi / 8
        cv.dot((math.cos(a) * 0.50, math.sin(a) * 0.50), 0.014, mix(WHITE, main, 0.4), 0.6)


def boss_helios_ring(cv):
    cv.name = "boss_helios_ring"
    main, light, deep = PAL["gold"]["main"], PAL["gold"]["light"], PAL["gold"]["deep"]
    violet = PAL["violet"]["main"]
    for r, seg, col, w in ((1.06, 3, mix(violet, light, 0.25), 0.012),
                           (1.18, 4, mix(main, light, 0.2), 0.010)):
        for i in range(seg):
            a0 = i * math.pi * 2 / seg + 0.12
            pts = []
            for k in range(18):
                a = a0 + k / 17 * (math.pi * 2 / seg - 0.32)
                pts.append((math.cos(a) * r, math.sin(a) * r))
            cv.line(pts, col, w, 0.55)
            am = a0 + (math.pi * 2 / seg - 0.32) * 0.5
            cv.emissive((math.cos(am) * r, math.sin(am) * r), 0.018, WHITE, main, 0.025, 0.5)


# ---------------------------------------------------------------- 弹体 / 道具
def draw_bullet(cv, kind):
    cv.name = "bullet_" + kind
    if kind == "player_bolt":
        # 竖直胶囊，白芯 + 柔光；运行时按武器线染色
        cv.glow(cv.mask_ellipse((0, 0), (0.28, 0.9)), (0.4, 1.0, 0.95), 0.12, 0.45)
        cv.ellipse((0, 0), (0.22, 0.62), (0.85, 0.98, 1.0), alpha=0.9)
        cv.ellipse((0, 0), (0.12, 0.50), WHITE)
        cv.ellipse((0, 0.05), (0.05, 0.34), WHITE)
    elif kind == "player_lance":
        cv.glow(cv.mask_poly([(0, -1), (0.30, 0), (0, 1), (-0.30, 0)]), (1, 0.6, 0.8), 0.12, 0.45)
        cv.plate([(0, -1), (0.26, 0), (0, 1), (-0.26, 0)], (1, 1, 1), (0.75, 0.85, 1),
                 stroke=(0.9, 0.7, 0.85), stroke_w=0.05, bevel_w=0.04, track=False)
        cv.line([(0, -0.85), (0, 0.85)], WHITE, 0.06, 1)
    elif kind == "player_micro":
        cv.glow(cv.mask_poly([(0, -0.95), (0.35, 0.5), (0, 0.8), (-0.35, 0.5)]), (1, 0.85, 0.5), 0.12, 0.4)
        cv.plate([(0, -0.95), (0.22, 0.1), (0.35, 0.5), (0.12, 0.75), (0, 0.5),
                  (-0.12, 0.75), (-0.35, 0.5), (-0.22, 0.1)],
                 (0.95, 0.97, 1), (0.7, 0.76, 0.86), stroke=(1, 0.9, 0.6),
                 stroke_w=0.05, bevel_w=0.04, track=False)
        cv.ellipse((0, -0.15), (0.1, 0.28), WHITE)
    elif kind == "enemy_orb":
        cv.glow(cv.mask_circle((0, 0), 0.62), WHITE, 0.16, 0.4)
        cv.ellipse((0, 0), (0.62, 0.62), cv.radial((0, 0), 0.62, (1, 1, 1), (0.55, 0.62, 0.72)))
        cv.ellipse((0, 0), (0.46, 0.46), cv.radial((0, 0), 0.46, WHITE, (0.82, 0.86, 0.92)))
        cv.ellipse((0, 0), (0.22, 0.22), WHITE)
        cv.ellipse((-0.16, -0.18), (0.1, 0.08), WHITE, alpha=0.9)
    elif kind == "enemy_needle":
        cv.glow(cv.mask_poly([(0, -1), (0.18, 0.2), (0, 1), (-0.18, 0.2)]), WHITE, 0.14, 0.4)
        cv.plate([(0, -1), (0.20, 0.15), (0.10, 0.35), (0, 0.95), (-0.10, 0.35), (-0.20, 0.15)],
                 (1, 1, 1), (0.7, 0.78, 0.86), stroke=(0.9, 0.95, 1),
                 stroke_w=0.05, bevel_w=0.04, track=False)
        cv.ellipse((0, -0.2), (0.08, 0.34), WHITE)
    elif kind == "enemy_heavy":
        cv.glow(cv.mask_circle((0, 0), 0.7), WHITE, 0.18, 0.45)
        cv.ellipse((0, 0), (0.7, 0.7), cv.radial((0, 0), 0.7, (1, 1, 1), (0.5, 0.55, 0.65)))
        cv.ellipse((0, 0), (0.54, 0.54), None, stroke=(0.95, 0.95, 1), stroke_w=0.06)
        cv.ellipse((0, 0), (0.38, 0.38), cv.radial((0, 0), 0.38, WHITE, (0.8, 0.85, 0.9)))
        cv.ellipse((0, 0), (0.18, 0.18), WHITE)
    elif kind == "enemy_mine":
        cv.glow(cv.mask_circle((0, 0), 0.5), WHITE, 0.14, 0.4)
        for i in range(8):
            a = i * math.pi / 4
            cv.line([(math.cos(a) * 0.28, math.sin(a) * 0.28),
                     (math.cos(a) * 0.95, math.sin(a) * 0.95)],
                    (0.85, 0.9, 0.95), 0.09, 0.9)
            cv.dot((math.cos(a) * 0.95, math.sin(a) * 0.95), 0.07, WHITE)
        cv.ellipse((0, 0), (0.52, 0.52), cv.radial((0, 0), 0.52, WHITE, (0.4, 0.45, 0.55)))
        cv.ellipse((0, 0), (0.30, 0.30), (0.05, 0.06, 0.08))
        cv.ellipse((0, 0), (0.15, 0.15), WHITE)
    elif kind == "flame":
        cv.glow(cv.mask_poly([(0, -0.9), (0.55, 0.05), (0.28, 0.55), (0, 0.95),
                              (-0.28, 0.55), (-0.55, 0.05)]), (0.7, 1.0, 1.0), 0.16, 0.55)
        cv.plate([(0, -0.95), (0.42, 0.0), (0.24, 0.42), (0, 0.55), (-0.24, 0.42), (-0.42, 0.0)],
                 (1, 1, 1), (0.55, 0.95, 1.0), stroke=None, track=False)
        cv.plate([(0, -0.7), (0.22, 0.05), (0.1, 0.4), (0, 0.5), (-0.1, 0.4), (-0.22, 0.05)],
                 WHITE, (0.9, 1, 1), stroke=None, track=False)
    elif kind == "shield":
        cv.glow(cv.mask_circle((0, 0), 0.98), (1, 1, 1), 0.08, 0.25)
        cv.line([(math.cos(a) * 0.9, math.sin(a) * 0.9)
                 for a in [k * math.pi / 24 for k in range(49)]], (0.9, 0.95, 1), 0.035, 0.75)
        # 六边形栅格
        for i in range(12):
            a = i * math.pi / 6
            cv.line([(math.cos(a) * 0.35, math.sin(a) * 0.35),
                     (math.cos(a) * 0.88, math.sin(a) * 0.88)], (0.8, 0.9, 1), 0.012, 0.45)
        for rr in (0.45, 0.69):
            cv.line([(math.cos(a) * rr, math.sin(a) * rr)
                     for a in [k * math.pi / 24 for k in range(49)]], (0.8, 0.9, 1), 0.012, 0.4)
        cv.ellipse((0, 0), (0.9, 0.9), None, stroke=(1, 1, 1), stroke_w=0.02)
    elif kind == "glow_soft":
        cv.paint(cv.mask_circle((0, 0), 0.98), cv.radial((0, 0), 0.98, WHITE, (0, 0, 0)), 0.9)
    elif kind == "aura_ring":
        cv.glow(cv.mask_circle((0, 0), 0.98), (1, 1, 1), 0.05, 0.3)
        for i in range(24):
            a0 = i * math.pi / 12
            pts = [(math.cos(a0 + k / 6 * 0.18) * 0.9, math.sin(a0 + k / 6 * 0.18) * 0.9)
                   for k in range(7)]
            cv.line(pts, (1, 1, 1), 0.014, 0.65)
        cv.ellipse((0, 0), (0.9, 0.9), None, stroke=(0.9, 0.95, 1), stroke_w=0.008)


def draw_crate(cv, kind):
    cv.name = "crate_" + kind
    pal = {
        "weapon": PAL["gold"], "drone": PAL["blue"],
        "shield": PAL["violet"], "charge": PAL["pink"],
        "prism": PAL["rose"], "swarm": PAL["gold"], "reflect": PAL["gold"],
        "life": PAL["rose"], "gem": PAL["gold"], "swap": PAL["cyan"],
    }[kind]
    main, light, deep, dark = pal["main"], pal["light"], pal["deep"], pal["dark"]
    box = [(-0.62, -0.80), (0.62, -0.80), (0.80, -0.60), (0.80, 0.62),
           (0.62, 0.80), (-0.62, 0.80), (-0.80, 0.62), (-0.80, -0.60)]
    cv.glow(cv.mask_poly(box), main, 0.09, 0.45)
    cv.paint(cv.mask_poly(box), INK, 1)
    m = cv.plate(box, mix(dark, main, 0.35), mix(dark, deep, 0.7),
                 stroke=mix(light, main, 0.3), stroke_w=0.022,
                 bevel_hi=light, bevel_lo=(0.01, 0.01, 0.02), bevel_w=0.03)
    cv.ellipse((0, 0), (0.40, 0.40), (0.02, 0.02, 0.03), stroke=mix(light, main, 0.3), stroke_w=0.02)
    cv.ellipse((0, 0), (0.34, 0.34), cv.radial((0, 0), 0.34, mix(dark, main, 0.3), dark))
    if kind == "weapon":
        # 向上的双箭头
        for dy in (-0.05, 0.15):
            cv.line([(-0.20, 0.10 + dy), (0, -0.14 + dy), (0.20, 0.10 + dy)],
                    mix(WHITE, main, 0.2), 0.075, 0.95)
    elif kind == "drone":
        # 小机翼
        cv.plate([(0, -0.22), (0.26, 0.08), (0.10, 0.20), (0, 0.10),
                  (-0.10, 0.20), (-0.26, 0.08)],
                 mix(WHITE, main, 0.2), mix(main, deep, 0.4),
                 stroke=light, stroke_w=0.02, bevel_w=0.02, track=False)
        cv.dot((0, 0.02), 0.06, WHITE)
    elif kind == "shield":
        hexp = [(math.cos(math.pi / 6 + i * math.pi / 3) * 0.24,
                 math.sin(math.pi / 6 + i * math.pi / 3) * 0.24) for i in range(6)]
        cv.plate(hexp, mix(WHITE, main, 0.25), mix(main, deep, 0.5),
                 stroke=light, stroke_w=0.02, bevel_w=0.02, track=False)
        cv.ellipse((0, 0), (0.08, 0.08), WHITE)
    elif kind == "charge":
        # 闪电
        cv.line([(0.10, -0.26), (-0.12, 0.02), (0.04, 0.02), (-0.08, 0.28)],
                mix(WHITE, main, 0.15), 0.085, 0.95)
    elif kind in ("prism", "reflect"):
        # 棱镜：贯穿长矛 / 力场：同心泡
        if kind == "prism":
            cv.plate([(0, -0.30), (0.13, 0), (0, 0.30), (-0.13, 0)],
                     mix(WHITE, main, 0.2), mix(main, deep, 0.5),
                     stroke=light, stroke_w=0.02, bevel_w=0.02, track=False)
            cv.dot((0, 0), 0.05, WHITE)
        else:
            cv.ellipse((0, 0), (0.26, 0.26), None, stroke=mix(WHITE, main, 0.2), stroke_w=0.025)
            cv.ellipse((0, 0), (0.15, 0.15), None, stroke=light, stroke_w=0.018)
            cv.dot((0, 0), 0.05, WHITE)
    elif kind == "swarm":
        # 蜂群：三枚小弹头
        for dx, dy in ((-0.14, 0.08), (0.14, 0.08), (0, -0.14)):
            cv.plate([(dx, dy - 0.12), (dx + 0.07, dy + 0.08), (dx, dy + 0.13), (dx - 0.07, dy + 0.08)],
                     mix(WHITE, main, 0.25), mix(main, deep, 0.5),
                     stroke=light, stroke_w=0.014, bevel_w=0.012, track=False)
    elif kind == "life":
        # 命：十字
        cv.plate([(-0.07, -0.26), (0.07, -0.26), (0.07, 0.26), (-0.07, 0.26)],
                 mix(WHITE, main, 0.2), mix(main, deep, 0.5), stroke=light,
                 stroke_w=0.016, bevel_w=0.014, track=False)
        cv.plate([(-0.26, -0.07), (0.26, -0.07), (0.26, 0.07), (-0.26, 0.07)],
                 mix(WHITE, main, 0.2), mix(main, deep, 0.5), stroke=light,
                 stroke_w=0.016, bevel_w=0.014, track=False)
    elif kind == "gem":
        cv.glow(cv.mask_poly([(0, -0.34), (0.24, 0), (0, 0.34), (-0.24, 0)]), main, 0.06, 0.5)
        cv.plate([(0, -0.34), (0.24, 0), (0, 0.34), (-0.24, 0)],
                 mix(WHITE, main, 0.3), mix(main, deep, 0.55),
                 stroke=mix(WHITE, light, 0.4), stroke_w=0.018,
                 bevel_hi=WHITE, bevel_lo=deep, bevel_w=0.02, track=False)
        cv.line([(0, -0.34), (0, 0.34)], mix(WHITE, light, 0.4), 0.012, 0.6)
        cv.line([(-0.24, 0), (0.24, 0)], mix(WHITE, light, 0.4), 0.012, 0.6)
    elif kind == "swap":
        # 换线：两条反向箭头
        cv.line([(-0.20, -0.12), (0.16, -0.12), (0.06, -0.24)], mix(WHITE, main, 0.2), 0.075, 0.95)
        cv.line([(0.20, 0.12), (-0.16, 0.12), (-0.06, 0.24)], mix(WHITE, main, 0.2), 0.075, 0.95)
    # 四角铆钉
    for sx in (-1, 1):
        for sy in (-1, 1):
            cv.dot((sx * 0.52, sy * 0.62), 0.055, light, 0.7)
            cv.dot((sx * 0.52, sy * 0.62), 0.025, WHITE, 0.9)


# ---------------------------------------------------------------- 生成
def main():
    only = None
    if "--only" in sys.argv:
        only = set(sys.argv[sys.argv.index("--only") + 1].split(","))

    def want(*names):
        if only is None:
            return True
        return any(n in only or n.split("_")[0] in only for n in names)

    os.makedirs(OUT, exist_ok=True)
    print("Aurora Rift artgen ->", OUT)

    if want("player"):
        for key, pid in (("teal", "pulse"), ("rose", "prism"), ("gold", "swarm")):
            pal = dict(PAL[key])
            pal["id"] = pid
            cv = Cv(512, 3)
            draw_player(cv, pal)
            cv.save()

    if want("drone"):
        for k in ("seeker", "cannon", "support", "swarm"):
            cv = Cv(192, 4)
            draw_drone(cv, k)
            cv.save()

    enemy_names = ("drifter", "wasp", "lancer", "orbiter", "splitter", "bulwark",
                   "rock", "sniper", "minekeeper", "sidewinder", "carrier", "shardling")
    if only is None or "enemy" in only or any(k in only or ("enemy_" + k) in only for k in enemy_names):
        picked = [k for k in enemy_names
                  if only is None or "enemy" in only or k in only or ("enemy_" + k) in only]
        for k in picked:
            cv = Cv(384, 3)
            draw_enemy(cv, k)
            cv.save()
        if only is None or "enemy" in only or "orbiter" in only or "enemy_orbiter_ring" in only:
            cv = Cv(384, 3)
            enemy_orbiter_ring(cv)
            cv.save()

    if want("boss"):
        for k in ("gatekeeper", "weaver", "helios"):
            cv = Cv(640, 3)
            draw_boss(cv, k)
            cv.save()
        for k, fn in (("gatekeeper", boss_gatekeeper_ring),
                      ("weaver", boss_weaver_ring),
                      ("helios", boss_helios_ring)):
            cv = Cv(640, 3)
            fn(cv)
            cv.save()

    if want("bullet"):
        for k, px in (("player_bolt", 96), ("player_lance", 96), ("player_micro", 96),
                      ("enemy_orb", 96), ("enemy_needle", 96), ("enemy_heavy", 128),
                      ("enemy_mine", 128), ("flame", 128), ("shield", 256),
                      ("glow_soft", 128), ("aura_ring", 256)):
            cv = Cv(px, 4, extent=0.44)
            draw_bullet(cv, k)
            cv.save()

    if want("crate"):
        for k in ("weapon", "drone", "shield", "charge", "prism", "swarm",
                  "reflect", "life", "gem", "swap"):
            cv = Cv(192, 4)
            draw_crate(cv, k)
            cv.save()

    print("done")


if __name__ == "__main__":
    main()
