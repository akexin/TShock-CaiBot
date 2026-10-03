using System.Text.RegularExpressions;
using CaiBotWindy.Data;

namespace CaiBotWindy.Services;

/// <summary>
/// 邮箱注册。
///
/// <para><b>为什么是邮箱</b>：QQ 官方机器人只给 OpenID —— 拿不到真实 QQ 号，也没法主动联系到人。
/// 邮箱是玩家能自己提供、且<b>可以验证真实性</b>的唯一身份锚点，因此用作注册凭据。</para>
///
/// <para><b>流程</b>：<c>/注册 &lt;邮箱&gt; &lt;角色名&gt;</c> 收发验证码 →
/// <c>/注册验证 &lt;验证码&gt;</c> 完成注册。验证码 10 分钟有效。</para>
///
/// <para>注册只解决「这个人是谁」；「这台机器能不能进」由 <see cref="WhitelistService"/> 的
/// IP / 设备校验负责，两者分工不同。</para>
/// </summary>
public static class RegisterService
{
    private const int CodeLifetimeMinutes = 10;

    /// <summary>只接受 QQ 邮箱本体与它的 Foxmail 别名。</summary>
    private static readonly Regex QqMailbox = new(
        @"^[A-Za-z0-9._%+-]{1,64}@(qq\.com|foxmail\.com)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsQqMailbox(string? email)
    {
        return !string.IsNullOrWhiteSpace(email) && QqMailbox.IsMatch(email.Trim());
    }

    /// <summary>邮件服务是否可用。不可用时注册会被拒绝 —— 不能假装验证过了。</summary>
    public static bool MailReady => App.Config.Smtp.Ready;

    /// <summary>发起注册：校验邮箱与角色名后发送验证码。</summary>
    public static async Task<(bool Success, string Message)> StartAsync(
        string openId, string email, string playerName)
    {
        email = email.Trim();
        playerName = playerName.Trim();

        if (!IsQqMailbox(email))
        {
            return (false, "邮箱格式不对。只接受 **QQ 邮箱**，例如 `123456789@qq.com` 或 `xxx@foxmail.com`。");
        }

        if (playerName.Length is < 1 or > 20)
        {
            return (false, "角色名长度不合法（1–20 个字符）。");
        }

        if (!MailReady)
        {
            return (false,
                "邮件服务未配置，注册暂不可用。\n" +
                "> 请联系管理员在 `Config/CaiBotWindy.json` 的 `Smtp` 节填入 QQ 邮箱与 SMTP 授权码。");
        }

        // 角色名占用：同一角色不能绑到两个 QQ 上。
        UserRecord? occupied = DataStore.FindUserByPlayerName(playerName);
        if (occupied is not null && occupied.OpenId != openId)
        {
            return (false, $"角色 **{playerName}** 已被其他 QQ 用户注册。\n> 如有异议请联系管理员。");
        }

        // 邮箱占用：一个邮箱只能注册一个角色，否则验证码发到同一个人手里可以无限开号。
        UserRecord? mailOwner = DataStore.FindUserByEmail(email);
        if (mailOwner is not null && mailOwner.OpenId != openId)
        {
            return (false, $"邮箱 `{Mask(email)}` 已经注册过角色 **{mailOwner.PlayerName}**。\n> 一个邮箱只能注册一个角色。");
        }

        UserRecord? existing = DataStore.FindUser(openId);
        if (existing is not null && existing.EmailVerified)
        {
            return (false,
                $"你已经注册过了（角色 **{existing.PlayerName}**，邮箱 `{Mask(existing.Email)}`）。\n" +
                $"> 需要换绑角色请联系管理员处理。");
        }

        string code = GenerateCode();

        // 同一个 QQ 重复发起注册：复用记录，只换验证码与角色名。
        UserRecord record = existing ?? new UserRecord { OpenId = openId };
        record.PlayerName = playerName;
        record.Email = email;
        record.EmailVerified = false;
        record.PendingCode = code;
        record.PendingCodeExpiresAtUtc = DateTime.UtcNow.AddMinutes(CodeLifetimeMinutes);
        record.RegisterSource = "email";
        record.BoundAtUtc = DateTime.UtcNow;
        DataStore.UpsertUser(record);

        (bool sent, string error) = await MailService.SendCodeAsync(email, code, playerName);
        if (!sent)
        {
            return (false,
                $"验证码邮件发送失败：{error}\n" +
                "> 请确认邮箱填写正确，或稍后再试。");
        }

        return (true,
            $"# 📧 验证码已发送\n" +
            $"- 邮箱：`{Mask(email)}`\n" +
            $"- 角色：**{playerName}**\n" +
            $"- 有效期：{CodeLifetimeMinutes} 分钟\n\n" +
            $"> 查收邮件后发送 {MenuKit.CmdInput("/注册验证 ", "注册验证 <验证码>")} 完成注册。");
    }

    /// <summary>校验验证码，完成注册。</summary>
    public static (bool Success, string Message) Verify(string openId, string code)
    {
        UserRecord? user = DataStore.FindUser(openId);
        if (user is null)
        {
            return (false, "你还没有发起注册。\n> 请先发送 " + MenuKit.CmdInput("/注册 ", "注册 <QQ邮箱> <角色名>") + "。");
        }

        if (user.EmailVerified)
        {
            return (false, $"你已经完成注册了（角色 **{user.PlayerName}**）。");
        }

        if (string.IsNullOrEmpty(user.PendingCode))
        {
            return (false, "没有待验证的注册。\n> 请重新发送 " + MenuKit.CmdInput("/注册 ", "注册 <QQ邮箱> <角色名>") + "。");
        }

        if (user.PendingCodeExpiresAtUtc is { } expires && DateTime.UtcNow > expires)
        {
            user.PendingCode = "";
            user.PendingCodeExpiresAtUtc = null;
            DataStore.UpsertUser(user);
            return (false, "验证码已过期。\n> 请重新发送 " + MenuKit.CmdInput("/注册 ", "注册 <QQ邮箱> <角色名>") + " 获取新的验证码。");
        }

        if (!string.Equals(user.PendingCode, code.Trim(), StringComparison.Ordinal))
        {
            return (false, "验证码不正确，请检查邮件后重试。");
        }

        user.EmailVerified = true;
        user.PendingCode = "";
        user.PendingCodeExpiresAtUtc = null;
        DataStore.UpsertUser(user);

        return (true,
            $"# ✅ 注册完成\n" +
            $"- 角色：**{user.PlayerName}**\n" +
            $"- 邮箱：`{Mask(user.Email)}`\n\n" +
            "> 现在可以进服了。首次进服会把你的 IP 与设备记为注册基准，之后换设备或换网络都需要管理员确认。");
    }

    /// <summary>注册状态摘要，供 <c>/我的注册</c> 展示。</summary>
    public static string Describe(string openId)
    {
        UserRecord? user = DataStore.FindUser(openId);
        if (user is null)
        {
            return "# 🍥 我的注册\n> 你还没有注册。\n> 发送 " + MenuKit.CmdInput("/注册 ", "注册 <QQ邮箱> <角色名>") + " 开始注册。";
        }

        string state = user.EmailVerified ? "✅ 已完成" :
            string.IsNullOrEmpty(user.Email) ? "⚠️ 未注册邮箱（管理员添加）" : "⏳ 待验证";

        System.Text.StringBuilder builder = new();
        builder.Append("# 🍥 我的注册\n");
        builder.Append($"- 角色：**{user.PlayerName}**\n");
        builder.Append($"- 邮箱：{(string.IsNullOrEmpty(user.Email) ? "未绑定" : $"`{Mask(user.Email)}`")}\n");
        builder.Append($"- 状态：{state}\n");

        if (!string.IsNullOrEmpty(user.RegisterIp))
        {
            builder.Append($"- 注册 IP：`{user.RegisterIp}`\n");
        }

        if (!string.IsNullOrEmpty(user.RegisterUuid))
        {
            builder.Append($"- 注册设备：`{user.RegisterUuid}`\n");
        }

        builder.Append($"- 注册时间：{user.BoundAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n");

        if (!user.EmailVerified && !string.IsNullOrEmpty(user.PendingCode))
        {
            builder.Append("\n> 有待验证的注册，发送 " + MenuKit.CmdInput("/注册验证 ", "注册验证 <验证码>") + " 完成。");
        }

        return builder.ToString();
    }

    /// <summary>日志与消息里脱敏展示邮箱，不把完整地址写进日志。</summary>
    public static string Mask(string mail)
    {
        if (string.IsNullOrEmpty(mail))
        {
            return "未绑定";
        }

        int at = mail.IndexOf('@');
        if (at <= 1)
        {
            return "***";
        }

        string name = mail[..at];
        string domain = mail[at..];
        string head = name.Length <= 2 ? name[..1] : name[..2];
        return $"{head}***{domain}";
    }

    private static string GenerateCode()
    {
        return Random.Shared.Next(100000, 999999).ToString();
    }
}
