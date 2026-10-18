using System.Net.WebSockets;
using CaiBotWindy.Data;
using CaiBotWindy.Net;
using CaiBotWindy.Protocol;
using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Tests;

/// <summary>
/// 最小可用的测试替身。
///
/// <para>业务逻辑大量依赖 Windy SDK 的抽象类型（<see cref="Adaptor"/> 是抽象类、
/// <see cref="WebSocket"/> 也是），直接用真家伙就要连 QQ 平台。这里给出刚好够用的空实现，
/// 让「权限判定 / 白名单状态机 / 会话管理」这类纯逻辑能在进程内跑。</para>
/// </summary>
internal sealed class FakeAdaptor : Adaptor
{
    public FakeAdaptor() : base("fake", AdaptorType.QQOfficial, new AdaptorConfig())
    {
    }

    /// <summary>记录每条出站消息的目标 id，用于断言「有没有发」。</summary>
    public List<string> SentTargets { get; } = [];

    public override Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override Task SendMessage(
        SendTarget target,
        MessageContent content,
        SendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SentTargets.Add(target.Id);
        return Task.CompletedTask;
    }
}

/// <summary>不会真正收发数据的 WebSocket；<see cref="State"/> 初始为 Open。</summary>
internal sealed class FakeWebSocket : WebSocket
{
    private WebSocketState state = WebSocketState.Open;

    public override WebSocketCloseStatus? CloseStatus => null;

    public override string? CloseStatusDescription => null;

    public override WebSocketState State => state;

    public override string? SubProtocol => null;

    public override void Abort()
    {
        state = WebSocketState.Aborted;
    }

    public override Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        return CloseAsync(closeStatus, statusDescription, cancellationToken);
    }

    public override void Dispose()
    {
        state = WebSocketState.Closed;
    }

    public override Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        // 测试里没人读，返回「已关闭」让任何 Receive 循环立即退出。
        return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
    }

    public override Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>构造指令上下文与会话的便捷方法。</summary>
internal static class TestContext
{
    /// <summary>造一个 <see cref="CommandArgs"/>；<paramref name="groupId"/> 为 null 表示私聊。</summary>
    public static CommandArgs Command(
        string name,
        string authorId,
        string? groupId,
        GroupMemberRole role = GroupMemberRole.Member,
        params string[] parameters)
    {
        FakeAdaptor adaptor = new();
        MessageEventArgs message = new(
            adaptor,
            groupId is null ? MessageScene.Private : MessageScene.Group,
            "/" + name + (parameters.Length == 0 ? "" : " " + string.Join(' ', parameters)),
            "msg-1",
            authorId,
            groupId is null ? SendTarget.User(authorId) : SendTarget.Group(groupId))
        {
            GroupId = groupId,
            Role = role,
        };

        return new CommandArgs(name, parameters, message);
    }

    /// <summary>造一个绑定到指定群 / 令牌的 <see cref="ServerSession"/>。</summary>
    public static ServerSession Session(
        string token,
        string groupOpenId = "group-1",
        string serverName = "test-server")
    {
        ServerRecord record = new()
        {
            Token = token,
            GroupOpenId = groupOpenId,
            ServerName = serverName,
        };

        return new ServerSession(record, ServerType.TShock, new FakeWebSocket(), new PluginConfig());
    }
}
