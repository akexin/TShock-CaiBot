using CaiBotWindy.Services;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>
/// 合并版指令：把同类操作收进一个指令的子命令里，方便在 QQ 菜单有限的槽位中铺开。
///
/// <para><b>为什么需要它</b>：QQ 自定义菜单的结构上限是「9 个一级项 × 每组 5 个子项」，
/// 而本项目的指令是分散命名的（添加服务器 / 修改服务器 / 删除服务器 ……）。
/// 合并后菜单能覆盖全部功能，同时 <b>旧指令一个都没删</b>。</para>
///
/// <para><b>转发原理</b>：<see cref="CommandArgs"/> 的构造函数是公开的，把子命令剥掉、
/// 用剩余参数新建一个 <see cref="CommandArgs"/> 交给原方法即可 ——
/// 因此被合并的那些指令<b>实现不需要任何改动</b>，老玩家继续用旧写法完全不受影响。
/// 新指令只是给同一批功能多提供一条更整齐的入口。</para>
///
/// <para><b>子命令同时接受中文与英文</b>（如 <c>列表</c> / <c>list</c>），
/// 避免有人习惯打英文时突然卡住。</para>
/// </summary>
public static class MergedCommands
{
    /// <summary>剥掉子命令后，用剩余参数转发给原指令方法。</summary>
    private static CommandArgs Forward(CommandArgs args, string[] rest)
    {
        return new CommandArgs(args.CommandName, rest, args.Message);
    }

    private static string[] Rest(CommandArgs args, int skip)
    {
        return skip >= args.Parameters.Length ? [] : args.Parameters[skip..];
    }

    private static string Sub(CommandArgs args, int index = 0)
    {
        return args.GetOrDefault(index).Trim().ToLowerInvariant();
    }

    // ── /服务器 ────────────────────────────────────────────────────────────────

    private const string ServerUsage =
        "# 🍥 服务器\n" +
        "> 用法：`/服务器 <子命令>`\n" +
        "- `列表`　本群已绑定的服务器\n" +
        "- `信息 <序号>`　某台服务器的详情\n" +
        "- `添加 <IP> <端口> <绑定码>`　绑定新服务器\n" +
        "- `修改 <序号> [新IP] [新端口]`　改地址\n" +
        "- `删除 <序号>`　删除绑定\n" +
        "- `解绑 <序号>`　断开与服务器的连接\n\n" +
        "> 旧写法（`/服务器列表`、`/添加服务器` 等）仍然可用。";

    [Command("服务器", "服务器管理（列表 | 信息 | 添加 | 修改 | 删除 | 解绑）", MessageScene.Group, "fwq", "server", "srv")]
    [Command("服务器", "服务器管理（列表 | 信息 | 添加 | 修改 | 删除 | 解绑）", MessageScene.GroupAt, "fwq", "server", "srv")]
    public static async Task ServerAsync(CommandArgs args)
    {
        string[] rest = Rest(args, 1);
        string sub = Sub(args);

        if (sub.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, ServerUsage);
            return;
        }

        switch (sub)
        {
            case "列表" or "list":
                await ServerCommands.ServerListAsync(Forward(args, rest));
                break;
            case "信息" or "info":
                await ServerCommands.ServerInfoAsync(Forward(args, rest));
                break;
            case "添加" or "add":
                await ServerAdminCommands.AddServerAsync(Forward(args, rest));
                break;
            case "修改" or "edit":
                await ServerAdminCommands.EditServerAsync(Forward(args, rest));
                break;
            case "删除" or "delete" or "del":
                await ServerAdminCommands.DeleteServerAsync(Forward(args, rest));
                break;
            case "解绑" or "unbind":
                await ServerCommands.UnbindServerAsync(Forward(args, rest));
                break;
            default:
                await CommandHelpers.ReplyAsync(args, ServerUsage);
                break;
        }
    }

    // ── /白名单 ────────────────────────────────────────────────────────────────

    private const string WhitelistUsage =
        "# 🍥 白名单\n" +
        "> 用法：`/白名单 <子命令>`\n" +
        "- `添加 <角色名>`　**管理员**手动绑定\n" +
        "- `修改 <新角色名>`　**管理员**重新绑定\n" +
        "- `删除`　解除自己的绑定\n" +
        "- `我的`　查看自己的绑定信息\n" +
        "- `查询 <角色名>`　**管理员**按名字查记录\n\n" +
        "> 玩家自助请用 `/注册`；旧写法（`/添加白名单` 等）仍然可用。";

    [Command("白名单", "白名单管理（添加 | 修改 | 删除 | 我的 | 查询）", MessageScene.Group, "bmd", "whitelist", "白名单管理")]
    [Command("白名单", "白名单管理（添加 | 修改 | 删除 | 我的 | 查询）", MessageScene.GroupAt, "bmd", "whitelist", "白名单管理")]
    [Command("白名单", "白名单管理（添加 | 修改 | 删除 | 我的 | 查询）", MessageScene.Private, "bmd", "whitelist", "白名单管理")]
    public static async Task WhitelistAsync(CommandArgs args)
    {
        string[] rest = Rest(args, 1);
        string sub = Sub(args);

        if (sub.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, WhitelistUsage);
            return;
        }

        switch (sub)
        {
            case "添加" or "add":
                await WhitelistCommands.BindAsync(Forward(args, rest));
                break;
            case "修改" or "edit":
                await WhitelistCommands.RebindAsync(Forward(args, rest));
                break;
            case "删除" or "delete" or "del":
                await WhitelistCommands.UnbindAsync(Forward(args, rest));
                break;
            case "我的" or "my":
                await WhitelistCommands.MyWhitelistAsync(Forward(args, rest));
                break;
            case "查询" or "find":
                await WhitelistCommands.FindPlayerAsync(Forward(args, rest));
                break;
            default:
                await CommandHelpers.ReplyAsync(args, WhitelistUsage);
                break;
        }
    }

    // ── /地图 ──────────────────────────────────────────────────────────────────

    private const string MapUsage =
        "# 🍥 地图\n" +
        "> 用法：`/地图 <子命令>`\n" +
        "- `预览`　世界地图预览图\n" +
        "- `下载`　下载世界文件 `.wld`\n" +
        "- `小地图`　下载小地图文件 `.tmap`\n\n" +
        "> 旧写法（`/查看地图`、`/下载地图`、`/下载小地图`）仍然可用。";

    [Command("地图", "地图相关（预览 | 下载 | 小地图）", MessageScene.Group, "dt", "mapkit", "地图工具")]
    [Command("地图", "地图相关（预览 | 下载 | 小地图）", MessageScene.GroupAt, "dt", "mapkit", "地图工具")]
    public static async Task MapAsync(CommandArgs args)
    {
        string[] rest = Rest(args, 1);
        string sub = Sub(args);

        switch (sub)
        {
            case "预览" or "view":
                await ServerCommands.MapImageAsync(Forward(args, rest));
                break;
            case "下载" or "world":
                await ServerCommands.WorldFileAsync(Forward(args, rest));
                break;
            case "小地图" or "tmap":
                await ServerCommands.MapFileAsync(Forward(args, rest));
                break;
            default:
                await CommandHelpers.ReplyAsync(args, MapUsage);
                break;
        }
    }

    // ── /群（群管理总入口，含二级子命令）────────────────────────────────────────

    private const string GroupUsage =
        "# 🍥 群管理\n" +
        "> 用法：`/群 <子命令>`\n" +
        "- `信息`　本群与机器人绑定信息\n" +
        "- `设置 [项] [值]`　查看或修改群设置\n" +
        "- `管理 <列表|添加|删除>`　群管理员维护\n" +
        "- `父群 <绑定|解绑> <OpenID>`　父群联动\n" +
        "- `黑名单 <列表|添加|删除>`　本群黑名单\n" +
        "- `全局 <列表|封禁|解封>`　云黑（所有绑定服务器共享）\n" +
        "- `子群`　查看绑定到本群的子群（父群视角）\n" +
        "- `权限`　向群主申请机器人权限\n\n" +
        "> 旧写法（`/管理列表`、`/全局封禁` 等）仍然可用。";

    [Command("群", "群管理（信息 | 设置 | 管理 | 父群 | 黑名单 | 全局 | 权限）", MessageScene.Group, "q", "group", "qgl")]
    [Command("群", "群管理（信息 | 设置 | 管理 | 父群 | 黑名单 | 全局 | 权限）", MessageScene.GroupAt, "q", "group", "qgl")]
    public static async Task GroupAsync(CommandArgs args)
    {
        string sub = Sub(args);
        string[] rest = Rest(args, 1);

        switch (sub)
        {
            case "信息" or "info":
                await GroupCommands.GroupInfoAsync(Forward(args, rest));
                break;
            case "设置" or "set":
                await GroupCommands.SettingsAsync(Forward(args, rest));
                break;
            case "权限" or "perm":
                await GroupCommands.RequestPermissionAsync(Forward(args, rest));
                break;
            case "子群" or "children":
                await GroupCommands.ListChildGroupsAsync(Forward(args, rest));
                break;
            case "管理" or "admin":
                await GroupAdminAsync(args, rest);
                break;
            case "父群" or "parent":
                await GroupParentAsync(args, rest);
                break;
            case "黑名单" or "blacklist":
                await GroupBlacklistAsync(args, rest);
                break;
            case "全局" or "global":
                await GroupGlobalAsync(args, rest);
                break;
            default:
                await CommandHelpers.ReplyAsync(args, GroupUsage);
                break;
        }
    }

    private static async Task GroupAdminAsync(CommandArgs args, string[] rest)
    {
        string action = rest.Length > 0 ? rest[0].Trim().ToLowerInvariant() : "";
        string[] inner = rest.Length > 1 ? rest[1..] : [];

        switch (action)
        {
            case "列表" or "list":
                await GroupCommands.ListAdminAsync(Forward(args, inner));
                break;
            case "添加" or "add":
                await GroupCommands.AddAdminAsync(Forward(args, inner));
                break;
            case "删除" or "delete" or "del":
                await GroupCommands.RemoveAdminAsync(Forward(args, inner));
                break;
            default:
                await CommandHelpers.ReplyAsync(args,
                    "# 🍥 群管理 · 管理\n" +
                    "> 用法：`/群 管理 <列表|添加|删除>`\n" +
                    $"> 例：{MenuKit.CmdInput("/群 管理 添加 ", "群 管理 添加 <OpenID>")}");
                break;
        }
    }

    private static async Task GroupParentAsync(CommandArgs args, string[] rest)
    {
        string action = rest.Length > 0 ? rest[0].Trim().ToLowerInvariant() : "";
        string[] inner = rest.Length > 1 ? rest[1..] : [];

        switch (action)
        {
            case "绑定" or "bind":
                await GroupCommands.BindParentAsync(Forward(args, inner));
                break;
            case "解绑" or "unbind":
                await GroupCommands.UnbindParentAsync(Forward(args, inner));
                break;
            default:
                await CommandHelpers.ReplyAsync(args,
                    "# 🍥 群管理 · 父群\n" +
                    "> 用法：`/群 父群 <绑定|解绑> <OpenID>`");
                break;
        }
    }

    private static async Task GroupBlacklistAsync(CommandArgs args, string[] rest)
    {
        string action = rest.Length > 0 ? rest[0].Trim().ToLowerInvariant() : "";
        string[] inner = rest.Length > 1 ? rest[1..] : [];

        switch (action)
        {
            case "列表" or "list":
            case "":
                await GroupCommands.ListBlacklistAsync(Forward(args, inner));
                break;
            case "添加" or "add":
                await GroupCommands.AddBlacklistAsync(Forward(args, inner));
                break;
            case "删除" or "delete" or "del":
                await GroupCommands.RemoveBlacklistAsync(Forward(args, inner));
                break;
            default:
                await CommandHelpers.ReplyAsync(args,
                    "# 🍥 群管理 · 黑名单\n" +
                    "> 用法：`/群 黑名单 <列表|添加|删除>`");
                break;
        }
    }

    private static async Task GroupGlobalAsync(CommandArgs args, string[] rest)
    {
        string action = rest.Length > 0 ? rest[0].Trim().ToLowerInvariant() : "";
        string[] inner = rest.Length > 1 ? rest[1..] : [];

        switch (action)
        {
            case "列表" or "list":
            case "":
                await GroupCommands.ListGlobalBlacklistAsync(Forward(args, inner));
                break;
            case "封禁" or "ban":
                await GroupCommands.AddGlobalBlacklistAsync(Forward(args, inner));
                break;
            case "解封" or "unban":
                await GroupCommands.RemoveGlobalBlacklistAsync(Forward(args, inner));
                break;
            default:
                await CommandHelpers.ReplyAsync(args,
                    "# 🍥 群管理 · 云黑\n" +
                    "> 用法：`/群 全局 <列表|封禁|解封>`\n" +
                    "> 封禁维度：角色名 / ip / 设备 / qq\n" +
                    $"> 例：{MenuKit.CmdInput("/群 全局 封禁 ", "群 全局 封禁 <角色名>")}");
                break;
        }
    }
}
