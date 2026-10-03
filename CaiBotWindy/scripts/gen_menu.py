#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 QQ 开放平台「自定义菜单」与「指令面板」配置。

产物就是平台 API 的**请求体**，用 scripts/publish_menu.mjs 可直接推送：
  menu/menu.json     -> PUT https://api.bot.qq.com/v2/menu 的请求体 {"menu": {"items": [...]}}
  menu/panels.json   -> POST https://api.bot.qq.com/v2/panels 的请求体数组（CreatePanelPayload）

注：qq-bot-menu-panel 是可视化编辑器，只支持「连平台 → 拉取 → 编辑 → 存回」，
    **没有导入本地文件的功能**，所以别指望把这两个 json 拖进去。

平台限制（前六项来自官方文档，最后一项为实测结论）：
  一级菜单 <= 10 个；菜单名称 <= 10 字符（1 个中文按 2 字符计）
  子菜单   <= 5 个且不再嵌套；子菜单名称 <= 14 字符
  单个面板 <= 20 个元素；元素名称 <= 14 字符；元素描述 <= 30 字符
  备注     <= 255 字符；一个机器人最多 20 个面板
  ⚠ 同一 scope + target 下**只能存在 1 个面板**：后建的直接替换先建的（实测确认）
     → 因此 group 与 c2c 各配 1 个，把高频指令压进这 1 个面板的 20 个槽位；
       其余功能通过面板里的「菜单」入口逐层下钻

用法：python scripts/gen_menu.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

# ── 平台限制 ──────────────────────────────────────────────────────────────────
LIMIT_MENU_ITEMS = 10
LIMIT_MENU_ITEM_NAME = 10       # 1 个中文 = 2
LIMIT_SUB_MENU_ITEMS = 5
LIMIT_SUB_MENU_ITEM_NAME = 14
LIMIT_PANEL_ITEMS = 20
LIMIT_PANEL_ITEM_NAME = 14
LIMIT_PANEL_ITEM_DESC = 30
LIMIT_PANEL_REMARK = 255
LIMIT_PANEL_COUNT = 20

WIDTH = 2  # 宽字符宽度


def wlen(text: str) -> int:
    """按平台口径计算字符数：中日韩全角算 2，其余算 1。"""
    import unicodedata

    return sum(WIDTH if unicodedata.east_asian_width(ch) in ("W", "F") else 1 for ch in text)


def check(label: str, text: str, limit: int, errors: list[str]) -> None:
    n = wlen(text)
    if n > limit:
        errors.append(f"{label} 超限：{text!r} = {n} 字符（上限 {limit}）")


def sub(name: str, command: str) -> dict:
    return {"name": name, "type": "send_message", "send_message": command}


def menu_group(name: str, children: list[dict]) -> dict:
    return {"name": name, "type": "menu", "sub_menu_items": children}


def panel_item(name: str, desc: str, *, admin: bool = False) -> dict:
    item: dict = {"name": name, "desc": desc, "type": "command"}
    if admin:
        item["only_admin"] = True
    return item


# ── 自定义菜单（仅单聊 c2c 生效，全局唯一） ────────────────────────────────────
MENU_ITEMS: list[dict] = [
    menu_group("服务器管理", [
        sub("添加服务器", "/添加服务器 "),
        sub("服务器列表", "/服务器列表"),
        sub("服务器信息", "/服务器信息 "),
        sub("修改服务器", "/修改服务器 "),
        sub("删除服务器", "/删除服务器 "),
    ]),
    menu_group("快捷功能", [
        sub("在线", "/在线"),
        sub("进度查询", "/进度查询"),
        sub("查背包", "/查背包 "),
        sub("排行", "/排行 "),
        sub("插件列表", "/插件列表"),
    ]),
    menu_group("地图功能", [
        sub("查看地图", "/查看地图"),
        sub("下载地图", "/下载地图"),
        sub("下载小地图", "/下载小地图"),
    ]),
    menu_group("图鉴搜索", [
        sub("搜物品", "/si "),
        sub("搜生物", "/sn "),
        sub("搜弹幕", "/sp "),
        sub("搜增益", "/sb "),
        sub("搜修饰语", "/sx "),
    ]),
    menu_group("白名单", [
        sub("添加白名单", "/添加白名单 "),
        sub("我的白名单", "/我的白名单"),
        sub("登录", "/登录 "),
        sub("签到", "/签到"),
        sub("查询金币", "/查询金币"),
    ]),
    menu_group("群管理", [
        sub("群信息", "/获取群信息"),
        sub("管理列表", "/管理列表"),
        sub("群设置", "/设置 "),
        sub("黑名单列表", "/黑名单列表"),
        sub("权限请求", "/权限请求"),
    ]),
    sub("服务器列表", "/服务器列表"),
    sub("菜单面板", "/菜单面板"),
    sub("帮助", "/帮助"),
]

# ── 指令面板 ──────────────────────────────────────────────────────────────────
# ⚠ 同一 scope + target 下只能存在 1 个面板（后建替换先建，实测确认），
# 所以这里只声明 group / c2c 各 1 个，把高频入口压进 20 个槽位。
PANELS: list[dict] = [
    {
        "scope": "group",
        "target_type": "all",
        "panel": {
            "remark": "CaiBotWindy 服务器助手",
            "items": [
                # 前 7 个是「下钻入口」：点击后把指令填进输入框，发送即得到带按钮的子菜单，
                # 借此覆盖全部 51 条指令，不必都塞进面板
                panel_item("菜单", "查看全部功能"),
                panel_item("服务器管理", "服务器管理菜单"),
                panel_item("快捷功能", "快捷功能菜单"),
                panel_item("地图功能", "地图功能菜单"),
                panel_item("图鉴搜索菜单", "图鉴搜索菜单"),
                panel_item("白名单菜单", "白名单菜单"),
                panel_item("群管理", "群管理菜单"),
                # 以下为高频直连指令
                panel_item("在线", "获取在线玩家列表"),
                panel_item("进度查询", "查询世界 Boss 进度"),
                panel_item("服务器列表", "获取服务器地址与端口"),
                panel_item("服务器信息", "查看服务器详细信息"),
                panel_item("我的白名单", "查看自己的绑定信息"),
                panel_item("签到", "每日签到领取金币"),
                panel_item("查询金币", "查看金币余额"),
                panel_item("查看地图", "获取世界地图预览图"),
                panel_item("下载地图", "下载世界文件 .wld"),
                panel_item("下载小地图", "下载小地图文件 .tmap"),
                panel_item("查背包", "查询指定玩家的背包"),
                panel_item("排行", "查询服务器排行榜"),
                panel_item("插件列表", "查看插件与模组列表"),
            ],
        },
    },
    {
        "scope": "c2c",
        "target_type": "all",
        "panel": {
            "remark": "CaiBotWindy 私聊助手",
            "items": [
                panel_item("菜单", "查看全部功能"),
                panel_item("搜物品", "按名字或 ID 搜物品"),
                panel_item("搜生物", "按名字或 ID 搜生物"),
                panel_item("搜弹幕", "按名字或 ID 搜弹幕"),
                panel_item("搜增益", "按名字或 ID 搜增益"),
                panel_item("搜修饰语", "按名字或 ID 搜修饰语"),
                panel_item("我的白名单", "查看自己的绑定信息"),
                panel_item("签到", "每日签到领取金币"),
                panel_item("查询金币", "查看金币余额"),
            ],
        },
    },
]


def validate() -> list[str]:
    errors: list[str] = []

    if len(MENU_ITEMS) > LIMIT_MENU_ITEMS:
        errors.append(f"一级菜单数量超限：{len(MENU_ITEMS)} > {LIMIT_MENU_ITEMS}")

    for item in MENU_ITEMS:
        check("菜单名称", item["name"], LIMIT_MENU_ITEM_NAME, errors)
        children = item.get("sub_menu_items")
        if children is None:
            continue
        if len(children) > LIMIT_SUB_MENU_ITEMS:
            errors.append(f"子菜单数量超限：{item['name']} -> {len(children)} > {LIMIT_SUB_MENU_ITEMS}")
        for child in children:
            check(f"子菜单名称（{item['name']}）", child["name"], LIMIT_SUB_MENU_ITEM_NAME, errors)

    if len(PANELS) > LIMIT_PANEL_COUNT:
        errors.append(f"面板数量超限：{len(PANELS)} > {LIMIT_PANEL_COUNT}")

    # 实测结论：同一 scope + target 下只能存在 1 个面板，后建的直接替换先建的
    seen_scopes: dict[tuple[str, str], int] = {}
    for index, payload in enumerate(PANELS, 1):
        key = (payload["scope"], payload["target_type"])
        if key in seen_scopes:
            errors.append(
                f"同一 scope + target 只能有 1 个面板："
                f"{key[0]}/{key[1]} 已在第 {seen_scopes[key]} 个声明，第 {index} 个会把它顶掉"
            )
        else:
            seen_scopes[key] = index

    for index, payload in enumerate(PANELS, 1):
        panel = payload["panel"]
        tag = panel.get("remark", f"#{index}")
        if len(panel["items"]) > LIMIT_PANEL_ITEMS:
            errors.append(f"面板元素超限：{tag} -> {len(panel['items'])} > {LIMIT_PANEL_ITEMS}")
        check("面板备注", tag, LIMIT_PANEL_REMARK, errors)
        for item in panel["items"]:
            check(f"元素名称（{tag}）", item["name"], LIMIT_PANEL_ITEM_NAME, errors)
            check(f"元素描述（{tag}）", item.get("desc", ""), LIMIT_PANEL_ITEM_DESC, errors)

    return errors


def main() -> int:
    errors = validate()
    if errors:
        print("校验未通过：")
        for line in errors:
            print("  -", line)
        return 1

    root = Path(__file__).resolve().parent.parent
    menu_dir = root / "menu"
    menu_dir.mkdir(parents=True, exist_ok=True)

    menu_path = menu_dir / "menu.json"
    panels_path = menu_dir / "panels.json"

    # PUT /v2/menu 的请求体就是 {"menu": {...}}，落盘后可直接用 publish_menu.mjs 推送。
    menu_path.write_text(
        json.dumps({"menu": {"items": MENU_ITEMS}}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    panels_path.write_text(
        json.dumps(PANELS, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    total_items = sum(len(p["panel"]["items"]) for p in PANELS)
    print("校验通过，已生成：")
    print(f"  {menu_path}  一级菜单 {len(MENU_ITEMS)} 个，"
          f"子菜单 {sum(len(i.get('sub_menu_items', [])) for i in MENU_ITEMS)} 个，"
          f"{menu_path.stat().st_size:,} 字节")
    print(f"  {panels_path}  面板 {len(PANELS)} 个，指令 {total_items} 条，"
          f"{panels_path.stat().st_size:,} 字节")
    return 0


if __name__ == "__main__":
    sys.exit(main())
