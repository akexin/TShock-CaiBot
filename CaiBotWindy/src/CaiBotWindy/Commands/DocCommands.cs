using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Services;
using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Adaptor.QQOfficial;
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
                    $"> 指令清单也可以随时用 {MenuKit.CmdInput("/菜单")} 查看。");
                return;
            }
            catch (Exception ex)
            {
                Message.Yellow($"[文档] 发送文件失败，回退为文字: {ex.Message}");
            }
        }

        await CommandHelpers.ReplyAsync(args, UsageDocument);
    }

    // ── /菜单 ──────────────────────────────────────────────────────────────

    [Command("菜单", "功能菜单（分页）", MessageScene.Group, "所有指令", "syzl", "allcmd", "cd", "menu", "帮助", "帮助菜单", "指令列表", "全部指令")]
    [Command("菜单", "功能菜单（分页）", MessageScene.GroupAt, "所有指令", "syzl", "allcmd", "cd", "menu", "帮助", "帮助菜单", "指令列表", "全部指令")]
    [Command("菜单", "功能菜单（分页）", MessageScene.Private, "所有指令", "syzl", "allcmd", "cd", "menu", "帮助", "帮助菜单", "指令列表", "全部指令")]
    public static Task AllCommandsAsync(CommandArgs args)
    {
        // 第 1 页是首页（只列分类名），之后每页对应一个二级菜单的内容。
        int total = CommandPages.Length + 1;
        int page = args.TryGetInt(0, out int parsed) ? Math.Clamp(parsed, 1, total) : 1;

        // 不用按钮键盘：导航全靠正文里的蓝色文字链接，界面更干净。
        if (page == 1)
        {
            return CommandHelpers.ReplyAsync(args, BuildIndexPage());
        }

        (string title, MenuEntry[] items) = CommandPages[page - 2];

        StringBuilder builder = new();
        builder.Append($"# 🍥 {title}\n\n");

        foreach (MenuEntry entry in items)
        {
            builder.Append(RenderEntry(entry));
        }

        builder.Append($"\n> 返回 {Link("/菜单", "菜单")}");

        return CommandHelpers.ReplyAsync(args, builder.ToString());
    }

    /// <summary>首页：只列出二级菜单的名称，点名称即进入。</summary>
    private static string BuildIndexPage()
    {
        StringBuilder builder = new();
        builder.Append("# 🍥 菜单\n\n");

        for (int i = 0; i < CommandPages.Length; i++)
        {
            // 显示分类全名，点击把 `/菜单 N` 填进输入框。
            builder.Append($"◦ {Link($"/菜单 {i + 2}", CommandPages[i].Title)}\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// 蓝色可点击文字：显示 <paramref name="label"/>，点击把 <paramref name="command"/> 填进输入框。
    ///
    /// <para>⚠️ 必须用 <c>&lt;qqbot-cmd-input&gt;</c>，不能用 <c>&lt;qqbot-cmd-enter&gt;</c> ——
    /// 后者虽然点击即执行，但<b>群消息不支持</b>，平台会整条拒收
    /// （<c>40034106 群消息不支持qqbot-cmd-enter</c>）。踩过一次。</para>
    /// </summary>
    private static string Link(string command, string? label = null)
    {
        return QQOfficialLabel.CommandInput(command, label);
    }

    private static string RenderEntry(MenuEntry entry)
    {
        string alias = entry.Alias.Length > 0 ? $"　`{entry.Alias}`" : "";
        return $"◦ {Link(entry.Command)}　{entry.Description}{alias}\n";
    }

    /// <summary>菜单里的一条指令。</summary>
    private readonly record struct MenuEntry(string Command, string Description, string Alias = "");

    /// <summary>菜单分页数据 —— 每页就是一个二级菜单。</summary>
    private static readonly (string Title, MenuEntry[] Items)[] CommandPages =
    [
        ("📊 在线与状态", [
            new("/在线", "当前在线玩家", "zx / online / 谁在线"),
            new("/在线总览", "本群所有服务器的在线汇总", "zxzl"),
            new("/系统状态", "延迟 + 本机 CPU / 内存 / 磁盘", "xtzt / status"),
            new("/进度查询", "世界 Boss 进度", "jdcx / progress"),
            new("/插件列表", "已装插件 / 模组", "cjlb / plugins"),
            new("/排行", "服务器排行榜", "ph / rank"),
        ]),
        ("🎮 玩家数据", [
            new("/查背包", "查询玩家背包（后面加玩家名）", "cbb / bag"),
            new("/签到", "每日签到领金币", "qd / signin"),
            new("/查询金币", "金币余额", "cxjb / coins"),
            new("/si", "搜物品　（sn 生物 / sp 弹幕）"),
            new("/sb", "搜增益　（sx 修饰语）"),
        ]),
        ("🖥 服务器", [
            new("/服务器", "服务器管理（列表 | 信息 | 添加 | 修改 | 删除 | 解绑）"),
            new("/地图", "地图相关（预览 | 下载 | 小地图）"),
            new("/自踢", "断开所有服务器连接", "zt / kick"),
            new("/绑定信息", "群 ↔ 机器人 ↔ 服务器 的绑定关系", "bdxx"),
        ]),
        ("📄 注册与白名单", [
            new("/注册", "邮箱注册（QQ邮箱 + 角色名）", "zc"),
            new("/注册验证", "完成邮箱验证", "zcyz"),
            new("/我的注册", "注册状态与注册基准", "wdzc"),
            new("/白名单", "白名单管理（添加 | 修改 | 删除 | 我的）"),
            new("/登录", "批准新设备登录 / 审批登录申请", "dl / login"),
        ]),
        ("👥 父群 / 子群", [
            new("/设置 父群", "父子群总入口"),
            new("/设置 父群 列表", "查看名下子群"),
            new("/设置 父群 绑定", "挂到父群下（加父群 OpenID）"),
            new("/设置 父群 执行", "在父群替子群执行（序号 + 指令）"),
            new("/设置 父群 同步", "子群消息是否回流父群（开 | 关）"),
            new("/设置 父群 解绑", "解除父群绑定"),
        ]),
        ("🛡 群管理", [
            new("/群", "群管理总入口（信息 | 设置 | 管理 | 黑名单）"),
            new("/申请列表", "拉取入群申请", "sqlb"),
            new("/审批入群", "审批入群（OpenID + 同意 | 拒绝）", "spjr"),
            new("/入群审核", "审核方式（人工 | 自动 | 关闭）"),
            new("/禁言状态", "群禁言状态（禁言 加 OpenID + 分钟）"),
            new("/设置", "群开关项（whitelist | progress | remote | kickgroup）"),
        ]),
        ("⚙️ 管理与运维", [
            new("/物品监控", "背包物品超量告警", "wpjk"),
            new("/远程指令", "在服务端执行指令", "yczl / rcon"),
            new("/日志", "TShock 服务端日志（分页）", "rz / log"),
            new("/日志搜索", "筛选日志内容", "rzss"),
            new("/注册限制", "每 IP / 设备注册上限", "zcxz"),
            new("/菜单面板", "下发菜单与面板配置"),
        ]),
        ("ℹ️ 其它", [
            new("/菜单", "本菜单", "syzl / cd / menu"),
            new("/文档", "使用文档（群里以文件下发）"),
            new("/关于", "作者与开源仓库", "gy / about"),
        ]),
    ];

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

        > 完整清单：`/菜单`

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

        > 其余指令的别名见 `/菜单`。每条指令都有 2~4 个等价写法。

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
