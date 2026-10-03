using System.Net;
using System.Net.Mail;
using System.Text;
using Windy.SDK;

namespace CaiBotWindy.Services;

/// <summary>
/// 注册验证码的邮件发送。
///
/// <para>用 .NET 内置的 <see cref="SmtpClient"/> 而不是 MailKit，是为了不引入额外的 DLL 依赖 ——
/// Windy 插件从运行目录解析程序集，多带一个第三方库就要多拷好几个文件到 <c>deploy\</c>，
/// 而这里只需要发一封纯文本信。</para>
///
/// <para>代价是<b>只支持 587（STARTTLS）</b>：<see cref="SmtpClient"/> 不支持 465 那种
/// 连接建立即握手的隐式 SSL，配 465 会卡到超时。QQ 邮箱两种端口都开，用 587 即可。</para>
/// </summary>
public static class MailService
{
    /// <summary>发一封验证码邮件。失败时返回原因，调用方负责把它转达给玩家。</summary>
    public static async Task<(bool Success, string Error)> SendCodeAsync(string to, string code, string playerName)
    {
        SmtpSettings smtp = App.Config.Smtp;
        if (!smtp.Ready)
        {
            return (false, "邮件服务未配置");
        }

        try
        {
#pragma warning disable SYSLIB0014 // SmtpClient 已标记过时，但没有替代的内置方案，且这里只发纯文本信
            using SmtpClient client = new(smtp.Host, smtp.Port)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(smtp.User, smtp.Password),
                Timeout = 15000,
            };
#pragma warning restore SYSLIB0014

            using MailMessage message = new()
            {
                From = new MailAddress(smtp.User, smtp.FromName),
                Subject = $"[泰拉瑞亚] 注册验证码 {code}",
                Body = BuildBody(code, playerName),
                IsBodyHtml = false,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
            };
            message.To.Add(to);

            await client.SendMailAsync(message);
            Message.Green($"[邮箱注册] 验证码已发送至 {Mask(to)}");
            return (true, "");
        }
        catch (Exception ex)
        {
            Message.Yellow($"[邮箱注册] 发送到 {Mask(to)} 失败: {ex.Message}");
            return (false, ex.Message);
        }
    }

    private static string BuildBody(string code, string playerName)
    {
        return
            "你好，\n\n" +
            $"你正在为泰拉瑞亚角色「{playerName}」绑定 QQ 邮箱。\n\n" +
            $"验证码：{code}\n\n" +
            "验证码 10 分钟内有效。\n" +
            "如果这不是你本人的操作，直接忽略本邮件即可。\n";
    }

    /// <summary>日志里脱敏显示邮箱，避免把玩家的完整邮箱写进日志。</summary>
    private static string Mask(string mail)
    {
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
}
