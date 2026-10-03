using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using TShockAPI;
namespace CaiBotLite;

public class Config
{
    private static string ConfigPath = Path.Combine(TShock.SavePath, "CaiBotLite.json");
    public static Config Settings = new ();

    [JsonProperty("白名单开关")]
    public bool WhiteList = true;

    /// <summary>
    /// Bot 服务端地址，形如 <c>主机:端口</c>（不要带 http:// 或 https:// 前缀）。
    /// 官方服务为 <c>api.terraria.ink:22338</c>；自建 Bot 时改为你的地址，例如 <c>127.0.0.1:22338</c>。
    /// </summary>
    [JsonProperty("机器人服务端地址")]
    public string BotServerUrl = "api.terraria.ink:22338";

    /// <summary>
    /// 是否使用 TLS 加密连接。官方服务必须为 true；
    /// 自建 Bot 的 HttpListener 不提供 TLS，请设为 false 以走 http / ws 明文协议。
    /// </summary>
    [JsonProperty("使用TLS加密")]
    public bool UseTls = true;

    /// <summary>
    /// 固定绑定码。非 0 时使用该 8 位数字作为服务器绑定码，
    /// 便于自动化部署（无需从控制台日志中抓取随机码）。0 表示每次随机生成。
    /// </summary>
    [JsonProperty("固定绑定码")]
    public int BindCode;

    [JsonProperty("密钥")]
    public string Token = "";

    [JsonProperty("群OpenID")]
    public string GroupOpenId = "114514";

    [JsonProperty("在线显示进度")]
    public bool ShowProcessInPlayerList = true;

    [JsonProperty("商店分组标签")]
    public string ShopTag = "生存服";

    [JsonProperty("白名单拦截提示的群号")]
    public long GroupNumber;

    /// <summary>背包物品监控规则：持有量达到阈值就通过 ServerLog 包上报机器人广播到群聊。</summary>
    [JsonProperty("物品监控")]
    public List<ItemMonitorRule> ItemMonitors = new();


    /// <summary>
    /// 将配置文件写入硬盘
    /// </summary>
    internal void Write()
    {
        using FileStream fileStream = new (ConfigPath, FileMode.Create, FileAccess.Write, FileShare.Write);
        using StreamWriter streamWriter = new (fileStream);
        streamWriter.Write(JsonConvert.SerializeObject(this, JsonSettings));
    }

    /// <summary>
    /// 从硬盘读取配置文件
    /// </summary>
    internal void Read()
    {
        Config result;
        if (!File.Exists(ConfigPath))
        {
            result = new Config();
            result.Write();
        }
        else
        {
            using FileStream fileStream = new (ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using StreamReader streamReader = new (fileStream);
            result = JsonConvert.DeserializeObject<Config>(streamReader.ReadToEnd(), JsonSettings)!;
        }

        Settings = result;
    }

    private static readonly JsonSerializerSettings JsonSettings = new () { Formatting = Formatting.Indented, ObjectCreationHandling = ObjectCreationHandling.Replace };
}

/// <summary>一条背包监控规则：某物品的持有量达到 <see cref="Count"/> 时上报。</summary>
public class ItemMonitorRule
{
    [JsonProperty("物品ID")]
    public int ItemId;

    [JsonProperty("数量阈值")]
    public int Count;
}