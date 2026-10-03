using CaiBotLite.Enums;
using System.Diagnostics;
using Terraria;
using TShockAPI;

namespace CaiBotLite.Common;

/// <summary>
/// 背包物品监控：定时扫描在线玩家的背包，某物品持有量达到阈值就通过
/// <see cref="PackageType.ServerLog"/> 包上报机器人，由机器人广播到群聊。
///
/// <para>规则存在 CaiBotLite.json 的「物品监控」节里，游戏内用 <c>/cblmonitor</c> 管理
/// （机器人侧的 /物品监控 指令也是通过远程指令调它，这样规则只需存一处）。</para>
///
/// <para>同一玩家 + 同一物品 <b>5 分钟内最多报一次</b> —— 没有冷却的话，玩家反复整理背包
/// 会把群聊刷成瀑布。</para>
/// </summary>
internal static class ItemMonitor
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _lastScanMs;
    private const int ScanIntervalMs = 30_000;
    private const int AlertCooldownMinutes = 5;

    /// <summary>冷却表：键 = 「玩家名:物品ID」。</summary>
    private static readonly Dictionary<string, DateTime> LastAlertAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>由主线程的 GamePostUpdate 驱动，内部自行按 30 秒节流。</summary>
    internal static void Scan()
    {
        if (Config.Settings.ItemMonitors.Count == 0 || !WebsocketManager.IsWebsocketConnected)
        {
            return;
        }

        if (Clock.ElapsedMilliseconds - _lastScanMs < ScanIntervalMs)
        {
            return;
        }

        _lastScanMs = Clock.ElapsedMilliseconds;

        DateTime now = DateTime.UtcNow;

        foreach (TSPlayer player in TShock.Players)
        {
            if (player is not { Active: true })
            {
                continue;
            }

            // 只统计主背包（inventory），银行 / 虚空袋需要打开容器才同步，扫了也不准。
            Dictionary<int, int> totals = new();
            foreach (Item item in player.TPlayer.inventory)
            {
                if (item is null || item.IsAir || item.stack <= 0)
                {
                    continue;
                }

                totals[item.type] = totals.GetValueOrDefault(item.type) + item.stack;
            }

            foreach (ItemMonitorRule rule in Config.Settings.ItemMonitors)
            {
                int total = totals.GetValueOrDefault(rule.ItemId);
                if (total < rule.Count)
                {
                    continue;
                }

                string key = $"{player.Name}:{rule.ItemId}";
                if (LastAlertAt.TryGetValue(key, out DateTime last) && now - last < TimeSpan.FromMinutes(AlertCooldownMinutes))
                {
                    continue;
                }

                LastAlertAt[key] = now;

                string itemName = Lang.GetItemNameValue(rule.ItemId);
                new PackageWriter(PackageType.ServerLog, false, null)
                    .Write("kind", "item_monitor")
                    .Write("player_name", player.Name)
                    .Write("message", $"玩家 **{player.Name}** 背包持有 [{itemName}](ID {rule.ItemId}) × **{total}**（阈值 {rule.Count}）")
                    .Send();
            }

            // 冷却表顺手清理，避免长期运行的内存膨胀。
            if (LastAlertAt.Count > 512)
            {
                List<string> expired = LastAlertAt
                    .Where(kv => now - kv.Value > TimeSpan.FromMinutes(30))
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (string key in expired)
                {
                    LastAlertAt.Remove(key);
                }
            }
        }
    }

    /// <summary>游戏内管理指令 /cblmonitor list|add|del —— 机器人侧的 /物品监控 也走远程指令调这里。</summary>
    internal static void HandleCommand(CommandArgs args)
    {
        string sub = args.Parameters.Count > 0 ? args.Parameters[0].ToLowerInvariant() : "list";

        switch (sub)
        {
            case "add":
            {
                if (args.Parameters.Count < 3 ||
                    !int.TryParse(args.Parameters[1], out int itemId) ||
                    !int.TryParse(args.Parameters[2], out int count))
                {
                    args.Player.SendErrorMessage("用法：/cblmonitor add <物品ID> <数量阈值>");
                    return;
                }

                if (count < 1)
                {
                    args.Player.SendErrorMessage("阈值必须 ≥ 1。");
                    return;
                }

                Config.Settings.ItemMonitors.RemoveAll(rule => rule.ItemId == itemId);
                Config.Settings.ItemMonitors.Add(new ItemMonitorRule { ItemId = itemId, Count = count });
                Config.Settings.Write();
                args.Player.SendSuccessMessage($"已监控 [{Lang.GetItemNameValue(itemId)}](ID {itemId})：持有量 ≥ {count} 时上报机器人。");
                break;
            }

            case "del":
            {
                if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out int delId))
                {
                    args.Player.SendErrorMessage("用法：/cblmonitor del <物品ID>");
                    return;
                }

                int removed = Config.Settings.ItemMonitors.RemoveAll(rule => rule.ItemId == delId);
                Config.Settings.Write();
                args.Player.SendSuccessMessage(removed > 0 ? $"已移除物品 {delId} 的监控。" : $"物品 {delId} 本来就没有监控。");
                break;
            }

            default:
            {
                args.Player.SendSuccessMessage($"共 {Config.Settings.ItemMonitors.Count} 条物品监控规则：");
                foreach (ItemMonitorRule rule in Config.Settings.ItemMonitors)
                {
                    args.Player.SendSuccessMessage($"  [{Lang.GetItemNameValue(rule.ItemId)}](ID {rule.ItemId})  持有量 ≥ {rule.Count}");
                }

                break;
            }
        }
    }
}
