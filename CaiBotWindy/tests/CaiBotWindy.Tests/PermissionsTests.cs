using CaiBotWindy.Data;
using CaiBotWindy.Services;
using Windy.SDK.Events;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 权限判定测试。
///
/// <para><b>为什么值得单独测</b>：这是唯一的安全边界 —— 判宽了，普通群员能取走
/// 服务端存档、踢人、改白名单；判严了，管理员自己都用不了。而且它由四条来源叠加
/// （配置所有者 / 全局管理员 / QQ 角色 / 群管理员白名单），任一条写错都不会报错。</para>
/// </summary>
public class PermissionsTests
{
    private static string NewTempFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "caibot-perm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "store.json");
    }

    /// <summary>每个用例都重置配置与存储，避免上一条的残留影响判定。</summary>
    private static void Reset()
    {
        App.Config = new PluginConfig();
        DataStore.Load(NewTempFile());
    }

    [Fact]
    public void EmptyAuthorId_IsNeverAdmin()
    {
        Reset();

        Assert.False(Permissions.IsAdmin(TestContext.Command("test", "", "group-1")));
    }

    [Fact]
    public void OwnerInConfig_IsAdmin()
    {
        Reset();
        App.Config.OwnerOpenIds = ["owner-1"];

        Assert.True(Permissions.IsAdmin(TestContext.Command("test", "owner-1", "group-1")));
    }

    [Fact]
    public void OperatorInConfig_IsAdmin()
    {
        Reset();
        App.Config.Operators = ["op-1"];

        Assert.True(Permissions.IsAdmin(TestContext.Command("test", "op-1", "group-1")));
    }

    [Theory]
    [InlineData(GroupMemberRole.Admin)]
    [InlineData(GroupMemberRole.Owner)]
    public void QqGroupAdminOrOwner_IsAdmin(GroupMemberRole role)
    {
        Reset();

        Assert.True(Permissions.IsAdmin(TestContext.Command("test", "qq-1", "group-1", role)));
    }

    [Fact]
    public void PlainMember_WithoutAnyGrant_IsNotAdmin()
    {
        Reset();

        Assert.False(Permissions.IsAdmin(TestContext.Command("test", "qq-1", "group-1", GroupMemberRole.Member)));
    }

    [Fact]
    public void GroupAdminWhitelist_IsAdmin()
    {
        Reset();
        GroupRecord group = DataStore.GetOrCreateGroup("group-1");
        group.Admins.Add("admin-1");
        DataStore.SaveServerChange();

        Assert.True(Permissions.IsAdmin(TestContext.Command("test", "admin-1", "group-1")));
    }

    [Fact]
    public void ParentGroupAdmin_IsAdminInChildGroup()
    {
        // 父群要能管它名下的子群，否则多群部署时还得在每个子群里重复配一遍管理员。
        Reset();
        GroupRecord parent = DataStore.GetOrCreateGroup("parent-1");
        parent.Admins.Add("admin-1");
        GroupRecord child = DataStore.GetOrCreateGroup("child-1");
        child.ParentGroupOpenId = "parent-1";
        DataStore.SaveServerChange();

        Assert.True(Permissions.IsAdmin(TestContext.Command("test", "admin-1", "child-1")));
    }

    [Fact]
    public void GroupAdminOfAnotherGroup_IsNotAdminHere()
    {
        // 跨群不该串权：A 群管理员在 B 群只是普通成员。
        Reset();
        GroupRecord other = DataStore.GetOrCreateGroup("other-group");
        other.Admins.Add("admin-1");
        DataStore.SaveServerChange();

        Assert.False(Permissions.IsAdmin(TestContext.Command("test", "admin-1", "group-1")));
    }

    [Fact]
    public void PrivateChat_WithoutConfigGrant_IsNotAdmin()
    {
        // 私聊里没有群上下文，只能靠配置或 QQ 角色。
        Reset();

        Assert.False(Permissions.IsAdmin(
            TestContext.Command("test", "qq-1", null, GroupMemberRole.Member)));
    }

    [Fact]
    public void IsOwner_OnlyMatchesOwnerList_NotOperators()
    {
        Reset();
        App.Config.OwnerOpenIds = ["owner-1"];
        App.Config.Operators = ["op-1"];

        Assert.True(Permissions.IsOwner(TestContext.Command("test", "owner-1", "group-1")));
        Assert.False(Permissions.IsOwner(TestContext.Command("test", "op-1", "group-1")));
        Assert.False(Permissions.IsOwner(TestContext.Command("test", "qq-admin", "group-1", GroupMemberRole.Admin)));
    }
}
