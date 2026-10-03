using CaiBotWindy.Services;
using Windy.SDK.Adaptor;
using Windy.SDK.Adaptor.QQOfficial;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>帮助菜单，按钮布局与 CaiBotLite 的菜单面板保持一致。</summary>
public static class HelpCommands
{
    private static readonly ButtonKeyboard MainMenu = MenuKit.Keyboard(
        ("服务器管理", "/服务器管理"),
        ("快捷功能", "/快捷功能"),
        ("地图功能", "/地图功能"),
        ("图鉴搜索", "/图鉴搜索菜单"),
        ("白名单", "/白名单菜单"),
        ("群管理", "/群管理"),
        ("菜单面板", "/菜单面板"));

    /// <summary>私聊（c2c）里可用的入口，只列不依赖群上下文的指令。</summary>
    public static ButtonKeyboard PrivateMenu { get; } = MenuKit.Keyboard(
        ("图鉴搜索", "/图鉴搜索菜单"),
        ("白名单", "/白名单菜单"),
        ("菜单", "/菜单"));

    private static string Tag(string command, string description)
    {
        return $"{QQOfficialLabel.CommandInput(command)} {description}";
    }

    [Command("菜单", "查看功能菜单", MessageScene.Group, "帮助")]
    [Command("菜单", "查看功能菜单", MessageScene.GroupAt, "帮助")]
    [Command("菜单", "查看功能菜单", MessageScene.Private, "帮助")]
    public static Task MenuAsync(CommandArgs args)
    {
        return CommandHelpers.ReplyAsync(args,
            "# 🍥 帮助\n> 泰拉瑞亚服务器管理机器人\n> 不看文档是🐖\n\n" +
            $"绑定服务器：{Tag("/添加服务器 ", "`<IP>` `<端口>` `<绑定码>`")}\n" +
            "绑定码在服务器控制台查看（`[CaiBotLite] 您的服务器绑定码为: …`）。",
            MainMenu);
    }

    [Command("服务器管理", "服务器管理菜单", MessageScene.Group)]
    [Command("服务器管理", "服务器管理菜单", MessageScene.GroupAt)]
    [Command("服务器管理", "服务器管理菜单", MessageScene.Private)]
    public static Task ServerHelpAsync(CommandArgs args)
    {
        return CommandHelpers.ReplyAsync(args,
            "# 💾 服务器管理\n" +
            Tag("/添加服务器", "`<IP>` `<端口>` `<绑定码>` 添加服务器") + "\n" +
            Tag("/修改服务器", "`<序号>` `<IP>` `<端口>` 修改服务器") + "\n" +
            Tag("/删除服务器", "`<序号>` 删除服务器") + "\n" +
            Tag("/服务器列表", "获取服务器地址端口等") + "\n" +
            Tag("/服务器信息", "`<序号>` 获取服务器详细信息") + "\n" +
            Tag("/解绑服务器", "`<序号>` 主动解除绑定"),
            MainMenu);
    }

    [Command("快捷功能", "快捷功能菜单", MessageScene.Group)]
    [Command("快捷功能", "快捷功能菜单", MessageScene.GroupAt)]
    [Command("快捷功能", "快捷功能菜单", MessageScene.Private)]
    public static Task QuickHelpAsync(CommandArgs args)
    {
        return CommandHelpers.ReplyAsync(args,
            "# ⚡ 快捷功能\n" +
            Tag("/在线", "获取服务器在线玩家") + "\n" +
            Tag("/进度查询", "查询世界 Boss 进度") + "\n" +
            Tag("/查背包", "`<玩家名>` 查询玩家背包") + "\n" +
            Tag("/排行", "`<类型>` 查询排行榜") + "\n" +
            Tag("/插件列表", "查看服务器插件 / 模组") + "\n" +
            Tag("/远程指令", "`<指令>` 在服务器执行指令") + "\n" +
            Tag("/自踢", "断开所有服务器连接") + "\n" +
            Tag("/服务器列表", "获取服务器地址端口等"),
            MainMenu);
    }

    [Command("地图功能", "地图功能菜单", MessageScene.Group)]
    [Command("地图功能", "地图功能菜单", MessageScene.GroupAt)]
    [Command("地图功能", "地图功能菜单", MessageScene.Private)]
    public static Task MapHelpAsync(CommandArgs args)
    {
        return CommandHelpers.ReplyAsync(args,
            "# 🗺️ 地图功能\n" +
            Tag("/查看地图", "`[序号]` 获取世界地图预览图") + "\n" +
            Tag("/下载地图", "`[序号]` 获取世界文件 (.wld)") + "\n" +
            Tag("/下载小地图", "`[序号]` 获取小地图文件 (.tmap)"),
            MainMenu);
    }

    [Command("白名单菜单", "白名单菜单", MessageScene.Group)]
    [Command("白名单菜单", "白名单菜单", MessageScene.GroupAt)]
    [Command("白名单菜单", "白名单菜单", MessageScene.Private)]
    public static Task WhitelistHelpAsync(CommandArgs args)
    {
        return CommandHelpers.ReplyAsync(args,
            "# 📄 白名单\n" +
            Tag("/添加白名单", "`<角色名>` 绑定游戏角色") + "\n" +
            Tag("/修改白名单", "`<角色名>` 重新绑定角色") + "\n" +
            Tag("/删除白名单", "解除绑定") + "\n" +
            Tag("/我的白名单", "查看自己的绑定信息") + "\n" +
            Tag("/登录", "`<验证码>` 批准新设备登录") + "\n" +
            Tag("/签到", "每日签到领取金币") + "\n" +
            Tag("/查询金币", "查看金币余额"),
            MainMenu);
    }

    [Command("图鉴搜索菜单", "图鉴搜索菜单", MessageScene.Group)]
    [Command("图鉴搜索菜单", "图鉴搜索菜单", MessageScene.GroupAt)]
    [Command("图鉴搜索菜单", "图鉴搜索菜单", MessageScene.Private)]
    public static Task SearchHelpAsync(CommandArgs args)
    {
        return CommandHelpers.ReplyAsync(args,
            "# 🔍 图鉴搜索\n" +
            Tag("/si", "`<名字|ID>` 搜物品") + "\n" +
            Tag("/sn", "`<名字|ID>` 搜生物") + "\n" +
            Tag("/sp", "`<名字|ID>` 搜弹幕") + "\n" +
            Tag("/sb", "`<名字|ID>` 搜增益") + "\n" +
            Tag("/sx", "`<名字|ID>` 搜修饰语"),
            MainMenu);
    }

    [Command("群管理", "群管理菜单", MessageScene.Group)]
    [Command("群管理", "群管理菜单", MessageScene.GroupAt)]
    [Command("群管理", "群管理菜单", MessageScene.Private)]
    public static Task GroupHelpAsync(CommandArgs args)
    {
        return CommandHelpers.ReplyAsync(args,
            "# #️⃣ 群管理\n" +
            Tag("/管理列表", "列出机器人管理员") + "\n" +
            Tag("/添加管理", "`<OpenID>` 添加机器人管理员") + "\n" +
            Tag("/删除管理", "`<OpenID>` 删除机器人管理员") + "\n" +
            Tag("/绑定父群", "`<父群 OpenID>` 绑定父群") + "\n" +
            Tag("/解绑父群", "解除父群绑定") + "\n" +
            Tag("/获取群信息", "获取本群的 ID 等信息") + "\n" +
            Tag("/设置", "`<项>` `<开|关>` 修改群设置") + "\n" +
            Tag("/黑名单列表", "查看被屏蔽的玩家") + "\n" +
            Tag("/添加黑名单", "`<角色名>` 封禁玩家") + "\n" +
            Tag("/删除黑名单", "`<角色名>` 解封玩家") + "\n" +
            Tag("/全局黑名单", "查看机器人级黑名单（仅所有者）") + "\n" +
            Tag("/权限请求", "查询如何获得管理权限") + "\n\n" +
            "> 重新拉机器人进群即可重置管理员。",
            MainMenu);
    }

    /// <summary>
    /// 下发当前生效的自定义菜单与指令面板配置（即平台 API 的请求体）。
    /// 这两个文件就是 <c>PUT /v2/menu</c> 与 <c>POST /v2/panels</c> 的请求体，
    /// 可直接用 <c>scripts/publish_menu.mjs</c> 发布到 QQ 开放平台。
    /// </summary>
    [Command("菜单面板", "下发自定义菜单与指令面板配置", MessageScene.Group)]
    [Command("菜单面板", "下发自定义菜单与指令面板配置", MessageScene.GroupAt)]
    [Command("菜单面板", "下发自定义菜单与指令面板配置", MessageScene.Private)]
    public static async Task PanelConfigAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        // QQ 官方适配器只支持向群聊发文件，私聊里改用文字说明，避免抛异常后用户收不到任何回复。
        if (string.IsNullOrEmpty(args.Message.GroupId))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 菜单面板配置\n" +
                "> 私聊不支持接收文件，请在**群聊**中执行 `/菜单面板`。\n" +
                "> 或者直接在本机运行 `node CaiBotWindy/scripts/publish_menu.mjs push` 发布。");
            return;
        }

        string menuDirectory = Path.Combine(Windy.SDK.WindyRuntime.BasicPath, "Menu");
        MenuSeeder.Ensure(menuDirectory);

        string menuFile = Path.Combine(menuDirectory, "menu.json");
        string panelFile = Path.Combine(menuDirectory, "panels.json");

        if (!File.Exists(menuFile) || !File.Exists(panelFile))
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 没有找到菜单配置文件\n" +
                $"> 预期路径：`{menuDirectory}`\n" +
                "> 请确认部署时已把项目的 `menu/` 目录复制到 Windy 运行目录下。");
            return;
        }

        await CommandHelpers.ReplyAsync(args,
            "# 🍥 菜单面板配置\n" +
            "> 以下文件就是 QQ 开放平台的 API 请求体：\n" +
            $"- `menu.json` → `PUT /v2/menu`（自定义菜单，{new FileInfo(menuFile).Length} 字节）\n" +
            $"- `panels.json` → `POST /v2/panels`（指令面板，{new FileInfo(panelFile).Length} 字节）\n\n" +
            "> 发布：项目根执行 `node CaiBotWindy/scripts/publish_menu.mjs push`。\n" +
            "> 可视化微调：启动 **qq-bot-menu-panel** 后从平台拉取（该工具不支持导入本地文件）。\n" +
            "> 平台限制：每个场景（群聊 / 单聊）只能存在 1 个全局面板，后建的会顶掉先建的。");

        await args.Adaptor.SendFile(await File.ReadAllBytesAsync(menuFile), "menu.json", "application/json; charset=utf-8");
        await args.Adaptor.SendFile(await File.ReadAllBytesAsync(panelFile), "panels.json", "application/json; charset=utf-8");
    }
}
