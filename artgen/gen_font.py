#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
重新生成 HUD 中文字体子集（Noto Sans CJK SC）。

背景：安卓上 Font.CreateDynamicFontFromOSFont 只有 Roboto，中文 HUD 会整段空白；
所以把 UI 用到的字形裁成 274KB 的小字体随包带。

用法：
    python artgen/gen_font.py [NotoSansCJK-Regular.ttc] [NotoSansCJK-Bold.ttc]
不给参数时依次尝试：
    Regular：$ANDROID_HOME/system/fonts/NotoSansCJK-Regular.ttc（adb 拉下来的副本）
             C:/Windows/Fonts/NotoSansCJK-Regular.ttc
    Bold：   C:/Windows/Fonts/Noto Sans SC Bold (TrueType).otf（Noto Sans SC Bold，OFL）
需要 pyftsubset（pip install fonttools）。
"""
import glob
import os
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHARS = os.path.join(ROOT, "artgen", "fontwork", "chars.txt")
OUT = os.path.join(ROOT, "Assets", "Resources", "Fonts", "AuroraCJK.ttf")
OUT_BOLD = os.path.join(ROOT, "Assets", "Resources", "Fonts", "AuroraCJK-Bold.ttf")


def pick_source():
    if len(sys.argv) > 1:
        return sys.argv[1]
    env = os.environ.get("ANDROID_HOME") or os.environ.get("ANDROID_SDK_ROOT")
    cands = []
    if env:
        cands.append(os.path.join(env, "system", "fonts", "NotoSansCJK-Regular.ttc"))
    cands += [
        os.path.join(ROOT, "artgen", "fontwork", "NotoSansCJK-Regular.ttc"),
        "C:/Windows/Fonts/NotoSansCJK-Regular.ttc",
        "/system/fonts/NotoSansCJK-Regular.ttc",
    ]
    for p in cands:
        if p and os.path.exists(p):
            return p
    return None


def pick_bold_source():
    if len(sys.argv) > 2:
        return sys.argv[2]
    cands = [
        os.path.join(ROOT, "artgen", "fontwork", "NotoSansCJK-Bold.ttc"),
        "C:/Windows/Fonts/Noto Sans SC Bold (TrueType).otf",
        "C:/Windows/Fonts/NotoSansCJK-Bold.ttc",
        "C:/Windows/Fonts/NotoSansSC-Bold.otf",
    ]
    for p in cands:
        if p and os.path.exists(p):
            return p
    return None


def font_number(path):
    return 2 if path.lower().endswith(".ttc") else None


def subset(src, out, number=None):
    tool = shutil.which("pyftsubset") or "pyftsubset"
    cmd = [tool, src]
    if number is not None:
        cmd.append("--font-number=%d" % number)
    cmd += [
        "--text-file=" + CHARS,
        "--unicodes=U+0000-00FF,U+2000-206F,U+2190-21FF,U+25A0-25FF",
        "--layout-features=", "--no-hinting",
        "--output-file=" + out,
    ]
    subprocess.run(cmd, check=True)


def main():
    src = pick_source()
    if not src:
        print("找不到 NotoSansCJK-Regular.ttc；可从安卓设备 adb pull /system/fonts/ 拉一份。")
        return 1

    chars = set()
    for p in glob.glob(os.path.join(ROOT, "Assets", "Scripts", "**", "*.cs"), recursive=True):
        with open(p, encoding="utf-8") as f:
            chars.update(f.read())
    text = "".join(sorted(c for c in chars if ord(c) >= 0x20 and (ord(c) < 128 or ord(c) >= 0xA0)))
    os.makedirs(os.path.dirname(CHARS), exist_ok=True)
    with open(CHARS, "w", encoding="utf-8") as f:
        f.write(text)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)

    print("chars:", len(set(text)))
    subset(src, OUT, font_number(src))
    print("regular:", os.path.getsize(OUT), "bytes ->", OUT)

    bold = pick_bold_source()
    if bold:
        subset(bold, OUT_BOLD, font_number(bold))
        print("bold:", os.path.getsize(OUT_BOLD), "bytes ->", OUT_BOLD)
    else:
        print("找不到 Bold 字体源；界面会回退到 Regular + 叠画假粗体")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
