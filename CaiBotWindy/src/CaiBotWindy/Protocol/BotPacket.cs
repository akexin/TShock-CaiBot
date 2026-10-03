using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CaiBotWindy.Protocol;

/// <summary>
/// Bot 与泰拉瑞亚服务端之间统一的 JSON 封包。
/// <code>
/// {
///   "version": "2025.7.18",
///   "direction": "to_server",
///   "type": "player_list",
///   "is_request": true,
///   "request_id": "…",
///   "payload": { }
/// }
/// </code>
/// </summary>
public sealed class BotPacket
{
    public string Version { get; set; } = ProtocolVersions.Default;

    public PackageDirection Direction { get; set; } = PackageDirection.ToServer;

    public PackageType Type { get; set; } = PackageType.Unknown;

    public bool IsRequest { get; set; }

    public string? RequestId { get; set; }

    public JObject Payload { get; set; } = new();

    /// <summary>构造一个「Bot -> Server」的请求包（<c>is_request = true</c>）。</summary>
    public static BotPacket Request(PackageType type, string requestId, JObject? payload = null)
    {
        return new BotPacket
        {
            Version = ProtocolVersions.For(type),
            Direction = PackageDirection.ToServer,
            Type = type,
            IsRequest = true,
            RequestId = requestId,
            Payload = payload ?? new JObject(),
        };
    }

    /// <summary>构造一个「Bot -> Server」的单向通知包（<c>is_request = false</c>）。</summary>
    public static BotPacket Notify(PackageType type, JObject? payload = null)
    {
        return new BotPacket
        {
            Version = ProtocolVersions.For(type),
            Direction = PackageDirection.ToServer,
            Type = type,
            IsRequest = false,
            RequestId = null,
            Payload = payload ?? new JObject(),
        };
    }

    public JObject ToJsonObject()
    {
        return new JObject
        {
            ["version"] = Version,
            ["direction"] = Direction.ToWire(),
            ["type"] = Type.ToWire(),
            ["is_request"] = IsRequest,
            // request_id 字段始终存在；非请求型显式写 JSON null（与适配插件的 Package 模型一致）。
            // 注意必须用 JValue.CreateNull()：直接赋 null 会让 JObject 根本不写入该键。
            ["request_id"] = IsRequest ? new JValue(RequestId) : JValue.CreateNull(),
            ["payload"] = Payload,
        };
    }

    /// <summary>序列化为紧凑 JSON。<b>不要</b>改成缩进格式：适配插件单帧缓冲区只有 1024 字节。</summary>
    public string ToJson() => ToJsonObject().ToString(Formatting.None);

    /// <summary>解析来自服务端的封包。格式非法时返回 <c>null</c>。</summary>
    public static BotPacket? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JObject? root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        BotPacket packet = new()
        {
            Version = root.Value<string>("version") ?? ProtocolVersions.Default,
            Type = ProtocolNames.ParseType(root.Value<string>("type")),
        };

        if (!ProtocolNames.TryParseDirection(root.Value<string>("direction"), out PackageDirection direction))
        {
            // 适配插件固定发送 to_bot；direction 缺失或异常时按 to_bot 处理。
            direction = PackageDirection.ToBot;
        }

        packet.Direction = direction;
        packet.IsRequest = root.Value<bool?>("is_request") ?? false;
        packet.RequestId = root.Value<string>("request_id");
        packet.Payload = root["payload"] as JObject ?? new JObject();

        if (!packet.IsRequest)
        {
            packet.RequestId = null;
        }

        return packet;
    }
}

/// <summary>安全读取 payload 字段的辅助方法（缺字段时返回默认值而不是抛异常）。</summary>
public static class PayloadReader
{
    public static string GetString(this JObject payload, string key, string fallback = "")
    {
        JToken? token = payload[key];
        if (token is null || token.Type == JTokenType.Null)
        {
            return fallback;
        }

        return token.Type == JTokenType.String ? token.Value<string>() ?? fallback : token.ToString();
    }

    public static bool GetBool(this JObject payload, string key, bool fallback = false)
    {
        JToken? token = payload[key];
        if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return fallback;
        }

        return token.Type switch
        {
            JTokenType.Boolean => token.Value<bool>(),
            JTokenType.Integer => token.Value<long>() != 0,
            JTokenType.String => bool.TryParse(token.Value<string>(), out bool parsed) ? parsed : fallback,
            _ => fallback,
        };
    }

    /// <summary>
    /// 读取「存在性」字段。适配插件对不同分支分别写入 <c>0</c> / <c>false</c> / <c>true</c>，
    /// 因此这里统一按「非 0 即真」处理。
    /// </summary>
    public static bool GetExists(this JObject payload, string key = "exist")
    {
        return GetBool(payload, key);
    }

    public static int GetInt(this JObject payload, string key, int fallback = 0)
    {
        JToken? token = payload[key];
        if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return fallback;
        }

        return token.Type switch
        {
            JTokenType.Integer => token.Value<int>(),
            JTokenType.Float => (int)token.Value<double>(),
            JTokenType.String => int.TryParse(token.Value<string>(), out int parsed) ? parsed : fallback,
            JTokenType.Boolean => token.Value<bool>() ? 1 : 0,
            _ => fallback,
        };
    }

    public static List<string> GetStringList(this JObject payload, string key)
    {
        List<string> result = [];
        if (payload[key] is not JArray array)
        {
            return result;
        }

        foreach (JToken item in array)
        {
            string? text = item.Type == JTokenType.String ? item.Value<string>() : item.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    public static List<int> GetIntList(this JObject payload, string key)
    {
        List<int> result = [];
        if (payload[key] is not JArray array)
        {
            return result;
        }

        foreach (JToken item in array)
        {
            if (item.Type == JTokenType.Integer)
            {
                result.Add(item.Value<int>());
            }
            else if (int.TryParse(item.ToString(), out int parsed))
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    /// <summary>读取 <c>[[物品ID, 数量], …]</c> 形式的背包格子数据。</summary>
    public static List<(int ItemId, int Stack)> GetItemSlots(this JObject payload, string key)
    {
        List<(int, int)> result = [];
        if (payload[key] is not JArray array)
        {
            return result;
        }

        foreach (JToken slot in array)
        {
            if (slot is not JArray pair || pair.Count == 0)
            {
                continue;
            }

            int itemId = pair[0].Type == JTokenType.Integer
                ? pair[0].Value<int>()
                : int.TryParse(pair[0].ToString(), out int parsedId) ? parsedId : 0;
            int stack = pair.Count > 1
                ? pair[1].Type == JTokenType.Integer
                    ? pair[1].Value<int>()
                    : int.TryParse(pair[1].ToString(), out int parsedStack) ? parsedStack : 0
                : 0;

            result.Add((itemId, stack));
        }

        return result;
    }

    /// <summary>读取 <c>{"键": true/false}</c> 形式的布尔映射。</summary>
    public static Dictionary<string, bool> GetBoolMap(this JObject payload, string key)
    {
        Dictionary<string, bool> result = new(StringComparer.OrdinalIgnoreCase);
        if (payload[key] is not JObject map)
        {
            return result;
        }

        foreach (KeyValuePair<string, JToken?> pair in map)
        {
            result[pair.Key] = pair.Value is not null && pair.Value.Type != JTokenType.Null && pair.Value.ToObject<bool>();
        }

        return result;
    }

    /// <summary>读取 <c>{"键": 数字}</c> 形式的计数映射。</summary>
    public static Dictionary<string, int> GetIntMap(this JObject payload, string key)
    {
        Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);
        if (payload[key] is not JObject map)
        {
            return result;
        }

        foreach (KeyValuePair<string, JToken?> pair in map)
        {
            if (pair.Value is null || pair.Value.Type == JTokenType.Null)
            {
                continue;
            }

            result[pair.Key] = pair.Value.Type == JTokenType.Integer
                ? pair.Value.Value<int>()
                : int.TryParse(pair.Value.ToString(), out int parsed) ? parsed : 0;
        }

        return result;
    }

    /// <summary>读取 <c>{"键": "文本"}</c> 形式的字符串映射。</summary>
    public static Dictionary<string, string> GetStringMap(this JObject payload, string key)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        if (payload[key] is not JObject map)
        {
            return result;
        }

        foreach (KeyValuePair<string, JToken?> pair in map)
        {
            if (pair.Value is null || pair.Value.Type == JTokenType.Null)
            {
                continue;
            }

            result[pair.Key] = pair.Value.Type == JTokenType.String
                ? pair.Value.Value<string>() ?? ""
                : pair.Value.ToString();
        }

        return result;
    }

    /// <summary>读取 <c>rank</c> 这类 <c>{title, rank_lines:{…}}</c> 结构。</summary>
    public static (string Title, Dictionary<string, string> Lines) GetRank(this JObject payload, string key = "rank")
    {
        if (payload[key] is not JObject rank)
        {
            return ("", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        return (rank.GetString("title"), rank.GetStringMap("rank_lines"));
    }

    /// <summary>读取 <c>plugin_list</c> 返回的插件数组（字段名为 PascalCase）。</summary>
    public static List<TerrariaPluginInfo> GetPluginList(this JObject payload, string key = "plugins")
    {
        List<TerrariaPluginInfo> result = [];
        if (payload[key] is not JArray array)
        {
            return result;
        }

        foreach (JToken item in array)
        {
            if (item is not JObject plugin)
            {
                continue;
            }

            result.Add(new TerrariaPluginInfo(
                plugin.GetString("Name"),
                plugin.GetString("Version"),
                plugin.GetString("Author"),
                plugin.GetString("Description")));
        }

        return result;
    }
}

/// <summary>服务端上报的插件/模组信息。</summary>
public sealed record TerrariaPluginInfo(string Name, string Version, string Author, string Description);
