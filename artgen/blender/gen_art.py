#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Aurora Rift — Blender 硬表面机体渲染器（v0.19.0 美术管线）

把机体在 Blender 里程序化建出来，用正交顶视相机 + 三点布光渲成透明 PNG，
原位替换 Assets/Resources/Ships/ 下的精灵。**不改任何 C# 代码**：
ShipArt 的约定是「画布高度 → PPU、pivot 居中、主体外缘 = 0.46 × 边长」，
所以这里负责把每台机体归一化到半径 0.92（= 0.46 × 画布 2 单位）并居中。

用法:
  blender --background --factory-startup --python artgen/blender/gen_art.py -- \
      --only player_pulse,boss_gatekeeper,bullet_enemy_needle [--out DIR] [--samples 128]
  blender ... -- --list            # 看已实现的资产
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_OUT = os.path.join(ROOT, "Assets", "Resources", "Ships")
ART_EXTENT = 0.46          # 与 ShipArt.ArtExtent 一致
VIEW = 2.0                 # 正交相机视野跨度（世界单位）


# ----------------------------------------------------------------- 场景装置

def reset_scene():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for item in list(coll):
            if item.users == 0:
                coll.remove(item)
    random.seed(7)


def setup_render(samples=128):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices_for_type('OPTIX')
        for d in prefs.devices:
            d.use = True
        sc.cycles.device = 'GPU'
    except Exception:
        sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 6
    sc.cycles.transparent_max_bounces = 8
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.image_settings.color_depth = '8'
    sc.render.image_settings.compression = 15
    # 霓虹配色靠 Standard 才不会被 AgX 洗灰
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.view_settings.exposure = 0.0


def setup_world():
    world = bpy.data.worlds.get("AuroraWorld") or bpy.data.worlds.new("AuroraWorld")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.012, 0.018, 0.035, 1)
    bg.inputs[1].default_value = 0.12
    bpy.context.scene.world = world


def setup_camera():
    cam_data = bpy.data.cameras.new("OrthoCam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = VIEW
    cam = bpy.data.objects.new("OrthoCam", cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = (0, 0, 8)
    cam.rotation_euler = (0, 0, 0)          # 相机朝 -Z，正好俯视 XY 平面
    bpy.context.scene.camera = cam


def _area(name, loc, rot, size, energy, color):
    d = bpy.data.lights.new(name, 'AREA')
    d.shape = 'RECTANGLE'
    d.size, d.size_y = size
    d.energy = energy
    d.color = color
    ob = bpy.data.objects.new(name, d)
    bpy.context.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = rot
    return ob


def setup_lights():
    # 顶光 key：小而亮，专门制造局部金属高光与硬阴影（大面光会把暗装甲照成纸片）
    _area("Key", (1.1, 1.5, 3.2), (math.radians(18), math.radians(12), 0), (2.2, 2.2), 170, (1.0, 0.96, 0.90))
    # 冷色 fill：极弱，只把阴影从死黑里提一点
    _area("Fill", (-2.2, -1.6, 2.2), (math.radians(34), math.radians(-24), 0), (4.0, 4.0), 22, (0.55, 0.68, 1.0))
    # 轮廓光 rim：低角度长条，勾出霓虹边——硬表面的"金属感"主要靠这条
    _area("Rim", (2.8, -0.2, 0.5), (math.radians(84), 0, math.radians(96)), (2.4, 0.6), 420, (0.42, 0.55, 1.0))
    # 底部轮廓：从 -Y 侧托一道暗青，让机体边缘与深色背景分开
    _area("Under", (-0.6, -2.6, 0.8), (math.radians(70), 0, math.radians(-14)), (2.2, 0.6), 120, (0.35, 0.85, 0.95))


# ----------------------------------------------------------------- 材质

def mat_pbr(name, base, metallic=0.88, rough=0.34, emit=None, emit_strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*base, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    if emit is not None:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1)
        bsdf.inputs["Emission Strength"].default_value = emit_strength
    return m


def mat_emit(name, color, strength=1.6):
    """
    纯 Emission 着色器（不是 Principled 的自发光口）。
    用 Principled 时，420W 轮廓光在发光条上的镜面反射会把颜色直接打到 1.0，
    霓虹全变纯白——色相就没了。发光件必须对布光免疫，亮度只由 strength 决定。
    """
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL':
            nt.nodes.remove(n)
    out = nt.nodes["Material Output"]
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (*color, 1.0)
    em.inputs[1].default_value = strength
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    return m


def mat_glass(accent):
    m = mat_pbr("glass", (0.02, 0.035, 0.05), metallic=0.3, rough=0.06,
                emit=tuple(c * 0.25 for c in accent), emit_strength=0.6)
    return m


def mat_mask():
    m = bpy.data.materials.get("__mask")
    if m:
        return m
    m = bpy.data.materials.new("__mask")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL':
            nt.nodes.remove(n)
    out = nt.nodes["Material Output"]
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (1, 1, 1, 1)
    em.inputs[1].default_value = 1.0
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    return m


def hex_rgb(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


# ----------------------------------------------------------------- 造型原语

def poly_prism(name, pts, z0, z1, mat, bevel=0.012, loc=(0, 0, 0), rot_z=0.0):
    """2D 轮廓沿 Z 挤出成硬表面块——翼面、六边形要塞、弹体都用它。"""
    me = bpy.data.meshes.new(name)
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    bm = bmesh.new()
    vs = [bm.verts.new((x, y, z0)) for x, y in pts]
    face = bm.faces.new(vs)
    res = bmesh.ops.extrude_face_region(bm, geom=[face])
    top = [e for e in res['geom'] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, 0, z1 - z0), verts=top)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob.data.materials.append(mat)
    ob.location = loc
    ob.rotation_euler = (0, 0, rot_z)
    if bevel > 0:
        m = ob.modifiers.new("bev", 'BEVEL')
        m.width = bevel
        m.segments = 2
        m.limit_method = 'ANGLE'
    return ob


def box(name, size, loc, mat, bevel=0.01, rot=(0, 0, 0)):
    w, h, d = size
    pts = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
    return poly_prism(name, pts, -d / 2, d / 2, mat, bevel, loc, rot[2])


def cyl(name, radius, depth, loc, mat, verts=24, rot=(0, 0, 0), bevel=0.006):
    pts = [(math.cos(i * 2 * math.pi / verts) * radius,
            math.sin(i * 2 * math.pi / verts) * radius) for i in range(verts)]
    ob = poly_prism(name, pts, -depth / 2, depth / 2, mat, bevel, loc)
    ob.rotation_euler = rot
    return ob


def arc_ring(name, r0, r1, a0, a1, z0, z1, mat, mat2=None, seg=18, loc=(0, 0, 0), bevel=0.0):
    """环带的一段（Boss 的旋转环层用）。"""
    pts = []
    for i in range(seg + 1):
        a = a0 + (a1 - a0) * i / seg
        pts.append((math.cos(a) * r1, math.sin(a) * r1))
    for i in range(seg + 1):
        a = a1 - (a1 - a0) * i / seg
        pts.append((math.cos(a) * r0, math.sin(a) * r0))
    return poly_prism(name, pts, z0, z1, mat, bevel, loc)


def mirrored(builder, **kw):
    """左右对称：把一台机翼造两遍（x 取反），比 Mirror 修改器更可控。"""
    a = builder(1.0, **kw)
    b = builder(-1.0, **kw)
    return [a, b]


# ----------------------------------------------------------------- 机体

def build_player_pulse(accent):
    """玩家机（脉冲线）：匕首形拦截机，机头 +Y，双引擎，霓虹能量线。"""
    hull = mat_pbr("hull_steel", (0.030, 0.040, 0.062), 0.95, 0.22)
    armor = mat_pbr("armor_plate", (0.060, 0.078, 0.110), 0.90, 0.32)
    dark = mat_pbr("frame", (0.012, 0.016, 0.026), 0.70, 0.55)
    neon = mat_emit("neon", accent, 1.35)
    glass = mat_pbr("canopy_glass", (0.010, 0.020, 0.030), 0.30, 0.03)

    objs = []
    # 主机脊：从机头 0.98 到尾 -0.58，中段最宽
    spine = [(0, 0.98), (0.085, 0.66), (0.125, 0.18), (0.10, -0.22),
             (0.055, -0.50), (0, -0.58), (-0.055, -0.50), (-0.10, -0.22),
             (-0.125, 0.18), (-0.085, 0.66)]
    objs.append(poly_prism("spine", spine, -0.055, 0.055, hull, 0.016))
    # 机背装甲板（略窄一圈、抬高一点，形成层次）
    back = [(0, 0.80), (0.07, 0.52), (0.095, 0.12), (0.075, -0.24), (0, -0.44),
            (-0.075, -0.24), (-0.095, 0.12), (-0.07, 0.52)]
    objs.append(poly_prism("back_plate", back, 0.055, 0.085, armor, 0.010))

    # 后掠翼（左右各一）
    def wing(sign):
        pts = [(sign * 0.10, 0.30), (sign * 0.30, 0.22), (sign * 0.70, -0.06),
               (sign * 0.80, -0.16), (sign * 0.66, -0.30), (sign * 0.34, -0.34),
               (sign * 0.12, -0.24)]
        return poly_prism("wing", pts, -0.02, 0.03, hull, 0.014)
    objs += mirrored(wing)
    # 翼面装甲条 + 前缘能量线
    def wing_plate(sign):
        pts = [(sign * 0.22, 0.20), (sign * 0.52, 0.02), (sign * 0.60, -0.10),
               (sign * 0.36, -0.20), (sign * 0.18, -0.12)]
        return poly_prism("wing_plate", pts, 0.03, 0.052, armor, 0.008)
    objs += mirrored(wing_plate)

    def wing_neon(sign):
        pts = [(sign * 0.115, 0.26), (sign * 0.28, 0.19), (sign * 0.66, -0.10),
               (sign * 0.72, -0.145), (sign * 0.70, -0.175), (sign * 0.64, -0.145),
               (sign * 0.26, 0.155), (sign * 0.105, 0.225)]
        return poly_prism("wing_neon", pts, 0.030, 0.040, neon, 0.0)
    objs += mirrored(wing_neon)

    # 座舱盖 + 一圈霓虹边（纯俯视下，座舱的"玻璃感"靠高光与边光，不靠透明度）
    canopy = [(math.cos(i * 2 * math.pi / 14) * 0.062, 0.18 + math.sin(i * 2 * math.pi / 14) * 0.17)
              for i in range(14)]
    objs.append(poly_prism("canopy", canopy, 0.085, 0.135, glass, 0.012))
    canopy_neon = mat_emit("canopy_neon", accent, 2.4)
    objs.append(arc_ring("canopy_neon", 0.068, 0.076, 0, 2 * math.pi, 0.128, 0.138, canopy_neon,
                         seg=28, loc=(0, 0.18, 0), bevel=0.0))
    # 机头能量芯
    objs.append(poly_prism("nose_neon", [(0, 0.90), (0.014, 0.62), (-0.014, 0.62)],
                           -0.062, -0.056, neon, 0.0))

    # 尾喷：两条嵌在机尾的发光槽 + 一圈暗框，俯视下比外挂圆盘更像"双发"
    for sx in (-1, 1):
        objs.append(box("nozzle_frame", (0.082, 0.16, 0.075), (sx * 0.062, -0.50, 0.005), dark, 0.008))
        objs.append(box("nozzle_slot", (0.048, 0.115, 0.02), (sx * 0.062, -0.50, 0.058), neon, bevel=0.0))
    objs.append(box("tail_plate", (0.20, 0.075, 0.02), (0, -0.575, 0.030), armor, 0.006))

    # 细节：进气口与炮位
    for sx in (-1, 1):
        objs.append(box("intake", (0.05, 0.12, 0.05), (sx * 0.105, 0.30, 0.005), dark))
        objs.append(box("gun", (0.028, 0.16, 0.045), (sx * 0.28, -0.02, 0.02), dark))
    for i in range(5):
        objs.append(box("greeble", (0.03, 0.02, 0.03),
                        (-0.02 + i * 0.01, -0.30 - i * 0.03, 0.095), dark, bevel=0.004))
    return objs, []


def build_boss_gatekeeper(accent):
    """守门人：六边形要塞，中央核心 + 六片装甲瓣 + 外圈旋转环（ring 层）。"""
    hull = mat_pbr("boss_hull", (0.040, 0.028, 0.045), 0.94, 0.24)
    armor = mat_pbr("boss_armor", (0.080, 0.052, 0.072), 0.90, 0.30)
    dark = mat_pbr("boss_frame", (0.022, 0.020, 0.030), 0.7, 0.5)
    neon = mat_emit("boss_neon", accent, 1.35)
    core = mat_emit("boss_core", (0.95, 0.30, 0.45), 1.10)

    objs = []
    # 主六边形（朝下的一角略长，形成"下颌"）
    hexpts = []
    for i in range(6):
        a = math.pi / 2 + i * math.pi / 3
        r = 0.66 if i == 3 else 0.60
        hexpts.append((math.cos(a) * r, math.sin(a) * r))
    objs.append(poly_prism("hex_hull", hexpts, -0.07, 0.09, hull, 0.02))

    # 六片装甲瓣
    for i in range(6):
        a = math.pi / 2 + i * math.pi / 3
        objs.append(box("petal", (0.34, 0.17, 0.10),
                        (math.cos(a) * 0.42, math.sin(a) * 0.42, 0.10), armor, 0.014, rot=(0, 0, a)))

    # 中央核心（三层：暗座 + 环 + 亮芯）
    objs.append(cyl("core_seat", 0.24, 0.24, (0, 0, 0.02), dark, 24))
    objs.append(cyl("core_ring", 0.195, 0.26, (0, 0, 0.03), neon, 24, bevel=0.0))
    objs.append(cyl("core_lens", 0.135, 0.30, (0, 0, 0.04), core, 24, bevel=0.0))
    objs.append(cyl("core_pin", 0.045, 0.34, (0, 0, 0.05), mat_emit("boss_core_pin", (1.0, 0.85, 0.90), 1.30), 16, bevel=0.0))

    # 下颌（朝 -Y）
    objs.append(poly_prism("jaw", [(0.20, -0.42), (0.10, -0.80), (-0.10, -0.80), (-0.20, -0.42)],
                           -0.05, 0.06, armor, 0.012))
    objs.append(box("jaw_neon", (0.06, 0.22, 0.02), (0, -0.66, 0.07), neon, 0.0))

    # 细节块
    for i in range(6):
        a = math.pi / 2 + i * math.pi / 3 + math.pi / 6
        objs.append(box("greeble", (0.10, 0.06, 0.06),
                        (math.cos(a) * 0.30, math.sin(a) * 0.30, 0.12), dark, 0.006,
                        rot=(0, 0, a)))

    # ---- ring 层：外圈三段弧 + 六个光点，游戏里单独旋转 ----
    ring_neon = mat_emit("boss_ring_neon", accent, 0.85)
    ring = []
    for k in range(3):
        a0 = k * 2 * math.pi / 3 + 0.12
        a1 = a0 + 1.45
        ring.append(arc_ring("ring_arc", 0.74, 0.80, a0, a1, -0.02, 0.03, ring_neon))
    for i in range(6):
        a = i * math.pi / 3
        ring.append(cyl("ring_dot", 0.035, 0.05, (math.cos(a) * 0.77, math.sin(a) * 0.77, 0.02),
                        ring_neon, 12, bevel=0.0))
    return objs, ring


def build_bullet_needle(accent):
    """敌方针弹：细长飞镖，机头 +Y。"""
    hull = mat_pbr("needle_hull", (0.05, 0.22, 0.24), 0.6, 0.18, emit=(0.30, 0.95, 0.90), emit_strength=1.6)
    neon = mat_emit("needle_neon", (0.35, 0.95, 0.92), 2.2)
    objs = []
    pts = [(0, 0.96), (0.085, 0.30), (0.075, -0.30), (0, -0.62),
           (-0.075, -0.30), (-0.085, 0.30)]
    objs.append(poly_prism("needle_body", pts, -0.05, 0.05, hull, 0.014))
    objs.append(poly_prism("needle_core", [(0, 0.82), (0.028, 0.1), (0, -0.5), (-0.028, 0.1)],
                           -0.056, -0.050, neon, 0.0))
    return objs, []


# ================================================================= 批量：机体与 Boss
# 12 种敌机 / 3 套玩家形态 / 3 台 Boss。敌机机头朝 -Y（游戏里向下），玩家与弹体朝 +Y。
# 轮廓用简单多边形 + 少量细节块，保证：
#   · 40-60px 下剪影可辨（这是弹幕游戏的第一需求）
#   · 材质/布光/发光强度全走同一套，换图后风格统一

def _mats(tag, base=(0.030, 0.040, 0.062), armor=(0.060, 0.078, 0.110)):
    return (mat_pbr(tag + "_hull", base, 0.95, 0.22),
            mat_pbr(tag + "_armor", armor, 0.90, 0.32),
            mat_pbr(tag + "_dark", (0.012, 0.016, 0.026), 0.70, 0.55))


def _octagon(r, squash=1.0, n=8, rot=0.0):
    return [(math.cos(rot + i * 2 * math.pi / n) * r,
             math.sin(rot + i * 2 * math.pi / n) * r * squash) for i in range(n)]


ENEMY_SPECS = {
    # 普通机群：小三角翼
    "enemy_drifter": dict(accent="#FF6F91", body=[
        ("poly", [(0, -0.95), (0.34, -0.25), (0.62, 0.42), (0.20, 0.30), (0.09, 0.62),
                  (-0.09, 0.62), (-0.20, 0.30), (-0.62, 0.42), (-0.34, -0.25)], -0.05, 0.05, "hull"),
        ("poly", [(0, -0.62), (0.20, -0.16), (0.30, 0.26), (-0.30, 0.26), (-0.20, -0.16)], 0.05, 0.075, "armor"),
        ("poly", [(0.30, 0.34), (0.56, 0.40), (0.52, 0.30), (0.28, 0.26)], 0.03, 0.06, "neon"),
        ("poly", [(-0.30, 0.34), (-0.56, 0.40), (-0.52, 0.30), (-0.28, 0.26)], 0.03, 0.06, "neon"),
        ("cyl", 0.055, 0.10, (0, 0.58, 0.02), "dark"),
    ]),
    # 俯冲蜂：细长针形，后掠刺
    "enemy_wasp": dict(accent="#FF9AB4", body=[
        ("poly", [(0, -0.98), (0.13, -0.34), (0.52, 0.66), (0.16, 0.38), (0.07, 0.28),
                  (-0.07, 0.28), (-0.16, 0.38), (-0.52, 0.66), (-0.13, -0.34)], -0.04, 0.04, "hull"),
        ("poly", [(0, -0.80), (0.05, -0.10), (0.04, 0.46), (-0.04, 0.46), (-0.05, -0.10)],
         -0.045, -0.038, "neon"),
    ]),
    # 扇形射手：宽箭头 + 两侧炮舱
    "enemy_lancer": dict(accent="#A597FF", body=[
        ("poly", [(0, -0.92), (0.24, -0.20), (0.80, 0.08), (0.74, 0.34), (0.28, 0.28),
                  (0.11, 0.58), (-0.11, 0.58), (-0.28, 0.28), (-0.74, 0.34), (-0.80, 0.08),
                  (-0.24, -0.20)], -0.05, 0.06, "hull"),
        ("poly", [(0, -0.66), (0.14, -0.10), (0.20, 0.34), (-0.20, 0.34), (-0.14, -0.10)], 0.06, 0.09, "armor"),
        ("box", (0.13, 0.34, 0.09), (0.52, 0.06, 0.02), "dark", 0.008),
        ("box", (0.13, 0.34, 0.09), (-0.52, 0.06, 0.02), "dark", 0.008),
        ("box", (0.05, 0.20, 0.03), (0.52, -0.14, 0.09), "neon", 0.0),
        ("box", (0.05, 0.20, 0.03), (-0.52, -0.14, 0.09), "neon", 0.0),
    ]),
    # 环状炮台：八边形 + 发光环
    "enemy_orbiter": dict(accent="#9B8CFF", body=[
        ("poly", _octagon(0.78, 0.92, 8, math.pi / 8), -0.06, 0.06, "hull"),
        ("poly", _octagon(0.56, 0.92, 8, math.pi / 8), 0.06, 0.09, "armor"),
        ("ring", 0.40, 0.50, 0.0, 2 * math.pi, 0.09, 0.105, "neon"),
        ("cyl", 0.16, 0.14, (0, 0, 0.10), "dark"),
        ("cyl", 0.10, 0.16, (0, 0, 0.12), "neon"),
    ]),
    # 分裂体：中央仓 + 两块可拆的侧体
    "enemy_splitter": dict(accent="#FF6F91", body=[
        ("poly", [(0, -0.86), (0.28, -0.32), (0.32, 0.30), (0.15, 0.70), (-0.15, 0.70),
                  (-0.32, 0.30), (-0.28, -0.32)], -0.05, 0.05, "hull"),
        ("box", (0.30, 0.52, 0.10), (0.58, 0.16, 0.0), "armor", 0.012),
        ("box", (0.30, 0.52, 0.10), (-0.58, 0.16, 0.0), "armor", 0.012),
        ("box", (0.06, 0.34, 0.03), (0.58, 0.16, 0.06), "neon", 0.0),
        ("box", (0.06, 0.34, 0.03), (-0.58, 0.16, 0.06), "neon", 0.0),
        ("cyl", 0.10, 0.12, (0, -0.30, 0.08), "dark"),
    ]),
    # 重装堡垒：厚甲 + 双炮
    "enemy_bulwark": dict(accent="#C3CBE0", body=[
        ("poly", [(0, -0.70), (0.40, -0.50), (0.86, 0.10), (0.86, 0.60), (0.30, 0.80),
                  (-0.30, 0.80), (-0.86, 0.60), (-0.86, 0.10), (-0.40, -0.50)], -0.07, 0.08, "hull"),
        ("poly", [(0, -0.46), (0.28, -0.28), (0.60, 0.16), (0.60, 0.52), (-0.60, 0.52),
                  (-0.60, 0.16), (-0.28, -0.28)], 0.08, 0.12, "armor"),
        ("box", (0.16, 0.44, 0.12), (0.40, -0.42, 0.02), "dark", 0.010),
        ("box", (0.16, 0.44, 0.12), (-0.40, -0.42, 0.02), "dark", 0.010),
        ("box", (0.07, 0.26, 0.03), (0.40, -0.60, 0.10), "neon", 0.0),
        ("box", (0.07, 0.26, 0.03), (-0.40, -0.60, 0.10), "neon", 0.0),
    ]),
    # 陨石：不规则岩块
    "enemy_rock": dict(accent="#AAB6CC", body=[
        ("poly", [(0.10, -0.92), (0.55, -0.52), (0.84, 0.06), (0.62, 0.62), (0.16, 0.86),
                  (-0.30, 0.80), (-0.74, 0.44), (-0.86, -0.10), (-0.48, -0.62), (-0.12, -0.86)],
         -0.16, 0.14, "hull"),
        ("poly", [(0.24, -0.40), (0.42, 0.02), (0.24, 0.38), (-0.10, 0.30), (-0.22, -0.06),
                  (-0.02, -0.36)], 0.14, 0.20, "armor"),
    ]),
    # 长炮狙击：长轨 + 瞄具
    "enemy_sniper": dict(accent="#69F5DC", body=[
        ("poly", [(0, -0.98), (0.14, -0.54), (0.22, 0.30), (0.44, 0.62), (0.20, 0.74),
                  (-0.20, 0.74), (-0.44, 0.62), (-0.22, 0.30), (-0.14, -0.54)], -0.05, 0.05, "hull"),
        ("box", (0.07, 0.62, 0.06), (0, 0.10, 0.075), "armor", 0.006),
        ("box", (0.035, 0.56, 0.02), (0, 0.10, 0.105), "neon", 0.0),
        ("cyl", 0.09, 0.10, (0, 0.44, 0.09), "dark"),
        ("box", (0.42, 0.10, 0.06), (0, 0.56, 0.02), "armor", 0.006),
    ]),
    # 布雷者：圆胖机体 + 两侧雷舱
    "enemy_minekeeper": dict(accent="#FFD27A", body=[
        ("poly", _octagon(0.66, 1.0, 10, math.pi / 10), -0.07, 0.07, "hull"),
        ("poly", _octagon(0.44, 1.0, 10, math.pi / 10), 0.07, 0.10, "armor"),
        ("ring", 0.30, 0.38, 0.0, 2 * math.pi, 0.10, 0.115, "neon"),
        ("cyl", 0.20, 0.30, (0.60, 0.10, 0.0), "dark"),
        ("cyl", 0.20, 0.30, (-0.60, 0.10, 0.0), "dark"),
        ("cyl", 0.13, 0.32, (0.60, 0.10, 0.01), "neon"),
        ("cyl", 0.13, 0.32, (-0.60, 0.10, 0.01), "neon"),
    ]),
    # 侧掠弯刀：不对称刀锋
    "enemy_sidewinder": dict(accent="#9B8CFF", body=[
        ("poly", [(0.10, -0.90), (0.30, -0.20), (0.92, 0.55), (0.62, 0.74), (0.10, 0.26),
                  (-0.10, 0.42), (-0.26, 0.68), (-0.42, 0.44), (-0.20, -0.20)], -0.05, 0.05, "hull"),
        ("poly", [(0.26, -0.30), (0.60, 0.34), (0.44, 0.44), (0.16, -0.10)], 0.05, 0.075, "armor"),
        ("poly", [(0.34, -0.16), (0.76, 0.46), (0.70, 0.50), (0.28, -0.14)], 0.02, 0.045, "neon"),
    ]),
    # 召唤母舰：大块舰体 + 机库口 + 侧鳍
    "enemy_carrier": dict(accent="#8FA4C9", body=[
        ("poly", [(0, -0.66), (0.32, -0.66), (0.36, -0.10), (0.94, 0.16), (0.94, 0.56),
                  (0.40, 0.68), (-0.40, 0.68), (-0.94, 0.56), (-0.94, 0.16), (-0.36, -0.10),
                  (-0.32, -0.66)], -0.08, 0.08, "hull"),
        ("poly", [(0, -0.50), (0.26, -0.50), (0.30, 0.10), (0.66, 0.26), (-0.66, 0.26),
                  (-0.30, 0.10), (-0.26, -0.50)], 0.08, 0.12, "armor"),
        ("box", (0.34, 0.14, 0.03), (0, -0.56, 0.09), "neon", 0.0),
        ("box", (0.10, 0.40, 0.03), (0.70, 0.40, 0.09), "neon", 0.0),
        ("box", (0.10, 0.40, 0.03), (-0.70, 0.40, 0.09), "neon", 0.0),
        ("cyl", 0.14, 0.12, (0, 0.30, 0.14), "dark"),
    ]),
    # 分裂幼体：小碎片
    "enemy_shardling": dict(accent="#FF9AB4", body=[
        ("poly", [(0, -0.92), (0.40, 0.0), (0.16, 0.80), (-0.16, 0.80), (-0.40, 0.0)],
         -0.04, 0.04, "hull"),
        ("poly", [(0, -0.60), (0.06, -0.10), (0, 0.30), (-0.06, -0.10)], -0.045, -0.038, "neon"),
    ]),
}


def build_from_spec(name, spec):
    # 敌机在游戏里只有 16-38px，比玩家机更依赖"提亮"：暗装甲缩到 20px 会糊成黑块
    hull, armor, dark = _mats(name, (0.062, 0.082, 0.118), (0.105, 0.128, 0.170))
    neon = mat_emit(name + "_neon", hex_rgb(spec["accent"]), 1.35)
    table = {"hull": hull, "armor": armor, "dark": dark, "neon": neon}
    objs = []
    for i, part in enumerate(spec["body"]):
        kind = part[0]
        if kind == "poly":
            _, pts, z0, z1, mat = part
            objs.append(poly_prism(f"{name}_p{i}", pts, z0, z1, table[mat],
                                   0.014 if mat != "neon" else 0.0))
        elif kind == "box":
            _, size, loc, mat, bev = part
            objs.append(box(f"{name}_b{i}", size, loc, table[mat], bev))
        elif kind == "cyl":
            _, r, d, loc, mat = part[:5]
            rot = part[5] if len(part) > 5 else (0, 0, 0)
            objs.append(cyl(f"{name}_c{i}", r, d, loc, table[mat], 20, rot=rot))
        elif kind == "ring":
            _, r0, r1, a0, a1, z0, z1, mat = part
            objs.append(arc_ring(f"{name}_r{i}", r0, r1, a0, a1, z0, z1, table[mat], seg=28, bevel=0.0))
    return objs, []



def flip_180(objs):
    """绕世界 Z 轴 180°：玩家机轮廓若按敌机朝向写的，用它翻正（对称机体等价于镜像）。"""
    for ob in objs:
        ob.location = (-ob.location.x, -ob.location.y, ob.location.z)
        ob.rotation_euler = (ob.rotation_euler.x, ob.rotation_euler.y,
                             ob.rotation_euler.z + math.pi)
    return objs

def build_player_prism(accent):
    """棱镜线：宽人字翼 + 长枪机头（单发重击的身份）。"""
    hull, armor, dark = _mats("prism_player")
    neon = mat_emit("prism_neon", accent, 1.35)
    glass = mat_pbr("prism_glass", (0.010, 0.020, 0.030), 0.30, 0.03)
    objs = [poly_prism("spine", [(0, -0.98), (0.07, -0.40), (0.10, 0.20), (0.07, 0.56),
                                 (0, 0.72), (-0.07, 0.56), (-0.10, 0.20), (-0.07, -0.40)],
                       -0.05, 0.06, hull, 0.014)]

    def wing(sign):
        return poly_prism("wing", [(sign * 0.08, -0.30), (sign * 0.26, -0.62), (sign * 0.92, 0.52),
                                   (sign * 0.66, 0.72), (sign * 0.30, 0.28), (sign * 0.10, 0.34)],
                          -0.03, 0.03, hull, 0.014)

    def plate(sign):
        return poly_prism("plate", [(sign * 0.16, -0.40), (sign * 0.32, -0.48), (sign * 0.72, 0.44),
                                    (sign * 0.58, 0.52), (sign * 0.30, 0.16)], 0.03, 0.055, armor, 0.008)

    def edge(sign):
        return poly_prism("edge", [(sign * 0.30, -0.60), (sign * 0.86, 0.48), (sign * 0.80, 0.52),
                                   (sign * 0.26, -0.56)], 0.015, 0.035, neon, 0.0)

    objs += mirrored(wing) + mirrored(plate) + mirrored(edge)
    objs.append(poly_prism("lance", [(0, -0.99), (0.035, -0.55), (-0.035, -0.55)], -0.048, -0.040, neon, 0.0))
    objs.append(poly_prism("canopy", _octagon(0.07, 1.9, 12), 0.06, 0.10, glass, 0.010))
    for sx in (-1, 1):
        objs.append(box("nozzle", (0.06, 0.14, 0.05), (sx * 0.05, 0.62, 0.05), neon, 0.0))
    return flip_180(objs), []


def build_player_swarm(accent):
    """蜂群线：双引擎舱 + 导弹巢（多弹散射的身份）。"""
    hull, armor, dark = _mats("swarm_player")
    neon = mat_emit("swarm_neon", accent, 1.35)
    glass = mat_pbr("swarm_glass", (0.010, 0.020, 0.030), 0.30, 0.03)
    objs = [poly_prism("spine", [(0, -0.92), (0.10, -0.30), (0.13, 0.24), (0.10, 0.60),
                                 (0, 0.74), (-0.10, 0.60), (-0.13, 0.24), (-0.10, -0.30)],
                       -0.05, 0.06, hull, 0.014)]

    def nacelle(sign):
        out = [poly_prism("nacelle", [(sign * 0.20, -0.62), (sign * 0.46, -0.30), (sign * 0.52, 0.34),
                                      (sign * 0.40, 0.62), (sign * 0.20, 0.50), (sign * 0.18, 0.0)],
                          -0.06, 0.05, hull, 0.014)]
        out.append(poly_prism("nacelle_neon", [(sign * 0.44, -0.24), (sign * 0.49, 0.30),
                                               (sign * 0.45, 0.30), (sign * 0.40, -0.24)],
                              0.05, 0.070, neon, 0.0))
        out.append(cyl("pod", 0.055, 0.22, (sign * 0.33, 0.10, 0.07), dark, 12))
        return out

    objs += _flat(mirrored(nacelle))
    objs.append(poly_prism("canopy", _octagon(0.06, 1.8, 12), 0.06, 0.10, glass, 0.010))
    for sx in (-1, 1):
        for i in range(3):
            objs.append(cyl("missile", 0.018, 0.10, (sx * 0.33, -0.02 + i * 0.11, 0.085), neon, 8))
    return flip_180(objs), []


def build_boss_weaver(accent):
    """织星者：两把长刀翼 + 细长脊柱，紫色。"""
    hull, armor, dark = _mats("weaver_boss", (0.038, 0.030, 0.058), (0.070, 0.056, 0.105))
    neon = mat_emit("weaver_neon", accent, 1.35)
    core = mat_emit("weaver_core", (0.62, 0.50, 1.0), 1.05)
    objs = [poly_prism("spine", [(0, -0.86), (0.14, -0.40), (0.18, 0.30), (0.10, 0.78),
                                 (-0.10, 0.78), (-0.18, 0.30), (-0.14, -0.40)],
                       -0.07, 0.09, hull, 0.018)]

    def blade(sign):
        return poly_prism("blade", [(sign * 0.12, -0.30), (sign * 0.44, -0.74), (sign * 1.00, -0.06),
                                    (sign * 0.96, 0.26), (sign * 0.40, -0.10), (sign * 0.16, 0.10)],
                          -0.05, 0.04, hull, 0.016)

    def blade_neon(sign):
        return poly_prism("blade_neon", [(sign * 0.46, -0.66), (sign * 0.94, -0.04),
                                         (sign * 0.92, 0.10), (sign * 0.42, -0.56)], 0.04, 0.055, neon, 0.0)

    objs += mirrored(blade) + mirrored(blade_neon)
    objs.append(cyl("core_seat", 0.20, 0.22, (0, 0.10, 0.02), dark, 24))
    objs.append(cyl("core_lens", 0.13, 0.26, (0, 0.10, 0.04), core, 24, bevel=0.0))
    for sx in (-1, 1):
        objs.append(cyl("eye", 0.05, 0.08, (sx * 0.30, -0.34, 0.06), neon, 12, bevel=0.0))
    ring = []
    ring_mat = mat_emit("weaver_ring", accent, 0.85)
    for k in range(4):
        a0 = k * math.pi / 2 + 0.10
        ring.append(arc_ring("ring_arc", 0.80, 0.86, a0, a0 + 1.15, -0.02, 0.03, ring_mat))
    return objs, ring


def build_boss_helios(accent):
    """新星核心：同心环 + 四根尖刺 + 亮核，金色。"""
    hull, armor, dark = _mats("helios_boss", (0.050, 0.042, 0.026), (0.090, 0.074, 0.040))
    neon = mat_emit("helios_neon", accent, 1.35)
    core = mat_emit("helios_core", (1.0, 0.78, 0.35), 1.10)
    objs = [poly_prism("disc", _octagon(0.74, 1.0, 16), -0.07, 0.08, hull, 0.02),
            poly_prism("disc_top", _octagon(0.50, 1.0, 16), 0.08, 0.11, armor, 0.012)]
    for i in range(4):
        a = i * math.pi / 2 + math.pi / 4
        objs.append(box("spike", (0.16, 0.52, 0.10),
                        (math.cos(a) * 0.62, math.sin(a) * 0.62, 0.02), armor, 0.012,
                        rot=(0, 0, a - math.pi / 2)))
        objs.append(box("spike_neon", (0.05, 0.34, 0.02),
                        (math.cos(a) * 0.70, math.sin(a) * 0.70, 0.075), neon, 0.0,
                        rot=(0, 0, a - math.pi / 2)))
    objs.append(cyl("core_seat", 0.22, 0.24, (0, 0, 0.02), dark, 24))
    objs.append(arc_ring("core_ring", 0.15, 0.22, 0, 2 * math.pi, 0.10, 0.13, neon, seg=32, bevel=0.0))
    objs.append(cyl("core_lens", 0.14, 0.30, (0, 0, 0.06), core, 24, bevel=0.0))
    ring = []
    ring_mat = mat_emit("helios_ring", accent, 0.85)
    for k in range(3):
        a0 = k * 2 * math.pi / 3
        ring.append(arc_ring("ring_arc", 0.84, 0.90, a0, a0 + 1.7, -0.02, 0.03, ring_mat))
    return objs, ring


def _enemy_builder(asset):
    return lambda accent: build_from_spec(asset, ENEMY_SPECS[asset])


MODELS = {
    # 玩家三线（512）
    "player_pulse": (build_player_pulse, 512, "#35E0C8"),
    "player_prism": (build_player_prism, 512, "#FF5C8A"),
    "player_swarm": (build_player_swarm, 512, "#FFC24B"),
    # 三台 Boss（640，另有 _ring 层）
    "boss_gatekeeper": (build_boss_gatekeeper, 640, "#FF6F91"),
    "boss_weaver": (build_boss_weaver, 640, "#9B8CFF"),
    "boss_helios": (build_boss_helios, 640, "#FFD27A"),
    # 弹体（96）
    "bullet_enemy_needle": (build_bullet_needle, 96, "#8CF7EA"),
}
for _k in ENEMY_SPECS:
    MODELS[_k] = (_enemy_builder(_k), 384, ENEMY_SPECS[_k]["accent"])


# ----------------------------------------------------------------- 归一化与渲染

def _flat(items):
    out = []
    for it in items:
        if isinstance(it, (list, tuple)):
            out.extend(_flat(it))
        else:
            out.append(it)
    return out


def normalize(objs, radius=ART_EXTENT * VIEW):
    """把这一组机体缩放/平移到「外接半径 = 0.92、几何中心在原点」。"""
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for ob in objs:
        for c in ob.bound_box:
            w = ob.matrix_world @ Vector(c)
            lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
            hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
    center = (lo + hi) * 0.5
    extent = max((hi - lo).x, (hi - lo).y) * 0.5
    k = radius / max(1e-6, extent)
    for ob in objs:
        ob.location = (ob.location - center) * k
        ob.scale = ob.scale * k
    bpy.context.view_layer.update()


def set_materials(objs, mat):
    for ob in objs:
        if not ob.data.materials:
            ob.data.materials.append(mat)
        else:
            for i in range(len(ob.data.materials)):
                ob.data.materials[i] = mat


def render_to(path, canvas):
    sc = bpy.context.scene
    sc.render.resolution_x = canvas
    sc.render.resolution_y = canvas
    sc.render.resolution_percentage = 100
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDER", os.path.basename(path))


def fit_to_contract(objs, out_dir, name, preview=160):
    """
    先渲一张小样、量出 alpha 的外接框中心与外接半径，再反推缩放/位移。
    纯按几何包围盒归一化会偏（薄片、发光边、隐藏件都会让视觉框和几何框不一致），
    这里直接用玩家实际会看到的像素来对齐 ShipArt 的 0.46 约定。
    """
    import numpy as np
    sc = bpy.context.scene
    tmp = os.path.join(out_dir, "_fit_" + name + ".png")
    sc.render.resolution_x = sc.render.resolution_y = preview
    sc.render.filepath = tmp
    bpy.ops.render.render(write_still=True)

    img = bpy.data.images.load(tmp)
    w, h = img.size
    a = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[:, :, 3]
    bpy.data.images.remove(img)
    ys, xs = np.nonzero(a > 0.5)
    if len(xs) == 0:
        print("FIT_SKIP empty", name)
        return
    cx = (xs.min() + xs.max() + 1) / 2.0
    cy = (ys.min() + ys.max() + 1) / 2.0            # 图像行序自下而上，正好等于视图 +y
    dx = (xs + 0.5 - cx) * VIEW / w
    dy = (ys + 0.5 - cy) * VIEW / h
    r_view = float(np.sqrt(dx * dx + dy * dy).max())
    if r_view < 1e-6:
        return
    k = (ART_EXTENT * VIEW) / r_view
    cxv = (cx / w - 0.5) * VIEW
    cyv = (cy / h - 0.5) * VIEW
    for ob in objs:
        ob.location = (ob.location - Vector((cxv, cyv, 0.0))) * k
        ob.scale = ob.scale * k
    bpy.context.view_layer.update()
    os.remove(tmp)
    print("FIT", name, "r=%.3f k=%.3f" % (r_view, k))



def bake_glow(path, tint, radius_frac=0.026, strength=0.55, lift=0.08):
    """
    把一圈同色低透明外发光烘进 PNG：旧手绘图的"存在感"主要来自这圈光。
    alpha 上限压到 0.45，check_art 用 >0.5 判轮廓时不会被光晕带偏，
    机体仍然是 0.46 的外接半径。
    """
    from PIL import Image, ImageFilter
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    a = im.getchannel("A")
    r, g, b, _ = im.split()
    if lift > 0:
        f = lambda v: min(255, int(v + 255 * lift))
        r, g, b = r.point(f), g.point(f), b.point(f)
    body = Image.merge("RGBA", (r, g, b, a))
    rad = max(2, int(w * radius_frac))
    blur = a.filter(ImageFilter.GaussianBlur(rad))
    cap = int(0.45 * 255 * strength / 0.55)
    glow = Image.new("RGBA", im.size, (tint[0], tint[1], tint[2], 0))
    glow.putalpha(blur.point(lambda v: min(cap, int(v * strength))))
    Image.alpha_composite(glow, body).save(path)

def render_asset(name, out_dir, samples):
    builder, canvas, hexaccent = MODELS[name]
    reset_scene()
    setup_render(samples)
    setup_world()
    setup_camera()
    setup_lights()
    accent = hex_rgb(hexaccent)

    body, ring = builder(accent)
    body, ring = _flat(body), _flat(ring)
    normalize(body + ring)
    fit_to_contract(body + ring, out_dir, name)

    render_to(os.path.join(out_dir, name + ".png"), canvas)

    saved = [(ob, list(ob.data.materials)) for ob in body]
    set_materials(body, mat_mask())
    render_to(os.path.join(out_dir, name + "_mask.png"), canvas)
    for ob, mats in saved:
        ob.data.materials.clear()
        for m in mats:
            ob.data.materials.append(m)

    if ring:
        for ob in body:
            ob.hide_render = True
        render_to(os.path.join(out_dir, name + "_ring.png"), canvas)
        set_materials(ring, mat_mask())
        render_to(os.path.join(out_dir, name + "_ring_mask.png"), canvas)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--list" in argv:
        print("ASSETS", ",".join(sorted(MODELS)))
        return
    only = "player_pulse,boss_gatekeeper,bullet_enemy_needle"
    out = DEFAULT_OUT
    samples = 128
    for i, a in enumerate(argv):
        if a == "--only" and i + 1 < len(argv):
            only = argv[i + 1]
        elif a == "--out" and i + 1 < len(argv):
            out = argv[i + 1]
        elif a == "--samples" and i + 1 < len(argv):
            samples = int(argv[i + 1])
    os.makedirs(out, exist_ok=True)
    for name in [s.strip() for s in only.split(",") if s.strip()]:
        if name not in MODELS:
            print("SKIP unknown asset:", name)
            continue
        render_asset(name, out, samples)


if __name__ == "__main__":
    main()
