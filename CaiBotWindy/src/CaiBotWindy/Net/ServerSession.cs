using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Infrastructure;
using CaiBotWindy.Protocol;
using Newtonsoft.Json.Linq;
using Windy.SDK;
using Windy.SDK.Adaptor;

namespace CaiBotWindy.Net;

/// <summary>Bot 与服务端之间的 RPC 异常（超时或服务端返回 <c>error</c> 包）。</summary>
public sealed class ServerRpcException : Exception
{
    public ServerRpcException(string message, bool isTimeout) : base(message)
    {
        IsTimeout = isTimeout;
    }

    public bool IsTimeout { get; }
}

/// <summary>
/// 一条已建立的服务端连接。负责收发数据包、维护请求 / 响应配对与超时。
/// </summary>
public sealed class ServerSession
{
    private const int MaxIncomingBytes = 64 * 1024 * 1024;

    private readonly WebSocket socket;
    private readonly PluginConfig config;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BotPacket>> pending = new(StringComparer.Ordinal);

    public ServerSession(ServerRecord record, ServerType serverType, WebSocket socket, PluginConfig config)
    {
        Record = record;
        ServerType = serverType;
        this.socket = socket;
        this.config = config;
    }

    public ServerRecord Record { get; }

    public ServerType ServerType { get; }

    public string Token => Record.Token;

    public string GroupOpenId => Record.GroupOpenId;

    public DateTime ConnectedAtUtc { get; } = DateTime.UtcNow;

    public bool IsOpen => socket.State == WebSocketState.Open;

    /// <summary>服务端上报 hello 时触发。</summary>
    public event Action<ServerSession>? HelloReceived;

    /// <summary>服务端请求白名单校验时触发，处理方需返回校验结果（同步返回，避免阻塞接收循环）。</summary>
    public event Func<ServerSession, JObject, WhitelistResult>? WhitelistRequested;

    /// <summary>连接结束（正常关闭、异常或对端断开）时触发。</summary>
    public event Action<ServerSession>? Closed;

    /// <summary>接收循环，直到连接关闭才返回。</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                string? json = await ReceiveTextAsync(cancellationToken);
                if (json is null)
                {
                    break;
                }

                if (config.Debug)
                {
                    Message.Blue($"[CaiBotWindy][{Record.GroupOpenId}] <- {json}");
                }

                BotPacket? packet = BotPacket.Parse(json);
                if (packet is null)
                {
                    Message.Red($"[CaiBotWindy] 无法解析服务端数据包: {Trim(json)}");
                    continue;
                }

                Dispatch(packet);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Message.Red($"[CaiBotWindy] 连接异常（{Record.GroupOpenId}）: {ex.Message}");
        }
        finally
        {
            FailAllPending("连接已断开");
            Record.LastSeenUtc = DateTime.UtcNow;
            Closed?.Invoke(this);
        }
    }

    private void Dispatch(BotPacket packet)
    {
        Record.LastSeenUtc = DateTime.UtcNow;

        // 请求-响应链路：服务端返回的响应包同样带 is_request = true 并复用 request_id。
        if (packet.IsRequest && !string.IsNullOrEmpty(packet.RequestId))
        {
            if (pending.TryRemove(packet.RequestId, out TaskCompletionSource<BotPacket>? waiter))
            {
                if (packet.Type == PackageType.Error)
                {
                    waiter.TrySetException(new ServerRpcException(
                        packet.Payload.GetString("error", "服务端处理请求时发生异常"), isTimeout: false));
                }
                else
                {
                    waiter.TrySetResult(packet);
                }
            }
            else if (packet.Type == PackageType.Error)
            {
                Message.Red($"[CaiBotWindy] 服务端返回未匹配的 error 包: {packet.Payload.GetString("error")}");
            }

            return;
        }

        switch (packet.Type)
        {
            case PackageType.Hello:
                ApplyHello(packet);
                HelloReceived?.Invoke(this);
                break;

            case PackageType.Whitelist:
                HandleWhitelist(packet);
                break;

            case PackageType.Heartbeat:
                // 适配插件每 60 秒发送一次心跳，仅用于刷新在线时间。
                break;

            case PackageType.ServerLog:
                HandleServerLog(packet);
                break;

            case PackageType.Unknown:
            case PackageType.Error:
                break;

            default:
                if (config.Debug)
                {
                    Message.Yellow($"[CaiBotWindy] 忽略未预期的包: {packet.Type.ToWire()}");
                }

                break;
        }
    }

    private void ApplyHello(BotPacket packet)
    {
        JObject payload = packet.Payload;
        Record.ServerName = payload.GetString("server_name");
        Record.GameVersion = payload.GetString("game_version");
        Record.CoreVersion = payload.GetString("server_core_version");
        Record.PluginVersion = payload.GetString("plugin_version");
        Record.System = payload.GetString("system");
        Record.EnableWhitelist = payload.GetBool("enable_whitelist");
        Record.LastSeenUtc = DateTime.UtcNow;
        DataStore.SaveServerChange();

        Message.Green(
            $"[CaiBotWindy] 服务器已上线: {Record.ServerName} | 群 {Record.GroupOpenId} | " +
            $"Terraria {Record.GameVersion} | {Record.CoreVersion} | 适配插件 {Record.PluginVersion} | " +
            $"白名单 {(Record.EnableWhitelist ? "开启" : "关闭")}");
    }

    /// <summary>
    /// 服务端事件日志（背包监控等）→ 广播到绑定群。
    /// 走异步：这条回包发生在 WebSocket 读线程上，绝不能同步等 QQ 接口。
    /// </summary>
    private void HandleServerLog(BotPacket packet)
    {
        string message = packet.Payload.GetString("message");
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        string groupOpenId = Record.GroupOpenId;
        string serverName = string.IsNullOrWhiteSpace(Record.ServerName) ? "服务器" : Record.ServerName;

        // 事件广播最容易「一瞬间来一堆」（背包监控这类会连着刷好几条）。
        // 走队列摊平：既不会把平台限频打满，也不会占住 WebSocket 读取线程。
        App.Outbox.Enqueue(
            async _ =>
            {
                Adaptor? adaptor = App.Adaptor;
                if (adaptor is null)
                {
                    return;
                }

                await adaptor.SendMessage(
                    SendTarget.Group(groupOpenId),
                    new MessageContent().AddMarkdown($"# 📡 {serverName} · 事件\n{message}"));
            },
            $"事件广播（{serverName}）");
    }

    private void HandleWhitelist(BotPacket packet)
    {
        if (WhitelistRequested is null)
        {
            return;
        }

        WhitelistResult result;
        try
        {
            result = WhitelistRequested(this, packet.Payload);
        }
        catch (Exception ex)
        {
            Message.Red($"[CaiBotWindy] 白名单校验异常: {ex.Message}");
            result = WhitelistResult.NeedLogin;
        }

        JObject reply = new()
        {
            ["player_name"] = packet.Payload.GetString("player_name"),
            ["is_admin"] = false,
            ["whitelist_result"] = result.ToWire(),
        };

        // 白名单是「服务器 -> Bot」的查询，Bot 的回包仍为单向通知（不参与请求-响应链路）。
        _ = SendAsync(BotPacket.Notify(PackageType.Whitelist, reply), CancellationToken.None);
    }

    /// <summary>发送一个数据包；不等待响应。</summary>
    public async Task SendAsync(BotPacket packet, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            throw new ServerRpcException("连接已关闭，无法发送数据包", isTimeout: false);
        }

        string json = packet.ToJson();
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        if (bytes.Length > config.MaxOutgoingFrameBytes)
        {
            Message.Yellow(
                $"[CaiBotWindy] 警告：发往服务端的 {packet.Type.ToWire()} 包为 {bytes.Length} 字节，" +
                $"超过适配插件单帧 {config.MaxOutgoingFrameBytes} 字节的安全上限，对端可能解析失败。");
        }

        if (config.Debug)
        {
            Message.Blue($"[CaiBotWindy][{Record.GroupOpenId}] -> {json}");
        }

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }

    /// <summary>发送单向通知包（<c>is_request = false</c>），例如 <c>self_kick</c> / <c>unbind_server</c>。</summary>
    public Task NotifyAsync(PackageType type, JObject? payload, CancellationToken cancellationToken)
    {
        return SendAsync(BotPacket.Notify(type, payload), cancellationToken);
    }

    /// <summary>发起 RPC 调用并等待服务端响应。</summary>
    public async Task<BotPacket> RequestAsync(
        PackageType type,
        JObject? payload,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string requestId = Guid.NewGuid().ToString("N");
        TaskCompletionSource<BotPacket> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[requestId] = waiter;

        // 计时：跨机等待基本只发生在这一处，指令「变慢」绝大多数时候就是这里慢。
        Stopwatch watch = Stopwatch.StartNew();
        bool success = false;

        try
        {
            await SendAsync(BotPacket.Request(type, requestId, payload), cancellationToken);

            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                BotPacket packet = await waiter.Task.WaitAsync(timeoutSource.Token);
                success = true;
                return packet;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ServerRpcException(
                    $"等待服务器响应 {type.ToWire()} 超时（{timeout.TotalSeconds:0} 秒）", isTimeout: true);
            }
        }
        finally
        {
            watch.Stop();
            PerfMonitor.Record(
                type.ToWire(),
                watch.Elapsed,
                success,
                string.IsNullOrEmpty(Record.ServerName) ? null : Record.ServerName);

            pending.TryRemove(requestId, out _);
        }
    }

    /// <summary>普通超时的 RPC 调用。</summary>
    public Task<BotPacket> RequestAsync(PackageType type, JObject? payload, CancellationToken cancellationToken)
    {
        return RequestAsync(type, payload, TimeSpan.FromSeconds(config.RequestTimeoutSeconds), cancellationToken);
    }

    /// <summary>文件类（地图 / 世界文件）RPC 调用，使用更长的超时。</summary>
    public Task<BotPacket> RequestFileAsync(PackageType type, JObject? payload, CancellationToken cancellationToken)
    {
        return RequestAsync(type, payload, TimeSpan.FromSeconds(config.FileRequestTimeoutSeconds), cancellationToken);
    }

    /// <summary>主动关闭连接。</summary>
    public async Task CloseAsync(string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken);
            }
        }
        catch
        {
            // 对端已断开时忽略关闭异常。
        }
    }

    private async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream accumulated = new();

        while (true)
        {
            ValueWebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            }
            catch (WebSocketException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            accumulated.Write(buffer, 0, result.Count);

            if (accumulated.Length > MaxIncomingBytes)
            {
                Message.Red("[CaiBotWindy] 单个数据包超过 64MB，已丢弃该连接。");
                return null;
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(accumulated.ToArray());
    }

    private void FailAllPending(string reason)
    {
        foreach (KeyValuePair<string, TaskCompletionSource<BotPacket>> item in pending)
        {
            item.Value.TrySetException(new ServerRpcException(reason, isTimeout: false));
        }

        pending.Clear();
    }

    private static string Trim(string text)
    {
        return text.Length <= 300 ? text : text[..300] + "…";
    }
}
