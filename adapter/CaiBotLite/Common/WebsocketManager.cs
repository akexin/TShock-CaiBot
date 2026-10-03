using CaiBotLite.Enums;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using Terraria;
using TShockAPI;

namespace CaiBotLite.Common;

public static class WebsocketManager
{
    public static ClientWebSocket? WebSocket;

    /// <summary>
    /// Bot 服务端主机与端口，来自配置文件「机器人服务端地址」。
    /// 默认官方服务 api.terraria.ink:22338；自建 Bot 时填 <c>127.0.0.1:22338</c> 之类即可。
    /// </summary>
    private static string BotServerUrl => Config.Settings.BotServerUrl;

    /// <summary>
    /// HTTP 协议前缀。官方服务走 <c>https</c>；自建 Bot 的 HttpListener 无 TLS 证书，需走 <c>http</c>。
    /// </summary>
    private static string HttpScheme => Config.Settings.UseTls ? "https" : "http";

    /// <summary>
    /// WebSocket 协议前缀，与 <see cref="HttpScheme"/> 对应。
    /// </summary>
    private static string WsScheme => Config.Settings.UseTls ? "wss" : "ws";

    /// <summary>
    /// 供 <c>/cbl info</c> 显示当前实际连接地址。
    /// </summary>
    internal static string DisplayEndpoint => $"{HttpScheme}://{BotServerUrl}";

    internal static bool IsWebsocketConnected => WebSocket?.State == WebSocketState.Open;
    private static bool _isStopWebsocket;

    public static void Init()
    {
        Task.Factory.StartNew(StartCaiApi, TaskCreationOptions.LongRunning);
        Task.Factory.StartNew(StartHeartBeat, TaskCreationOptions.LongRunning);
        _isStopWebsocket = false;
    }

    public static void StopWebsocket()
    {
        _isStopWebsocket = true;
        WebSocket?.Dispose();
    }

    private static async Task? StartHeartBeat()
    {
        while (!_isStopWebsocket)
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
            try
            {
                if (WebSocket?.State == WebSocketState.Open)
                {
                    var packetWriter = new PackageWriter(PackageType.Heartbeat, false, null);
                    packetWriter.Send();
                }
            }
            catch
            {
                TShock.Log.ConsoleInfo("[CaiBotLite]心跳包发送失败!");
            }
        }
    }

    private static async Task? StartCaiApi()
    {
        while (!_isStopWebsocket)
        {
            try
            {
                WebSocket = new ClientWebSocket();
                while (string.IsNullOrEmpty(Config.Settings.Token))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    HttpClient client = new ();
                    client.Timeout = TimeSpan.FromSeconds(5.0);
                    var response = await client.GetAsync($"{HttpScheme}://{BotServerUrl}/server/token/{CaiBotLite.InitCode}");
                    if (response.StatusCode != HttpStatusCode.OK || Config.Settings.Token != "")
                    {
                        continue;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(responseBody);
                    var token = json["token"]!.ToString();
                    var groupOpenId = json["group_open_id"]!.ToString();
                    Config.Settings.Token = token;
                    Config.Settings.GroupOpenId = groupOpenId;
                    Config.Settings.Write();
                    TShock.Log.ConsoleInfo("[CaiBotLite]被动绑定成功!");
                }

                WebSocket.Options.SetRequestHeader("authorization", $"Bearer {Config.Settings.Token}");
                await WebSocket.ConnectAsync(new Uri($"{WsScheme}://{BotServerUrl}/server/ws/{Config.Settings.GroupOpenId}/tshock/"), CancellationToken.None);


                new PackageWriter(PackageType.Hello, false, null)
                    .Write("server_core_version", TShock.VersionNum.ToString())
                    .Write("plugin_version", CaiBotLite.VersionNum)
                    .Write("game_version", Main.versionNumber)
                    .Write("enable_whitelist", Config.Settings.WhiteList)
                    .Write("system", RuntimeInformation.RuntimeIdentifier)
                    .Write("server_name", TShock.Config.Settings.UseServerName ? TShock.Config.Settings.ServerName : Main.worldName)
                    .Write("settings", new Dictionary<string, object>())
                    .Send();

                TShock.Log.ConsoleInfo($"[CaiBotLite]Bot连接成功... ({DisplayEndpoint})");

                while (true)
                {
                    var buffer = new byte[1024];
                    var memoryStream = new MemoryStream();

                    WebSocketReceiveResult result;
                    do
                    {
                        result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        await memoryStream.WriteAsync(buffer.AsMemory(0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // 连接关闭时获取原因
                        await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        var statusCode = (int) result.CloseStatus!;
                        switch (statusCode)
                        {
                            case 4003:
                                Config.Settings.Token = "";
                                Config.Settings.Write();
                                TShock.Log.ConsoleError("[CaiBotLite]服务器认证失败, 请重新绑定!");
                                TShock.Log.ConsoleError($"原因({statusCode}): {result.CloseStatusDescription}");
                                CaiBotLite.GenBindCode(null);
                                break;
                            default:
                                TShock.Log.ConsoleError("[CaiBotLite]Bot主动断开连接!");
                                TShock.Log.ConsoleError($"原因({statusCode}): {result.CloseStatusDescription}");
                                break;
                        }

                        break;
                    }

                    // 必须用累积后的完整帧数据：单帧上限仅 1024 字节，
                    // 大报文（排行 / 玩家列表 / 插件列表）会被拆成多帧，
                    // 若只取 buffer[0..result.Count] 会丢掉除最后一帧外的全部内容。
                    var receivedData = Encoding.UTF8.GetString(memoryStream.ToArray());
                    if (CaiBotLite.DebugMode)
                    {
                        TShock.Log.ConsoleInfo($"[CaiBotLite]收到BOT数据包: {receivedData}");
                    }

                    _ = Task.Run(() =>
                    {
                        try
                        {
                            CaiBotApi.HandleMessage(receivedData);
                        }
                        catch (Exception e)
                        {
                            TShock.Log.ConsoleError("[CaiBotLite]处理消息时发生错误: \n" +
                                                   $"{e}");
                        }
                       
                    });


                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleInfo("[CaiBotLite]Bot断开连接...");
                if (!_isStopWebsocket)
                {
                    TShock.Log.ConsoleError(ex.ToString());
                }
            }

            await Task.Delay(5000);
        }
    }
}