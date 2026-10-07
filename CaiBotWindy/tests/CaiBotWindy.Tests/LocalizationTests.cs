using CaiBotWindy.Localization;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 多语言框架的取值与回退规则。
///
/// <para>回退链是本模块最容易出错的地方：当前语言 → 简体中文 → key 本身。
/// 少了任何一环，缺词条时界面上就会出现空白或者「error.xxx」这种怪东西。</para>
/// </summary>
public class LocalizationTests
{
    public LocalizationTests()
    {
        // 每个用例前复位，避免语言状态在用例之间泄漏。
        L.Use(AppLanguage.ZhCn);
    }

    [Fact]
    public void Chinese_IsTheDefault()
    {
        L.Use(AppLanguage.ZhCn);

        Assert.Equal(AppLanguage.ZhCn, L.Current);
        Assert.Equal("⛔ 权限不足", L.Get("error.permission_denied"));
    }

    [Fact]
    public void English_ReturnsTranslatedText()
    {
        L.Use(AppLanguage.EnUs);

        Assert.Equal("⛔ Permission denied", L.Get("error.permission_denied"));
    }

    [Fact]
    public void UnknownKey_ReturnsKeyItself()
    {
        // 返回 key 而不是空串：界面上出现 "no.such.key" 能一眼看出缺哪条。
        Assert.Equal("no.such.key", L.Get("no.such.key"));
    }

    [Fact]
    public void Format_SubstitutesPlaceholders()
    {
        string text = L.Format("error.server_index", 3);

        Assert.Contains("3", text);
        Assert.DoesNotContain("{0}", text);
    }

    [Fact]
    public void Format_WithMismatchedArgs_ReturnsTemplateInsteadOfThrowing()
    {
        // 占位符对不上时宁可显示原模板，也不能抛异常打断指令。
        string text = L.Format("error.permission_denied", "多余的参数");

        Assert.Equal("⛔ 权限不足", text);
    }

    [Theory]
    [InlineData("zh-CN", AppLanguage.ZhCn)]
    [InlineData("en-US", AppLanguage.EnUs)]
    [InlineData("zh", AppLanguage.ZhCn)]
    [InlineData("en", AppLanguage.EnUs)]
    [InlineData("ZH-CN", AppLanguage.ZhCn)]
    [InlineData("klingon", AppLanguage.ZhCn)]
    [InlineData("", AppLanguage.ZhCn)]
    public void Parse_ResolvesLanguageCodes(string? code, AppLanguage expected)
    {
        Assert.Equal(expected, AppLanguages.Parse(code));
    }

    [Fact]
    public void Code_RoundTrips()
    {
        foreach (AppLanguage language in AppLanguages.All)
        {
            Assert.Equal(language, AppLanguages.Parse(AppLanguages.Code(language)));
        }
    }

    [Fact]
    public void EveryLanguageHasTheCommonKeys()
    {
        // 防止「加了新语言但漏翻常用词条」——至少这些基础键必须每种语言都有。
        string[] required = ["common.online", "common.offline", "error.permission_denied", "hint.menu"];

        foreach (AppLanguage language in AppLanguages.All)
        {
            L.Use(language);
            foreach (string key in required)
            {
                Assert.NotEqual(key, L.Get(key));
            }
        }
    }
}
