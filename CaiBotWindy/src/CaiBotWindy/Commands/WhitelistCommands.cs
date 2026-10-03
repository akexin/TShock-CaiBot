using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Services;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>白名单绑定与设备授权（对应 CaiBotLite 的白名单菜单 + 登录码流程）。</summary>
public static class WhitelistCommands
{
    private static readonly ButtonKeyboard WhitelistKeyboard = MenuKit.Keyboard(
        ("添加白名单", "/添加白名单 "),
        ("我的白名单", "/我的白名单"),
        ("签到", "/签到"),
        ("查询金币", "/查询金币"),
        ("帮助", "/帮助"));

    // ── 绑定 ────────────────────────────────────────────────────────────────────

    [Command("添加白名单", "绑定游戏角色名", MessageScene.Group, "绑定")]
    [Command("添加白名单", "绑定游戏角色名", MessageScene.GroupAt, "绑定")]
    [Command("添加白名单", "绑定游戏角色名", MessageScene.Private, "绑定")]
    public static async Task BindAsync(CommandArgs args)
    {
        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args, "# 🍥 添加白名单\n> 用法：`/添加白名单 <角色名字>`");
            return;
        }

        string playerName = args.GetOrDefault(0).Trim();
        if (playerName.Length is < 1 or > 20)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 角色名长度不合法。");
            return;
        }

        UserRecord? occupied = DataStore.FindUserByPlayerName(playerName);
        if (occupied is not null && occupied.OpenId != args.Message.AuthorId)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# ⛔ 角色 **{playerName}** 已被其他 QQ 用户绑定。\n> 如需申诉请联系群管理员。");
            return;
        }

        UserRecord? existing = DataStore.FindUser(args.Message.AuthorId);
        if (existing is not null)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# ⚠️ 你已经绑定了 **{existing.PlayerName}**\n" +
                $"> 如需换绑，请使用 `/修改白名单 {playerName}`。");
            return;
        }

        DataStore.UpsertUser(new UserRecord
        {
            OpenId = args.Message.AuthorId,
            PlayerName = playerName,
            BoundAtUtc = DateTime.UtcNow,
        });

        await CommandHelpers.ReplyAsync(args,
            $"# ✅ 白名单已添加\n- 角色：**{playerName}**\n- 绑定 QQ：`{args.Message.AuthorId}`\n\n" +
            "> 现在可以直接进服了喵~",
            WhitelistKeyboard);
    }

    [Command("修改白名单", "重新绑定游戏角色名", MessageScene.Group, "重新绑定")]
    [Command("修改白名单", "重新绑定游戏角色名", MessageScene.GroupAt, "重新绑定")]
    [Command("修改白名单", "重新绑定游戏角色名", MessageScene.Private, "重新绑定")]
    public static async Task RebindAsync(CommandArgs args)
    {
        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args, "# 🍥 修改白名单\n> 用法：`/修改白名单 <新的角色名字>`");
            return;
        }

        UserRecord? existing = DataStore.FindUser(args.Message.AuthorId);
        if (existing is null)
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 你还没有添加白名单\n> 请先使用 `/添加白名单 <角色名>`。");
            return;
        }

        string playerName = args.GetOrDefault(0).Trim();
        UserRecord? occupied = DataStore.FindUserByPlayerName(playerName);
        if (occupied is not null && occupied.OpenId != args.Message.AuthorId)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 角色 **{playerName}** 已被其他 QQ 用户绑定。");
            return;
        }

        string oldName = existing.PlayerName;
        existing.PlayerName = playerName;
        // 换绑后设备需要重新授权，避免旧设备直接复用。
        existing.DeviceId = "";
        DataStore.UpsertUser(existing);

        await CommandHelpers.ReplyAsync(args,
            $"# ✅ 绑定已更新\n- 原角色：{oldName}\n- 新角色：**{playerName}**\n\n" +
            "> 由于更换了角色，该设备需要重新登录授权。");
    }

    [Command("删除白名单", "解除自己的白名单绑定", MessageScene.Group)]
    [Command("删除白名单", "解除自己的白名单绑定", MessageScene.GroupAt)]
    [Command("删除白名单", "解除自己的白名单绑定", MessageScene.Private)]
    public static async Task UnbindAsync(CommandArgs args)
    {
        UserRecord? existing = DataStore.FindUser(args.Message.AuthorId);
        if (existing is null)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 你还没有添加白名单。");
            return;
        }

        DataStore.RemoveUser(args.Message.AuthorId);
        await CommandHelpers.ReplyAsync(args, $"# ✅ 已解除绑定（{existing.PlayerName}）");
    }

    [Command("我的白名单", "查看自己的绑定信息", MessageScene.Group)]
    [Command("我的白名单", "查看自己的绑定信息", MessageScene.GroupAt)]
    [Command("我的白名单", "查看自己的绑定信息", MessageScene.Private)]
    public static async Task MyWhitelistAsync(CommandArgs args)
    {
        UserRecord? existing = DataStore.FindUser(args.Message.AuthorId);
        if (existing is null)
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 我的白名单\n> 你还没有绑定角色。\n> 使用 `/添加白名单 <角色名>` 绑定。",
                WhitelistKeyboard);
            return;
        }

        await CommandHelpers.ReplyAsync(args,
            "# 🍥 我的白名单\n" +
            $"- 角色：**{existing.PlayerName}**\n" +
            $"- 绑定时间：{existing.BoundAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
            $"- 设备授权：{(string.IsNullOrEmpty(existing.DeviceId) ? "待首次进服自动授权" : "已授权")}\n" +
            $"- 累计签到：{existing.TotalSignInDays} 天（连续 {existing.SignInStreak} 天）\n" +
            $"- 金币：{existing.Coins}",
            WhitelistKeyboard);
    }

    // ── 设备登录 ────────────────────────────────────────────────────────────────

    [Command("登录", "批准新设备登录", MessageScene.Group)]
    [Command("登录", "批准新设备登录", MessageScene.GroupAt)]
    [Command("登录", "批准新设备登录", MessageScene.Private)]
    public static async Task LoginAsync(CommandArgs args)
    {
        if (!args.Require(1))
        {
            List<LoginAttempt> pending = WhitelistService.PendingAttempts(args.Message.AuthorId);
            if (pending.Count == 0)
            {
                await CommandHelpers.ReplyAsync(args,
                    "# 🍥 设备登录\n> 你当前没有待批准的设备。\n" +
                    "> 如果进服时被提示「未授权设备」，请直接再次发送 `/登录`。");
                return;
            }

            StringBuilder builder = new();
            builder.Append("# 🍥 待批准的设备\n");
            foreach (LoginAttempt attempt in pending)
            {
                builder.Append($"\n- 角色：**{attempt.PlayerName}**\n");
                builder.Append($"  - IP：{attempt.Ip}\n");
                builder.Append($"  - 触发原因：{WhitelistService.DescribeReason(attempt.Reason)}\n");
                builder.Append($"  - 申请时间：{attempt.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n");
                builder.Append($"  - 验证码：`{attempt.Code}`\n");
            }

            builder.Append("\n> 确认是你本人后，发送 `/登录 <验证码>` 完成批准。");
            await CommandHelpers.ReplyAsync(args, builder.ToString());
            return;
        }

        string code = args.GetOrDefault(0).Trim();
        (bool success, string message) = WhitelistService.Approve(code);
        await CommandHelpers.ReplyAsync(args,
            success ? $"# ✅ 登录成功\n{message}" : $"# ⛔ 登录失败\n> {message}");
    }

    // ── 登录确认 / 拒绝（群里那张确认卡片的两个按钮就是发这两条指令）──────────────

    [Command("确认登录", "批准一条登录申请", MessageScene.Group)]
    [Command("确认登录", "批准一条登录申请", MessageScene.GroupAt)]
    public static async Task ConfirmLoginAsync(CommandArgs args)
    {
        await ReviewLoginAsync(args, approve: true);
    }

    [Command("拒绝登录", "拒绝一条登录申请", MessageScene.Group)]
    [Command("拒绝登录", "拒绝一条登录申请", MessageScene.GroupAt)]
    public static async Task RejectLoginAsync(CommandArgs args)
    {
        await ReviewLoginAsync(args, approve: false);
    }

    /// <summary>
    /// 处理一条登录申请。具备权限的只有两类人：群管理员（/OwnerOpenIds 里配的也算）、
    /// 以及该角色绑定的本人 —— 别人不能替别人批准登录。
    /// </summary>
    private static async Task ReviewLoginAsync(CommandArgs args, bool approve)
    {
        string verb = approve ? "确认登录" : "拒绝登录";

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args, $"# 🍥 {verb}\n> 用法：`/{verb} <验证码>`");
            return;
        }

        string code = args.GetOrDefault(0).Trim();
        LoginAttempt? attempt = DataStore.FindLoginAttempt(code);
        if (attempt is null)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 没有找到该验证码\n> 可能已被处理或已过期，让对方重新进服即可。");
            return;
        }

        bool isAdmin = Permissions.IsAdmin(args);
        bool isSelf = !string.IsNullOrEmpty(attempt.OpenId) && attempt.OpenId == args.Message.AuthorId;

        if (!isAdmin && !isSelf)
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 权限不足\n" +
                $"> 这条申请属于角色 **{attempt.PlayerName}**。\n" +
                "> 只有群管理员或该角色绑定的本人可以处理。");
            return;
        }

        if (approve)
        {
            (bool ok, string message) = WhitelistService.Approve(code);
            await CommandHelpers.ReplyAsync(args, ok ? $"# ✅ 已批准登录\n> {message}" : $"# ⛔ 操作失败\n> {message}");
            return;
        }

        // 拒绝时默认只作废本次申请；管理员额外加「拉黑」参数才会写进云黑名单。
        // 默认不拉黑是有意的 —— 玩家换手机误触「拒绝」不该直接变成封号。
        bool blacklist = isAdmin &&
                         args.Parameters.Length > 1 &&
                         args.GetOrDefault(1).Trim() is "拉黑" or "黑名单" or "ban";

        (bool rejected, string rejectedMessage) = WhitelistService.Reject(code, blacklist);
        await CommandHelpers.ReplyAsync(args,
            rejected ? $"# ✅ 已拒绝登录\n> {rejectedMessage}" : $"# ⛔ 操作失败\n> {rejectedMessage}");
    }

    // ── 签到 / 金币 ─────────────────────────────────────────────────────────────

    [Command("签到", "每日签到领取金币", MessageScene.Group)]
    [Command("签到", "每日签到领取金币", MessageScene.GroupAt)]
    [Command("签到", "每日签到领取金币", MessageScene.Private)]
    public static async Task SignInAsync(CommandArgs args)
    {
        UserRecord? user = DataStore.FindUser(args.Message.AuthorId);
        if (user is null)
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 请先使用 `/添加白名单 <角色名>` 绑定角色后再签到。");
            return;
        }

        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (user.LastSignDate == today)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# 🍥 今天已经签到过了\n- 连续签到：{user.SignInStreak} 天\n- 金币：{user.Coins}");
            return;
        }

        string yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
        user.SignInStreak = user.LastSignDate == yesterday ? user.SignInStreak + 1 : 1;
        user.LastSignDate = today;
        user.TotalSignInDays++;

        int reward = 10 + Math.Min(50, user.SignInStreak * 2);
        user.Coins += reward;
        DataStore.UpsertUser(user);

        await CommandHelpers.ReplyAsync(args,
            "# ✅ 签到成功\n" +
            $"- 本次获得：**{reward}** 金币\n" +
            $"- 连续签到：{user.SignInStreak} 天\n" +
            $"- 金币余额：{user.Coins}");
    }

    [Command("查询金币", "查看金币余额", MessageScene.Group)]
    [Command("查询金币", "查看金币余额", MessageScene.GroupAt)]
    [Command("查询金币", "查看金币余额", MessageScene.Private)]
    public static async Task CoinsAsync(CommandArgs args)
    {
        UserRecord? user = DataStore.FindUser(args.Message.AuthorId);
        if (user is null)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 请先绑定角色。");
            return;
        }

        await CommandHelpers.ReplyAsync(args,
            $"# 🍥 金币\n- 角色：**{user.PlayerName}**\n- 余额：**{user.Coins}** 金币");
    }

    // ── 管理员查询 ──────────────────────────────────────────────────────────────

    [Command("查询玩家", "按名字查询白名单记录", MessageScene.Group)]
    [Command("查询玩家", "按名字查询白名单记录", MessageScene.GroupAt)]
    [Command("查询玩家", "按名字查询白名单记录", MessageScene.Private)]
    public static async Task FindPlayerAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args, "# 🍥 查询玩家\n> 用法：`/查询玩家 <角色名>`");
            return;
        }

        string playerName = args.GetOrDefault(0);
        UserRecord? user = DataStore.FindUserByPlayerName(playerName);
        if (user is null)
        {
            await CommandHelpers.ReplyAsync(args, $"# 🍥 查询玩家\n> 没有找到角色 **{playerName}** 的绑定记录。");
            return;
        }

        await CommandHelpers.ReplyAsync(args,
            $"# 🍥 查询玩家 · {user.PlayerName}\n" +
            $"- QQ OpenID：`{user.OpenId}`\n" +
            $"- 绑定时间：{user.BoundAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
            $"- 设备授权：{(string.IsNullOrEmpty(user.DeviceId) ? "未授权" : $"已授权（{user.DeviceId}）")}\n" +
            $"- 累计签到：{user.TotalSignInDays} 天\n" +
            $"- 金币：{user.Coins}");
    }
}
