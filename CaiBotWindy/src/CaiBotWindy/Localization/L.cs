namespace CaiBotWindy.Localization;

/// <summary>
/// 本地化文案门面 —— 全站文案的唯一入口。
///
/// <para><b>为什么要有它</b>：文案原本散落在各个指令方法里（<c>"# ⛔ 权限不足"</c> 这样硬编码），
/// 想支持第二种语言就得满仓库翻改，改漏一处就是中英混排。集中之后，
/// 翻译只需要动这个文件。</para>
///
/// <para><b>用法</b>：<c>L.Get("error.permission_denied")</c>；
/// 带占位符用 <c>L.Format("error.server_offline", name)</c>。</para>
///
/// <para><b>取不到时的行为</b>：当前语言 → 简体中文 → 返回 key 本身。
/// 最后一步兜底保证界面上出现的是「error.xxx」这种<b>能一眼看出缺哪条</b>的串，
/// 而不是空白或异常。</para>
///
/// <para><b>迁移策略</b>：不必一口气搬完。新代码一律用 <see cref="Get"/>；
/// 老文案按优先级逐步搬（先错误消息、再提示语、最后长篇说明）。</para>
/// </summary>
public static class L
{
    private static AppLanguage current = AppLanguage.ZhCn;

    /// <summary>当前语言。默认简体中文。</summary>
    public static AppLanguage Current => current;

    /// <summary>切换语言。</summary>
    public static void Use(AppLanguage language)
    {
        current = language;
    }

    /// <summary>按语言代码切换（无法识别时回退到简体中文）。</summary>
    public static void Use(string? code)
    {
        current = AppLanguages.Parse(code);
    }

    /// <summary>
    /// 取某种语言的文案表。
    ///
    /// <para><b>必须是方法而不是静态字段</b>：静态字段按声明顺序初始化，
    /// 若在类顶部用 <c>static readonly</c> 字典把 ZhCn / EnUs 收进去，
    /// 那时这两个表还没初始化，收进去的就是 null。惰性求值才不会有这个顺序陷阱。</para>
    /// </summary>
    private static Dictionary<string, string> CatalogOf(AppLanguage language) => language switch
    {
        AppLanguage.EnUs => EnUs,
        _ => ZhCn,
    };

    /// <summary>取一条文案。取不到时按「当前语言 → 中文 → key」依次回退。</summary>
    public static string Get(string key)
    {
        if (CatalogOf(current).TryGetValue(key, out string? text))
        {
            return text;
        }

        // 非中文缺词条时，回退到中文而不是直接给 key —— 对用户来说
        // 一句中文比 "error.xxx" 更有用。
        if (current != AppLanguage.ZhCn &&
            ZhCn.TryGetValue(key, out string? fallback))
        {
            return fallback;
        }

        return key;
    }

    /// <summary>取一条带占位符的文案（<c>{0}</c>、<c>{1}</c>…）。</summary>
    public static string Format(string key, params object[] args)
    {
        string template = Get(key);
        if (args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            // 占位符数量对不上时退回原模板，宁可显示错也不要抛异常打断指令。
            return template;
        }
    }

    /// <summary>判断某条文案是否存在（用于自检 / 单元测试）。</summary>
    public static bool Has(string key) => ZhCn.ContainsKey(key);

    // ── 简体中文 ──────────────────────────────────────────────────────────────

    private static Dictionary<string, string> ZhCn { get; } = new(StringComparer.Ordinal)
    {
        // 通用状态词
        ["common.online"] = "在线",
        ["common.offline"] = "离线",
        ["common.enabled"] = "开启",
        ["common.disabled"] = "关闭",
        ["common.unknown"] = "未知",
        ["common.none"] = "无",

        // 错误标题
        ["error.permission_denied"] = "⛔ 权限不足",
        ["error.group_only"] = "⛔ 该指令只能在群聊中使用",
        ["error.not_ready"] = "⏳ 机器人尚未就绪",
        ["error.no_server"] = "⛔ 本群还没有绑定服务器",
        ["error.server_offline"] = "⛔ 服务器 **{0}** 当前离线",
        ["error.server_index"] = "⛔ 序号 {0} 对应的服务器不存在或未连接",
        ["error.rpc_timeout"] = "⏰ 服务器响应超时",
        ["error.rpc_failed"] = "⚠️ 服务器返回错误",
        ["error.call_failed"] = "⚠️ 调用服务器失败",
        ["error.unknown_command"] = "❓ 没有这条指令",
        ["error.execution_failed"] = "⚠️ 指令执行失败",

        // 错误正文 / 补充提示
        ["hint.not_ready_body"] = "HTTP 服务或图鉴数据还在初始化，请稍后再试。",
        ["hint.bind_server"] = "在服务器控制台查看绑定码，然后发送：",
        ["hint.server_offline_body"] = "请确认服务端已启动且适配插件已连接成功。",
        ["hint.server_list"] = "用「服务器列表」查看当前绑定情况。",
        ["hint.menu"] = "发送 `/菜单` 查看全部功能。",
        ["hint.check_logs"] = "详情已记录到控制台日志，请联系管理员排查。",
        ["hint.retry"] = "请稍后重试。",

        // 注册 / 白名单
        ["register.mail_required"] = "邮件服务未配置，注册暂不可用。",
        ["register.mail_sent"] = "📧 验证码已发送",
        ["register.done"] = "✅ 注册完成",
        ["whitelist.login_request"] = "🔐 登录验证",
    };

    // ── English ───────────────────────────────────────────────────────────────

    private static Dictionary<string, string> EnUs { get; } = new(StringComparer.Ordinal)
    {
        // Common states
        ["common.online"] = "Online",
        ["common.offline"] = "Offline",
        ["common.enabled"] = "Enabled",
        ["common.disabled"] = "Disabled",
        ["common.unknown"] = "Unknown",
        ["common.none"] = "None",

        // Error titles
        ["error.permission_denied"] = "⛔ Permission denied",
        ["error.group_only"] = "⛔ This command only works in group chats",
        ["error.not_ready"] = "⏳ Bot is not ready yet",
        ["error.no_server"] = "⛔ No server is bound to this group",
        ["error.server_offline"] = "⛔ Server **{0}** is offline",
        ["error.server_index"] = "⛔ No server found at index {0}",
        ["error.rpc_timeout"] = "⏰ Server response timed out",
        ["error.rpc_failed"] = "⚠️ Server returned an error",
        ["error.call_failed"] = "⚠️ Failed to reach the server",
        ["error.unknown_command"] = "❓ Unknown command",
        ["error.execution_failed"] = "⚠️ Command failed",

        // Error bodies / hints
        ["hint.not_ready_body"] = "HTTP service or item data is still initializing. Please try again shortly.",
        ["hint.bind_server"] = "Find the bind code in the server console, then send:",
        ["hint.server_offline_body"] = "Make sure the server is running and the adapter plugin is connected.",
        ["hint.server_list"] = "Use \"server list\" to see current bindings.",
        ["hint.menu"] = "Send `/menu` to see all features.",
        ["hint.check_logs"] = "Details are in the console log. Please contact an administrator.",
        ["hint.retry"] = "Please try again later.",

        // Registration / whitelist
        ["register.mail_required"] = "Mail service is not configured; registration is unavailable.",
        ["register.mail_sent"] = "📧 Verification code sent",
        ["register.done"] = "✅ Registration complete",
        ["whitelist.login_request"] = "🔐 Login verification",
    };
}
