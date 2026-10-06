using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Services;
using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>绑定信息、内置使用文档、全部指令清单。</summary>
public static class DocCommands
{
    // ── /绑定信息 ──────────────────────────────────────────────────────────────

    [Command("绑定信息", "查看本群与机器人、服务器的绑定关系", MessageScene.Group, "bdxx", "binding", "绑定关系")]
    [Command("绑定信息", "查看本群与机器人、服务器的绑定关系", MessageScene.GroupAt, "bdxx", "binding", "绑定关系")]
    [Command("绑定信息", "查看本群与机器人、服务器的绑定关系", MessageScene.Private, "bdxx", "binding", "绑定关系")]
    public static async Task BindingInfoAsync(CommandArgs args)
    {
        string? groupOpenId = args.Message.GroupId;
        StringBuilder builder = new();

        builder.Append("# 🔗 绑定信息\n");

        builder.Append("\n**机器人**\n");
        builder.Append($"- 名称：CaiBotWindy v{typeof(DocCommands).Assembly.GetName().Version?.ToString(3) ?? "dev"}\n");
        string appId = App.Config.BotAppId;
        if (!string.IsNullOrEmpty(appId))
        {
            builder.Append($"- AppID：`{appId}`\n");
        }
        else
        {
            // 配置里没填就退回适配器上报的 AppId（Windy.json 里那份）。
            builder.Append("- AppID：由适配器提供\n");
        }

        builder.Append($"- 公网地址：{(string.IsNullOrEmpty(App.Config.PublicBaseUrl) ? "未配置" : App.Config.PublicBaseUrl)}\n");

        if (string.IsNullOrEmpty(groupOpenId))
        {
            builder.Append("\n**本群**\n- 私聊场景没有群上下文\n");
        }
        else
        {
            builder.Append("\n**本群**\n");
            builder.Append($"- 群 OpenID：`{groupOpenId}`\n");

            GroupRecord? group = DataStore.FindGroup(groupOpenId);
            builder.Append($"- 机器人管理员：**{group?.Admins.Count ?? 0}** 人\n");

            if (group is not null && !string.IsNullOrEmpty(group.ParentGroupOpenId))
            {
                builder.Append($"- 父群：`{group.ParentGroupOpenId}`\n");
            }

            List<ServerRecord> servers = DataStore.GetServers(groupOpenId);
            builder.Append($"\n**已绑定的服务器（{servers.Count}）**\n");

            if (servers.Count == 0)
            {
                builder.Append("> 还没有绑定服务器。\n");
            }
            else
            {
                foreach (ServerRecord server in servers)
                {
                    bool online = App.Hub.TryResolve(groupOpenId, server.DisplayIndex, out _, out _);
                    string name = string.IsNullOrWhiteSpace(server.ServerName) ? "(未命名)" : server.ServerName;
                    builder.Append($"- `{server.DisplayIndex}` **{name}**　{server.Ip}:{server.Port}　{(online ? "🟢 在线" : "⚪ 离线")}\n");
                }
            }

            builder.Append(
                "\n> 绑定新服务器：在**服务器控制台**看到绑定码后，发 `/添加服务器 <IP> <端口> <绑定码>`\n" +
                "> 解绑：`/服务器 解绑 <序号>`　看详细状态：`/系统状态`");
        }

        await CommandHelpers.ReplyAsync(args, builder.ToString());
    }

    // ── /文档 ──────────────────────────────────────────────────────────────────

    [Command("文档", "获取机器人使用文档", MessageScene.Group, "wdoc", "doc", "说明书", "使用文档")]
    [Command("文档", "获取机器人使用文档", MessageScene.GroupAt, "wdoc", "doc", "说明书", "使用文档")]
    [Command("文档", "获取机器人使用文档", MessageScene.Private, "wdoc", "doc", "说明书", "使用文档")]
    public static async Task DocAsync(CommandArgs args)
    {
        // 群里直接发文件，方便转发保存；私聊不支持发文件，改为贴出正文。
        if (!string.IsNullOrEmpty(args.Message.GroupId))
        {
            try
            {
                await args.Adaptor.SendFile(
                    Encoding.UTF8.GetBytes(UsageDocument),
                    "CaiBotWindy-使用文档.md",
                    "text/markdown; charset=utf-8");

                await CommandHelpers.ReplyAsync(args,
                    "# 📘 使用文档已发送\n" +
                    "> 上面就是完整的指令说明，可以直接转发给群友。\n" +
                    $"> 指令清单也可以随时用 {MenuKit.CmdInput("/所有指令")} 查看。");
                return;
            }
            catch (Exception ex)
            {
                Message.Yellow($"[文档] 发送文件失败，回退为文字: {ex.Message}");
            }
        }

        await CommandHelpers.ReplyAsync(args, UsageDocument);
    }

    // ── /所有指令 ──────────────────────────────────────────────────────────────

    [Command("所有指令", "列出机器人的全部指令", MessageScene.Group, "syzl", "allcmd", "全部指令", "指令列表")]
    [Command("所有指令", "列出机器人的全部指令", MessageScene.GroupAt, "syzl", "allcmd", "全部指令", "指令列表")]
    [Command("所有指令", "列出机器人的全部指令", MessageScene.Private, "syzl", "allcmd", "全部指令", "指令列表")]
    public static Task AllCommandsAsync(CommandArgs args)
    {
        return CommandHelpers.ReplyAsync(args, AllCommandList,
            MenuKit.Keyboard(("菜单", "/菜单"), ("使用文档", "/文档"), ("我的注册", "/我的注册"), ("关于", "/关于")));
    }

    /// <summary>全部指令清单。合并指令用 <c>&lt;a|b&gt;</c> 表示子命令。</summary>
    private const string AllCommandList =
        "# 🍥 全部指令\n" +
        "> 尖括号是**参数**，竖线是**可选子命令**。旧写法与合并写法都能用。\n" +

        "\n**📊 在线与状态**\n" +
        "- `/在线` 当前在线玩家\n" +
        "- `/在线总览` 本群所有服务器的在线汇总\n" +
        "- `/系统状态` 服务器延迟 + 本机 CPU / 内存 / 磁盘\n" +
        "- `/进度查询` 世界 Boss 进度\n" +
        "- `/插件列表` 已装插件 / 模组\n" +
        "- `/排行 <类型>` 服务器排行榜\n" +

        "\n**🎮 玩家数据**\n" +
        "- `/查背包 <玩家名>` 查询玩家背包\n" +
        "- `/签到` 每日签到领金币\n" +
        "- `/查询金币` 金币余额\n" +
        "- `/si <名字|ID>` 搜物品　`/sn` 搜生物　`/sp` 搜弹幕\n" +
        "- `/sb` 搜增益　`/sx` 搜修饰语\n" +

        "\n**🖥 服务器**\n" +
        "- `/服务器 <列表|信息|添加|修改|删除|解绑>` 服务器管理\n" +
        "- `/地图 <预览|下载|小地图>` 地图相关\n" +
        "- `/自踢` 断开所有服务器连接\n" +
        "- `/绑定信息` 群 ↔ 机器人 ↔ 服务器 的绑定关系\n" +

        "\n**📄 注册与白名单**\n" +
        "- `/注册 <QQ邮箱> <角色名>` 邮箱注册（玩家自助）\n" +
        "- `/注册验证 <验证码>` 完成邮箱验证\n" +
        "- `/我的注册` 注册状态与注册基准\n" +
        "- `/白名单 <添加|修改|删除|我的|查询>` 白名单管理\n" +
        "- `/登录 <验证码>` 批准新设备登录\n" +
        "- `/登录 <确认|拒绝> <验证码>` 审批一条登录申请\n" +

        "\n**👥 群管理**（需机器人是群管理员）\n" +
        "- `/群 <信息|设置|管理|父群|黑名单|全局|权限>` 群管理总入口\n" +
        "- `/申请列表` 拉取入群申请\n" +
        "- `/审批入群 <OpenID> <同意|拒绝>` 审批入群\n" +
        "- `/入群审核 <人工|自动|关闭>` 审核方式（默认人工）\n" +
        "- `/审批策略 <列表|开启|关闭>` 平台自动审批策略\n" +
        "- `/禁言状态` 群禁言状态\n" +
        "- `/禁言 <OpenID> <分钟>` 设置禁言（0 = 解除）\n" +

        "\n**🛡 管理与运维**\n" +
        "- `/物品监控 <add|del> …` 背包物品超量告警\n" +
        "- `/远程指令 <指令>` 在服务端执行指令\n" +
        "- `/注册限制 [数量]` 每个 IP / 设备的注册上限\n" +
        "- `/菜单面板` 下发菜单与面板配置（管理员）\n" +

        "\n**ℹ️ 其它**\n" +
        "- `/帮助` `/菜单` 功能菜单\n" +
        "- `/文档` 使用文档（可转发）\n" +
        "- `/所有指令` 本清单\n" +
        "- `/关于` 作者与开源仓库";

    /// <summary>内置使用文档（群里以 .md 文件形式下发）。</summary>
    private const string UsageDocument = """
        # CaiBotWindy 使用文档

        > 泰拉瑞亚服务器 + QQ 群一体化管理机器人。机器人本体（CaiBotWindy）+ TShock 适配插件（CaiBotLite），
        > 开源地址：https://github.com/akexin/TShock-CaiBot

        ## 一、快速开始

        ### 1. 绑定服务器（管理员）
        1. 启动 TShock 服务端，在控制台找到这一行：
           `[CaiBotLite] 您的服务器绑定码为: 12345678`
        2. 在群里发送：`/添加服务器 <公网IP> <端口> <绑定码>`
        3. 绑定成功后机器人会往群里发一条「服务器已上线」通知。

        用 `/绑定信息` 可以随时查看绑定关系，用 `/服务器列表` 看已绑定的全部服务器。

        ### 2. 注册角色（玩家）
        ```
        /注册 123456789@qq.com 你的角色名
        /注册验证 123456
        ```
        机器人会把 6 位验证码发到你填的 QQ 邮箱。只接受 QQ 邮箱（`@qq.com` / `@foxmail.com`）；
        一个邮箱只能注册一个角色。

        注册完成后首次进服会自动记录你的 IP 与设备作为「注册基准」。
        之后换设备或换网络进服时，群里会弹出确认卡片，等管理员点一下即可。

        ### 3. 进服
        地址用 `/服务器列表` 查看；被白名单拦下时，提示会告诉你去哪一步。

        ## 二、常用指令

        | 场景 | 指令 |
        | --- | --- |
        | 看在线玩家 | `/在线` `/在线总览` |
        | 看服务器状态 | `/系统状态` `/服务器信息 <序号>` |
        | 查玩家背包 | `/查背包 <玩家名>` |
        | 搜图鉴 | `/si` `/sn` `/sp` `/sb` `/sx` |
        | 地图 | `/地图 <预览\|下载\|小地图>` |
        | 签到领金币 | `/签到` `/查询金币` |
        | 注册相关 | `/注册` `/注册验证` `/我的注册` |

        > 完整清单：`/所有指令`

        ## 三、进服被拦住了？

        | 提示 | 原因 | 怎么办 |
        | --- | --- | --- |
        | 你还没有注册 | 该角色名没有注册记录 | 发 `/注册 <QQ邮箱> <角色名>` |
        | 注册还没有完成 | 验证码没填 | 查邮箱后发 `/注册验证 <验证码>` |
        | 这台设备还没有得到确认 | 换了设备或网络 | 群里等管理员点「确认登录」后重新进服 |

        ## 四、管理员常用

        ```
        /服务器 <列表|信息|添加|修改|删除|解绑>   服务器管理
        /白名单 <添加|修改|删除|我的|查询>         白名单管理
        /群 <信息|设置|管理|父群|黑名单|全局>      群管理
        /申请列表                                  拉取入群申请
        /审批入群 <OpenID> <同意|拒绝>              审批入群
        /入群审核 <人工|自动|关闭>                  审核方式（默认人工）
        /禁言 <OpenID> <分钟>                      设置禁言
        /物品监控 <add|del>                        背包物品超量告警
        /远程指令 <指令>                           在服务端执行指令
        /注册限制 <数量>                           注册上限（默认 2 个 / IP）
        ```

        ## 五、别名与多服务器

        ### 别名：每条指令都有多种写法
        记不住中文名也没关系，英文、拼音简写、近义词都能用：

        | 常用指令 | 等价写法 |
        | --- | --- |
        | `/在线` | `/zx` `/online` `/谁在线` `/在线玩家` |
        | `/查背包` | `/cbb` `/bag` `/看背包` |
        | `/系统状态` | `/xtzt` `/status` `/机器状态` |
        | `/日志` | `/rz` `/log` `/logs` `/服务端日志` |
        | `/注册` | `/zc` `/register` `/邮箱注册` |
        | `/禁言` | `/jy` `/mute` `/禁言成员` |
        | `/服务器` | `/fwq` `/server` `/srv` |
        | `/群` | `/q` `/group` |

        > 其余指令的别名见 `/所有指令`。每条指令都有 2~4 个等价写法。

        ### 多服务器：命令后面加数字
        群里绑了多台服务器时，序号跟在命令后面：

        ```
        /在线 1
        /在线 2
        /远程指令 1 /help
        /远程指令 2 /help
        ```

        不带序号时，多数指令会作用于**全部服务器**，或在回复里列出可选序号。

        ## 六、常见问题

        **Q：机器人不回话？**
        A：确认机器人已被拉进群，且服务器已绑定（`/绑定信息`）。群聊里需要 @机器人 或直接发指令。

        **Q：收不到验证码邮件？**
        A：先看垃圾箱；再确认邮箱填对了（只支持 QQ 邮箱）。

        **Q：换了手机 / 换了网络进不去？**
        A：这是设备与 IP 校验。群里会弹确认卡片，让管理员点「确认登录」。

        **Q：想换绑角色？**
        A：联系管理员，需要管理员代为处理（`/白名单 修改`）。

        **Q：怎么拉黑捣乱的人？**
        A：`/群 黑名单 添加 <角色名|OpenID>`；解除用 `/群 黑名单 删除 <角色名|OpenID>`。
        云黑（所有绑定服务器共享）用 `/群 全局 封禁 <角色名|ip|设备|qq> ...`。

        ---

        作者：ak　·　https://github.com/akexin/TShock-CaiBot
        """;
}
