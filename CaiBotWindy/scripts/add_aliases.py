#!/usr/bin/env python3
"""给 CaiBotWindy 的全部指令批量追加别名。

别名重名会让 SDK 的 CommandRegistry 抛异常、整个机器人启动崩溃，
所以这个脚本先做冲突检测（dry-run），通过后才写文件。

用法：
    python add_aliases.py            # 只检测，打印计划
    python add_aliases.py --apply    # 实际写入
"""

import re
import sys
from pathlib import Path

COMMAND_DIR = Path(__file__).resolve().parent.parent / "src" / "CaiBotWindy" / "Commands"

# 指令名 -> 追加的别名（相似名称 / 英文 / 拼音简写）
ALIASES: dict[str, list[str]] = {
    # ── 在线与状态 ──
    "在线": ["zx", "online", "谁在线", "在线玩家"],
    "在线总览": ["zxzl", "allonline", "全部在线"],
    "系统状态": ["xtzt", "status", "机器状态", "状态查询"],
    "进度查询": ["jdcx", "progress", "boss进度"],
    "查背包": ["cbb", "bag", "看背包", "背包查询"],
    "排行": ["ph", "rank", "榜单", "排行榜"],
    "插件列表": ["cjlb", "plugins", "插件", "模组"],
    "自踢": ["zt", "kick", "断开连接"],
    # ── 服务器 ──
    "服务器列表": ["fwqlb", "servers", "服列表"],
    "服务器信息": ["fwqxx", "serverinfo", "服务器详情"],
    "查看地图": ["ckdt", "map", "世界地图"],
    "下载地图": ["xzdt", "dlmap", "世界文件"],
    "下载小地图": ["xzxdt", "tmap", "小地图"],
    "服务器": ["fwq", "server", "srv"],
    "地图": ["dt", "mapkit", "地图工具"],
    # ── 图鉴 ──
    "si": ["item", "wp"],
    "sn": ["npc", "sw"],
    "sp": ["proj", "dm"],
    "sb": ["buff", "zy"],
    "sx": ["prefix", "xsy"],
    # ── 注册与白名单 ──
    "注册": ["zc", "register", "邮箱注册", "reg"],
    "注册验证": ["zcyz", "verify", "验证邮箱"],
    "我的注册": ["wdzc", "myreg", "我的账号"],
    "白名单": ["bmd", "whitelist", "白名单管理"],
    "登录": ["dl", "login", "登录验证"],
    "签到": ["qd", "signin", "sign", "每日签到"],
    "查询金币": ["cxjb", "coins", "金币", "money"],
    "添加白名单": ["tjbmd", "addwhitelist"],
    "修改白名单": ["xgbmd", "editwhitelist"],
    "删除白名单": ["scbmd", "delwhitelist", "解绑"],
    "我的白名单": ["wdbmd", "mywhitelist"],
    "查询玩家": ["cxwj", "findplayer"],
    "确认登录": ["qrdl", "confirmlogin"],
    "拒绝登录": ["jjdl", "rejectlogin"],
    "注册限制": ["zcxz", "reglimit", "注册上限"],
    # ── 群管理 ──
    "群": ["q", "group", "qgl"],
    "申请列表": ["sqlb", "joins", "入群申请"],
    "审批入群": ["spjr", "approve", "批准入群"],
    "入群审核": ["rqsh", "joinreview", "审核方式"],
    "审批策略": ["spcl", "strategy", "自动审批"],
    "禁言状态": ["jyzt", "mutestatus", "禁言查询"],
    "禁言": ["jy", "mute", "禁言成员"],
    "全局封禁": ["qjfj", "gban", "云黑封禁"],
    "全局解封": ["qjjf", "gunban", "云黑解封"],
    "全局黑名单": ["qjhmd", "gblacklist", "云黑"],
    "管理列表": ["gllb", "admins", "管理员列表"],
    "添加管理": ["tjgl", "addadmin"],
    "删除管理": ["scgl", "deladmin"],
    "绑定父群": ["bdfq", "bindparent"],
    "解绑父群": ["jbfq", "unbindparent"],
    "获取群信息": ["hqqxx", "groupinfo", "群信息"],
    "设置": ["sz", "settings"],
    "添加黑名单": ["tjhmd", "addblacklist", "拉黑"],
    "删除黑名单": ["schmd", "delblacklist", "解除拉黑"],
    "黑名单列表": ["hmdlb", "blacklist", "黑名单"],
    "权限请求": ["qqql", "permreq", "申请权限"],
    # ── 服务器管理 ──
    "添加服务器": ["tjfwq", "addserver"],
    "修改服务器": ["xgfwq", "editserver"],
    "删除服务器": ["scfwq", "delserver"],
    "解绑服务器": ["jbfwq", "unbindserver"],
    # ── 运维 ──
    "物品监控": ["wpjk", "monitor", "监控"],
    "远程指令": ["yczl", "rcon", "remote", "执行指令"],
    "菜单面板": ["cdyb", "panel", "面板配置"],
    # ── 其它 ──
    "关于": ["gy", "about", "作者", "版本信息"],
    "文档": ["wdoc", "doc", "说明书", "使用文档"],
    "所有指令": ["syzl", "allcmd", "全部指令", "指令列表"],
    "绑定信息": ["bdxx", "binding", "绑定关系"],
    "日志": ["rz", "log", "logs", "服务端日志"],
    "日志搜索": ["rzss", "logsearch", "搜日志"],
    "菜单": ["cd", "menu", "帮助菜单"],
    "帮助": ["bz", "help", "指令帮助"],
    "服务器管理": ["fwqgl", "servermanage"],
    "快捷功能": ["kjgn", "quick"],
    "地图功能": ["dtgn", "mapmenu"],
    "图鉴搜索菜单": ["tjsscd", "searchmenu"],
    "白名单菜单": ["bmdcd", "wlmenu"],
    "群管理": ["qglcd", "groupmenu"],
}

PATTERN = re.compile(
    r'(\[Command\("(?P<name>[^"]+)"[^\)]*?)(\)\])'
)


def main() -> int:
    apply = "--apply" in sys.argv
    files = sorted(COMMAND_DIR.glob("*.cs"))

    # 第一遍：收集所有已存在的名字与场景，用于冲突检测
    existing: dict[tuple[str, str], str] = {}
    plan: list[tuple[Path, str, str]] = []

    for path in files:
        text = path.read_text(encoding="utf-8")
        for m in re.finditer(r'\[Command\("([^"]+)"[^\]]*?MessageScene\.(\w+)([^\]]*)\)\]', text):
            name, scene, tail = m.group(1), m.group(2), m.group(3)
            existing.setdefault((name, scene), path.name)
            for alias in re.findall(r'"([^"]+)"', tail):
                existing.setdefault((alias, scene), path.name)

    print(f"现有（名字/别名 × 场景）组合：{len(existing)}")

    # 第二遍：检测新别名冲突
    conflicts: list[str] = []
    added_total = 0

    for path in files:
        text = path.read_text(encoding="utf-8")
        new_text = text

        for m in PATTERN.finditer(text):
            name = m.group("name")
            aliases = ALIASES.get(name)
            if not aliases:
                continue

            line = m.group(0)
            scene_m = re.search(r'MessageScene\.(\w+)', line)
            scene = scene_m.group(1) if scene_m else "?"
            tail = line[len(m.group(1)):-2]

            already = set(re.findall(r'"([^"]+)"', tail))
            fresh = [a for a in aliases if a not in already]

            for alias in fresh:
                key = (alias, scene)
                if key in existing:
                    conflicts.append(f"  ✗ 「{alias}」在 {scene} 场景已被 {existing[key]} 占用（想给「{name}」）")
                else:
                    existing[key] = path.name

            if fresh:
                added_total += len(fresh)
                plan.append((path, name, ", ".join(f'"{a}"' for a in fresh)))

    if conflicts:
        print("\n发现别名冲突，已中止：")
        for c in conflicts:
            print(c)
        return 1

    print(f"计划新增别名 {added_total} 个（去重后），覆盖 {len(plan)} 个属性\n")
    by_file: dict[str, list[tuple[str, str]]] = {}
    for path, name, aliases in plan:
        by_file.setdefault(path.name, []).append((name, aliases))

    for fname, items in by_file.items():
        print(f"  {fname}:")
        for name, aliases in items[:4]:
            print(f"    {name} ← {aliases}")
        if len(items) > 4:
            print(f"    …（共 {len(items)} 个）")

    if not apply:
        print("\n（dry-run，未写文件。加 --apply 生效）")
        return 0

    # 第三遍：写文件
    for path in files:
        text = path.read_text(encoding="utf-8")

        def replace(m: re.Match) -> str:
            name = m.group("name")
            aliases = ALIASES.get(name)
            if not aliases:
                return m.group(0)
            head, closer = m.group(1), m.group(3)
            tail = head[len(f'Command("{name}"'):]
            # 去掉现有别名后的参数部分
            parsed = re.match(r'("[^"]*",\s*MessageScene\.\w+)?(.*)', tail)
            base = parsed.group(1) or ""
            existing_tail = parsed.group(2) or ""
            already = set(re.findall(r'"([^"]+)"', existing_tail))
            fresh = [a for a in aliases if a not in already]
            if not fresh:
                return m.group(0)
            extra = "".join(f', "{a}"' for a in fresh)
            return f'{head}{extra}{closer}'

        new_text = PATTERN.sub(replace, text)
        if new_text != text:
            path.write_text(new_text, encoding="utf-8")
            print(f"  已写入 {path.name}")

    print("\n完成")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
