using System.Net;
using System.Net.WebSockets;
using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Protocol;
using Newtonsoft.Json.Linq;
using Windy.SDK;

namespace CaiBotWindy.Net;

/// <summary>
/// Bot 侧的 HTTP / WebSocket 服务端。实现了 CaiBotLite 协议约定的全部 HTTP 接口与连接鉴权流程：
/// <list type="bullet">
///   <item><c>GET /ping</c> 健康检查</item>
///   <item><c>GET /server/token/{init_code}</c> 服务端用绑定码换取连接令牌</item>
///   <item><c>GET /download/{file_id}</c> 临时文件二次下载（地图 / 小地图）</item>
///   <item><c>GET /plugin/{name}</c> 下载 plugins 目录下的插件文件</item>
///   <item><c>GET /assets/{path}</c> 图鉴素材（物品 / 生物图标）</item>
///   <item><c>GET /{appid}.json</c> QQ 侧域名校验</item>
///   <item><c>WS /server/ws/{group_open_id}/{server_type}/</c> 服务端长连接</item>
/// </list>
/// </summary>
public sealed class BotHttpServer
{
    private readonly PluginConfig config;
    private readonly SessionHub hub;
    private readonly TempFileStore tempFiles;

    private HttpListener? listener;
    private CancellationTokenSource? cancellation;
    private Task? acceptLoop;

    public BotHttpServer(PluginConfig config, SessionHub hub, TempFileStore tempFiles)
    {
        this.config = config;
        this.hub = hub;
        this.tempFiles = tempFiles;
    }

    /// <summary>白名单校验回调，由插件在启动时注入。</summary>
    public Func<ServerSession, JObject, WhitelistResult>? WhitelistEvaluator { get; set; }

    /// <summary>图鉴素材根目录，用于 <c>GET /assets/{path}</c>（物品 / 生物图标）。</summary>
    public string AssetRoot { get; set; } = "";

    /// <summary>服务端上报 hello（即首次上线）时的回调。</summary>
    public Action<ServerSession>? ServerOnline { get; set; }

    public bool IsRunning => listener?.IsListening ?? false;

    public void Start()
    {
        if (listener is not null)
        {
            return;
        }

        HttpListener http = new();
        foreach (string prefix in config.ListenPrefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                http.Prefixes.Add(prefix.Trim());
            }
        }

        try
        {
            http.Start();
        }
        catch (HttpListenerException ex)
        {
            Message.Red($"[CaiBotWindy] HTTP 服务启动失败: {ex.Message}");
            Message.Yellow("[CaiBotWindy] 若为「拒绝访问」，请以管理员身份执行一次：");
            Message.Yellow($"[CaiBotWindy]   netsh http add urlacl url={config.ListenPrefixes.FirstOrDefault() ?? "http://+:22338/"} user=Everyone");
            throw;
        }

        listener = http;
        cancellation = new CancellationTokenSource();
        acceptLoop = Task.Run(() => AcceptLoopAsync(cancellation.Token));

        Message.Green($"[CaiBotWindy] 已监听: {string.Join(", ", http.Prefixes)}");
    }

    public async Task StopAsync()
    {
        try
        {
            cancellation?.Cancel();
            listener?.Stop();
            listener?.Close();
        }
        catch
        {
            // 关闭过程中的异常忽略。
        }

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // 等待超时不影响关闭流程。
            }
        }

        foreach (ServerSession session in hub.All)
        {
            await session.CloseAsync("机器人正在关闭");
        }

        cancellation?.Dispose();
        cancellation = null;
        listener = null;
        acceptLoop = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener!.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        string path = context.Request.Url?.AbsolutePath ?? "/";

        try
        {
            if (path is "/ping" or "/ping/")
            {
                await WriteJsonAsync(context, 200, new JObject { ["result"] = "pong" });
                return;
            }

            if (path.StartsWith("/server/token/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleTokenAsync(context, path["/server/token/".Length..]);
                return;
            }

            if (path.StartsWith("/server/ws/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleWebSocketAsync(context, path, cancellationToken);
                return;
            }

            if (path.StartsWith("/download/", StringComparison.OrdinalIgnoreCase))
            {
                HandleDownload(context, path["/download/".Length..]);
                return;
            }

            if (path.StartsWith("/plugin/", StringComparison.OrdinalIgnoreCase))
            {
                await HandlePluginFileAsync(context, path["/plugin/".Length..]);
                return;
            }

            if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleAssetAsync(context, path["/assets/".Length..]);
                return;
            }

            string appIdJson = "/" + config.BotAppId + ".json";
            if (!string.IsNullOrEmpty(config.BotAppId) && path.Equals(appIdJson, StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 200, new JObject
                {
                    ["appid"] = config.BotAppId,
                    ["name"] = "CaiBotWindy",
                });
                return;
            }

            await WriteTextAsync(context, 404, "Not Found", "text/plain; charset=utf-8");
        }
        catch (Exception ex)
        {
            Message.Red($"[CaiBotWindy] 处理 {path} 时出错: {ex}");
            TryWriteStatus(context, 500);
        }
    }

    // ── GET /server/token/{init_code} ───────────────────────────────────────────

    private async Task HandleTokenAsync(HttpListenerContext context, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            await WriteTextAsync(context, 404, "Not Found", "text/plain; charset=utf-8");
            return;
        }

        ServerRecord? record = DataStore.FindServerByCode(code);
        if (record is null)
        {
            await WriteTextAsync(context, 404, "Not Found", "text/plain; charset=utf-8");
            return;
        }

        if (DateTime.UtcNow - record.CreatedAtUtc > TimeSpan.FromMinutes(config.BindCodeLifetimeMinutes))
        {
            Message.Yellow($"[CaiBotWindy] 绑定码 {code} 已过期（{config.BindCodeLifetimeMinutes} 分钟）。");
            await WriteTextAsync(context, 404, "Not Found", "text/plain; charset=utf-8");
            return;
        }

        if (!record.Bound)
        {
            record.Bound = true;
            DataStore.SaveServerChange();
            Message.Green($"[CaiBotWindy] 绑定码 {code} 已被服务端换取令牌，群 {record.GroupOpenId}。");
        }

        await WriteJsonAsync(context, 200, new JObject
        {
            ["token"] = record.Token,
            ["group_open_id"] = record.GroupOpenId,
        });
    }

    // ── WS /server/ws/{group_open_id}/{server_type}/ ────────────────────────────

    private async Task HandleWebSocketAsync(HttpListenerContext context, string path, CancellationToken cancellationToken)
    {
        string[] segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        // 期望：server / ws / {group_open_id} / {server_type}
        if (segments.Length != 4)
        {
            await WriteTextAsync(context, 404, "Not Found", "text/plain; charset=utf-8");
            return;
        }

        string groupOpenId = Uri.UnescapeDataString(segments[2]);
        string serverTypeText = Uri.UnescapeDataString(segments[3]);

        string? authorization = context.Request.Headers["Authorization"] ?? context.Request.Headers["authorization"];

        // ── 校验顺序与关闭码对齐 CaiBotLite 文档 ──
        // 1. 是否提供 Authorization
        if (string.IsNullOrEmpty(authorization))
        {
            await RejectWebSocketAsync(context, 1008, "缺失认证令牌");
            return;
        }

        // 2. 是否以 Bearer 开头
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await RejectWebSocketAsync(context, 1008, "认证格式错误，应为 'Bearer <token>'");
            return;
        }

        string token = authorization[7..].Trim();

        // 3. server_type 是否受支持
        if (!ProtocolNames.TryParseServerType(serverTypeText, out ServerType serverType))
        {
            await RejectWebSocketAsync(context, 1008, $"不支持的服务器类型: {serverTypeText}");
            return;
        }

        // 4. 令牌是否有效 / 5. 是否能匹配服务器记录 / 6. 群是否一致
        ServerRecord? record = DataStore.FindServerByToken(token);
        if (record is null)
        {
            await RejectWebSocketAsync(context, 4003, "无效令牌，请重新绑定");
            return;
        }

        if (!string.Equals(record.GroupOpenId, groupOpenId, StringComparison.Ordinal))
        {
            await RejectWebSocketAsync(context, 4003, "令牌与群 OpenID 不匹配");
            return;
        }

        if (!context.Request.IsWebSocketRequest)
        {
            await WriteTextAsync(context, 400, "Bad Request: 需要 WebSocket 升级请求", "text/plain; charset=utf-8");
            return;
        }

        HttpListenerWebSocketContext webSocketContext;
        try
        {
            webSocketContext = await context.AcceptWebSocketAsync(null);
        }
        catch (Exception ex)
        {
            Message.Red($"[CaiBotWindy] WebSocket 升级失败: {ex.Message}");
            TryWriteStatus(context, 500);
            return;
        }

        if (record.ServerType != serverType)
        {
            record.ServerType = serverType;
            DataStore.SaveServerChange();
        }

        ServerSession session = new(record, serverType, webSocketContext.WebSocket, config);
        session.WhitelistRequested += (source, payload) =>
            WhitelistEvaluator?.Invoke(source, payload) ?? WhitelistResult.NeedLogin;
        session.HelloReceived += source => ServerOnline?.Invoke(source);
        session.Closed += source => hub.Remove(source);

        hub.Add(session);
        Message.Blue($"[CaiBotWindy] 服务端已连接: 群 {groupOpenId} / {serverType.DisplayName()}");

        try
        {
            await session.RunAsync(cancellationToken);
        }
        finally
        {
            hub.Remove(session);
            Message.Yellow($"[CaiBotWindy] 服务端已断开: 群 {groupOpenId} / {serverType.DisplayName()}");
        }
    }

    /// <summary>
    /// 鉴权失败时先完成 WebSocket 握手再按协议关闭，以便对端能读到 1008 / 4003 关闭码。
    /// </summary>
    private static async Task RejectWebSocketAsync(HttpListenerContext context, int closeCode, string reason)
    {
        Message.Yellow($"[CaiBotWindy] 拒绝连接（{closeCode}）: {reason}");

        if (!context.Request.IsWebSocketRequest)
        {
            await WriteTextAsync(context, 401, reason, "text/plain; charset=utf-8");
            return;
        }

        try
        {
            HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
            WebSocket socket = webSocketContext.WebSocket;
            try
            {
                await socket.CloseAsync((WebSocketCloseStatus)closeCode, reason, CancellationToken.None);
            }
            finally
            {
                socket.Dispose();
            }
        }
        catch (Exception ex)
        {
            Message.Red($"[CaiBotWindy] 关闭连接失败: {ex.Message}");
            TryWriteStatus(context, 500);
        }
    }

    // ── GET /download/{file_id} ────────────────────────────────────────────────

    private void HandleDownload(HttpListenerContext context, string fileId)
    {
        (string Path, string FileName)? file =
            tempFiles.Resolve(Uri.UnescapeDataString(fileId), TimeSpan.FromMinutes(config.DownloadFileLifetimeMinutes));

        if (file is null)
        {
            WriteText(context, 404, "Not Found", "text/plain; charset=utf-8");
            return;
        }

        byte[] data = File.ReadAllBytes(file.Value.Path);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/octet-stream";
        context.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{file.Value.FileName}\"");
        WriteBytes(context, data);
    }

    // ── GET /plugin/{name} ─────────────────────────────────────────────────────

    private async Task HandlePluginFileAsync(HttpListenerContext context, string name)
    {
        string decoded = Uri.UnescapeDataString(name);
        if (decoded.Contains('/') || decoded.Contains('\\') || decoded.Contains("..", StringComparison.Ordinal))
        {
            await WriteTextAsync(context, 403, "Forbidden", "text/plain; charset=utf-8");
            return;
        }

        string pluginDirectory = Path.Combine(WindyRuntime.BasicPath, "Plugins");
        string fullPath = Path.GetFullPath(Path.Combine(pluginDirectory, decoded));
        if (!fullPath.StartsWith(Path.GetFullPath(pluginDirectory), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            await WriteTextAsync(context, 404, "Not Found", "text/plain; charset=utf-8");
            return;
        }

        byte[] data = await File.ReadAllBytesAsync(fullPath);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/octet-stream";
        WriteBytes(context, data);
    }

    // ── GET /assets/{path} ─────────────────────────────────────────────────────

    /// <summary>
    /// 图鉴素材（物品 / 生物图标）。与 <c>/plugin/</c> 不同，这里允许子目录（例如
    /// <c>Items/Item_39.png</c>），因此对路径逐段校验并最终做一次根目录包含性检查。
    /// </summary>
    private async Task HandleAssetAsync(HttpListenerContext context, string path)
    {
        if (string.IsNullOrWhiteSpace(AssetRoot))
        {
            await WriteTextAsync(context, 404, "Not Found", "text/plain; charset=utf-8");
            return;
        }

        string decoded = Uri.UnescapeDataString(path).Replace('\\', '/').Trim('/');
        if (decoded.Length == 0)
        {
            await WriteTextAsync(context, 404, "Not Found", "text/plain; charset=utf-8");
            return;
        }

        // 逐段校验：拒绝空段、"."、".." 与非法字符。
        foreach (string segment in decoded.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                await WriteTextAsync(context, 403, "Forbidden", "text/plain; charset=utf-8");
                return;
            }
        }

        string root = Path.GetFullPath(AssetRoot);
        string fullPath = Path.GetFullPath(Path.Combine(root, decoded.Replace('/', Path.DirectorySeparatorChar)));

        // 必须落在素材根目录内，且必须是文件（防止目录列举 / 越权读取）。
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            await WriteTextAsync(context, 404, "Not Found", "text/plain; charset=utf-8");
            return;
        }

        byte[] data = await File.ReadAllBytesAsync(fullPath);
        context.Response.StatusCode = 200;
        context.Response.ContentType = ContentTypeOf(fullPath);
        context.Response.AddHeader("Cache-Control", "public, max-age=86400");
        WriteBytes(context, data);
    }

    private static string ContentTypeOf(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }

    // ── 响应工具 ───────────────────────────────────────────────────────────────

    private static Task WriteJsonAsync(HttpListenerContext context, int status, JObject body)
    {
        return WriteTextAsync(context, status, body.ToString(Newtonsoft.Json.Formatting.None), "application/json; charset=utf-8");
    }

    private static Task WriteTextAsync(HttpListenerContext context, int status, string body, string contentType)
    {
        byte[] data = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        WriteBytes(context, data);
        return Task.CompletedTask;
    }

    private static void WriteText(HttpListenerContext context, int status, string body, string contentType)
    {
        WriteTextAsync(context, status, body, contentType).GetAwaiter().GetResult();
    }

    private static void WriteBytes(HttpListenerContext context, byte[] data)
    {
        try
        {
            context.Response.ContentLength64 = data.Length;
            context.Response.OutputStream.Write(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            Message.Yellow($"[CaiBotWindy] 写响应失败: {ex.Message}");
        }
        finally
        {
            try
            {
                context.Response.OutputStream.Close();
                context.Response.Close();
            }
            catch
            {
                // 连接已被对端关闭时忽略。
            }
        }
    }

    private static void TryWriteStatus(HttpListenerContext context, int status)
    {
        try
        {
            context.Response.StatusCode = status;
            context.Response.OutputStream.Close();
            context.Response.Close();
        }
        catch
        {
            // 忽略。
        }
    }
}
