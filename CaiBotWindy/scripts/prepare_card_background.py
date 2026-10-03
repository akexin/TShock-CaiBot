"""把 AI 生成的底图处理成卡片可用的 1080x1440 背景（3:4 竖版）。

AI 出图往往带两条尾巴：底部的「AI生成」水印、以及比目标更高的画幅比例。
本脚本只做三件事：按 3:4 裁窗口 -> 缩放到画布尺寸 -> 存 PNG。

用法：
    python prepare_card_background.py <源图> <输出png> [top]

    top = 3:4 裁剪窗口的起始 y 坐标（默认居中）。
          源图底部有水印时传 top=0（从顶部开始裁），水印就会落在窗口之外。
          传 top=-1 则从底部对齐（保留底部、切顶部）。
"""

from __future__ import annotations

import sys

from PIL import Image

TARGET_W, TARGET_H = 1080, 1440
RATIO = TARGET_W / TARGET_H  # 0.75


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    src, dst = sys.argv[1], sys.argv[2]
    image = Image.open(src).convert("RGB")
    width, height = image.size
    print(f"源图 {width}x{height}  比例 1:{height / width:.3f}")

    # 以宽度为准算出 3:4 需要的高度；窗口统一按宽度铺满，避免二次缩放引入模糊
    need_h = int(round(width / RATIO))
    if need_h > height:
        # 源图不够高：退回按高度算宽度，宁可左右各裁一点
        need_w = int(round(height * RATIO))
        left = (width - need_w) // 2
        box = (left, 0, left + need_w, height)
        print(f"源图偏矮，按高度取窗口 {box[2] - box[0]}x{box[3] - box[1]}")
    else:
        top = 0 if len(sys.argv) < 4 else int(sys.argv[3])
        if top == -1:
            top = height - need_h
        top = max(0, min(top, height - need_h))
        box = (0, top, width, top + need_h)
        print(f"裁剪窗口 y {box[1]}..{box[3]}（高 {need_h}，3:4 达标）")

    cropped = image.crop(box)

    # 缩放：AI 出图本身平滑，用 LANCZOS 即可；倍数接近 1 时几乎无损
    resized = cropped.resize((TARGET_W, TARGET_H), Image.LANCZOS)
    resized.save(dst, "PNG", optimize=True)

    gray = resized.convert("L")
    values = list(gray.get_flattened_data()) if hasattr(gray, "get_flattened_data") else list(gray.getdata())
    mean = sum(values) / len(values)
    print(f"输出 {dst}  {TARGET_W}x{TARGET_H}  平均亮度 {mean:.0f}")
    if mean > 140:
        print("⚠️ 背景偏亮：ProgressRenderer 用的是浅色字，偏亮会读不清")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
