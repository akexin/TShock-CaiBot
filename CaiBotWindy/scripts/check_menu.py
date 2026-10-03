#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
交叉校验：确保 menu/menu.json 与 menu/panels.json 里出现的每一条指令，
都能在 C# 源码的 [Command(...)] 里找到对应的「命令名或别名」。

原因：指令面板的元素 name 会被填进输入框（见 qq-bot-menu-panel
PANEL_TYPE_HINTS.command = '点击后把名称填入输入框，由用户确认发送'），
所以 name 必须是真正可执行的指令名或别名，否则用户点一下就是一条无效输入。

Windy 的 CommandAttribute 第 4 个参数 params string[] parameters 实际充当别名表，
匹配逻辑见 Windy.SDK/Command/CommandRegistry.cs:
    c.Parameters.Any(p => string.Equals(p, commandName, OrdinalIgnoreCase))

用法：python scripts/check_menu.py
"""

from __future__ import annotations

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
COMMAND_DIR = ROOT / "src" / "CaiBotWindy" / "Commands"

# 匹配整个 [Command(...)] 特性体（可能跨行），再逐条抽出字符串字面量。
ATTR_RE = re.compile(r"\[Command\(([^]]*)\)\]", re.S)
STR_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')

SCENES = {"MessageScene.Group", "MessageScene.GroupAt", "MessageScene.Private", "MessageScene.Channel"}


def collect_commands() -> tuple[set[str], set[str], dict[str, str]]:
    """返回 (命令名集合, 别名集合, 命令名 -> 声明文件)。"""
    names: set[str] = set()
    aliases: set[str] = set()
    origin: dict[str, str] = {}

    for cs in sorted(COMMAND_DIR.glob("*.cs")):
        text = cs.read_text(encoding="utf-8")
        for body in ATTR_RE.findall(text):
            parts = [p.strip() for p in split_top_level(body)]
            if not parts:
                continue
            name = parts[0].strip('"')
            names.add(name)
            origin.setdefault(name, cs.name)
            rest = parts[2:] if len(parts) > 2 else []
            for part in rest:
                if part in SCENES or not part.startswith('"'):
                    continue
                aliases.add(part.strip('"'))

    return names, aliases, origin


def split_top_level(body: str) -> list[str]:
    """按顶层逗号切分实参，忽略引号内的逗号。"""
    out, buf, in_str, esc = [], [], False, False
    for ch in body:
        if esc:
            buf.append(ch)
            esc = False
            continue
        if ch == "\\" and in_str:
            buf.append(ch)
            esc = True
            continue
        if ch == '"':
            in_str = not in_str
            buf.append(ch)
            continue
        if ch == "," and not in_str:
            out.append("".join(buf).strip())
            buf = []
            continue
        buf.append(ch)
    if buf:
        out.append("".join(buf).strip())
    return [p for p in out if p]


def first_token(text: str) -> str:
    """从 '/添加服务器 ' 这类文本里取出指令名。"""
    return text.strip().lstrip("/").lstrip("!").lstrip("！").split(" ")[0].split("\t")[0]


def main() -> int:
    names, aliases, origin = collect_commands()
    known = names | aliases

    print(f"C# 命令名 {len(names)} 个，别名 {len(aliases)} 个，合计可触发 {len(known)} 个")

    refs: list[tuple[str, str]] = []  # (来源, 指令名)

    menu = json.loads((ROOT / "menu" / "menu.json").read_text(encoding="utf-8"))

    def walk(items: list[dict], path: str) -> None:
        for item in items:
            label = f"{path}/{item.get('name', '')}"
            if item.get("send_message"):
                refs.append((label, first_token(item["send_message"])))
            sub = item.get("sub_menu_items")
            if sub:
                walk(sub, label)

    walk(menu["menu"]["items"], "菜单")

    panels = json.loads((ROOT / "menu" / "panels.json").read_text(encoding="utf-8"))
    for payload in panels:
        remark = payload.get("panel", {}).get("remark", "?")
        for item in payload["panel"]["items"]:
            refs.append((f"面板[{remark}]", first_token(item["name"])))

    bad = [(src, cmd) for src, cmd in refs if cmd not in known]
    if bad:
        print(f"\n!! {len(bad)} 处引用了不存在的指令（点击后会插入无效文本）：")
        for src, cmd in bad:
            print(f"   - {src} -> {cmd!r}")
        return 1

    print(f"\nOK: 菜单 {sum(1 for _ in refs)} 处引用全部可执行")

    used = {cmd for _, cmd in refs}
    unused = sorted(names - used)
    print(f"\n未被菜单/面板引用的命令({len(unused)}): {'、'.join(unused) if unused else '无'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
