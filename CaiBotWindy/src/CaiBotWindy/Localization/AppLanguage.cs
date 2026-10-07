namespace CaiBotWindy.Localization;

/// <summary>
/// 支持的语言。
///
/// <para>新增一门语言的步骤：① 在此加一个枚举值；② 在 <see cref="AppLanguages"/> 的
/// 三个映射里补上对应条目；③ 在 <see cref="L"/> 的文案表里补一份翻译。
/// 业务代码不需要任何改动。</para>
/// </summary>
public enum AppLanguage
{
    /// <summary>简体中文（默认）。</summary>
    ZhCn = 0,

    /// <summary>English。</summary>
    EnUs = 1,
}

/// <summary>语言代码与显示名的映射，用于配置读写和界面展示。</summary>
public static class AppLanguages
{
    /// <summary>语言 → 配置里写的代码（大小写不敏感）。</summary>
    private static readonly Dictionary<AppLanguage, string> Codes = new()
    {
        [AppLanguage.ZhCn] = "zh-CN",
        [AppLanguage.EnUs] = "en-US",
    };

    /// <summary>语言 → 自身的名称（用该语言写，方便母语者辨认）。</summary>
    private static readonly Dictionary<AppLanguage, string> Names = new()
    {
        [AppLanguage.ZhCn] = "简体中文",
        [AppLanguage.EnUs] = "English",
    };

    /// <summary>
    /// 解析语言代码。无法识别时回退到 <see cref="AppLanguage.ZhCn"/> ——
    /// 配置文件里写错不该让机器人讲「火星文」。
    /// </summary>
    public static AppLanguage Parse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AppLanguage.ZhCn;
        }

        foreach (KeyValuePair<AppLanguage, string> pair in Codes)
        {
            if (string.Equals(pair.Value, code.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        // 也接受 "zh"、"en" 这种简写。
        string head = code.Trim()[..Math.Min(2, code.Trim().Length)];
        return head.ToLowerInvariant() switch
        {
            "zh" => AppLanguage.ZhCn,
            "en" => AppLanguage.EnUs,
            _ => AppLanguage.ZhCn,
        };
    }

    /// <summary>该语言在配置文件里使用的代码。</summary>
    public static string Code(AppLanguage language) =>
        Codes.TryGetValue(language, out string? code) ? code : Codes[AppLanguage.ZhCn];

    /// <summary>该语言自身的名称。</summary>
    public static string Name(AppLanguage language) =>
        Names.TryGetValue(language, out string? name) ? name : Names[AppLanguage.ZhCn];

    /// <summary>全部支持的语言，供「切换语言」类界面列举。</summary>
    public static IReadOnlyList<AppLanguage> All { get; } = [AppLanguage.ZhCn, AppLanguage.EnUs];
}
