using Newtonsoft.Json;

namespace CaiBotWindy;

/// <summary>插件配置。文件位置：<c>Config/CaiBotWindy.json</c>（相对 Windy 运行目录）。</summary>
/// <remarks>
/// 注意：Newtonsoft 反序列化集合属性时默认「追加」到属性现有实例，而非替换，
/// 会让字段初始值再叠加一份配置内容。因此所有集合属性都必须显式声明
/// <see cref="ObjectCreationHandling.Replace"/>。
/// </remarks>
public sealed class PluginConfig
{
    /// <summary>HTTP / WebSocket 监听前缀。HttpListener 语法，可用 <c>+</c> 表示全部网卡。</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> ListenPrefixes { get; set; } = ["http://+:22338/"];

    /// <summary>
    /// 对外可访问的基地址（不带结尾斜杠）。用于把本地图片转成 QQ 能拉取的 URL。
    /// 例：<c>https://bot.example.com:22338</c>。留空则物品图标等图片不发送。
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>机器人 AppID，用于 QQ 侧域名校验接口 <c>/{AppId}.json</c>；留空则随机填 0。</summary>
    public string BotAppId { get; set; } = "";

    /// <summary>机器人所有者 OpenID 列表，拥有全部权限。</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> OwnerOpenIds { get; set; } = [];

    /// <summary>全局管理员 OpenID 列表（跨群生效）。</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> Operators { get; set; } = [];

    /// <summary>普通 RPC 请求超时（秒）。</summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>地图 / 世界文件类 RPC 请求超时（秒）。</summary>
    public int FileRequestTimeoutSeconds { get; set; } = 60;

    /// <summary>绑定验证码有效期（分钟）。验证码由 TShock 侧插件生成并打印在控制台。</summary>
    public int BindCodeLifetimeMinutes { get; set; } = 30;

    /// <summary>通过 <c>/download/{file_id}</c> 托管的临时文件保留时长（分钟）。</summary>
    public int DownloadFileLifetimeMinutes { get; set; } = 10;

    /// <summary>
    /// 单帧 JSON 软上限（字节）。TShock 侧适配插件接收缓冲区为 1024 字节且只解析最后一帧，
    /// 超过该值会导致对端解析失败，因此发出前会记警告。
    /// </summary>
    public int MaxOutgoingFrameBytes { get; set; } = 1000;

    /// <summary>单个群最多可绑定的服务器数量。</summary>
    public int MaxServersPerGroup { get; set; } = 5;

    /// <summary>单个机器人最多创建的指令面板数量（QQ 开放平台限制），用于校验菜单配置。</summary>
    public int MaxPanels { get; set; } = 20;

    /// <summary>图鉴数据目录（相对 Windy 运行目录）。</summary>
    public string DataDirectory { get; set; } = "Data";

    /// <summary>
    /// 图鉴素材目录（相对 Windy 运行目录），用于向 QQ 提供物品 / 生物图标。
    /// <c>/assets/{path}</c> 直接映射到该目录下的文件。
    /// </summary>
    public string AssetDirectory { get; set; } = "Asserts";

    /// <summary>数据存储目录（相对 Windy 运行目录）。</summary>
    public string StorageDirectory { get; set; } = "Config/CaiBotWindy";

    /// <summary>
    /// 入群申请的审核策略。
    /// <list type="bullet">
    ///   <item><c>auto</c>（默认）—— 命中云黑自动拒绝并拉黑，其余自动通过；</item>
    ///   <item><c>manual</c> —— 机器人不自动处理，一律转发到群里等管理员。</item>
    ///   <item><c>off</c> —— 完全不介入，交给 QQ 群自身的审核设置。</item>
    /// </list>
    /// </summary>
    public string GroupJoinReview { get; set; } = "auto";

    /// <summary>自动处理入群申请后是否往群里发一条结果通知。默认关闭（只写控制台，不打扰管理员）。</summary>
    public bool GroupJoinNotify { get; set; }

    /// <summary>注册验证码的邮件发送配置。留空则注册功能自动降级为「不需要验证码」。</summary>
    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>是否在控制台打印收到的每个数据包（排障用）。</summary>
    public bool Debug { get; set; }
}

/// <summary>
/// 注册验证码的发件邮箱配置。
///
/// <para><b>关于 QQ 邮箱</b>：<c>Password</c> 填的不是 QQ 密码，而是「SMTP 授权码」——
/// 要去 QQ 邮箱 → 设置 → 账户 → 开启 SMTP 服务 后生成的那串 16 位字符。
/// 端口用 <b>587</b>（STARTTLS）：<c>SmtpClient</c> 不支持 465 那种一上来就握手的隐式 SSL，
/// 用 465 会直接卡住直到超时。</para>
/// </summary>
public sealed class SmtpSettings
{
    /// <summary>是否启用邮件发送。关闭时注册流程会跳过验证码环节。</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "smtp.qq.com";

    /// <summary>587 = STARTTLS（推荐）；465 是隐式 SSL，SmtpClient 不支持。</summary>
    public int Port { get; set; } = 587;

    /// <summary>发件邮箱，例如 <c>123456789@qq.com</c>。</summary>
    public string User { get; set; } = "";

    /// <summary>SMTP 授权码（不是 QQ 密码）。</summary>
    public string Password { get; set; } = "";

    /// <summary>收件人看到的发件人名称。</summary>
    public string FromName { get; set; } = "泰拉瑞亚服务器";

    /// <summary>是否配置完整、可以真正发信。</summary>
    [JsonIgnore]
    public bool Ready => Enabled &&
                         !string.IsNullOrWhiteSpace(Host) &&
                         !string.IsNullOrWhiteSpace(User) &&
                         !string.IsNullOrWhiteSpace(Password) &&
                         Port > 0;
}
