using CaiBotWindy.Commands;
using Windy.SDK.Command;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 指令参数解析测试：服务器序号在「末尾」（普通指令）与「开头」（<c>/远程指令</c>）两种约定。
///
/// <para><b>为什么值得单独测</b>：序号解析写错不会报错，只会「静默选错服务器」——
/// 玩家查的是 2 号服的背包，看到的却是 1 号服的数据。这种错最难被发现。</para>
/// </summary>
public class CommandParsingTests
{
    private static CommandArgs Args(params string[] parameters)
    {
        return TestContext.Command("test", "user-1", "group-1", parameters: parameters);
    }

    // ── ParseServerIndex：固定位置的序号 ─────────────────────────────────────

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("99", 99)]
    public void ParseServerIndex_ValidPositive_ReturnsValue(string token, int expected)
    {
        Assert.Equal(expected, CommandHelpers.ParseServerIndex(Args(token), 0));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.5")]
    public void ParseServerIndex_Invalid_ReturnsZero(string token)
    {
        Assert.Equal(0, CommandHelpers.ParseServerIndex(Args(token), 0));
    }

    [Fact]
    public void ParseServerIndex_PositionOutOfRange_ReturnsZero()
    {
        Assert.Equal(0, CommandHelpers.ParseServerIndex(Args(), 0));
        Assert.Equal(0, CommandHelpers.ParseServerIndex(Args("1"), 3));
    }

    // ── ParseTrailingIndex：序号在最后（/查背包 玩家名 2） ────────────────────

    [Fact]
    public void ParseTrailingIndex_TrailingNumber_ReturnsValue()
    {
        Assert.Equal(2, CommandHelpers.ParseTrailingIndex(Args("玩家名", "2"), 1));
    }

    [Fact]
    public void ParseTrailingIndex_NoTrailingNumber_ReturnsZero()
    {
        Assert.Equal(0, CommandHelpers.ParseTrailingIndex(Args("玩家名"), 1));
        Assert.Equal(0, CommandHelpers.ParseTrailingIndex(Args("玩家名", "abc"), 1));
    }

    [Fact]
    public void ParseTrailingIndex_ZeroAndNegative_ReturnsZero()
    {
        // 0 / -1 不是合法序号，必须退回「自动选择」，否则会去查一个不存在的服务器。
        Assert.Equal(0, CommandHelpers.ParseTrailingIndex(Args("玩家名", "0"), 1));
        Assert.Equal(0, CommandHelpers.ParseTrailingIndex(Args("玩家名", "-1"), 1));
    }

    [Fact]
    public void ParseTrailingIndex_OnlyRequiredParams_ReturnsZero()
    {
        // 参数数量刚好等于必需数时，末位属于业务参数而不是序号。
        Assert.Equal(0, CommandHelpers.ParseTrailingIndex(Args("2"), 1));
    }

    [Fact]
    public void ParseTrailingIndex_MultiWordPath_UsesLastTokenOnly()
    {
        // /发文件 路径带空格时，序号只看最后一个 token。
        Assert.Equal(3, CommandHelpers.ParseTrailingIndex(Args("/srv/my world.wld", "3"), 1));
        Assert.Equal(0, CommandHelpers.ParseTrailingIndex(Args("/srv/my world.wld"), 1));
    }

    // ── ParseLeadingIndex：序号在前（/远程指令 1 /help） ──────────────────────

    [Fact]
    public void ParseLeadingIndex_LeadingNumber_SplitsIt()
    {
        (int index, List<string> remaining) = CommandHelpers.ParseLeadingIndex(Args("1", "/help"));

        Assert.Equal(1, index);
        Assert.Equal(["/help"], remaining);
    }

    [Fact]
    public void ParseLeadingIndex_MultiWordCommand_KeepsRestIntact()
    {
        (int index, List<string> remaining) = CommandHelpers.ParseLeadingIndex(Args("2", "/time", "noon"));

        Assert.Equal(2, index);
        Assert.Equal(["/time", "noon"], remaining);
    }

    [Fact]
    public void ParseLeadingIndex_NoNumber_KeepsAllAsCommand()
    {
        (int index, List<string> remaining) = CommandHelpers.ParseLeadingIndex(Args("/help"));

        Assert.Equal(0, index);
        Assert.Equal(["/help"], remaining);
    }

    [Fact]
    public void ParseLeadingIndex_SingleNumber_IsNotTreatedAsIndex()
    {
        // 只有序号、没有指令内容 —— 不能把 "1" 吃掉，否则调用方无从提示「缺少指令」。
        (int index, List<string> remaining) = CommandHelpers.ParseLeadingIndex(Args("1"));

        Assert.Equal(0, index);
        Assert.Equal(["1"], remaining);
    }

    [Fact]
    public void ParseLeadingIndex_ThreeDigitNumber_IsNotIndex()
    {
        // 序号只可能是 1~2 位；三位数更可能是指令自带的参数。
        (int index, List<string> remaining) = CommandHelpers.ParseLeadingIndex(Args("123", "/help"));

        Assert.Equal(0, index);
        Assert.Equal(["123", "/help"], remaining);
    }

    [Fact]
    public void ParseLeadingIndex_TrailingNumber_IsNotLeadingIndex()
    {
        // 旧写法 /远程指令 /help 1 不再被当成序号：1 会作为指令参数原样透传。
        (int index, List<string> remaining) = CommandHelpers.ParseLeadingIndex(Args("/help", "1"));

        Assert.Equal(0, index);
        Assert.Equal(["/help", "1"], remaining);
    }

    [Fact]
    public void ParseLeadingIndex_Zero_IsConsumedButMeansAutoSelect()
    {
        // "0" 是数字，会被当占位吃掉；index=0 语义仍是「自动选择」。
        (int index, List<string> remaining) = CommandHelpers.ParseLeadingIndex(Args("0", "/help"));

        Assert.Equal(0, index);
        Assert.Equal(["/help"], remaining);
    }
}
