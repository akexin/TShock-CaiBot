using CaiBotWindy.Data;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 群关系解析的测试：父子链、配置归属、环检测。
///
/// <para><b>环检测为什么值得单独测</b>：父子群是链式结构，配置出错（A 认 B 做父、B 又认 A 做父）
/// 就会成环。遍历一旦没做防护就会死循环 —— 而它发生在消息处理路径上，
/// 表现是机器人「卡住不回话」，很难联想到是配置问题。</para>
/// </summary>
public class DataStoreTests
{
    /// <summary>每个用例用一份独立的临时存储，避免静态状态互相污染。</summary>
    private static string NewTempFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "caibot-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "store.json");
    }

    [Fact]
    public async Task CyclicParentChain_DoesNotHangServerLookup()
    {
        DataStore.Load(NewTempFile());

        GroupRecord a = DataStore.GetOrCreateGroup("A");
        GroupRecord b = DataStore.GetOrCreateGroup("B");
        a.ParentGroupOpenId = "B";
        b.ParentGroupOpenId = "A";
        DataStore.SaveServerChange();

        Assert.True(
            await CompletesWithin(() => DataStore.GetServers("A"), TimeSpan.FromSeconds(5)),
            "环形父子绑定导致 GetServers 未能返回（疑似死循环）");
    }

    [Fact]
    public async Task CyclicParentChain_DoesNotHangWhitelistLookup()
    {
        DataStore.Load(NewTempFile());

        GroupRecord a = DataStore.GetOrCreateGroup("A");
        GroupRecord b = DataStore.GetOrCreateGroup("B");
        a.ParentGroupOpenId = "B";
        b.ParentGroupOpenId = "A";
        a.EnableWhitelist = true;
        DataStore.SaveServerChange();

        Assert.True(
            await CompletesWithin(() => DataStore.FindWhitelistOwner("B"), TimeSpan.FromSeconds(5)),
            "环形父子绑定导致 FindWhitelistOwner 未能返回（疑似死循环）");
    }

    /// <summary>
    /// 在限定时间内跑完某个操作并返回「是否跑完」。
    ///
    /// <para><b>为什么不用 <c>Task.Wait</c></b>：死循环的场景下必须能得出「没跑完」
    /// 这个结论，而不是把测试线程一起卡住。用 <c>WhenAny</c> 等它或等超时，
    /// 谁先完成都拿得到结果。</para>
    /// </summary>
    private static async Task<bool> CompletesWithin<T>(Func<T> operation, TimeSpan timeout)
    {
        Task<T> task = Task.Run(operation);
        Task finished = await Task.WhenAny(task, Task.Delay(timeout));
        return ReferenceEquals(finished, task);
    }

    [Fact]
    public void GetConfigOwner_ReturnsParent_WhenGroupHasParent()
    {
        DataStore.Load(NewTempFile());

        DataStore.GetOrCreateGroup("P");
        GroupRecord child = DataStore.GetOrCreateGroup("C");
        child.ParentGroupOpenId = "P";
        DataStore.SaveServerChange();

        // 子群的配置归父群管：改设置、加黑名单都落在父群上，一次生效到所有子群。
        Assert.Equal("P", DataStore.GetConfigOwner("C").GroupOpenId);
    }

    [Fact]
    public void GetConfigOwner_ReturnsSelf_WhenGroupHasNoParent()
    {
        DataStore.Load(NewTempFile());

        Assert.Equal("Solo", DataStore.GetConfigOwner("Solo").GroupOpenId);
    }

    [Fact]
    public void GetChildGroups_ListsDirectChildrenOnly()
    {
        DataStore.Load(NewTempFile());

        DataStore.GetOrCreateGroup("P");
        GroupRecord child = DataStore.GetOrCreateGroup("C");
        child.ParentGroupOpenId = "P";
        GroupRecord grandChild = DataStore.GetOrCreateGroup("G");
        grandChild.ParentGroupOpenId = "C";
        DataStore.SaveServerChange();

        List<GroupRecord> children = DataStore.GetChildGroups("P");

        Assert.Single(children);
        Assert.Equal("C", children[0].GroupOpenId);
    }

    [Fact]
    public void FindWhitelistOwner_WalksUpUntilEnabled()
    {
        DataStore.Load(NewTempFile());

        GroupRecord parent = DataStore.GetOrCreateGroup("P");
        parent.EnableWhitelist = true;
        GroupRecord child = DataStore.GetOrCreateGroup("C");
        child.ParentGroupOpenId = "P";
        DataStore.SaveServerChange();

        GroupRecord? owner = DataStore.FindWhitelistOwner("C");

        Assert.NotNull(owner);
        Assert.Equal("P", owner.GroupOpenId);
    }

    [Fact]
    public void FindWhitelistOwner_ReturnsSelf_WhenSelfEnabled()
    {
        DataStore.Load(NewTempFile());

        GroupRecord group = DataStore.GetOrCreateGroup("Self");
        group.EnableWhitelist = true;
        DataStore.SaveServerChange();

        Assert.Equal("Self", DataStore.FindWhitelistOwner("Self")?.GroupOpenId);
    }

    [Fact]
    public void FindWhitelistOwner_ReturnsNull_WhenNobodyEnabled()
    {
        DataStore.Load(NewTempFile());

        GroupRecord group = DataStore.GetOrCreateGroup("Plain");
        group.EnableWhitelist = false;
        DataStore.SaveServerChange();

        Assert.Null(DataStore.FindWhitelistOwner("Plain"));
    }

    [Fact]
    public void GetServers_ParentAndChildShareTheSameSet()
    {
        DataStore.Load(NewTempFile());

        DataStore.GetOrCreateGroup("P");
        GroupRecord child = DataStore.GetOrCreateGroup("C");
        child.ParentGroupOpenId = "P";
        DataStore.SaveServerChange();

        DataStore.AddOrReplaceServer(new ServerRecord
        {
            GroupOpenId = "P",
            Ip = "127.0.0.1",
            Port = 7777,
            Token = "token-p",
            ServerName = "父群的服务器",
        });

        // 父群绑一次，子群也能看到 —— 这是父子联动的核心语义。
        Assert.Single(DataStore.GetServers("P"));
        Assert.Single(DataStore.GetServers("C"));
    }

    [Fact]
    public void GetServers_AssignsSequentialDisplayIndex()
    {
        DataStore.Load(NewTempFile());

        for (int i = 1; i <= 3; i++)
        {
            DataStore.AddOrReplaceServer(new ServerRecord
            {
                GroupOpenId = "G",
                Ip = $"10.0.0.{i}",
                Port = 7777 + i,
                Token = $"token-{i}",
            });
        }

        List<ServerRecord> servers = DataStore.GetServers("G");

        Assert.Equal(3, servers.Count);
        Assert.Equal([1, 2, 3], servers.Select(item => item.DisplayIndex).ToArray());
    }
}
