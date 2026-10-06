using CaiBotWindy.Data;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Services;

/// <summary>指令权限判定。</summary>
public static class Permissions
{
    /// <summary>
    /// 是否具备管理权限。满足任一条件即可：
    /// 配置里的机器人所有者 / 全局管理员、QQ 群主或群管理员、本群管理员白名单。
    /// </summary>
    public static bool IsAdmin(CommandArgs args)
    {
        string openId = args.Message.AuthorId;
        if (string.IsNullOrEmpty(openId))
        {
            return false;
        }

        PluginConfig config = App.Config;
        if (config.OwnerOpenIds.Contains(openId) || config.Operators.Contains(openId))
        {
            return true;
        }

        if (args.Message.Role is GroupMemberRole.Admin or GroupMemberRole.Owner)
        {
            return true;
        }

        string? groupOpenId = args.Message.GroupId;
        if (!string.IsNullOrEmpty(groupOpenId))
        {
            GroupRecord? group = DataStore.FindGroup(groupOpenId);
            if (group is not null)
            {
                if (group.Admins.Contains(openId))
                {
                    return true;
                }

                // 父群的管理员在子群里也管事 —— 父群要能管理它名下的子群。
                GroupRecord? parent = DataStore.FindParent(group);
                if (parent is not null && parent.Admins.Contains(openId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>是否为本机器人所有者。</summary>
    public static bool IsOwner(CommandArgs args)
    {
        string openId = args.Message.AuthorId;
        return !string.IsNullOrEmpty(openId) && App.Config.OwnerOpenIds.Contains(openId);
    }

    /// <summary>要求管理权限；不足时直接回消息并返回 false。</summary>
    public static async Task<bool> RequireAdminAsync(CommandArgs args)
    {
        if (IsAdmin(args))
        {
            return true;
        }

        await args.Adaptor.SendMessage(new Windy.SDK.Adaptor.MessageContent().AddMarkdown(
            "# ⛔ 权限不足\n> 该指令需要 QQ 群主 / 群管理员 / 机器人管理员身份。"));
        return false;
    }

    /// <summary>要求所有者权限。</summary>
    public static async Task<bool> RequireOwnerAsync(CommandArgs args)
    {
        if (IsOwner(args))
        {
            return true;
        }

        await args.Adaptor.SendMessage(new Windy.SDK.Adaptor.MessageContent().AddMarkdown(
            "# ⛔ 权限不足\n> 该指令仅机器人所有者可用（见 Config/CaiBotWindy.json 的 OwnerOpenIds）。"));
        return false;
    }

    /// <summary>仅在群聊场景下可用；私聊时回提示。</summary>
    public static async Task<bool> RequireGroupAsync(CommandArgs args)
    {
        if (!string.IsNullOrEmpty(args.Message.GroupId))
        {
            return true;
        }

        await args.Adaptor.SendMessage(new Windy.SDK.Adaptor.MessageContent().AddMarkdown(
            "# ⛔ 该指令只能在群聊中使用"));
        return false;
    }
}
