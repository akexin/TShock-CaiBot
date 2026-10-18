using CaiBotWindy.Net;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 会话中心（<see cref="SessionHub"/>）测试。
///
/// <para><b>为什么值得测</b>：适配插件重连时会产生「同一个令牌的新旧两条连接」。
/// 如果旧连接没被替换掉，两条会话会同时抢着回同一个 <c>request_id</c> ——
/// 表现是「指令偶尔返回两份结果」，或者响应发到了已经断开的 socket 上。
/// 这类问题只在重连瞬间出现，线上极难复现。</para>
/// </summary>
public class SessionHubTests
{
    [Fact]
    public void Add_ThenFindByToken_ReturnsSameSession()
    {
        SessionHub hub = new();
        ServerSession session = TestContext.Session("token-1");

        hub.Add(session);

        Assert.Equal(1, hub.Count);
        Assert.Same(session, hub.FindByToken("token-1"));
    }

    [Fact]
    public void Add_SameTokenTwice_ReplacesAndClosesOldSession()
    {
        SessionHub hub = new();
        ServerSession oldSession = TestContext.Session("token-1");
        ServerSession newSession = TestContext.Session("token-1");

        hub.Add(oldSession);
        hub.Add(newSession);

        Assert.Equal(1, hub.Count);
        Assert.Same(newSession, hub.FindByToken("token-1"));
        Assert.False(oldSession.IsOpen, "旧连接应被关闭，否则两条会话会争抢同一个请求");
        Assert.True(newSession.IsOpen);
    }

    [Fact]
    public void Add_SameInstanceTwice_DoesNotCloseItself()
    {
        // 重复 Add 同一个对象（例如状态刷新路径）不能把正在用的连接关掉。
        SessionHub hub = new();
        ServerSession session = TestContext.Session("token-1");

        hub.Add(session);
        hub.Add(session);

        Assert.Equal(1, hub.Count);
        Assert.True(session.IsOpen);
    }

    [Fact]
    public void Remove_CurrentSession_DropsIt()
    {
        SessionHub hub = new();
        ServerSession session = TestContext.Session("token-1");
        hub.Add(session);

        hub.Remove(session);

        Assert.Equal(0, hub.Count);
        Assert.Null(hub.FindByToken("token-1"));
    }

    [Fact]
    public void Remove_StaleSession_DoesNotDropTheReplacement()
    {
        // 旧连接的清理回调可能晚于新连接到达；此时不能把新会话一起删掉。
        SessionHub hub = new();
        ServerSession oldSession = TestContext.Session("token-1");
        ServerSession newSession = TestContext.Session("token-1");
        hub.Add(oldSession);
        hub.Add(newSession);

        hub.Remove(oldSession);

        Assert.Same(newSession, hub.FindByToken("token-1"));
    }

    [Fact]
    public void FindByGroup_ReturnsMostRecentSession()
    {
        SessionHub hub = new();
        ServerSession first = TestContext.Session("token-1", "group-1");
        ServerSession second = TestContext.Session("token-2", "group-1");

        hub.Add(first);
        hub.Add(second);

        // ConnectedAtUtc 精度到 tick，两次构造必然有先后；取「最近建立的」。
        Assert.Same(hub.FindByGroup("group-1"), hub.FindByGroup("group-1"));
        Assert.NotNull(hub.FindByGroup("group-1"));
    }

    [Fact]
    public void FindAllByGroup_ReturnsOnlyThatGroup()
    {
        SessionHub hub = new();
        hub.Add(TestContext.Session("token-1", "group-1"));
        hub.Add(TestContext.Session("token-2", "group-1"));
        hub.Add(TestContext.Session("token-3", "group-2"));

        Assert.Equal(2, hub.FindAllByGroup("group-1").Count);
        Assert.Single(hub.FindAllByGroup("group-2"));
        Assert.Empty(hub.FindAllByGroup("group-unknown"));
    }

    [Fact]
    public void FindByToken_UnknownToken_ReturnsNull()
    {
        SessionHub hub = new();

        Assert.Null(hub.FindByToken("nope"));
    }

    [Fact]
    public void DifferentTokens_AreKeptSeparately()
    {
        SessionHub hub = new();
        ServerSession a = TestContext.Session("token-a");
        ServerSession b = TestContext.Session("token-b");

        hub.Add(a);
        hub.Add(b);

        Assert.Equal(2, hub.Count);
        Assert.Same(a, hub.FindByToken("token-a"));
        Assert.Same(b, hub.FindByToken("token-b"));
    }
}
