#!/usr/bin/env python3
"""给「输出内容多」的指令回复加上 code: true（走卡片头 + 代码块渲染）。

只处理真正的数据输出（builder.ToString() / markdown / MenuKit.Render* / payload.*），
错误提示一类的单行消息不动。
"""

import re
import sys
from pathlib import Path

COMMAND_DIR = Path(__file__).resolve().parent.parent / "src" / "CaiBotWindy" / "Commands"

TARGETS = {
    "DocCommands.cs": ["BindingInfoAsync", "AllCommandsAsync"],
    "GroupManageCommands.cs": ["JoinRequestListAsync", "MuteStatusAsync"],
    "ServerCommands.cs": [
        "OnlineAsync",
        "OnlineOverviewAsync",
        "ServerListAsync",
        "ServerInfoAsync",
        "RankAsync",
        "PluginListAsync",
        "SystemStatusAsync",
        "ItemMonitorAsync",
        "ProgressAsync",
    ],
}

# 内容表达式里出现这些才认为是「数据输出」
DATA_PATTERNS = ("builder.ToString()", "markdown", "MenuKit.Render", "payload.GetString")


def find_matching(text: str, open_index: int) -> int:
    """返回与 text[open_index]=='(' 配对的 ')' 的下标。"""
    depth = 0
    i = open_index
    in_str = False
    while i < len(text):
        c = text[i]
        if in_str:
            if c == "\\":
                i += 2
                continue
            if c == '"':
                in_str = False
        else:
            if c == '"':
                in_str = True
            elif c == "(":
                depth += 1
            elif c == ")":
                depth -= 1
                if depth == 0:
                    return i
        i += 1
    return -1


def main() -> int:
    apply = "--apply" in sys.argv
    total = 0

    for fname, methods in TARGETS.items():
        path = COMMAND_DIR / fname
        text = path.read_text(encoding="utf-8")
        edits: list[tuple[int, str]] = []

        for method in methods:
            m = re.search(
                rf"public static (?:async )?Task {method}\(CommandArgs args\)(.*?)\n    \}}",
                text,
                re.S,
            )
            if not m:
                print(f"  ⚠️ 未找到 {fname}:{method}")
                continue

            body_start = m.start(1)
            body = m.group(1)

            for call in re.finditer(r"CommandHelpers\.ReplyAsync\(", body):
                open_paren = body_start + call.end() - 1  # 指向 '('
                close_paren = find_matching(text, open_paren)
                if close_paren < 0:
                    continue

                args_text = text[open_paren + 1:close_paren]
                if "code:" in args_text:
                    continue
                if not any(p in args_text for p in DATA_PATTERNS):
                    continue

                edits.append((close_paren, ", code: true"))
                print(f"  {fname:24} {method:20} {args_text.strip()[:60]}")

        for pos, insertion in sorted(edits, reverse=True):
            text = text[:pos] + insertion + text[pos:]
            total += 1

        if apply and edits:
            path.write_text(text, encoding="utf-8")

    print(f"\n共 {total} 处{'（已写入）' if apply else '（dry-run）'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
