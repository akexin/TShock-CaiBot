using System.Security.Cryptography;
using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Net;
using CaiBotWindy.Protocol;
using Newtonsoft.Json.Linq;

namespace CaiBotWindy.Services;

/// <summary>
/// 白名单与设备授权。
/// <para>授权流程：</para>
/// <list type="number">
///   <item>玩家进服 → 服务端上报校验 → 设备不匹配时返回 <c>need_login</c>，玩家被踢并提示在群内发送 <c>/登录</c></item>
///   <item>玩家在群里发送 <c>/登录</c> → 机器人列出待批准的设备（含 6 位验证码）</item>
///   <item>玩家在群里发送 <c>/登录 &lt;验证码&gt;</c> → 设备写入白名单，玩家可正常进服</item>
/// </list>
/// </summary>
public static class WhitelistService
{
    private const int LoginCodeLifetimeMinutes = 30;

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

        // 机器人级全局黑名单优先于一切。
        if (global is not null && ContainsName(global.Blacklist, playerName))
        {
            return WhitelistResult.InBotBlacklist;
        }

        if (ContainsName(owner.Blacklist, playerName))
        {
            return WhitelistResult.InGroupBlacklist;
        }

        UserRecord? user = DataStore.FindUserByPlayerName(playerName);
        if (user is null)
        {
            return WhitelistResult.NotInWhitelist;
        }

        string deviceId = DeviceId(playerIp, playerUuid);
        if (string.IsNullOrEmpty(user.DeviceId) || user.DeviceId == deviceId)
        {
            if (string.IsNullOrEmpty(user.DeviceId))
            {
                user.DeviceId = deviceId;
                DataStore.UpsertUser(user);
            }

            return WhitelistResult.Accept;
        }

        // 设备不匹配 → 生成 / 刷新登录验证码。
        LoginAttempt? existing = DataStore.FindLoginAttemptByPlayer(playerName);
        if (existing is null || DateTime.UtcNow - existing.CreatedAtUtc > TimeSpan.FromMinutes(LoginCodeLifetimeMinutes))
        {
            DataStore.UpsertLoginAttempt(new LoginAttempt
            {
                OpenId = user.OpenId,
                PlayerName = playerName,
                Ip = playerIp,
                Uuid = playerUuid,
                Code = GenerateCode(),
                CreatedAtUtc = DateTime.UtcNow,
            }, LoginCodeLifetimeMinutes);
        }

        return WhitelistResult.NeedLogin;
    }

    /// <summary>批准某个验证码，把对应设备写入白名单。</summary>
    public static (bool Success, string Message) Approve(string code, string currentServerGroupOpenId)
    {
        LoginAttempt? attempt = DataStore.FindLoginAttempt(code);
        if (attempt is null)
        {
            return (false, "没有找到该验证码，可能已过期或已被使用。");
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
            return (false, "该玩家已不在白名单中。");
        }

        user.DeviceId = DeviceId(attempt.Ip, attempt.Uuid);
        DataStore.UpsertUser(user);
        DataStore.RemoveLoginAttempt(code);

        return (true,
            $"已批准 **{attempt.PlayerName}** 的新设备登录。\n" +
            $"- IP：{attempt.Ip}\n" +
            $"- 现在可以进服了。");
    }

    /// <summary>取某个用户待批准的登录申请。</summary>
    public static List<LoginAttempt> PendingAttempts(string openId)
    {
        return DataStore.Data.LoginAttempts
            .Where(item => item.OpenId == openId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToList();
    }

    public static string DeviceId(string ip, string uuid)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{ip}|{uuid}"));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static bool ContainsName(IEnumerable<string> names, string playerName)
    {
        return names.Any(name => string.Equals(name, playerName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateCode()
    {
        return Random.Shared.Next(100000, 999999).ToString();
    }
}
