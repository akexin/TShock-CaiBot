using CaiBotWindy.Services;
using Windy.SDK.Adaptor;
using Windy.SDK.Adaptor.QQOfficial;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>
/// 图鉴检索：物品 / 生物 / 弹幕 / 增益 / 修饰语。数据源为 TerrariaID 导出的 ID 表。
/// </summary>
public static class SearchCommands
{
    private static readonly ButtonKeyboard SearchKeyboard = MenuKit.Keyboard(
        ("搜物品", "/si "),
        ("搜生物", "/sn "),
        ("搜弹幕", "/sp "),
        ("搜增益", "/sb "),
        ("搜修饰", "/sx "),
        ("帮助", "/帮助"));

    // ── 物品 ────────────────────────────────────────────────────────────────────

    [Command("si", "搜索物品", MessageScene.Group, "搜物品")]
    [Command("si", "搜索物品", MessageScene.GroupAt, "搜物品")]
    [Command("si", "搜索物品", MessageScene.Private, "搜物品")]
    public static Task SearchItemAsync(CommandArgs args)
    {
        const string Title = "# ☾搜物品☽";
        return SearchAsync(args, Title, () => TerrariaData.SearchItems(args.GetOrDefault(0)),
            item => item.Name, item => item.ItemId, RenderItem);
    }

    private static string RenderItem(ItemInfo item)
    {
        List<string> lines =
        [
            $"- 物品ID：{item.ItemId}",
            $"- 名称：{item.Name}",
            $"- 堆叠：{item.MaxStack}",
            $"- 价值：{item.MonetaryValue.Format()}",
        ];

        if (item.Damage > 0)
        {
            lines.Add($"- 伤害：{item.Damage}");
        }

        if (item.Crit > 0)
        {
            lines.Add($"- 暴击率：{item.Crit}%");
        }

        if (item.Shoot > 0)
        {
            lines.Add($"- 射弹：{TerrariaData.ProjectileName(item.Shoot)}（{item.Shoot}）");
        }

        string image = MenuKit.ImageMarkdown(
            MenuKit.ItemImageUrl(item.ItemId), $"images/items/Item_{item.ItemId}.png");
        string description = DescriptionLines(item.Description);
        return string.Join('\n', lines) + image + description;
    }

    // ── 生物 ────────────────────────────────────────────────────────────────────

    [Command("sn", "搜索生物", MessageScene.Group, "搜生物")]
    [Command("sn", "搜索生物", MessageScene.GroupAt, "搜生物")]
    [Command("sn", "搜索生物", MessageScene.Private, "搜生物")]
    public static Task SearchNpcAsync(CommandArgs args)
    {
        const string Title = "# ☾搜生物☽";
        return SearchAsync(args, Title, () => TerrariaData.SearchNpcs(args.GetOrDefault(0)),
            npc => npc.Name, npc => npc.NpcId, RenderNpc);
    }

    private static string RenderNpc(NpcInfo npc)
    {
        List<string> lines =
        [
            $"- 生物ID：{npc.NpcId}",
            $"- 名称：{npc.Name}",
            $"- 血量：{npc.LifeMax}",
            $"- 价值：{npc.MonetaryValue.Format()}",
        ];

        if (npc.Damage > 0)
        {
            lines.Add($"- 伤害：{npc.Damage}");
        }

        string image = MenuKit.ImageMarkdown(
            MenuKit.NpcImageUrl(npc.NpcId), $"images/npcs/NPC_{npc.NpcId}.png");
        return string.Join('\n', lines) + image + DescriptionLines(npc.Description);
    }

    // ── 弹幕 ────────────────────────────────────────────────────────────────────

    [Command("sp", "搜索弹幕", MessageScene.Group, "搜弹幕")]
    [Command("sp", "搜索弹幕", MessageScene.GroupAt, "搜弹幕")]
    [Command("sp", "搜索弹幕", MessageScene.Private, "搜弹幕")]
    public static Task SearchProjectileAsync(CommandArgs args)
    {
        const string Title = "# ☾搜弹幕☽";
        return SearchAsync(args, Title, () => TerrariaData.SearchProjectiles(args.GetOrDefault(0)),
            projectile => projectile.Name, projectile => projectile.ProjId,
            projectile => string.Join('\n',
                $"- 弹幕ID：{projectile.ProjId}",
                $"- 名称：{projectile.Name}",
                $"- AiStyle：{projectile.AiStyle}",
                $"- 友好：{(projectile.Friendly ? "是" : "否")}") +
                          MenuKit.ImageMarkdown(
                              MenuKit.ProjectileImageUrl(projectile.ProjId),
                              $"images/projectiles/Projectile_{projectile.ProjId}.png"));
    }

    // ── 增益 ────────────────────────────────────────────────────────────────────

    [Command("sb", "搜索增益", MessageScene.Group, "搜增益")]
    [Command("sb", "搜索增益", MessageScene.GroupAt, "搜增益")]
    [Command("sb", "搜索增益", MessageScene.Private, "搜增益")]
    public static Task SearchBuffAsync(CommandArgs args)
    {
        const string Title = "# ☾搜增益☽";
        return SearchAsync(args, Title, () => TerrariaData.SearchBuffs(args.GetOrDefault(0)),
            buff => buff.Name, buff => buff.BuffId,
            buff => string.Join('\n',
                $"- 增益ID：{buff.BuffId}",
                $"- 名称：{buff.Name}") +
                    MenuKit.ImageMarkdown(MenuKit.BuffImageUrl(buff.BuffId), $"images/buffs/Buff_{buff.BuffId}.png") +
                    DescriptionLines(buff.Description));
    }

    // ── 修饰语 ──────────────────────────────────────────────────────────────────

    [Command("sx", "搜索修饰语", MessageScene.Group, "搜修饰", "搜修饰语")]
    [Command("sx", "搜索修饰语", MessageScene.GroupAt, "搜修饰", "搜修饰语")]
    [Command("sx", "搜索修饰语", MessageScene.Private, "搜修饰", "搜修饰语")]
    public static Task SearchPrefixAsync(CommandArgs args)
    {
        const string Title = "# ☾搜修饰☽";
        return SearchAsync(args, Title, () => TerrariaData.SearchPrefixes(args.GetOrDefault(0)),
            prefix => prefix.Name, prefix => prefix.PrefixId,
            prefix => string.Join('\n',
                $"- 修饰语ID：{prefix.PrefixId}",
                $"- 名称：{prefix.Name}"));
    }

    // ── 通用检索流程 ────────────────────────────────────────────────────────────

    private static async Task SearchAsync<T>(
        CommandArgs args,
        string title,
        Func<List<T>> search,
        Func<T, string> nameSelector,
        Func<T, int> idSelector,
        Func<T, string> render)
    {
        if (!TerrariaData.IsLoaded)
        {
            await CommandHelpers.ReplyAsync(args,
                $"{title}\n> 图鉴数据未加载，请把 ID 表放入机器人的 `Data/` 目录后重启。");
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                $"{title}\n> 请输入名称或 ID 喵~\n> 例如：{QQOfficialLabel.CommandInput("/si 天顶剑")}",
                SearchKeyboard);
            return;
        }

        List<T> matches = search();
        if (matches.Count == 0)
        {
            await CommandHelpers.ReplyAsync(args, $"{title}\n> 未找到相关内容！", SearchKeyboard);
            return;
        }

        if (matches.Count > 1)
        {
            string command = args.CommandName;
            string list = MenuKit.CandidateList(matches, nameSelector, idSelector, command);
            await CommandHelpers.ReplyAsync(args, $"{title}\n\n{list}", SearchKeyboard);
            return;
        }

        string body = render(matches[0]);
        await CommandHelpers.ReplyAsync(args, $"{title}\n{body}", SearchKeyboard);
    }

    /// <summary>把图鉴里的多行描述转成 Markdown 引用块，并清理原始数据里的杂字符。</summary>
    private static string DescriptionLines(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "";
        }

        string cleaned = description
            .Replace(" ", "")
            .Replace("\r", "")
            .Replace("#️⃣", "")
            .Replace("​", "");

        List<string> lines = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .Select(line => $"> {line}")
            .ToList();

        return lines.Count == 0 ? "" : "\n" + string.Join('\n', lines);
    }
}
