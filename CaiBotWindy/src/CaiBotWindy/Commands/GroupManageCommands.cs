using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Services;
using Newtonsoft.Json.Linq;
using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Adaptor.QQOfficial;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>
/// 群成员与入群申请管理。
///
/// <para>依赖 QQ 群管理接口（都需要机器人有群管理员身份）：
/// <c>GET join_request_list</c>、<c>POST approval_join_request/{id}</c>、
/// <c>GET/POST restrict_chat_setting</c>、<c>join_approval_strategy</c> 系列。</para>
/// </summary>
public static class GroupManageCommands
{
    /// <summary>取 QQ 官方适配器实例；非 QQ 平台时为 null。</summary>
    private static QQOfficialAdaptor? Qq => App.Adaptor as QQOfficialAdaptor;

    private static string? CurrentGroup(CommandArgs args)
    {
        string? id = args.Message.GroupId;
        return string.IsNullOrEmpty(id) ? null : id;
    }

    // ── 入群申请列表 ────────────────────────────────────────────────────────────

    [Command("申请列表", "拉取入群申请列表", MessageScene.Group, "sqlb", "joins", "入群申请")]
    [Command("申请列表", "拉取入群申请列表", MessageScene.GroupAt, "sqlb", "joins", "入群申请")]
    public static async Task JoinRequestListAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? group = CurrentGroup(args);
        if (group is null || Qq is not { } qq)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在 QQ 群内使用");
            return;
        }

        try
        {
            JObject result = await qq.GetGroupJoinRequestListAsync(group, cursor: null, limit: 20);
            JArray list = result["list"] as JArray ?? [];

            if (list.Count == 0)
            {
                await CommandHelpers.ReplyAsync(args,
                    "# 📋 入群申请\n> 当前没有待处理的申请。\n" +
                    "> 新申请到达时机器人会自动推送到群里。");
                return;
            }

            StringBuilder builder = new();
            builder.Append($"# 📋 入群申请（{list.Count} 条）\n");

            int index = 1;
            foreach (JToken item in list)
            {
                string memberOpenId = item.Value<string>("member_openid") ?? "";
                string username = item.Value<string>("username") ?? "(未知昵称)";
                string applySource = item.Value<string>("apply_source") ?? "";
                string verify = item.Value<string>("verify_content") ?? "";

                builder.Append($"\n**{index}. {username}**\n");
                builder.Append($"- OpenID：`{memberOpenId}`\n");
                if (!string.IsNullOrEmpty(applySource))
                {
                    builder.Append($"- 来源：{applySource}\n");
                }

                if (!string.IsNullOrEmpty(verify))
                {
                    builder.Append($"- 验证信息：{verify}\n");
                }

                index++;
            }

            builder.Append("\n> 审批：`/审批入群 <成员OpenID> 同意` 或 `/审批入群 <成员OpenID> 拒绝 [拉黑]`\n" +
                           "> 也可以直接点群内卡片上的按钮。");

            await CommandHelpers.ReplyAsync(args, builder.ToString());
        }
        catch (Exception ex)
        {
            Message.Yellow($"[群管理] 拉取入群申请失败: {ex.Message}");
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 拉取失败\n> " + ex.Message +
                "\n> 请确认机器人已获得**群管理员**身份。");
        }
    }

    // ── 入群申请审批 ────────────────────────────────────────────────────────────

    [Command("审批入群", "审批一条入群申请（同意 | 拒绝 [拉黑]）", MessageScene.Group, "spjr", "approve", "批准入群")]
    [Command("审批入群", "审批一条入群申请（同意 | 拒绝 [拉黑]）", MessageScene.GroupAt, "spjr", "approve", "批准入群")]
    public static async Task ApproveJoinRequestAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? group = CurrentGroup(args);
        if (group is null || Qq is not { } qq)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在 QQ 群内使用");
            return;
        }

        if (!args.Require(2))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 审批入群\n> 用法：`/审批入群 <成员OpenID> <同意|拒绝>`\n" +
                "> 拒绝时可加 `拉黑`：`/审批入群 <OpenID> 拒绝 拉黑`\n" +
                "> 不知道 OpenID？先用 `/申请列表` 拉取。");
            return;
        }

        string memberOpenId = args.GetOrDefault(0).Trim();
        string action = args.GetOrDefault(1).Trim().ToLowerInvariant();
        bool blacklist = args.Parameters.Skip(2).Any(p => p.Contains("拉黑") || p.Equals("ban", StringComparison.OrdinalIgnoreCase));

        // QQ 的审批接口必须带 join_request_id，否则报 40103007「无效或已过期的审批令牌」。
        // 按钮只回传固定文本，所以这里按 group + member 反查落库的申请记录。
        JoinRequestRecord? record = DataStore.FindJoinRequest(group, memberOpenId);
        string? joinRequestId = string.IsNullOrEmpty(record?.JoinRequestId) ? null : record!.JoinRequestId;

        try
        {
            if (action is "同意" or "批准" or "approve" or "ok")
            {
                await qq.ApproveGroupJoinRequestAsync(group, memberOpenId, joinRequestId);
                DataStore.RemoveJoinRequest(group, memberOpenId);
                await CommandHelpers.ReplyAsync(args, $"# ✅ 已批准 **{record?.UserName ?? memberOpenId}** 入群");
            }
            else if (action is "拒绝" or "decline" or "reject" or "no")
            {
                await qq.RejectGroupJoinRequestAsync(group, memberOpenId, null, blacklist, joinRequestId);
                DataStore.RemoveJoinRequest(group, memberOpenId);
                await CommandHelpers.ReplyAsync(args,
                    $"# 🚫 已拒绝 **{record?.UserName ?? memberOpenId}** 的入群申请" +
                    (blacklist ? "\n> 同时加入了群黑名单。" : ""));
            }
            else
            {
                await CommandHelpers.ReplyAsync(args, "# ⛔ 第二个参数只能是「同意 / 批准」或「拒绝」");
            }
        }
        catch (Exception ex)
        {
            Message.Yellow($"[群管理] 审批入群失败: {ex.Message}");
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 审批失败\n> " + ex.Message +
                (joinRequestId is null
                    ? "\n> 没有找到这条申请的令牌，先发 `/申请列表` 重新拉取一次再试。"
                    : ""));
        }
    }

    // ── 禁言 ────────────────────────────────────────────────────────────────────

    [Command("禁言状态", "查询群禁言状态与禁言成员", MessageScene.Group, "jyzt", "mutestatus", "禁言查询")]
    [Command("禁言状态", "查询群禁言状态与禁言成员", MessageScene.GroupAt, "jyzt", "mutestatus", "禁言查询")]
    public static async Task MuteStatusAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? group = CurrentGroup(args);
        if (group is null || Qq is not { } qq)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在 QQ 群内使用");
            return;
        }

        try
        {
            JObject result = await qq.GetGroupMuteSettingAsync(group);
            string mode = result["global_rule"]?.Value<string>("mode") ?? "none";
            string modeText = mode switch
            {
                "always" => "🔴 全员禁言中",
                "schedule" => "🟡 定时禁言",
                _ => "🟢 未开启全员禁言",
            };

            JArray members = result["members"] as JArray ?? [];
            StringBuilder builder = new();
            builder.Append("# 🔇 群禁言状态\n");
            builder.Append($"- 全员禁言：{modeText}\n");
            builder.Append($"- 禁言中的成员：**{members.Count}** 人\n");

            int index = 1;
            foreach (JToken item in members.Take(20))
            {
                string openId = item.Value<string>("member_openid") ?? "";
                string expire = item.Value<string>("mute_expire_at") ?? "";
                builder.Append($"\n{index}. `{openId}`\n   - 到期：{expire}\n");
                index++;
            }

            if (members.Count > 20)
            {
                builder.Append($"\n> 仅显示前 20 条，共 {members.Count} 条。");
            }

            builder.Append("\n\n> 设置禁言：`/禁言 <成员OpenID> <分钟>`（0 分钟 = 解除）");

            await CommandHelpers.ReplyAsync(args, builder.ToString());
        }
        catch (Exception ex)
        {
            Message.Yellow($"[群管理] 查询禁言状态失败: {ex.Message}");
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 查询失败\n> " + ex.Message + "\n> 请确认机器人已获得**群管理员**身份。");
        }
    }

    [Command("禁言", "设置群成员禁言（0 分钟解除）", MessageScene.Group, "jy", "mute", "禁言成员")]
    [Command("禁言", "设置群成员禁言（0 分钟解除）", MessageScene.GroupAt, "jy", "mute", "禁言成员")]
    public static async Task MuteAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? group = CurrentGroup(args);
        if (group is null || Qq is not { } qq)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在 QQ 群内使用");
            return;
        }

        if (!args.Require(2) || !args.TryGetInt(1, out int minutes) || minutes < 0)
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 群禁言\n> 用法：`/禁言 <成员OpenID> <分钟>`\n" +
                "> `0` 分钟表示解除禁言。\n" +
                $" > 例：{MenuKit.CmdInput("/禁言 ", "禁言 <成员OpenID> 10")}");
            return;
        }

        string memberOpenId = args.GetOrDefault(0).Trim();

        try
        {
            await qq.SetGroupMemberMuteAsync(group, memberOpenId, minutes * 60L);
            await CommandHelpers.ReplyAsync(args,
                minutes == 0
                    ? $"# 🔊 已解除 **{memberOpenId}** 的禁言"
                    : $"# 🔇 已禁言 **{memberOpenId}** {minutes} 分钟");
        }
        catch (Exception ex)
        {
            Message.Yellow($"[群管理] 设置禁言失败: {ex.Message}");
            await CommandHelpers.ReplyAsync(args, "# ⛔ 设置失败\n> " + ex.Message);
        }
    }

    // ── 入群自动审批策略 ────────────────────────────────────────────────────────

    [Command("审批策略", "入群自动审批策略（列表 | 开启 | 关闭）", MessageScene.Group, "spcl", "strategy", "自动审批")]
    [Command("审批策略", "入群自动审批策略（列表 | 开启 | 关闭）", MessageScene.GroupAt, "spcl", "strategy", "自动审批")]
    public static async Task StrategyAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? group = CurrentGroup(args);
        if (group is null || Qq is not { } qq)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在 QQ 群内使用");
            return;
        }

        string sub = args.GetOrDefault(0).Trim().ToLowerInvariant();

        try
        {
            JObject result = await qq.GetJoinApprovalStrategiesAsync();
            JArray strategies = result["strategies"] as JArray ?? result["list"] as JArray ?? [];

            switch (sub)
            {
                case "":
                case "列表":
                case "list":
                {
                    StringBuilder builder = new();
                    builder.Append("# 🍥 入群自动审批策略\n");
                    builder.Append($"- 当前共 **{strategies.Count}** 条\n\n");

                    foreach (JToken item in strategies)
                    {
                        builder.Append($"- `{item.Value<string>("strategy_id")}`\n" +
                                       $"  状态：{item.Value<string>("status")}　" +
                                       $"模式：{item.Value<string>("mode")}\n");
                    }

                    builder.Append("\n> 说明：**自动审批默认关闭** —— 所有入群申请都会推送到群里等人工确认。\n" +
                                   "> 只有显式开启后，符合策略的申请才会被机器人自动放行。\n" +
                                   "> `/审批策略 开启 <strategy_id>`　`/审批策略 关闭 <strategy_id>`");
                    await CommandHelpers.ReplyAsync(args, builder.ToString());
                    break;
                }

                case "开启":
                case "on":
                {
                    if (strategies.Count == 0)
                    {
                        await CommandHelpers.ReplyAsync(args,
                            "# ⚠️ 还没有可用的策略\n" +
                            "> 自动审批策略需要在 QQ 开放平台创建（或由机器人创建）后，这里才能开关。\n" +
                            "> 当前保持**默认关闭**：申请仍然推群里人工审批。");
                        return;
                    }

                    string id = args.GetOrDefault(1).Trim();
                    JToken target = string.IsNullOrEmpty(id)
                        ? strategies[0]
                        : strategies.FirstOrDefault(s => s.Value<string>("strategy_id") == id) ?? strategies[0];

                    string strategyId = target.Value<string>("strategy_id") ?? "";
                    await qq.ExecuteJoinApprovalStrategyAsync(strategyId);
                    await CommandHelpers.ReplyAsync(args, $"# ✅ 已执行审批策略 `{strategyId}`");
                    break;
                }

                case "关闭":
                case "off":
                {
                    string id = args.GetOrDefault(1).Trim();
                    JToken? target = string.IsNullOrEmpty(id)
                        ? strategies.FirstOrDefault()
                        : strategies.FirstOrDefault(s => s.Value<string>("strategy_id") == id);

                    if (target is null)
                    {
                        await CommandHelpers.ReplyAsync(args, "# ⛔ 没有找到对应的策略");
                        return;
                    }

                    string strategyId = target.Value<string>("strategy_id") ?? "";
                    await qq.DeleteJoinApprovalStrategyAsync(strategyId);
                    await CommandHelpers.ReplyAsync(args,
                        $"# 🚫 已关闭（删除）审批策略 `{strategyId}`\n> 入群申请恢复为人工确认。");
                    break;
                }

                default:
                    await CommandHelpers.ReplyAsync(args,
                        "# 🍥 审批策略\n> 用法：`/审批策略 <列表|开启|关闭> [strategy_id]`");
                    break;
            }
        }
        catch (Exception ex)
        {
            Message.Yellow($"[群管理] 审批策略操作失败: {ex.Message}");
            await CommandHelpers.ReplyAsync(args, "# ⛔ 操作失败\n> " + ex.Message);
        }
    }

    // ── 入群审核模式（机器人侧开关，默认人工）──────────────────────────────────

    [Command("入群审核", "入群申请审核方式（自动 | 人工 | 关闭）", MessageScene.Group, "rqsh", "joinreview", "审核方式")]
    [Command("入群审核", "入群申请审核方式（自动 | 人工 | 关闭）", MessageScene.GroupAt, "rqsh", "joinreview", "审核方式")]
    public static async Task JoinReviewAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string sub = args.GetOrDefault(0).Trim().ToLowerInvariant();

        if (sub.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 入群审核\n" +
                $"- 当前方式：**{DescribeReviewMode(App.Config.GroupJoinReview)}**\n\n" +
                "> **人工**（默认）　申请推群里，由管理员点「同意 / 拒绝」\n" +
                "> **自动**　命中云黑自动拒绝并拉黑，其余自动通过\n" +
                "> **关闭**　不处理、也不通知\n\n" +
                "点下面按钮直接切换：",
                MenuKit.Keyboard(
                    ("切换为人工", "/入群审核 人工"),
                    ("切换为自动", "/入群审核 自动"),
                    ("关闭审核", "/入群审核 关闭")));
            return;
        }

        string? mode = sub switch
        {
            "人工" or "manual" => "manual",
            "自动" or "auto" => "auto",
            "关闭" or "off" => "off",
            _ => null,
        };

        if (mode is null)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 参数只能是「人工」「自动」或「关闭」");
            return;
        }

        App.Config.GroupJoinReview = mode;
        App.SaveConfig();

        await CommandHelpers.ReplyAsync(args,
            $"# ✅ 入群审核已切换为 **{DescribeReviewMode(mode)}**\n" +
            (mode == "auto"
                ? "> ⚠️ 自动放行有风险，请先确认云黑名单已维护好。"
                : "> 改动已写入配置文件，重启也不会丢。"));
    }

    private static string DescribeReviewMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "auto" => "自动审批",
            "off" => "已关闭",
            _ => "人工审批",
        };
    }
}
