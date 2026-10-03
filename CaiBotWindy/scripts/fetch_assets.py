#!/usr/bin/env python3
"""从 UnrealMultiple/CaiBotLite 仓库拉取素材。

素材目录结构与官方 assets/ 完全一致（会剥掉仓库里的 assets/ 前缀，
这样插件的 HTTP 路由就是 /assets/images/items/Item_39.png）。

用法：
    python scripts/fetch_assets.py core   # 进度卡片渲染必需的素材（约 70 个）
    python scripts/fetch_assets.py all    # 全部素材（约 8500 个，含图鉴图标）

说明：
    - 默认走 testingcf.jsdelivr.net（实测国内最快，20 并发约 9 文件/秒）；
      cdn.jsdelivr.net 常返回 301，必须让 curl 跟随重定向（脚本已带 -L）。
    - 已存在且非空的文件会跳过，所以可以随时中断、重跑续抓。
"""

from __future__ import annotations

import argparse
import concurrent.futures
import json
import os
import subprocess
import sys
import urllib.parse

REPO = "UnrealMultiple/CaiBotLite"
BRANCH = "master"
DEFAULT_CDN = "testingcf.jsdelivr.net"
TREE_API = f"https://api.github.com/repos/{REPO}/git/trees/{BRANCH}?recursive=1"

# 进度卡片渲染必需：背景 + Boss 头像 + 世界图标 + 字体 + 进度锁图标
CORE_PREFIXES = (
    "assets/fonts/",
    "assets/images/bosses/",
    "assets/images/backgrounds/",
    "assets/images/world_icon/",
)
CORE_FILES = ("assets/images/items/Item_5328.png",)


def list_assets(tree_file: str) -> list[str]:
    """优先读本地缓存的 git tree（api.github.com 在国内常连不上）。"""
    if tree_file and os.path.isfile(tree_file):
        with open(tree_file, "r", encoding="utf-8") as fh:
            tree = json.load(fh)["tree"]
    else:
        proc = subprocess.run(
            ["curl", "-sL", "--max-time", "120", "-H", "User-Agent: cai-windy-assets", TREE_API],
            capture_output=True,
            check=True,
        )
        tree = json.loads(proc.stdout.decode("utf-8"))["tree"]
    return [item["path"] for item in tree if item["type"] == "blob" and item["path"].startswith("assets/")]


def select(paths: list[str], mode: str) -> list[str]:
    if mode == "all":
        return paths
    return [p for p in paths if p.startswith(CORE_PREFIXES) or p in CORE_FILES]


def download(path: str, out_root: str, cdn: str) -> tuple[str, bool, str]:
    # 仓库里是 assets/images/...，落盘时剥掉 assets/ 前缀（HTTP 路由已经带 /assets/）
    relative = path[len("assets/"):] if path.startswith("assets/") else path
    target = os.path.join(out_root, relative.replace("/", os.sep))
    if os.path.isfile(target) and os.path.getsize(target) > 0:
        return path, True, "cached"

    os.makedirs(os.path.dirname(target) or ".", exist_ok=True)
    url = f"https://{cdn}/gh/{REPO}@{BRANCH}/{urllib.parse.quote(path)}"
    result = subprocess.run(
        ["curl", "-sL", "--max-time", "40", "--retry", "2", "-o", target, "-w", "%{http_code}", url],
        capture_output=True,
    )
    code = result.stdout.decode("utf-8", errors="ignore").strip()
    if code.startswith("2") and os.path.isfile(target) and os.path.getsize(target) > 0:
        return path, True, f"{os.path.getsize(target)}B"

    if os.path.isfile(target):
        try:
            os.remove(target)
        except OSError:
            pass
    return path, False, f"HTTP {code or result.returncode}"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["core", "all"], nargs="?", default="core")
    parser.add_argument("--out", default=os.path.join("CaiBotWindy", "deploy", "Asserts"))
    parser.add_argument("--tree", default="", help="本地 git tree json（api.github.com 连不上时用）")
    parser.add_argument("--cdn", default=DEFAULT_CDN)
    parser.add_argument("--workers", type=int, default=20)
    args = parser.parse_args()

    paths = select(list_assets(args.tree), args.mode)
    print(f"待下载 {len(paths)} 个文件 → {args.out}（CDN: {args.cdn}，并发 {args.workers}）", flush=True)

    ok = 0
    failed: list[str] = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = {pool.submit(download, p, args.out, args.cdn): p for p in paths}
        for index, future in enumerate(concurrent.futures.as_completed(futures), 1):
            path, success, info = future.result()
            if success:
                ok += 1
            else:
                failed.append(f"{path} ({info})")
            if index % 250 == 0 or index == len(paths):
                print(f"  [{index}/{len(paths)}] 成功 {ok}，失败 {len(failed)}", flush=True)

    print(f"\n完成：成功 {ok} / {len(paths)}")
    if failed:
        print("失败清单（前 20，重跑本脚本即可续抓）：")
        for item in failed[:20]:
            print("  -", item)
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main())
