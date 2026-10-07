using CaiBotWindy.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windy.SDK;
using Windy.SDK.Utils;

namespace CaiBotWindy.Data;

/// <summary>持久化根对象，保存在 <c>Config/CaiBotWindy/store.json</c>。</summary>
public sealed class PluginData
{
    // 集合属性必须声明 Replace：Newtonsoft 默认把 JSON 数组「追加」到属性现有实例，
    // 若保留字段初始值会造成重复条目（重复加载时尤其明显）。
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<ServerRecord> Servers { get; set; } = [];

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<GroupRecord> Groups { get; set; } = [];

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<UserRecord> Users { get; set; } = [];

    /// <summary>未授权设备的登录申请（对应 CaiBot 的登录码流程）。</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<LoginAttempt> LoginAttempts { get; set; } = [];

    /// <summary>
    /// 待处理的入群申请。
    /// <para>必须落库：QQ 的审批接口要求带 <c>join_request_id</c>，不传会报
    /// <c>40103007 无效或已过期的审批令牌</c> —— 而按钮点击只回传一个固定的指令文本，
    /// 拿不到事件里的 id，只能在这里按 memberOpenId 反查。</para>
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<JoinRequestRecord> JoinRequests { get; set; } = [];

    /// <summary>
    /// 同一个注册基准 IP / 设备最多允许的账号数（默认 2）。
    /// 管理员可用 <c>/注册限制</c> 实时调整，改动即存。
    /// </summary>
    public int RegisterLimitPerIp { get; set; } = 2;
}

/// <summary>一次未授权设备的登录申请。</summary>
public sealed class LoginAttempt
{
    public string OpenId { get; set; } = "";

    public string PlayerName { get; set; } = "";

    public string Ip { get; set; } = "";

    public string Uuid { get; set; } = "";

    /// <summary>6 位登录验证码，管理员在群里点「确认」按钮批准（等价于发送 <c>/确认登录 &lt;验证码&gt;</c>）。</summary>
    public string Code { get; set; } = "";

    /// <summary>
    /// 触发本次确认的原因：<c>device</c>（换了设备）/ <c>ip</c>（换了网络）/ <c>both</c>（都换了）/
    /// <c>ip-taken</c>（该 IP 已被别的角色注册）/ <c>device-taken</c>（该设备已被别的角色注册）。
    /// </summary>
    public string Reason { get; set; } = "";

    /// <summary>补充说明（例如占用该 IP 的角色名），显示在确认卡片上供管理员判断。</summary>
    public string Note { get; set; } = "";

    /// <summary>确认消息是否已推送到群里 —— 玩家反复重连时不能每次都刷一条。</summary>
    public bool Notified { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>一条待处理的入群申请。</summary>
public sealed class JoinRequestRecord
{
    /// <summary>群 OpenID。</summary>
    public string GroupOpenId { get; set; } = "";

    /// <summary>申请人的 OpenID。</summary>
    public string MemberOpenId { get; set; } = "";

    /// <summary>QQ 审批接口需要的申请 ID（不传会报 40103007「无效或已过期的审批令牌」）。</summary>
    public string JoinRequestId { get; set; } = "";

    /// <summary>申请时的昵称，仅用于展示。</summary>
    public string UserName { get; set; } = "";

    /// <summary>收到申请的时间。</summary>
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>一个已绑定的泰拉瑞亚服务器。</summary>
public sealed class ServerRecord
{
    /// <summary>群 OpenID，绑定目标群。</summary>
    public string GroupOpenId { get; set; } = "";

    /// <summary>服务器地址（仅展示用，实际连接由服务端主动发起）。</summary>
    public string Ip { get; set; } = "";

    public int Port { get; set; }

    /// <summary>连接令牌，服务端通过 <c>/server/token/{code}</c> 换取。</summary>
    public string Token { get; set; } = "";

    /// <summary>绑定验证码（由 TShock 侧插件生成）。</summary>
    public string InitCode { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>验证码是否已被服务端换取过令牌。</summary>
    public bool Bound { get; set; }

    public Protocol.ServerType ServerType { get; set; } = Protocol.ServerType.TShock;

    /// <summary>hello 包上报的服务器名 / 世界名。</summary>
    public string ServerName { get; set; } = "";

    public string GameVersion { get; set; } = "";

    public string CoreVersion { get; set; } = "";

    public string PluginVersion { get; set; } = "";

    public string System { get; set; } = "";

    public bool EnableWhitelist { get; set; }

    public DateTime? LastSeenUtc { get; set; }

    /// <summary>群内展示用的序号（1 起）。</summary>
    [JsonIgnore]
    public int DisplayIndex { get; set; }
}

/// <summary>群级配置。</summary>
public sealed class GroupRecord
{
    public string GroupOpenId { get; set; } = "";

    /// <summary>父群 OpenID，用于跨群共享白名单（0 或空表示无父群）。</summary>
    public string ParentGroupOpenId { get; set; } = "";

    /// <summary>本群管理员 OpenID 列表。</summary>
    public List<string> Admins { get; set; } = [];

    /// <summary>群黑名单（玩家名）。</summary>
    public List<string> Blacklist { get; set; } = [];

    /// <summary>群黑名单（OpenID）。</summary>
    public List<string> BlacklistOpenIds { get; set; } = [];

    /// <summary>黑名单（IP）—— 云黑维度之一：封 IP 比封角色名更难绕过。</summary>
    public List<string> BlacklistIps { get; set; } = [];

    /// <summary>黑名单（设备 UUID）—— 云黑维度之一：换号不换客户端也照样拦得住。</summary>
    public List<string> BlacklistUuids { get; set; } = [];

    /// <summary>是否在在线列表里附带世界进度。</summary>
    public bool ShowProcessInPlayerList { get; set; } = true;

    /// <summary>白名单拦截时是否附带提示群号。</summary>
    public bool ShowGroupNumberInKick { get; set; } = true;

    /// <summary>是否允许本群使用远程指令。</summary>
    public bool AllowRemoteCommand { get; set; } = true;

    /// <summary>是否是白名单群（未通过校验的玩家会被拦下）。</summary>
    public bool EnableWhitelist { get; set; }

    /// <summary>机器人是否已通过 <c>群聊</c> 建立记录。</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>用户白名单绑定记录。</summary>
public sealed class UserRecord
{
    public string OpenId { get; set; } = "";

    public string PlayerName { get; set; } = "";

    public DateTime BoundAtUtc { get; set; } = DateTime.UtcNow;

    public int SignInStreak { get; set; }

    /// <summary>最近签到日期（yyyy-MM-dd，本地时区）。</summary>
    public string LastSignDate { get; set; } = "";

    public int TotalSignInDays { get; set; }

    public long Coins { get; set; }

    public string DeviceId { get; set; } = "";

    /// <summary>
    /// 已授权过的设备 UUID（只保留最近若干条，避免无限增长）。
    /// <para>与 <see cref="Ips"/> 分开记录，才能区分「换了设备」和「换了网络」—— 两者都要确认，
    /// 但提示文案不同，管理员看到原因才好判断是不是本人。</para>
    /// </summary>
    public List<string> Uuids { get; set; } = [];

    /// <summary>历史登录过的 IP（保留最近若干条）。</summary>
    public List<string> Ips { get; set; } = [];

    // ── 邮箱注册 ────────────────────────────────────────────────────────────────

    /// <summary>绑定的 QQ 邮箱（玩家自助注册时填写）。</summary>
    public string Email { get; set; } = "";

    /// <summary>邮箱是否已通过验证码验证。未验证的记录不允许进服。</summary>
    public bool EmailVerified { get; set; }

    /// <summary>待验证的验证码（注册流程的中间状态，验证通过后清空）。</summary>
    public string PendingCode { get; set; } = "";

    /// <summary>验证码过期时间。</summary>
    public DateTime? PendingCodeExpiresAtUtc { get; set; }

    /// <summary>
    /// 注册来源：<c>email</c>（玩家自助邮箱注册）/ <c>admin</c>（管理员手动添加）/
    /// <c>legacy</c>（升级前就存在的老记录）。
    /// </summary>
    public string RegisterSource { get; set; } = "";

    /// <summary>
    /// 该角色第一次成功进服时的 IP。用于「一个 IP 不能重复注册」的判定 ——
    /// 后续比对也以它为基准。
    /// </summary>
    public string RegisterIp { get; set; } = "";

    /// <summary>该角色第一次成功进服时的设备 UUID，用于「一个设备不能重复注册」的判定。</summary>
    public string RegisterUuid { get; set; } = "";
}

/// <summary>数据存储：读取 / 落盘 / 查询。</summary>
public static class DataStore
{
    private static readonly object SyncRoot = new();
    private static string filePath = "";
    private static PluginData data = new();

    /// <summary>全局作用域的伪群 ID，用于存放机器人级黑名单等跨群配置。</summary>
    public const string GlobalScope = "*";

    /// <summary>
    /// 保留的 store.json 备份份数。
    /// 落盘很频繁（每次进服校验都写），份数按「够回滚、不撑爆磁盘」取一个折中。
    /// </summary>
    public const int BackupKeepCount = 7;

    public static PluginData Data
    {
        get
        {
            lock (SyncRoot)
            {
                return data;
            }
        }
    }

    /// <summary>
    /// 载入数据。主文件损坏时自动回退到最近一份可用备份 ——
    /// 配置损坏最典型的后果是「机器人再也起不来」，这里必须兜住。
    /// </summary>
    public static void Load(string path)
    {
        lock (SyncRoot)
        {
            filePath = path;
            data = LoadOrDefault(path);
        }
    }

    /// <summary>
    /// 落盘。写入经 <see cref="AtomicFile"/>：先写临时文件再原子替换，替换前自动备份旧内容。
    ///
    /// <para>序列化在锁内完成（拿到一致快照），磁盘 IO 放在锁外 ——
    /// 避免磁盘卡顿拖慢白名单校验这类高频路径。</para>
    /// </summary>
    public static void Save()
    {
        string json;
        lock (SyncRoot)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            json = JsonConvert.SerializeObject(data, Formatting.Indented);
        }

        try
        {
            AtomicFile.Write(filePath, json, backup: true, keepBackups: BackupKeepCount);
        }
        catch (Exception ex)
        {
            // 写失败不抛出：内存里的改动还在，下次写入会重试。
            // 抛出去反而可能打断正在进行的白名单校验。
            Message.Yellow($"[CaiBotWindy] 保存数据失败（内存改动仍在，稍后重试）: {ex.Message}");
        }
    }

    private static PluginData LoadOrDefault(string path)
    {
        string? json = AtomicFile.ReadWithFallback(path, IsUsableJson);
        if (json is null)
        {
            return new PluginData();
        }

        try
        {
            return JsonConvert.DeserializeObject<PluginData>(json) ?? new PluginData();
        }
        catch (Exception ex)
        {
            Message.Yellow($"[CaiBotWindy] store.json 解析失败，已回退到空数据: {ex.Message}");
            return new PluginData();
        }
    }

    /// <summary>内容是否像一份能解析的 JSON 对象（用于决定要不要回退到备份）。</summary>
    private static bool IsUsableJson(string content)
    {
        try
        {
            JObject.Parse(content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>取出群记录，不存在则创建并落盘。</summary>
    public static GroupRecord GetOrCreateGroup(string groupOpenId)
    {
        lock (SyncRoot)
        {
            GroupRecord? group = data.Groups.FirstOrDefault(item => item.GroupOpenId == groupOpenId);
            if (group is not null)
            {
                return group;
            }

            group = new GroupRecord { GroupOpenId = groupOpenId };
            data.Groups.Add(group);
            Save();
            return group;
        }
    }

    public static GroupRecord? FindGroup(string groupOpenId)
    {
        lock (SyncRoot)
        {
            return data.Groups.FirstOrDefault(item => item.GroupOpenId == groupOpenId);
        }
    }

    /// <summary>取全局作用域记录（机器人级黑名单 / 设置），不存在则创建。</summary>
    public static GroupRecord GetOrCreateGlobal()
    {
        return GetOrCreateGroup(GlobalScope);
    }

    /// <summary>沿父群链向上查找，返回第一个启用了白名单的群（含自身）。</summary>
    public static GroupRecord? FindWhitelistOwner(string groupOpenId)
    {
        lock (SyncRoot)
        {
            HashSet<string> visited = new(StringComparer.Ordinal);
            string? current = groupOpenId;
            while (!string.IsNullOrEmpty(current) && visited.Add(current))
            {
                GroupRecord? group = data.Groups.FirstOrDefault(item => item.GroupOpenId == current);
                if (group is null)
                {
                    return null;
                }

                if (group.EnableWhitelist)
                {
                    return group;
                }

                current = group.ParentGroupOpenId;
            }

            return null;
        }
    }

    /// <summary>取某个群的直接父群；未绑定或父群记录不存在时返回 null。</summary>
    public static GroupRecord? FindParent(GroupRecord group)
    {
        if (string.IsNullOrEmpty(group.ParentGroupOpenId))
        {
            return null;
        }

        lock (SyncRoot)
        {
            return data.Groups.FirstOrDefault(item => item.GroupOpenId == group.ParentGroupOpenId);
        }
    }

    /// <summary>
    /// 服务器绑定的**有效范围**：自己的 + 祖先链上的父群 + 所有后代子群。
    ///
    /// <para>父群与子群看到的是同一批服务器 —— 父群绑一次，名下子群都能用；
    /// 子群自己绑的，父群也能查看与管理。</para>
    /// </summary>
    private static HashSet<string> ResolveServerScope(string groupOpenId)
    {
        HashSet<string> scope = [groupOpenId];

        // 向上沿父群链（限深，防止脏数据成环）。
        string cursor = groupOpenId;
        for (int depth = 0; depth < 8; depth++)
        {
            GroupRecord? group = data.Groups.FirstOrDefault(item => item.GroupOpenId == cursor);
            if (group is null || string.IsNullOrEmpty(group.ParentGroupOpenId))
            {
                break;
            }

            cursor = group.ParentGroupOpenId;
            if (!scope.Add(cursor))
            {
                break;
            }
        }

        // 向下展开所有层级的子群。
        bool grew = true;
        for (int depth = 0; grew && depth < 8; depth++)
        {
            grew = false;
            foreach (GroupRecord group in data.Groups.Where(item => scope.Contains(item.ParentGroupOpenId)).ToList())
            {
                if (scope.Add(group.GroupOpenId))
                {
                    grew = true;
                }
            }
        }

        return scope;
    }

    /// <summary>找出所有把 <paramref name="parentOpenId"/> 认作父群的子群。</summary>
    public static List<GroupRecord> GetChildGroups(string parentOpenId)
    {
        if (string.IsNullOrEmpty(parentOpenId))
        {
            return [];
        }

        lock (SyncRoot)
        {
            return data.Groups
                .Where(item => item.ParentGroupOpenId == parentOpenId)
                .OrderBy(item => item.GroupOpenId)
                .ToList();
        }
    }

    /// <summary>
    /// 配置的「归属群」：子群绑定了父群时，配置一律沿用父群的 ——
    /// 改设置、加黑名单都落在父群上，一次生效到它下面所有子群。
    /// </summary>
    public static GroupRecord GetConfigOwner(string groupOpenId)
    {
        GroupRecord? group = FindGroup(groupOpenId);
        if (group is not null && FindParent(group) is { } parent)
        {
            return parent;
        }

        return GetOrCreateGroup(groupOpenId);
    }

    public static UserRecord? FindUser(string openId)
    {
        lock (SyncRoot)
        {
            return data.Users.FirstOrDefault(item => item.OpenId == openId);
        }
    }

    public static UserRecord? FindUserByPlayerName(string playerName)
    {
        lock (SyncRoot)
        {
            return data.Users.FirstOrDefault(item =>
                string.Equals(item.PlayerName, playerName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>按邮箱查找绑定 —— 用于「一个邮箱只能注册一个角色」。</summary>
    public static UserRecord? FindUserByEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        lock (SyncRoot)
        {
            return data.Users.FirstOrDefault(item =>
                string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>找出把该 IP 作为注册基准的角色 —— 用于「一个 IP 不能重复注册」。</summary>
    public static UserRecord? FindUserByRegisterIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        lock (SyncRoot)
        {
            return data.Users.FirstOrDefault(item => item.RegisterIp == ip);
        }
    }

    /// <summary>找出把该设备作为注册基准的角色 —— 用于「一个设备不能重复注册」。</summary>
    public static UserRecord? FindUserByRegisterUuid(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
        {
            return null;
        }

        lock (SyncRoot)
        {
            return data.Users.FirstOrDefault(item => item.RegisterUuid == uuid);
        }
    }

    // ── 入群申请（审批必须带 join_request_id）────────────────────────────────────

    /// <summary>记录一条待审入群申请；同一群同一人的旧记录会被替换。</summary>
    public static void UpsertJoinRequest(JoinRequestRecord record)
    {
        lock (SyncRoot)
        {
            data.JoinRequests.RemoveAll(item =>
                item.GroupOpenId == record.GroupOpenId && item.MemberOpenId == record.MemberOpenId);
            data.JoinRequests.Add(record);

            // 只保留最近 7 天，避免长期运行无限增长。
            DateTime cutoff = DateTime.UtcNow.AddDays(-7);
            data.JoinRequests.RemoveAll(item => item.AppliedAtUtc < cutoff);
        }

        SaveServerChange();
    }

    /// <summary>按群 + 成员查待审申请 —— 按钮点击时靠它反查 <c>join_request_id</c>。</summary>
    public static JoinRequestRecord? FindJoinRequest(string groupOpenId, string memberOpenId)
    {
        lock (SyncRoot)
        {
            return data.JoinRequests.FirstOrDefault(item =>
                item.GroupOpenId == groupOpenId && item.MemberOpenId == memberOpenId);
        }
    }

    public static List<JoinRequestRecord> ListJoinRequests(string groupOpenId)
    {
        lock (SyncRoot)
        {
            return data.JoinRequests
                .Where(item => item.GroupOpenId == groupOpenId)
                .OrderBy(item => item.AppliedAtUtc)
                .ToList();
        }
    }

    /// <summary>审批完成后移除记录。</summary>
    public static void RemoveJoinRequest(string groupOpenId, string memberOpenId)
    {
        lock (SyncRoot)
        {
            data.JoinRequests.RemoveAll(item =>
                item.GroupOpenId == groupOpenId && item.MemberOpenId == memberOpenId);
        }

        SaveServerChange();
    }

    public static void UpsertUser(UserRecord record)
    {
        lock (SyncRoot)
        {
            UserRecord? existing = data.Users.FirstOrDefault(item => item.OpenId == record.OpenId);
            if (existing is null)
            {
                data.Users.Add(record);
            }
            else
            {
                existing.PlayerName = record.PlayerName;
                existing.BoundAtUtc = record.BoundAtUtc;
                existing.SignInStreak = record.SignInStreak;
                existing.LastSignDate = record.LastSignDate;
                existing.TotalSignInDays = record.TotalSignInDays;
                existing.Coins = record.Coins;
                existing.DeviceId = record.DeviceId;
                existing.Uuids = record.Uuids;
                existing.Ips = record.Ips;
                existing.Email = record.Email;
                existing.EmailVerified = record.EmailVerified;
                existing.PendingCode = record.PendingCode;
                existing.PendingCodeExpiresAtUtc = record.PendingCodeExpiresAtUtc;
                existing.RegisterSource = record.RegisterSource;
                existing.RegisterIp = record.RegisterIp;
                existing.RegisterUuid = record.RegisterUuid;
            }

            Save();
        }
    }

    public static bool RemoveUser(string openId)
    {
        lock (SyncRoot)
        {
            int removed = data.Users.RemoveAll(item => item.OpenId == openId);
            if (removed > 0)
            {
                Save();
                return true;
            }

            return false;
        }
    }

    /// <summary>取某个群下的服务器列表（含 1 起的展示序号）。</summary>
    public static List<ServerRecord> GetServers(string groupOpenId)
    {
        lock (SyncRoot)
        {
            // 子群与父群共享同一批服务器（范围见 ResolveServerScope）。
            HashSet<string> scope = ResolveServerScope(groupOpenId);
            List<ServerRecord> servers = data.Servers
                .Where(item => scope.Contains(item.GroupOpenId))
                .OrderBy(item => item.CreatedAtUtc)
                .ToList();

            // 序号在每次调用时按当前视图重排：父子群合并后依然连续、无歧义。
            for (int i = 0; i < servers.Count; i++)
            {
                servers[i].DisplayIndex = i + 1;
            }

            return servers;
        }
    }

    public static ServerRecord? FindServerByToken(string token)
    {
        lock (SyncRoot)
        {
            return data.Servers.FirstOrDefault(item => item.Token == token);
        }
    }

    public static ServerRecord? FindServerByCode(string code)
    {
        lock (SyncRoot)
        {
            return data.Servers.FirstOrDefault(item => item.InitCode == code);
        }
    }

    public static void AddOrReplaceServer(ServerRecord record)
    {
        lock (SyncRoot)
        {
            data.Servers.RemoveAll(item =>
                item.GroupOpenId == record.GroupOpenId &&
                item.Ip == record.Ip &&
                item.Port == record.Port);
            data.Servers.Add(record);
            Save();
        }
    }

    public static bool RemoveServer(ServerRecord record)
    {
        lock (SyncRoot)
        {
            int removed = data.Servers.RemoveAll(item => item.Token == record.Token);
            if (removed > 0)
            {
                Save();
                return true;
            }

            return false;
        }
    }

    /// <summary>修改服务器记录后落盘（记录对象为引用，直接改字段再调用本方法即可）。</summary>
    public static void SaveServerChange()
    {
        lock (SyncRoot)
        {
            Save();
        }
    }

    // ── 登录申请 ────────────────────────────────────────────────────────────────

    public static void UpsertLoginAttempt(LoginAttempt attempt, int lifetimeMinutes)
    {
        lock (SyncRoot)
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-lifetimeMinutes);
            data.LoginAttempts.RemoveAll(item => item.CreatedAtUtc < cutoff);
            data.LoginAttempts.RemoveAll(item => item.PlayerName == attempt.PlayerName);
            data.LoginAttempts.Add(attempt);
            Save();
        }
    }

    public static LoginAttempt? FindLoginAttempt(string code)
    {
        lock (SyncRoot)
        {
            return data.LoginAttempts.LastOrDefault(item => item.Code == code);
        }
    }

    public static LoginAttempt? FindLoginAttemptByPlayer(string playerName)
    {
        lock (SyncRoot)
        {
            return data.LoginAttempts.LastOrDefault(item =>
                string.Equals(item.PlayerName, playerName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static bool RemoveLoginAttempt(string code)
    {
        lock (SyncRoot)
        {
            int removed = data.LoginAttempts.RemoveAll(item => item.Code == code);
            if (removed > 0)
            {
                Save();
                return true;
            }

            return false;
        }
    }
}
