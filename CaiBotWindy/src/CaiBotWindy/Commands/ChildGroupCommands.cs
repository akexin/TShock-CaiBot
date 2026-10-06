using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Services;
using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>
/// 父群对子群的远程操作。
///
/// <para><b>原理</b>：<see cref="MessageEventArgs"/> 是可构造的，而它的
/// <see cref="MessageEventArgs.GroupId"/> 又是<b>可写属性</b> ——
/// 于是可以造一个「上下文是子群、回复目标却是父群」的消息交给
/// <c>CommandRegistry.ExecuteAsync</c>：
/// 指令内部读到的 groupOpenId 是子群的（操作落在子群），
/// 输出却回到父群（管理员原地就能看到结果）。</para>
/// </summary>
public static class ChildGroupCommands
{
    // ── /子群执行 ──────────────────────────────────────────────────────────────

    [Command("子群执行", "以子群身份执行一条机器人指令（父群用）", MessageScene.Group, "zqzx", "runin")]
    [Command("子群执行", "以子群身份执行一条机器人指令（父群用）", MessageScene.GroupAt, "zqzx", "runin")]
    public static async Task RunInChildAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string parentOpenId = args.Message.GroupId ?? "";
        List<GroupRecord> children = DataStore.GetChildGroups(parentOpenId);

        if (children.Count == 0)
        {
            await CommandHelpers.ReplyAsync(args,
                "# 👥 子群执行\n> 本群名下没有子群，无法代执行。\n\n" +
                "> 让子群管理员在**子群**里执行：\n" +
                $"> `/绑定父群 {parentOpenId}`");
            return;
        }

        if (args.Count < 2)
        {
            StringBuilder usage = new();
            usage.Append("# 👥 子群执行\n");
            usage.Append("> 用法：`/子群执行 <子群序号> <指令>`\n");
            usage.Append("> 例：`/子群执行 1 /在线`、`/子群执行 2 /设置 白名单开关 开`\n\n");
            usage.Append("**可选子群**\n");
            for (int i = 0; i < children.Count; i++)
            {
                usage.Append($"- `{i + 1}`　`{children[i].GroupOpenId}`\n");
            }

            await CommandHelpers.ReplyAsync(args, usage.ToString());
            return;
        }

        if (!args.TryGetInt(0, out int index) || index < 1 || index > children.Count)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 序号需在 1 ~ {children.Count} 之间");
            return;
        }

        string childOpenId = children[index - 1].GroupOpenId;
        string commandLine = string.Join(' ', args.Parameters.Skip(1));
        if (!commandLine.StartsWith('/'))
        {
            commandLine = "/" + commandLine;
        }

        if (App.Adaptor is not { } adaptor || App.Commands is null)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 机器人尚未就绪，请稍后再试");
            return;
        }

        // ⚠️ msg_id 必须用**父群那条原始消息**的 id：QQ 的被动消息要凭它回帖，
        //    自己编一个会被平台判为「请求参数msg_id无效或越权」(40034024)。
        //    上下文 GroupId 仍是子群，所以操作落在子群。
        MessageEventArgs relayed = new(
            adaptor,
            MessageScene.Group,
            commandLine,
            messageId: args.Message.MessageId,
            authorId: args.Message.AuthorId,
            replyTarget: SendTarget.Group(parentOpenId),
            authorName: args.Message.AuthorName)
        {
            GroupId = childOpenId,
            Role = args.Message.Role,
        };

        // ExecuteAsync 在「指令不存在」和「指令执行中抛异常」两种情况下**都返回 false**，
        // 所以要先独立判断指令是否存在，否则执行失败会被误报成「未知指令」。
        if (!CommandExists(commandLine))
        {
            await CommandHelpers.ReplyAsync(args,
                $"# ⛔ 未知指令\n> `{commandLine}` 不是有效指令，可用 `/所有指令` 查看清单。");
            return;
        }

        try
        {
            bool handled = await App.Commands.ExecuteAsync(relayed, App.Hooks);
            if (!handled)
            {
                await CommandHelpers.ReplyAsync(args,
                    $"# ⚠️ 执行未完成\n> `{commandLine}` 已分派但没能收尾，多半是回复消息时被平台拒绝。\n" +
                    "> 可看机器人控制台日志确认。");
            }
        }
        catch (Exception ex)
        {
            Message.Yellow($"[子群执行] {childOpenId} 执行 {commandLine} 失败: {ex.Message}");
            await CommandHelpers.ReplyAsync(args, "# ⛔ 执行失败\n> " + ex.Message);
        }
    }

    /// <summary>指令名（含别名）是否已注册。</summary>
    private static bool CommandExists(string commandLine)
    {
        string name = commandLine.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (name.Length == 0 || App.Commands is null)
        {
            return false;
        }

        return App.Commands.All.Any(info =>
            string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase) ||
            info.Parameters.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)));
    }
}
