using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Services;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>群管理：管理员白名单、父群、群设置、黑名单（对应 CaiBotLite 的群管理菜单）。</summary>
public static class GroupCommands
{
    private static readonly ButtonKeyboard GroupKeyboard = MenuKit.Keyboard(
        ("管理列表", "/管理列表"),
        ("黑名单列表", "/黑名单列表"),
        ("群信息", "/获取群信息"),
        ("帮助", "/帮助"));

    // ── 管理员白名单 ────────────────────────────────────────────────────────────

    [Command("管理列表", "列出本群机器人管理员", MessageScene.Group)]
    [Command("管理列表", "列出本群机器人管理员", MessageScene.GroupAt)]
    public static async Task ListAdminAsync(CommandArgs args)
    {
        string groupOpenId = args.Message.GroupId ?? "";
        GroupRecord group = DataStore.GetOrCreateGroup(groupOpenId);

        StringBuilder builder = new();
        builder.Append("# 🍥 管理列表\n");
        builder.Append($"\n**群管理员白名单**（{group.Admins.Count}）\n");
        builder.Append(group.Admins.Count == 0
            ? "> 暂无。QQ 群主 / 群管理员默认拥有权限。\n"
            : string.Join('\n', group.Admins.Select(item => $"- `{item}`")) + "\n");

        builder.Append($"\n**机器人所有者**（{App.Config.OwnerOpenIds.Count}）\n");
        builder.Append(App.Config.OwnerOpenIds.Count == 0
            ? "> 未配置（见 Config/CaiBotWindy.json 的 OwnerOpenIds）\n"
            : string.Join('\n', App.Config.OwnerOpenIds.Select(item => $"- `{Mask(item)}`")) + "\n");

        if (App.Config.Operators.Count > 0)
        {
            builder.Append($"\n**全局管理员**（{App.Config.Operators.Count}）\n");
            builder.Append(string.Join('\n', App.Config.Operators.Select(item => $"- `{Mask(item)}`")) + "\n");
        }

        await CommandHelpers.ReplyAsync(args, builder.ToString(), GroupKeyboard);
    }

    [Command("添加管理", "添加机器人管理员", MessageScene.Group)]
    [Command("添加管理", "添加机器人管理员", MessageScene.GroupAt)]
    public static async Task AddAdminAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 添加管理\n> 用法：`/添加管理 <用户 OpenID>`\n" +
                "> 让对方先发送 `/我的白名单`，其 OpenID 会显示在回复里。");
            return;
        }

        string openId = args.GetOrDefault(0).Trim();
        GroupRecord group = DataStore.GetOrCreateGroup(args.Message.GroupId ?? "");

        if (group.Admins.Contains(openId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⚠️ 该用户已经是本群管理员。");
            return;
        }

        group.Admins.Add(openId);
        DataStore.SaveServerChange();
        await CommandHelpers.ReplyAsync(args, $"# ✅ 已添加管理员 `{openId}`");
    }

    [Command("删除管理", "移除机器人管理员", MessageScene.Group)]
    [Command("删除管理", "移除机器人管理员", MessageScene.GroupAt)]
    public static async Task RemoveAdminAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args, "# 🍥 删除管理\n> 用法：`/删除管理 <用户 OpenID>`");
            return;
        }

        string openId = args.GetOrDefault(0).Trim();
        GroupRecord group = DataStore.GetOrCreateGroup(args.Message.GroupId ?? "");

        if (!group.Admins.Remove(openId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该用户不在管理员白名单中。");
            return;
        }

        DataStore.SaveServerChange();
        await CommandHelpers.ReplyAsync(args, $"# ✅ 已移除管理员 `{openId}`");
    }

    // ── 父群 ────────────────────────────────────────────────────────────────────

    [Command("绑定父群", "绑定父群以共享白名单", MessageScene.Group)]
    [Command("绑定父群", "绑定父群以共享白名单", MessageScene.GroupAt)]
    public static async Task BindParentAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 绑定父群\n> 用法：`/绑定父群 <父群 OpenID>`\n" +
                "> 绑定后，白名单校验会沿父群链向上查找，父群开启白名单即可覆盖本群。");
            return;
        }

        string parent = args.GetOrDefault(0).Trim();
        if (parent == args.Message.GroupId)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 不能把自己绑定为自己的父群。");
            return;
        }

        // 防止形成环。
        HashSet<string> visited = new(StringComparer.Ordinal) { args.Message.GroupId ?? "" };
        string? cursor = parent;
        while (!string.IsNullOrEmpty(cursor) && visited.Add(cursor))
        {
            cursor = DataStore.FindGroup(cursor)?.ParentGroupOpenId;
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 检测到父群链形成了环，已拒绝该绑定。");
            return;
        }

        GroupRecord group = DataStore.GetOrCreateGroup(args.Message.GroupId ?? "");
        group.ParentGroupOpenId = parent;
        DataStore.SaveServerChange();
        await CommandHelpers.ReplyAsync(args, $"# ✅ 已绑定父群 `{parent}`");
    }

    [Command("解绑父群", "解除父群绑定", MessageScene.Group)]
    [Command("解绑父群", "解除父群绑定", MessageScene.GroupAt)]
    public static async Task UnbindParentAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        GroupRecord group = DataStore.GetOrCreateGroup(args.Message.GroupId ?? "");
        group.ParentGroupOpenId = "";
        DataStore.SaveServerChange();
        await CommandHelpers.ReplyAsync(args, "# ✅ 已解除父群绑定");
    }

    // ── 群信息 ──────────────────────────────────────────────────────────────────

    [Command("获取群信息", "查看当前群的信息", MessageScene.Group)]
    [Command("获取群信息", "查看当前群的信息", MessageScene.GroupAt)]
    public static async Task GroupInfoAsync(CommandArgs args)
    {
        string groupOpenId = args.Message.GroupId ?? "";
        GroupRecord group = DataStore.GetOrCreateGroup(groupOpenId);

        StringBuilder builder = new();
        builder.Append("# 🍥 群信息\n");
        builder.Append($"- 群 OpenID：`{Mask(groupOpenId)}`\n");
        builder.Append($"- 你的 OpenID：`{Mask(args.Message.AuthorId)}`\n");
        builder.Append($"- 你的身份：{RoleName(args.Message.Role)}\n");
        builder.Append($"- 父群：{(string.IsNullOrEmpty(group.ParentGroupOpenId) ? "未绑定" : $"`{Mask(group.ParentGroupOpenId)}`")}\n");
        builder.Append($"- 本群白名单：{(group.EnableWhitelist ? "开启" : "关闭")}\n");
        builder.Append($"- 在线列表显示进度：{(group.ShowProcessInPlayerList ? "是" : "否")}\n");
        builder.Append($"- 允许远程指令：{(group.AllowRemoteCommand ? "是" : "否")}\n");
        builder.Append($"- 群管理员白名单：{group.Admins.Count} 人\n");
        builder.Append($"- 群黑名单：{group.Blacklist.Count} 人\n");
        builder.Append($"- 已绑定服务器：{DataStore.GetServers(groupOpenId).Count} 台");

        await CommandHelpers.ReplyAsync(args, builder.ToString(), GroupKeyboard);
    }

    // ── 群设置 ──────────────────────────────────────────────────────────────────

    [Command("设置", "修改本群机器人设置", MessageScene.Group, "群设置")]
    [Command("设置", "修改本群机器人设置", MessageScene.GroupAt, "群设置")]
    public static async Task SettingsAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        GroupRecord group = DataStore.GetOrCreateGroup(args.Message.GroupId ?? "");

        if (!args.Require(2))
        {
            StringBuilder builder = new();
            builder.Append("# 🍥 群设置\n");
            builder.Append("> 用法：`/设置 <项> <开|关>`\n\n");
            builder.Append($"- `whitelist` 本群白名单：**{(group.EnableWhitelist ? "开" : "关")}**\n");
            builder.Append($"- `progress` 在线列表附带进度：**{(group.ShowProcessInPlayerList ? "开" : "关")}**\n");
            builder.Append($"- `remote` 允许远程指令：**{(group.AllowRemoteCommand ? "开" : "关")}**\n");
            builder.Append($"- `kickgroup` 踢出提示附带群号：**{(group.ShowGroupNumberInKick ? "开" : "关")}**");
            await CommandHelpers.ReplyAsync(args, builder.ToString());
            return;
        }

        string key = args.GetOrDefault(0).Trim().ToLowerInvariant();
        string value = args.GetOrDefault(1).Trim().ToLowerInvariant();
        bool enabled = value is "开" or "on" or "true" or "1" or "yes";

        switch (key)
        {
            case "whitelist":
            case "白名单":
                group.EnableWhitelist = enabled;
                break;

            case "progress":
            case "进度":
                group.ShowProcessInPlayerList = enabled;
                break;

            case "remote":
            case "远程":
                group.AllowRemoteCommand = enabled;
                break;

            case "kickgroup":
            case "群号":
                group.ShowGroupNumberInKick = enabled;
                break;

            default:
                await CommandHelpers.ReplyAsync(args, $"# ⛔ 未知的设置项 `{key}`，可用：whitelist / progress / remote / kickgroup");
                return;
        }

        DataStore.SaveServerChange();
        await CommandHelpers.ReplyAsync(args, $"# ✅ 设置已更新\n- `{key}` → **{(enabled ? "开" : "关")}**");
    }

    // ── 黑名单 ──────────────────────────────────────────────────────────────────

    [Command("黑名单列表", "查看本群黑名单", MessageScene.Group)]
    [Command("黑名单列表", "查看本群黑名单", MessageScene.GroupAt)]
    public static async Task ListBlacklistAsync(CommandArgs args)
    {
        GroupRecord group = DataStore.GetOrCreateGroup(args.Message.GroupId ?? "");
        await CommandHelpers.ReplyAsync(args,
            "# 🍥 黑名单列表\n" +
            (group.Blacklist.Count == 0
                ? "> 本群黑名单为空。"
                : string.Join('\n', group.Blacklist.Select(item => $"- {item}"))));
    }

    [Command("添加黑名单", "封禁玩家", MessageScene.Group)]
    [Command("添加黑名单", "封禁玩家", MessageScene.GroupAt)]
    public static async Task AddBlacklistAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args, "# 🍥 添加黑名单\n> 用法：`/添加黑名单 <角色名>`");
            return;
        }

        string name = args.GetOrDefault(0).Trim();
        GroupRecord group = DataStore.GetOrCreateGroup(args.Message.GroupId ?? "");
        if (group.Blacklist.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
        {
            await CommandHelpers.ReplyAsync(args, "# ⚠️ 该角色已在黑名单中。");
            return;
        }

        group.Blacklist.Add(name);
        DataStore.SaveServerChange();
        await CommandHelpers.ReplyAsync(args, $"# ✅ 已封禁 **{name}**");
    }

    [Command("删除黑名单", "解封玩家", MessageScene.Group)]
    [Command("删除黑名单", "解封玩家", MessageScene.GroupAt)]
    public static async Task RemoveBlacklistAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args, "# 🍥 删除黑名单\n> 用法：`/删除黑名单 <角色名>`");
            return;
        }

        string name = args.GetOrDefault(0).Trim();
        GroupRecord group = DataStore.GetOrCreateGroup(args.Message.GroupId ?? "");
        int removed = group.Blacklist.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该角色不在黑名单中。");
            return;
        }

        DataStore.SaveServerChange();
        await CommandHelpers.ReplyAsync(args, $"# ✅ 已解封 **{name}**");
    }

    // ── 云黑名单（仅所有者）────────────────────────────────────────────────────
    //
    // 云黑分四个维度，因为单封角色名根本拦不住人 —— 换个名字就回来了。
    //   角色名：/全局封禁 <角色名>        最容易绕过
    //   IP    ：/全局封禁 ip <IP>         换台机器就绕过
    //   设备  ：/全局封禁 设备 <UUID>     换个号、不换客户端也照样拦得住
    //   QQ    ：/全局封禁 qq <OpenID>     封真人，最彻底（也最需要谨慎）

    [Command("全局黑名单", "查看机器人级黑名单", MessageScene.Group)]
    [Command("全局黑名单", "查看机器人级黑名单", MessageScene.GroupAt)]
    public static async Task ListGlobalBlacklistAsync(CommandArgs args)
    {
        if (!await Permissions.RequireOwnerAsync(args))
        {
            return;
        }

        GroupRecord global = DataStore.GetOrCreateGlobal();

        StringBuilder builder = new();
        builder.Append("# 🍥 云黑名单\n");
        AppendBlacklistSection(builder, "角色名", global.Blacklist);
        AppendBlacklistSection(builder, "IP", global.BlacklistIps);
        AppendBlacklistSection(builder, "设备", global.BlacklistUuids);
        AppendBlacklistSection(builder, "QQ", global.BlacklistOpenIds);

        int total = global.Blacklist.Count + global.BlacklistIps.Count +
                    global.BlacklistUuids.Count + global.BlacklistOpenIds.Count;
        if (total == 0)
        {
            builder.Append("> 为空。\n");
        }

        builder.Append(
            "\n> 封禁：`/全局封禁 <角色名>`、`/全局封禁 ip <IP>`、`/全局封禁 设备 <UUID>`、`/全局封禁 qq <OpenID>`\n" +
            "> 解封把「封禁」换成「解封」即可。");
        await CommandHelpers.ReplyAsync(args, builder.ToString());
    }

    private static void AppendBlacklistSection(StringBuilder builder, string title, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.Append($"\n**{title}**（{items.Count}）\n");
        builder.Append(string.Join('\n', items.Take(20).Select(item => $"- {item}")));
        if (items.Count > 20)
        {
            builder.Append($"\n- …另有 {items.Count - 20} 条");
        }

        builder.Append('\n');
    }

    [Command("全局封禁", "加入机器人级黑名单", MessageScene.Group)]
    [Command("全局封禁", "加入机器人级黑名单", MessageScene.GroupAt)]
    public static async Task AddGlobalBlacklistAsync(CommandArgs args)
    {
        if (!await Permissions.RequireOwnerAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 全局封禁\n" +
                "- `/全局封禁 <角色名>`\n" +
                "- `/全局封禁 ip <IP>`\n" +
                "- `/全局封禁 设备 <UUID>`\n" +
                "- `/全局封禁 qq <OpenID>`");
            return;
        }

        (string kind, string value, string label) = ParseBanTarget(args);
        if (string.IsNullOrEmpty(value))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 封禁内容不能为空。");
            return;
        }

        GroupRecord global = DataStore.GetOrCreateGlobal();
        List<string> target = ResolveBlacklist(global, kind);

        if (target.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
        {
            await CommandHelpers.ReplyAsync(args, $"# 🍥 {label}**{value}** 已在云黑名单中。");
            return;
        }

        target.Add(value);
        DataStore.SaveServerChange();
        await CommandHelpers.ReplyAsync(args, $"# ✅ 已全局封禁{label}**{value}**（所有群生效）");
    }

    [Command("全局解封", "移出机器人级黑名单", MessageScene.Group)]
    [Command("全局解封", "移出机器人级黑名单", MessageScene.GroupAt)]
    public static async Task RemoveGlobalBlacklistAsync(CommandArgs args)
    {
        if (!await Permissions.RequireOwnerAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args, "# 🍥 全局解封\n> 用法同 `/全局封禁`，例如 `/全局解封 ip 1.2.3.4`");
            return;
        }

        (string kind, string value, string label) = ParseBanTarget(args);
        GroupRecord global = DataStore.GetOrCreateGlobal();
        List<string> target = ResolveBlacklist(global, kind);

        int removed = target.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            DataStore.SaveServerChange();
        }

        await CommandHelpers.ReplyAsync(args,
            removed > 0 ? $"# ✅ 已全局解封{label}**{value}**" : $"# ⛔ {label}**{value}** 不在云黑名单中。");
    }

    private static List<string> ResolveBlacklist(GroupRecord global, string kind)
    {
        return kind switch
        {
            "ip" => global.BlacklistIps,
            "uuid" => global.BlacklistUuids,
            "openid" => global.BlacklistOpenIds,
            _ => global.Blacklist,
        };
    }

    /// <summary>
    /// 解析封禁目标。<c>/全局封禁 ip 1.2.3.4</c> 是「维度 + 值」两段写法，
    /// 只给一段时按角色名处理 —— 保持与旧指令完全兼容。
    /// </summary>
    private static (string Kind, string Value, string Label) ParseBanTarget(CommandArgs args)
    {
        string first = args.GetOrDefault(0).Trim();

        if (args.Parameters.Length > 1)
        {
            switch (first.ToLowerInvariant())
            {
                case "ip":
                    return ("ip", args.GetOrDefault(1).Trim(), " IP ");
                case "设备" or "uuid" or "device":
                    return ("uuid", args.GetOrDefault(1).Trim(), "设备 ");
                case "qq" or "openid":
                    return ("openid", args.GetOrDefault(1).Trim(), " QQ ");
            }
        }

        return ("name", first, "角色 ");
    }

    // ── 权限请求 ────────────────────────────────────────────────────────────────

    [Command("权限请求", "查询如何获得管理权限", MessageScene.Group)]
    [Command("权限请求", "查询如何获得管理权限", MessageScene.GroupAt)]
    public static async Task RequestPermissionAsync(CommandArgs args)
    {
        await CommandHelpers.ReplyAsync(args,
            "# 🍥 权限请求\n" +
            $"- 你的 OpenID：`{args.Message.AuthorId}`\n" +
            $"- 当前身份：{RoleName(args.Message.Role)}\n\n" +
            "> 需要管理权限时，把上面的 OpenID 发给群管理员，由管理员执行：\n" +
            $"> `/添加管理 {args.Message.AuthorId}`");
    }

    // ── 工具 ────────────────────────────────────────────────────────────────────

    private static string RoleName(GroupMemberRole role) => role switch
    {
        GroupMemberRole.Owner => "群主",
        GroupMemberRole.Admin => "管理员",
        _ => "普通成员",
    };

    /// <summary>OpenID 属于敏感信息，展示时只保留前后各 4 位。</summary>
    private static string Mask(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 10)
        {
            return value;
        }

        return $"{value[..4]}…{value[^4..]}";
    }
}
