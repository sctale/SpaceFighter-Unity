#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把 Polyfork 的低模战机（interceptor）渲成玩家的三套精灵（v0.20.0）。

与 gen_art.py 的区别：造型不是我们程序化建的，而是导入资源库的 FBX，
所以这里只做四件事：导入 → 按武器线改主装甲色 → 压曝光（红色低模在我们的
布光下会过曝成粉白）→ 走同一套 fit/渲染/契约。
"""
import bpy
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_art as G

SRC = r"D:/V-Coding/.blender-setup/polyfork/interceptor-9d2289.fbx"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "polyfolk_player")

# 三条线的主色（与 GameConfig.LineDef 的 Tint 一致）
ACCENTS = {
    "player_pulse": (0.207, 0.878, 0.784),   # teal
    "player_prism": (1.000, 0.360, 0.541),   # rose
    "player_swarm": (1.000, 0.760, 0.294),   # gold
}


def saturated_materials(objs):
    """
    按「世界面积」找有色的装甲材质（返回从大到小）。
    只看面数会挑到深灰机体 plating（1680 面）；真正的主装甲是深红 hull（472 面但面积最大）。
    灰色件饱和度接近 0，直接排除。
    """
    area, sat = {}, {}
    for ob in objs:
        mw = ob.matrix_world
        for p in ob.data.polygons:
            if not ob.data.materials:
                continue
            m = ob.data.materials[min(p.material_index, len(ob.data.materials) - 1)]
            if m is None:
                continue
            area[m.name] = area.get(m.name, 0.0) + p.area * (mw.to_scale().x ** 2)
    for name in area:
        mat = bpy.data.materials.get(name)
        s0 = 0.0
        if mat is not None and mat.use_nodes and not any(
                x.type == 'TEX_IMAGE' for x in mat.node_tree.nodes):
            for b in (x for x in mat.node_tree.nodes if x.type == 'BSDF_PRINCIPLED'):
                r, g, b2, _ = b.inputs["Base Color"].default_value
                mx, mn = max(r, g, b2), min(r, g, b2)
                s0 = (mx - mn) / mx if mx > 1e-4 else 0.0
        sat[name] = s0
    names = [n for n in area if sat.get(n, 0.0) > 0.30]
    names.sort(key=lambda n: -area[n])
    print("MATS", [(n, round(area[n], 2), round(sat[n], 2)) for n in names[:4]])
    return names


def _set_color(mat, color):
    if mat is None or not mat.use_nodes:
        return
    if any(x.type == 'TEX_IMAGE' for x in mat.node_tree.nodes):
        return
    for bsdf in (x for x in mat.node_tree.nodes if x.type == 'BSDF_PRINCIPLED'):
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        # 低模的看点是大色块 + 硬转折，不是高光；粗糙度拉高、镜面压低，
        # 否则平面一被灯打到就整片泛白，低模的"块面感"全没了
        bsdf.inputs["Roughness"].default_value = 0.82
        if "Specular IOR Level" in bsdf.inputs:
            bsdf.inputs["Specular IOR Level"].default_value = 0.15


def recolor(objs, accent):
    names = saturated_materials(objs)
    if not names:
        return
    _set_color(bpy.data.materials.get(names[0]), accent)
    for extra in names[1:]:                     # 亮一档的同色系（原来 trim 的红）
        _set_color(bpy.data.materials.get(extra),
                   tuple(min(1.0, c * 1.18 + 0.05) for c in accent))
    print("RECOLOR", names[:2], "->", [round(c, 2) for c in accent])


def load_any(path):
    if path.lower().endswith((".fbx",)):
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.context.scene.objects if o.type == 'MESH']


def tone_down_for_bright_hull():
    """红色低模在 420W 轮廓光下会直接过曝，整体降一档曝光 + 削轮廓光。"""
    sc = bpy.context.scene
    sc.view_settings.exposure = -0.55
    for name, k in (("Rim", 0.35), ("Key", 0.7), ("Under", 0.45)):
        ob = bpy.data.objects.get(name)
        if ob is not None and ob.type == 'LIGHT':
            ob.data.energy *= k


def render_one(asset, accent):
    G.reset_scene()
    G.setup_render(96)
    G.setup_world()
    G.setup_camera()
    G.setup_lights()
    tone_down_for_bright_hull()

    objs = load_any(SRC)
    recolor(objs, accent)
    G.normalize(objs, radius=G.ART_EXTENT * G.VIEW)
    G.fit_to_contract(objs, OUT, asset, preview=160)

    G.render_to(os.path.join(OUT, asset + ".png"), 512)
    G.set_materials(objs, G.mat_mask())
    G.render_to(os.path.join(OUT, asset + "_mask.png"), 512)


def main():
    os.makedirs(OUT, exist_ok=True)
    for asset, accent in ACCENTS.items():
        render_one(asset, accent)


if __name__ == "__main__":
    main()
