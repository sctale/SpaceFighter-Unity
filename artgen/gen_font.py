#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
重新生成 HUD 中文字体子集（Noto Sans CJK SC）。

背景：安卓上 Font.CreateDynamicFontFromOSFont 只有 Roboto，中文 HUD 会整段空白；
所以把 UI 用到的字形裁成 274KB 的小字体随包带。

用法：
    python artgen/gen_font.py [NotoSansCJK-Regular.ttc]
不给参数时依次尝试：
    $ANDROID_HOME/system/fonts/NotoSansCJK-Regular.ttc（adb 拉下来的副本）
    C:/Windows/Fonts/NotoSansCJK-Regular.ttc
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

    tool = shutil.which("pyftsubset") or "pyftsubset"
    cmd = [
        tool, src, "--font-number=2",           # Noto Sans CJK SC
        "--text-file=" + CHARS,
        "--unicodes=U+0000-00FF,U+2000-206F,U+2190-21FF,U+25A0-25FF",
        "--layout-features=", "--no-hinting",
        "--output-file=" + OUT,
    ]
    print("chars:", len(set(text)), "->", OUT)
    subprocess.run(cmd, check=True)
    print("done:", os.path.getsize(OUT), "bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
