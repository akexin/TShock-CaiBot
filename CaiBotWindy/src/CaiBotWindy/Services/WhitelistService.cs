using System.Security.Cryptography;
using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Net;
using CaiBotWindy.Protocol;
using Newtonsoft.Json.Linq;
using Windy.SDK;
using Windy.SDK.Adaptor;

namespace CaiBotWindy.Services;

/// <summary>
/// 白名单、设备授权与云黑校验。
///
/// <para><b>三重检测</b>（对应「IP 检测 + 设备检测 + 角色名检测」）：</para>
/// <list type="number">
///   <item><b>角色名绑定</b> —— 该角色名必须已经通过 <c>/添加白名单</c> 绑定到某个 QQ 账号，否则直接拒绝；</item>
///   <item><b>设备检测</b> —— 客户端 UUID 必须在该角色的历史设备里；</item>
///   <item><b>IP 检测</b> —— 登录 IP 必须在该角色的历史 IP 里。</item>
/// </list>
/// <para>后两项任一不符就要求人工确认，而不是直接放行 —— 账号被盗时，盗号者换台电脑或换个网络
/// 就会卡在确认这一步，而不是静默登进去。</para>
///
/// <para><b>确认流程</b>：机器人在群里推一条带「确认登录 / 拒绝登录」按钮的消息，
/// 群管理员或本人点击生效（按钮本质是发送 <c>/确认登录 &lt;验证码&gt;</c>），玩家重新连接即可进服。</para>
/// </summary>
public static class WhitelistService
{
    private const int LoginCodeLifetimeMinutes = 30;

    /// <summary>每个角色最多记住多少条历史设备 / IP。留太多会变相放宽检测，留太少老玩家会反复被拦。</summary>
    private const int HistoryLimit = 8;

    /// <summary>校验某个玩家的进服资格。</summary>
    public static WhitelistResult Evaluate(ServerSession session, JObject payload)
    {
        string playerName = payload.GetString("player_name");
        string playerIp = payload.GetString("player_ip");
        string playerUuid = payload.GetString("player_uuid");

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return WhitelistResult.NotInWhitelist;
        }

        // 沿父群链找到启用了白名单的群；没找到说明本群未启用白名单。
        GroupRecord? owner = DataStore.FindWhitelistOwner(session.GroupOpenId);
        if (owner is null)
        {
            return WhitelistResult.Accept;
        }

        GroupRecord? global = DataStore.FindGroup(DataStore.GlobalScope);

        // ── 云黑：全局优先于本群，四个维度都要查 ─────────────────────────────
        // 只封角色名的话换个名字就能回来；IP 与设备 UUID 才是真正拦得住人的门槛。
        if (global is not null && HitBlacklist(global, playerName, playerIp, playerUuid))
        {
            return WhitelistResult.InBotBlacklist;
        }

        if (HitBlacklist(owner, playerName, playerIp, playerUuid))
        {
            return WhitelistResult.InGroupBlacklist;
        }

        // ── ① 角色名检测：必须有绑定记录 ────────────────────────────────────
        UserRecord? user = DataStore.FindUserByPlayerName(playerName);
        if (user is null)
        {
            return WhitelistResult.NotInWhitelist;
        }

        // ── ② 注册检查：走了邮箱注册的，必须完成验证码验证 ────────────────────
        if (string.Equals(user.RegisterSource, "email", StringComparison.OrdinalIgnoreCase) && !user.EmailVerified)
        {
            return WhitelistResult.NotRegistered;
        }

        bool hasBaseline = !string.IsNullOrEmpty(user.RegisterIp) || !string.IsNullOrEmpty(user.RegisterUuid);

        // ── ③ 首次进服：把这次的 IP 与设备定为「注册基准」──────────────────────
        // 定基准之前先查占用 —— 一个 IP / 一台设备只能对应一个角色，
        // 否则一个人可以拿同一台机器批量开号。
        if (!hasBaseline)
        {
            (string? takenReason, string? takenNote) = FindOccupier(user, playerIp, playerUuid);
            if (takenReason is not null)
            {
                LoginAttempt taken = EnsureAttempt(user, playerName, playerIp, playerUuid, takenReason, takenNote ?? "");
                NotifyPending(session.GroupOpenId, taken);
                return WhitelistResult.NeedLogin;
            }

            user.RegisterIp = playerIp;
            user.RegisterUuid = playerUuid;
            Remember(user, playerIp, playerUuid);
            DataStore.UpsertUser(user);
            return WhitelistResult.Accept;
        }

        // ── ④ 历史环境直接放行 ──────────────────────────────────────────────
        // 判定依据是 Uuids / Ips 历史而不是单一基准 —— 管理员点「确认登录」后新环境会进历史，
        // 如果这里只跟基准比对，批准就等于没批：基准停在旧环境，玩家会被反复弹窗
        // （上线首日移动网络玩家连点四次确认仍进不来，就是这个原因）。
        bool knownDevice = Contains(user.Uuids, playerUuid);
        bool knownIp = Contains(user.Ips, playerIp);

        if (knownDevice && knownIp)
        {
            Remember(user, playerIp, playerUuid);
            DataStore.UpsertUser(user);
            return WhitelistResult.Accept;
        }

        // ── ⑤ 新设备或新网络 → 群里弹确认框，管理员或本人决定放不放行 ──────────
        string reason = !knownDevice && !knownIp ? "both" : !knownDevice ? "device" : "ip";
        LoginAttempt attempt = EnsureAttempt(user, playerName, playerIp, playerUuid, reason, "");
        NotifyPending(session.GroupOpenId, attempt);

        return WhitelistResult.NeedLogin;
    }

    /// <summary>
    /// 查这枚 IP / 设备名下的账号数是否已达上限（<see cref="PluginData.RegisterLimitPerIp"/>）。
    /// 返回 (原因码, 说明)，未超限则返回 (null, null)。
    /// </summary>
    private static (string? Reason, string? Note) FindOccupier(UserRecord user, string playerIp, string playerUuid)
    {
        int limit = Math.Max(1, DataStore.Data.RegisterLimitPerIp);

        List<string> ipOwners = DataStore.Data.Users
            .Where(item => item.RegisterIp == playerIp && item.OpenId != user.OpenId)
            .Select(item => item.PlayerName)
            .ToList();
        if (ipOwners.Count >= limit)
        {
            return ("ip-taken",
                $"该 IP 已注册 {ipOwners.Count} 个角色（上限 {limit}）：{string.Join("、", ipOwners.Take(3))}");
        }

        List<string> deviceOwners = DataStore.Data.Users
            .Where(item => item.RegisterUuid == playerUuid && item.OpenId != user.OpenId)
            .Select(item => item.PlayerName)
            .ToList();
        if (deviceOwners.Count >= limit)
        {
            return ("device-taken",
                $"该设备已注册 {deviceOwners.Count} 个角色（上限 {limit}）：{string.Join("、", deviceOwners.Take(3))}");
        }

        return (null, null);
    }

    /// <summary>批准某个验证码，把该设备与 IP 写入历史（下次同样环境就直接放行）。</summary>
    public static (bool Success, string Message) Approve(string code)
    {
        LoginAttempt? attempt = DataStore.FindLoginAttempt(code);
        if (attempt is null)
        {
            return (false, "没有找到该验证码，可能已过期或已被处理。");
        }

        if (DateTime.UtcNow - attempt.CreatedAtUtc > TimeSpan.FromMinutes(LoginCodeLifetimeMinutes))
        {
            DataStore.RemoveLoginAttempt(code);
            return (false, "该验证码已过期，请让玩家重新进服以生成新的验证码。");
        }

        UserRecord? user = DataStore.FindUser(attempt.OpenId);
        if (user is null)
        {
            DataStore.RemoveLoginAttempt(code);
            return (false, "该角色已不在白名单中。");
        }

        // 批准 = 认可这个新环境，把它设为新的注册基准 —— 否则基准停在旧环境，
        // 「一 IP / 一设备 N 个账号」的占用统计和后续判定都会算错。
        user.RegisterIp = attempt.Ip;
        user.RegisterUuid = attempt.Uuid;
        Remember(user, attempt.Ip, attempt.Uuid);
        DataStore.UpsertUser(user);
        DataStore.RemoveLoginAttempt(code);

        return (true,
            $"已批准 **{attempt.PlayerName}** 的登录。\n" +
            $"- IP：`{attempt.Ip}`\n" +
            "- 现在可以进服了。");
    }

    /// <summary>
    /// 拒绝某个验证码。
    /// <para><paramref name="blacklist"/> 为真时，连同该角色名 / IP / 设备一起写进全局黑名单
    /// —— 拒绝一个明显不是本人的登录请求时，通常不希望对方反复重试。</para>
    /// </summary>
    public static (bool Success, string Message) Reject(string code, bool blacklist = false)
    {
        LoginAttempt? attempt = DataStore.FindLoginAttempt(code);
        if (attempt is null)
        {
            return (false, "没有找到该验证码，可能已过期或已被处理。");
        }

        DataStore.RemoveLoginAttempt(code);

        if (!blacklist)
        {
            return (true, $"已拒绝 **{attempt.PlayerName}** 的本次登录。\n> 对方可再次进服重新发起申请。");
        }

        GroupRecord global = DataStore.GetOrCreateGlobal();
        int added = 0;
        added += AddUnique(global.Blacklist, attempt.PlayerName);
        added += AddUnique(global.BlacklistIps, attempt.Ip);
        added += AddUnique(global.BlacklistUuids, attempt.Uuid);
        DataStore.SaveServerChange();

        return (true,
            $"已拒绝 **{attempt.PlayerName}** 的登录，并写入全局黑名单（{added} 条记录）。\n" +
            $"- 角色名：{attempt.PlayerName}\n" +
            $"- IP：`{attempt.Ip}`\n" +
            "> 该角色名 / IP / 设备将无法进入任何已绑定本机器人的服务器。");
    }

    /// <summary>取某个用户待批准的登录申请。</summary>
    public static List<LoginAttempt> PendingAttempts(string openId)
    {
        return DataStore.Data.LoginAttempts
            .Where(item => item.OpenId == openId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToList();
    }

    /// <summary>把触发原因翻译成人话（管理员判断是不是本人时，这条信息最关键）。</summary>
    public static string DescribeReason(string reason)
    {
        return reason switch
        {
            "device" => "换了设备",
            "ip" => "换了网络",
            "both" => "换了设备 + 换了网络",
            "ip-taken" => "该 IP 已被其他角色注册",
            "device-taken" => "该设备已被其他角色注册",
            _ => "未授权设备",
        };
    }

    public static string DeviceId(string ip, string uuid)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{ip}|{uuid}"));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    // ── 内部实现 ────────────────────────────────────────────────────────────────

    private static LoginAttempt EnsureAttempt(
        UserRecord user, string name, string ip, string uuid, string reason, string note)
    {
        LoginAttempt? existing = DataStore.FindLoginAttemptByPlayer(name);
        bool valid = existing is not null &&
                     DateTime.UtcNow - existing.CreatedAtUtc <= TimeSpan.FromMinutes(LoginCodeLifetimeMinutes);

        if (valid)
        {
            // 同一角色反复重连：刷新上报内容，但**沿用原验证码**，
            // 否则群里会刷出一堆不同验证码的卡片，管理员不知道该点哪条。
            existing!.Ip = ip;
            existing.Uuid = uuid;
            existing.Reason = reason;
            existing.Note = note;
            DataStore.SaveServerChange();
            return existing;
        }

        LoginAttempt attempt = new()
        {
            OpenId = user.OpenId,
            PlayerName = name,
            Ip = ip,
            Uuid = uuid,
            Reason = reason,
            Note = note,
            Code = GenerateCode(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        DataStore.UpsertLoginAttempt(attempt, LoginCodeLifetimeMinutes);
        return attempt;
    }

    /// <summary>把待确认的登录请求推到群里（带「确认登录 / 拒绝登录」按钮）。</summary>
    private static void NotifyPending(string groupOpenId, LoginAttempt attempt)
    {
        if (attempt.Notified || string.IsNullOrEmpty(groupOpenId))
        {
            return;
        }

        attempt.Notified = true;
        DataStore.SaveServerChange();

        // 这里跑在 WebSocket 回包线程上，绝不能同步等待 QQ 接口 —— 否则这次白名单校验会超时。
        _ = Task.Run(async () =>
        {
            try
            {
                Adaptor? adaptor = App.Adaptor;
                if (adaptor is null)
                {
                    return;
                }

                await adaptor.SendMessage(
                    SendTarget.Group(groupOpenId),
                    new MessageContent()
                        .AddMarkdown(BuildLoginRequest(attempt))
                        .AddButton(MenuKit.Keyboard(
                            ("确认登录", $"/确认登录 {attempt.Code}"),
                            ("拒绝登录", $"/拒绝登录 {attempt.Code}"))));
            }
            catch (Exception ex)
            {
                Message.Yellow($"[登录确认] 推送确认消息失败（多半是主动消息配额限制）：{ex.Message}");
            }
        });
    }

    private static string BuildLoginRequest(LoginAttempt attempt)
    {
        System.Text.StringBuilder builder = new();
        builder.Append("# 🔐 登录验证\n");
        builder.Append($"- 玩家名称：**{attempt.PlayerName}**\n");
        builder.Append($"- 登录 IP：`{attempt.Ip}`\n");
        builder.Append($"- 触发原因：**{DescribeReason(attempt.Reason)}**\n");

        if (!string.IsNullOrEmpty(attempt.Note))
        {
            builder.Append($"- 说明：{attempt.Note}\n");
        }

        builder.Append($"- 申请时间：{attempt.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n");
        builder.Append($"- 验证码：`{attempt.Code}`\n\n");
        builder.Append("> ✅ 是本人操作 → 点「确认登录」\n");
        builder.Append("> ❌ 不是本人 → 点「拒绝登录」\n");
        builder.Append("> ⚠️ 请确认后再操作，验证码 30 分钟内有效");

        return builder.ToString();
    }

    /// <summary>记住本次登录的设备与 IP（去重 + 只保留最近若干条）。</summary>
    private static void Remember(UserRecord user, string ip, string uuid)
    {
        // DeviceId 是旧版单值字段，继续写入以兼容老数据；新逻辑走 Uuids / Ips 双历史。
        if (!string.IsNullOrEmpty(ip) || !string.IsNullOrEmpty(uuid))
        {
            user.DeviceId = DeviceId(ip, uuid);
        }

        if (!string.IsNullOrEmpty(uuid))
        {
            user.Uuids.RemoveAll(item => string.Equals(item, uuid, StringComparison.OrdinalIgnoreCase));
            user.Uuids.Add(uuid);
            while (user.Uuids.Count > HistoryLimit)
            {
                user.Uuids.RemoveAt(0);
            }
        }

        if (!string.IsNullOrEmpty(ip))
        {
            user.Ips.RemoveAll(item => item == ip);
            user.Ips.Add(ip);
            while (user.Ips.Count > HistoryLimit)
            {
                user.Ips.RemoveAt(0);
            }
        }
    }

    private static bool HitBlacklist(GroupRecord group, string name, string ip, string uuid)
    {
        return Contains(group.Blacklist, name) ||
               Contains(group.BlacklistIps, ip) ||
               Contains(group.BlacklistUuids, uuid);
    }

    private static bool Contains(IEnumerable<string> values, string target)
    {
        return !string.IsNullOrEmpty(target) &&
               values.Any(item => string.Equals(item, target, StringComparison.OrdinalIgnoreCase));
    }

    private static int AddUnique(List<string> list, string value)
    {
        if (string.IsNullOrEmpty(value) ||
            list.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        list.Add(value);
        return 1;
    }

    private static string GenerateCode()
    {
        return Random.Shared.Next(100000, 999999).ToString();
    }
}
