using CaiBotWindy.Data;
using CaiBotWindy.Protocol;
using CaiBotWindy.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 白名单状态机测试。
///
/// <para><b>为什么这是最值得测的一块</b>：它决定了「谁能进服」。九个分支里任何一个走错，
/// 要么把正常玩家挡在门外（玩家以为是服坏了），要么把开了小号的人放进来（服主以为白名单在生效）。
/// 两者都不会抛异常，只能靠用例把每种组合钉死。</para>
///
/// <para>测试全程不碰网络：出站消息走 <see cref="App"/> 的出站队列，适配器为 null 时静默丢弃。</para>
/// </summary>
public class WhitelistServiceTests
{
    private const string Group = "group-1";

    private static void Reset()
    {
        App.Config = new PluginConfig();
        App.Adaptor = null;
        DataStore.Load(Path.Combine(
            Path.GetTempPath(),
            "caibot-wl-" + Guid.NewGuid().ToString("N"),
            "store.json"));
    }

    private static void EnableWhitelist()
    {
        GroupRecord group = DataStore.GetOrCreateGroup(Group);
        group.EnableWhitelist = true;
        DataStore.SaveServerChange();
    }

    private static void AddUser(
        string openId,
        string playerName,
        string registerIp = "",
        string registerUuid = "",
        bool emailVerified = true,
        string registerSource = "admin",
        params string[] knownIps)
    {
        UserRecord user = new()
        {
            OpenId = openId,
            PlayerName = playerName,
            RegisterIp = registerIp,
            RegisterUuid = registerUuid,
            EmailVerified = emailVerified,
            RegisterSource = registerSource,
        };

        user.Ips.AddRange(knownIps);
        if (!string.IsNullOrEmpty(registerIp))
        {
            user.Ips.Add(registerIp);
        }

        if (!string.IsNullOrEmpty(registerUuid))
        {
            user.Uuids.Add(registerUuid);
        }

        DataStore.UpsertUser(user);
    }

    private static JObject Payload(string name, string ip = "1.2.3.4", string uuid = "uuid-1")
    {
        return new JObject
        {
            ["player_name"] = name,
            ["player_ip"] = ip,
            ["player_uuid"] = uuid,
        };
    }

    private static WhitelistResult Evaluate(string name, string ip = "1.2.3.4", string uuid = "uuid-1")
    {
        return WhitelistService.Evaluate(TestContext.Session("token-1", Group), Payload(name, ip, uuid));
    }

    // ── 开关与基础分支 ───────────────────────────────────────────────────────

    [Fact]
    public void EmptyPlayerName_IsRejected()
    {
        Reset();
        EnableWhitelist();

        Assert.Equal(WhitelistResult.NotInWhitelist, Evaluate(""));
    }

    [Fact]
    public void WhitelistDisabled_AcceptsEveryone()
    {
        // 群没开白名单时不能拦人 —— 这是「装了插件但没启用」的默认状态。
        Reset();

        Assert.Equal(WhitelistResult.Accept, Evaluate("anyone"));
    }

    [Fact]
    public void PlayerWithoutBinding_IsRejected()
    {
        Reset();
        EnableWhitelist();

        Assert.Equal(WhitelistResult.NotInWhitelist, Evaluate("stranger"));
    }

    [Fact]
    public void UnverifiedEmailRegistration_IsRejected()
    {
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice", registerSource: "email", emailVerified: false);

        Assert.Equal(WhitelistResult.NotRegistered, Evaluate("alice"));
    }

    [Fact]
    public void AdminAddedUser_SkipsEmailCheck()
    {
        // 管理员手动加的白名单没有邮箱流程，不能因为 EmailVerified=false 就被拦。
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice", registerSource: "admin", emailVerified: false);

        Assert.Equal(WhitelistResult.Accept, Evaluate("alice"));
    }

    // ── 云黑四维（角色 / IP / 设备 / 群） ────────────────────────────────────

    [Fact]
    public void GlobalBlacklistByName_BeatsEverything()
    {
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice");
        DataStore.GetOrCreateGroup(DataStore.GlobalScope).Blacklist.Add("alice");

        Assert.Equal(WhitelistResult.InBotBlacklist, Evaluate("alice"));
    }

    [Fact]
    public void GroupBlacklistByIp_IsRejected()
    {
        // 只封角色名换个名字就能回来，所以 IP 维度必须单独生效。
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice");
        DataStore.GetOrCreateGroup(Group).BlacklistIps.Add("1.2.3.4");

        Assert.Equal(WhitelistResult.InGroupBlacklist, Evaluate("alice"));
    }

    [Fact]
    public void GroupBlacklistByDevice_IsRejected()
    {
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice");
        DataStore.GetOrCreateGroup(Group).BlacklistUuids.Add("uuid-1");

        Assert.Equal(WhitelistResult.InGroupBlacklist, Evaluate("alice"));
    }

    [Fact]
    public void BlacklistMatch_IsCaseInsensitive()
    {
        Reset();
        EnableWhitelist();
        AddUser("open-1", "Alice");
        DataStore.GetOrCreateGroup(Group).Blacklist.Add("ALICE");

        Assert.Equal(WhitelistResult.InGroupBlacklist, Evaluate("Alice"));
    }

    // ── 首次进服：建立基准 ───────────────────────────────────────────────────

    [Fact]
    public void FirstJoin_WithNoBaseline_IsAcceptedAndRecordsBaseline()
    {
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice");

        Assert.Equal(WhitelistResult.Accept, Evaluate("alice"));

        UserRecord? stored = DataStore.FindUserByPlayerName("alice");
        Assert.Equal("1.2.3.4", stored!.RegisterIp);
        Assert.Equal("uuid-1", stored.RegisterUuid);
    }

    [Fact]
    public void DefaultRegisterLimit_AllowsTwoAccountsPerIp()
    {
        // 默认上限是「一个 IP 最多 2 个角色」（RegisterLimitPerIp = 2）；
        // 已有 1 个账号时第二个仍可进，第三个才会被拦。
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice", registerIp: "1.2.3.4", registerUuid: "uuid-old");
        AddUser("open-2", "bob");

        Assert.Equal(WhitelistResult.Accept, Evaluate("bob"));
    }

    [Fact]
    public void FirstJoin_WithOccupiedIp_NeedsManualApproval()
    {
        // 把上限收紧到「一个 IP 一个角色」后，第二个想用同一 IP 进服的必须人工确认。
        Reset();
        EnableWhitelist();
        DataStore.Data.RegisterLimitPerIp = 1;
        AddUser("open-1", "alice", registerIp: "1.2.3.4", registerUuid: "uuid-old");
        AddUser("open-2", "bob");

        Assert.Equal(WhitelistResult.NeedLogin, Evaluate("bob"));
    }

    // ── 历史环境判定 ─────────────────────────────────────────────────────────

    [Fact]
    public void KnownDeviceAndIp_IsAcceptedWithoutPrompt()
    {
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice", registerIp: "1.2.3.4", registerUuid: "uuid-1");

        Assert.Equal(WhitelistResult.Accept, Evaluate("alice"));
    }

    [Fact]
    public void SameDeviceDifferentIp_NeedsConfirmation()
    {
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice", registerIp: "1.2.3.4", registerUuid: "uuid-1");

        Assert.Equal(WhitelistResult.NeedLogin, Evaluate("alice", ip: "9.9.9.9", uuid: "uuid-1"));
    }

    [Fact]
    public void SameIpDifferentDevice_NeedsConfirmation()
    {
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice", registerIp: "1.2.3.4", registerUuid: "uuid-1");

        Assert.Equal(WhitelistResult.NeedLogin, Evaluate("alice", ip: "1.2.3.4", uuid: "uuid-new"));
    }

    [Fact]
    public void NewDeviceAndNewIp_NeedsConfirmation()
    {
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice", registerIp: "1.2.3.4", registerUuid: "uuid-1");

        Assert.Equal(WhitelistResult.NeedLogin, Evaluate("alice", ip: "9.9.9.9", uuid: "uuid-new"));
    }

    [Fact]
    public void PreviouslyApprovedEnvironment_IsRemembered()
    {
        // 管理员批准过一次之后，新环境会进历史；下次同样的环境必须直接放行，
        // 否则移动网络玩家每次换基站都要重新确认。
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice", registerIp: "1.2.3.4", registerUuid: "uuid-1", knownIps: ["9.9.9.9"]);

        UserRecord stored = DataStore.FindUserByPlayerName("alice")!;
        stored.Uuids.Add("uuid-2");
        DataStore.UpsertUser(stored);

        Assert.Equal(WhitelistResult.Accept, Evaluate("alice", ip: "9.9.9.9", uuid: "uuid-2"));
    }

    [Fact]
    public void RepeatedNewEnvironment_CreatesOnlyOnePendingAttempt()
    {
        // 玩家被弹窗后反复重连很常见，不能因此堆出十几条待批申请。
        Reset();
        EnableWhitelist();
        AddUser("open-1", "alice", registerIp: "1.2.3.4", registerUuid: "uuid-1");

        Evaluate("alice", ip: "9.9.9.9", uuid: "uuid-new");
        Evaluate("alice", ip: "9.9.9.9", uuid: "uuid-new");
        Evaluate("alice", ip: "9.9.9.9", uuid: "uuid-new");

        Assert.Single(DataStore.Data.LoginAttempts);
    }

    // ── 辅助方法 ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("device", "换了设备")]
    [InlineData("ip", "换了网络")]
    [InlineData("both", "换了设备 + 换了网络")]
    [InlineData("ip-taken", "该 IP 已被其他角色注册")]
    [InlineData("device-taken", "该设备已被其他角色注册")]
    [InlineData("unknown", "未授权设备")]
    public void DescribeReason_MapsEveryReason(string reason, string expected)
    {
        Assert.Equal(expected, WhitelistService.DescribeReason(reason));
    }

    [Fact]
    public void DeviceId_IsStableAndSensitiveToBothInputs()
    {
        string baseline = WhitelistService.DeviceId("1.2.3.4", "uuid-1");

        Assert.Equal(baseline, WhitelistService.DeviceId("1.2.3.4", "uuid-1"));
        Assert.NotEqual(baseline, WhitelistService.DeviceId("1.2.3.5", "uuid-1"));
        Assert.NotEqual(baseline, WhitelistService.DeviceId("1.2.3.4", "uuid-2"));
        Assert.Equal(16, baseline.Length);
        Assert.All(baseline, c => Assert.True(char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)));
    }
}
