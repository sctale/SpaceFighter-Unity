#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
图标专用：把 Polyfork interceptor 摆成 3/4 英雄角度，渲一张 1024 透明底 PNG。
不走 ShipArt 合同（不要求 0.46 外接框），构图由 gen_icon.py 再二次排版。
"""
import bpy
import math
import os
import sys
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_art as G

SRC = r"D:/V-Coding/.blender-setup/polyfork/interceptor-9d2289.fbx"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "icon")
ACCENT = (0.207, 0.878, 0.784)   # teal / pulse


def load():
    bpy.ops.import_scene.fbx(filepath=SRC)
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    # FBX 自带一个 2×2×2 的 Cube（碰撞盒/占位），俯视时藏在机体内，
    # 3/4 视角会从机背顶出来变成一块黑板——图标里必须剔掉。
    for o in list(objs):
        if o.name == "Cube":
            bpy.data.objects.remove(o, do_unlink=True)
            objs.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    return objs


def tilt(objs, yaw=160.0, pitch=52.0):
    """机头 +Y、机背 +Z；抬头 52°、绕 Z 转 160°，画面里机头朝上、双尾喷在下方。"""
    R = mathutils.Matrix.Rotation(math.radians(yaw), 4, 'Z') @ \
        mathutils.Matrix.Rotation(math.radians(pitch), 4, 'X')
    for o in objs:
        o.matrix_world = R @ o.matrix_world
    bpy.context.view_layer.update()


def main():
    os.makedirs(OUT, exist_ok=True)
    G.reset_scene()
    G.setup_render(128)
    G.setup_world()
    G.setup_camera()
    G.setup_lights()

    # 3/4 视角下用一点侧补光，避免机腹全黑
    sc = bpy.context.scene
    sc.view_settings.exposure = -0.35

    objs = load()
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import polyfork_ship as P
    P.recolor(objs, ACCENT)
    P.tone_down_for_bright_hull()
    # 尾喷口改暖金自发光：图标里唯一的金色，正好落在两台引擎上
    thr = G.mat_emit("icon_thruster", (1.0, 0.72, 0.28), 3.2)
    for o in objs:
        if o.name == "nozzle-mesh_thruster":
            o.data.materials.clear()
            o.data.materials.append(thr)
    tilt(objs)

    # 压缩到 [-0.92,0.92]，给图标排版留一点边
    G.normalize(objs, radius=G.ART_EXTENT * G.VIEW)
    G.render_to(os.path.join(OUT, "hero.png"), 1024)
    print("ICON_RENDER", os.path.join(OUT, "hero.png"))


if __name__ == "__main__":
    main()
