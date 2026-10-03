using Newtonsoft.Json;
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

    /// <summary>触发本次确认的原因：<c>device</c>（换了设备）/ <c>ip</c>（换了网络）/ <c>both</c>（都换了）。</summary>
    public string Reason { get; set; } = "";

    /// <summary>确认消息是否已推送到群里 —— 玩家反复重连时不能每次都刷一条。</summary>
    public bool Notified { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
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
}

/// <summary>数据存储：读取 / 落盘 / 查询。</summary>
public static class DataStore
{
    private static readonly object SyncRoot = new();
    private static string filePath = "";
    private static PluginData data = new();

    /// <summary>全局作用域的伪群 ID，用于存放机器人级黑名单等跨群配置。</summary>
    public const string GlobalScope = "*";

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

    public static void Load(string path)
    {
        lock (SyncRoot)
        {
            filePath = path;
            data = JsonTool.Create<PluginData>(path).InitContent(new PluginData()).Read().Content ?? new PluginData();
        }
    }

    public static void Save()
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            JsonTool.Create<PluginData>(filePath).InitContent(data).Write();
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
            List<ServerRecord> servers = data.Servers
                .Where(item => item.GroupOpenId == groupOpenId)
                .OrderBy(item => item.CreatedAtUtc)
                .ToList();

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
