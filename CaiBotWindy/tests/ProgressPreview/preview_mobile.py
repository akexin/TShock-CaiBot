#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""把卡片缩到手机 QQ 聊天窗口的两种尺寸，评估实际可读性。

QQ 手机版聊天窗口：
  * 缩略图（不点开）宽度约 260~280 逻辑像素
  * 点开后全屏查看宽度约 390 逻辑像素（≈屏幕宽）
这里按 2 倍密度渲染，即 520 / 780 物理像素，再原样保存，
用原始像素尺寸直接对比字号退化程度。
"""
import sys
from pathlib import Path
from PIL import Image

OUT = Path(__file__).parent / "out"
DEST = Path(__file__).parent / "mobile"
DEST.mkdir(exist_ok=True)

# (标签, 目标宽度)
SIZES = [("thumb260", 260), ("open390", 390)]

for name in sys.argv[1:] or ["card-normal", "card-bag"]:
    src = OUT / f"{name}.png"
    if not src.exists():
        print(f"跳过（不存在）: {src}")
        continue
    img = Image.open(src).convert("RGB")
    for label, width in SIZES:
        ratio = width / img.width
        height = max(1, round(img.height * ratio))
        small = img.resize((width, height), Image.LANCZOS)
        # 按 2x 密度放大回来看真实设备上的观感
        shown = small.resize((width * 2, height * 2), Image.NEAREST)
        dst = DEST / f"{name}-{label}.png"
        shown.save(dst)
        print(f"{name} {img.width}x{img.height} -> {label} {width}x{height} (2x: {shown.width}x{shown.height})  {dst}")
