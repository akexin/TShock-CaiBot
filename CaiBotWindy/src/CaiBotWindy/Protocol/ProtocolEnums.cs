namespace CaiBotWindy.Protocol;

/// <summary>数据包语义方向。</summary>
public enum PackageDirection
{
    ToServer,
    ToBot,
}

/// <summary>
/// 数据包类型。取值顺序与名称对齐 TShock 侧适配插件 <c>CaiBotLite.Enums.PackageType</c>，
/// 线上以 snake_case 字符串传输。
/// </summary>
public enum PackageType
{
    Hello,
    Whitelist,
    PlayerList,
    Progress,
    LookBag,
    WorldFile,
    MapFile,
    MapImage,
    SelfKick,
    CallCommand,
    UnbindServer,
    Heartbeat,
    PluginList,
    RankData,
    ShopCondition,
    ShopBuy,
    Ping,
    ServerLog,
    ServerFile,
    Error,
    Unknown,
}

/// <summary>服务端类型，出现在 WebSocket 路径中。</summary>
public enum ServerType
{
    TShock,
    TModLoader,
    Bukkit,
}

/// <summary>白名单校验结果。</summary>
public enum WhitelistResult
{
    Accept,
    NeedLogin,

    /// <summary>角色名压根没有绑定记录。</summary>
    NotInWhitelist,

    /// <summary>
    /// 有绑定记录，但邮箱注册还没完成验证 —— 与「不在白名单」区分开，
    /// 因为给玩家的提示完全不同（一个让他去注册，一个让他去补验证码）。
    /// </summary>
    NotRegistered,

    InGroupBlacklist,
    InBotBlacklist,
    Unknown,
}

/// <summary>枚举与线上字符串的双向映射。</summary>
public static class ProtocolNames
{
    public static string ToWire(this PackageDirection value) => value switch
    {
        PackageDirection.ToServer => "to_server",
        _ => "to_bot",
    };

    public static bool TryParseDirection(string? text, out PackageDirection value)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "to_server":
                value = PackageDirection.ToServer;
                return true;
            case "to_bot":
                value = PackageDirection.ToBot;
                return true;
            default:
                value = PackageDirection.ToBot;
                return false;
        }
    }

    public static string ToWire(this PackageType value) => value switch
    {
        PackageType.Hello => "hello",
        PackageType.Whitelist => "whitelist",
        PackageType.PlayerList => "player_list",
        PackageType.Progress => "progress",
        PackageType.LookBag => "look_bag",
        PackageType.WorldFile => "world_file",
        PackageType.MapFile => "map_file",
        PackageType.MapImage => "map_image",
        PackageType.SelfKick => "self_kick",
        PackageType.CallCommand => "call_command",
        PackageType.UnbindServer => "unbind_server",
        PackageType.Heartbeat => "heartbeat",
        PackageType.PluginList => "plugin_list",
        PackageType.RankData => "rank_data",
        PackageType.ShopCondition => "shop_condition",
        PackageType.ShopBuy => "shop_buy",
        PackageType.Ping => "ping",
        PackageType.ServerLog => "server_log",
        PackageType.ServerFile => "server_file",
        PackageType.Error => "error",
        _ => "unknown",
    };

    public static PackageType ParseType(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "hello" => PackageType.Hello,
        "whitelist" => PackageType.Whitelist,
        "player_list" => PackageType.PlayerList,
        "progress" => PackageType.Progress,
        "look_bag" => PackageType.LookBag,
        "world_file" => PackageType.WorldFile,
        "map_file" => PackageType.MapFile,
        "map_image" => PackageType.MapImage,
        "self_kick" => PackageType.SelfKick,
        "call_command" => PackageType.CallCommand,
        "unbind_server" => PackageType.UnbindServer,
        "heartbeat" => PackageType.Heartbeat,
        "plugin_list" => PackageType.PluginList,
        "rank_data" => PackageType.RankData,
        "shop_condition" => PackageType.ShopCondition,
        "shop_buy" => PackageType.ShopBuy,
        "ping" => PackageType.Ping,
        "server_log" => PackageType.ServerLog,
        "server_file" => PackageType.ServerFile,
        "error" => PackageType.Error,
        _ => PackageType.Unknown,
    };

    public static string ToWire(this ServerType value) => value switch
    {
        ServerType.TModLoader => "tModLoader",
        ServerType.Bukkit => "bukkit",
        _ => "tshock",
    };

    public static bool TryParseServerType(string? text, out ServerType value)
    {
        switch (text?.Trim())
        {
            case "tshock":
                value = ServerType.TShock;
                return true;
            case "tModLoader":
            case "tmodloader":
                value = ServerType.TModLoader;
                return true;
            case "bukkit":
                value = ServerType.Bukkit;
                return true;
            default:
                value = ServerType.TShock;
                return false;
        }
    }

    /// <summary>服务端类型的中文显示名。</summary>
    public static string DisplayName(this ServerType value) => value switch
    {
        ServerType.TModLoader => "tModLoader",
        ServerType.Bukkit => "Bukkit",
        _ => "TShock",
    };

    public static string ToWire(this WhitelistResult value) => value switch
    {
        WhitelistResult.Accept => "accept",
        WhitelistResult.NeedLogin => "need_login",
        WhitelistResult.NotInWhitelist => "not_in_whitelist",
        WhitelistResult.NotRegistered => "not_registered",
        WhitelistResult.InGroupBlacklist => "in_group_blacklist",
        WhitelistResult.InBotBlacklist => "in_bot_blacklist",
        _ => "unknown",
    };

    public static WhitelistResult ParseWhitelistResult(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "accept" => WhitelistResult.Accept,
        "need_login" => WhitelistResult.NeedLogin,
        "not_in_whitelist" => WhitelistResult.NotInWhitelist,
        "not_registered" => WhitelistResult.NotRegistered,
        "in_group_blacklist" => WhitelistResult.InGroupBlacklist,
        "in_bot_blacklist" => WhitelistResult.InBotBlacklist,
        _ => WhitelistResult.Unknown,
    };

    /// <summary>白名单结果的中文文案（用于给玩家看的踢出原因）。</summary>
    public static string DisplayName(this WhitelistResult value) => value switch
    {
        WhitelistResult.Accept => "允许进入",
        WhitelistResult.NeedLogin => "需要先在机器人中完成登录",
        WhitelistResult.NotInWhitelist => "不在白名单中",
        WhitelistResult.NotRegistered => "邮箱注册尚未完成",
        WhitelistResult.InGroupBlacklist => "位于群黑名单中",
        WhitelistResult.InBotBlacklist => "位于机器人黑名单中",
        _ => "未知状态",
    };
}

/// <summary>各数据包类型的协议版本，取值与适配插件 <c>PackageTypeExtension.GetVersion</c> 一致。</summary>
public static class ProtocolVersions
{
    public const string Default = "2025.7.18";

    public static string For(PackageType type) => type switch
    {
        PackageType.UnbindServer => "2025.7.25",
        PackageType.Heartbeat => "2025.7.25",
        PackageType.RankData => "2025.7.25",
        PackageType.PluginList => "2025.7.25",
        PackageType.ShopBuy => "2025.7.25",
        PackageType.ShopCondition => "2025.7.25",
        PackageType.ServerFile => "2026.10.3.2",
        PackageType.Error => "2026.2.14",
        _ => Default,
    };
}
