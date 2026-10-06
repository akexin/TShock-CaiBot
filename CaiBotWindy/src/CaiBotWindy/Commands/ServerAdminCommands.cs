using CaiBotWindy.Data;
using CaiBotWindy.Services;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>服务器绑定管理：添加 / 修改 / 删除（对应 CaiBotLite 的服务器管理菜单）。</summary>
public static class ServerAdminCommands
{
    [Command("添加服务器", "绑定一台服务器", MessageScene.Group, "tjfwq", "addserver")]
    [Command("添加服务器", "绑定一台服务器", MessageScene.GroupAt, "tjfwq", "addserver")]
    public static async Task AddServerAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return;
        }

        if (!args.Require(3))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 添加服务器\n" +
                "> 用法：" + MenuKit.CmdInput("/添加服务器 ", "添加服务器 <IP> <端口> <绑定码>") + "\n" +
                "> 绑定码在**服务器控制台**中查看，TShock 侧的适配插件会打印：\n" +
                "> `[CaiBotLite] 您的服务器绑定码为: 01234567`\n" +
                "> 若没有看到，可在游戏内执行 `/cbl code` 重新生成。");
            return;
        }

        string ip = args.GetOrDefault(0);
        string portText = args.GetOrDefault(1);
        string code = args.GetOrDefault(2);

        if (!int.TryParse(portText, out int port) || port is <= 0 or > 65535)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 端口无效，应为 1-65535 之间的整数。");
            return;
        }

        if (!long.TryParse(code, out _) || code.Length < 4)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 绑定码无效。绑定码是服务器控制台打印的数字串。");
            return;
        }

        List<ServerRecord> existing = DataStore.GetServers(groupOpenId);
        if (existing.Count >= App.Config.MaxServersPerGroup)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# ⛔ 本群最多绑定 {App.Config.MaxServersPerGroup} 台服务器，请先解绑不用的。");
            return;
        }

        ServerRecord record = new()
        {
            GroupOpenId = groupOpenId,
            Ip = ip,
            Port = port,
            InitCode = code,
            Token = Guid.NewGuid().ToString(),
            Bound = false,
            CreatedAtUtc = DateTime.UtcNow,
        };

        DataStore.AddOrReplaceServer(record);

        await CommandHelpers.ReplyAsync(args,
            "# ✅ 服务器已登记\n" +
            $"- 地址：`{ip}:{port}`\n" +
            $"- 绑定码：`{code}`\n" +
            $"- 有效期：{App.Config.BindCodeLifetimeMinutes} 分钟\n\n" +
            "> 服务端插件每 10 秒会向机器人换取连接令牌，成功后本群即可使用查询指令。\n" +
            "> 若长时间没有上线，请确认服务端控制台已出现「Bot连接成功」。");
    }

    [Command("修改服务器", "修改服务器地址端口", MessageScene.Group, "xgfwq", "editserver")]
    [Command("修改服务器", "修改服务器地址端口", MessageScene.GroupAt, "xgfwq", "editserver")]
    public static async Task EditServerAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return;
        }

        if (!args.Require(3) || !args.TryGetInt(0, out int index) ||
            !int.TryParse(args.GetOrDefault(2), out int port) || port is <= 0 or > 65535)
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 修改服务器\n> 用法：" + MenuKit.CmdInput("/修改服务器 ", "修改服务器 <序号> <IP> <端口>"));
            return;
        }

        ServerRecord? target = DataStore.GetServers(groupOpenId)
            .FirstOrDefault(item => item.DisplayIndex == index);

        if (target is null)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 序号 {index} 不存在，请用「服务器列表」查看。");
            return;
        }

        target.Ip = args.GetOrDefault(1);
        target.Port = port;
        DataStore.SaveServerChange();

        await CommandHelpers.ReplyAsync(args,
            $"# ✅ 服务器 {index} 已更新\n- 新地址：`{target.Ip}:{target.Port}`");
    }

    [Command("删除服务器", "删除已绑定的服务器", MessageScene.Group, "scfwq", "delserver")]
    [Command("删除服务器", "删除已绑定的服务器", MessageScene.GroupAt, "scfwq", "delserver")]
    public static async Task DeleteServerAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return;
        }

        if (!args.Require(1) || !args.TryGetInt(0, out int index))
        {
            await CommandHelpers.ReplyAsync(args, "# 🍥 删除服务器\n> 用法：" + MenuKit.CmdInput("/删除服务器 ", "删除服务器 <序号>"));
            return;
        }

        ServerRecord? target = DataStore.GetServers(groupOpenId)
            .FirstOrDefault(item => item.DisplayIndex == index);

        if (target is null)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 序号 {index} 不存在，请用「服务器列表」查看。");
            return;
        }

        string name = string.IsNullOrEmpty(target.ServerName) ? $"{target.Ip}:{target.Port}" : target.ServerName;
        await App.Hub.UnbindAsync(target, "群管理员删除服务器");
        await CommandHelpers.ReplyAsync(args, $"# ✅ 已删除服务器 **{name}**");
    }
}
