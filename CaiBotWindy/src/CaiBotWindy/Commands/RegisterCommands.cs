using CaiBotWindy.Data;
using CaiBotWindy.Services;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>
/// 邮箱注册。替代原先「报个角色名就算绑定」的白名单方式 ——
/// 那种做法没有任何验证，谁都能替别人绑一个名字。
/// </summary>
public static class RegisterCommands
{
    private static readonly ButtonKeyboard RegisterKeyboard = MenuKit.Keyboard(
        ("我的注册", "/我的注册"),
        ("签到", "/签到"),
        ("查询金币", "/查询金币"),
        ("帮助", "/帮助"));

    // ── 注册 ────────────────────────────────────────────────────────────────────

    [Command("注册", "用 QQ 邮箱注册角色", MessageScene.Group)]
    [Command("注册", "用 QQ 邮箱注册角色", MessageScene.GroupAt)]
    [Command("注册", "用 QQ 邮箱注册角色", MessageScene.Private)]
    public static async Task RegisterAsync(CommandArgs args)
    {
        if (!args.Require(2))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 邮箱注册\n" +
                "> 用法：" + MenuKit.CmdInput("/注册 ", "注册 <QQ邮箱> <角色名>") + "\n" +
                "> 例：`/注册 123456789@qq.com 星`\n\n" +
                "> 机器人会往该邮箱发一封验证码，收到后发送 " + MenuKit.CmdInput("/注册验证 ", "注册验证 <验证码>") + " 完成注册。",
                RegisterKeyboard);
            return;
        }

        string email = args.GetOrDefault(0).Trim();
        string playerName = args.GetOrDefault(1).Trim();

        // 发信要走 SMTP，通常要一两秒，先给个回应免得玩家以为没动静。
        await CommandHelpers.ReplyAsync(args, "# 📧 正在发送验证码，请稍候…");

        (bool success, string message) = await RegisterService.StartAsync(args.Message.AuthorId, email, playerName);
        await CommandHelpers.ReplyAsync(args, success ? message : $"# ⛔ 注册失败\n> {message}");
    }

    [Command("注册验证", "提交邮箱验证码完成注册", MessageScene.Group)]
    [Command("注册验证", "提交邮箱验证码完成注册", MessageScene.GroupAt)]
    [Command("注册验证", "提交邮箱验证码完成注册", MessageScene.Private)]
    public static async Task VerifyAsync(CommandArgs args)
    {
        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 注册验证\n> 用法：" + MenuKit.CmdInput("/注册验证 ", "注册验证 <验证码>"));
            return;
        }

        (bool success, string message) = RegisterService.Verify(args.Message.AuthorId, args.GetOrDefault(0));
        await CommandHelpers.ReplyAsync(args,
            success ? message : $"# ⛔ 验证失败\n> {message}",
            success ? RegisterKeyboard : null);
    }

    [Command("我的注册", "查看注册状态", MessageScene.Group)]
    [Command("我的注册", "查看注册状态", MessageScene.GroupAt)]
    [Command("我的注册", "查看注册状态", MessageScene.Private)]
    public static Task MyRegistrationAsync(CommandArgs args)
    {
        return CommandHelpers.ReplyAsync(args, RegisterService.Describe(args.Message.AuthorId), RegisterKeyboard);
    }

    // ── 管理员：注册上限 ────────────────────────────────────────────────────────

    [Command("注册限制", "查看或设置每个 IP / 设备的注册上限", MessageScene.Group)]
    [Command("注册限制", "查看或设置每个 IP / 设备的注册上限", MessageScene.GroupAt)]
    public static async Task RegisterLimitAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        int current = DataStore.Data.RegisterLimitPerIp;

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 注册限制\n" +
                $"- 当前上限：**{current}** 个账号 / IP（设备同）\n" +
                $"> 修改：{MenuKit.CmdInput("/注册限制 ", "注册限制 <数量>")}（1–10 的整数）");
            return;
        }

        if (!args.TryGetInt(0, out int limit) || limit is < 1 or > 10)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 数量无效\n> 请输入 1–10 之间的整数。");
            return;
        }

        DataStore.Data.RegisterLimitPerIp = limit;
        DataStore.SaveServerChange();

        await CommandHelpers.ReplyAsync(args,
            $"# ✅ 注册上限已改为 **{limit}**\n" +
            "> 同一 IP / 同一设备名下超过该数量的账号，首次进服时会转人工确认。改动即时生效。");
    }
}
