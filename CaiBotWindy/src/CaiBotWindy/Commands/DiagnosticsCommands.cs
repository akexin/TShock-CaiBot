using System.Text;
using CaiBotWindy.Infrastructure;
using CaiBotWindy.Services;
using Windy.SDK;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>
/// 运维诊断类指令：性能统计、运行状态、备份管理。
///
/// <para>只开放给管理员 —— 它们暴露的是内部运行数据，对普通玩家没有意义，
/// 里面的服务器名、文件名也不适合公开。</para>
/// </summary>
public static class DiagnosticsCommands
{
    // ── /性能 ─────────────────────────────────────────────────────────────────

    [Command("性能", "查看调用耗时统计与消息队列状态（管理员）", MessageScene.Group, "xn", "perf", "性能统计")]
    [Command("性能", "查看调用耗时统计与消息队列状态（管理员）", MessageScene.GroupAt, "xn", "perf", "性能统计")]
    public static async Task PerformanceAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        await CommandHelpers.ReplyAsync(args,
            "# 📊 运行状态\n\n" +
            PerfMonitor.Report(10) + "\n\n" +
            "**出站消息队列**\n" +
            $"- {App.Outbox.Describe()}\n" +
            "- 队列只承载通知类消息（事件广播、上线通知、子群回流）；\n" +
            "- 指令回复是即时发送的，不走队列。\n\n" +
            "> 慢请求阈值 5s、超时阈值 15s，触发时会在控制台实时打印。",
            MenuKit.Keyboard(("刷新", "/性能"), ("系统状态", "/系统状态"), ("菜单", "/菜单")));
    }

    // ── /备份 ─────────────────────────────────────────────────────────────────

    [Command("备份", "查看配置与数据文件的自动备份（管理员）", MessageScene.Group, "bf", "backup")]
    [Command("备份", "查看配置与数据文件的自动备份（管理员）", MessageScene.GroupAt, "bf", "backup")]
    public static async Task BackupAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string storePath = Path.Combine(ResolveDirectory(App.Config.StorageDirectory), "store.json");

        StringBuilder builder = new();
        builder.Append("# 🗄 备份状态\n\n");
        builder.Append(DescribeBackups("数据 store.json", storePath));
        builder.Append("\n\n");
        builder.Append(DescribeBackups("配置 CaiBotWindy.json", App.ConfigPath));
        builder.Append("\n\n> 每次写入前自动备份，超过保留份数后清理最旧的一份。\n");
        builder.Append("> 主文件损坏时，启动时会自动回退到最近一份可用备份。");

        await CommandHelpers.ReplyAsync(args, builder.ToString(),
            MenuKit.Keyboard(("刷新", "/备份"), ("系统状态", "/系统状态"), ("菜单", "/菜单")));
    }

    /// <summary>列出某个文件的备份概况（份数 + 最近几份的时间与大小）。</summary>
    private static string DescribeBackups(string label, string path)
    {
        List<string> backups = BackupService.List(path);
        if (backups.Count == 0)
        {
            return $"**{label}**：暂无备份";
        }

        StringBuilder builder = new();
        builder.Append($"**{label}**：{backups.Count} 份");

        // 只展示最近几份：备份多的时候全列出来反而看不清。
        foreach (string file in backups.Take(5))
        {
            FileInfo info = new(file);
            builder.Append($"\n- `{Path.GetFileName(file)}`　{info.LastWriteTime:MM-dd HH:mm}　{info.Length / 1024.0:0.0} KB");
        }

        return builder.ToString();
    }

    /// <summary>与插件启动时一致的路径解析规则。</summary>
    private static string ResolveDirectory(string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(WindyRuntime.BasicPath, path);
    }
}
