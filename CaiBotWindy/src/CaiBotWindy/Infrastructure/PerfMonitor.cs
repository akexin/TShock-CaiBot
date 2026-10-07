using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Windy.SDK;

namespace CaiBotWindy.Infrastructure;

/// <summary>
/// 性能监控 —— 记录 RPC 与指令的耗时，慢调用当场留痕，可随时汇总 TOP 慢操作。
///
/// <para><b>为什么需要它</b>：「在线」「进度查询」这类指令要等服务端回包，
/// 网络一抖就悄悄卡上十几秒。没有统计的话，只能等玩家抱怨「机器人怎么不理人」，
/// 而那时已经不知道是哪台服务器、哪个包慢了。</para>
///
/// <para><b>设计取舍</b>：只做聚合统计（次数 / 平均 / 最大 / 失败数），
/// 不记录每一条的明细 —— 明细对排查帮助有限，却会在长期运行时无限增长。</para>
/// </summary>
public static class PerfMonitor
{
    /// <summary>超过该耗时记为「慢请求」，实时打一条日志。</summary>
    public static TimeSpan SlowThreshold { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>超过该耗时记为「超时级别」，用更醒目的日志。</summary>
    public static TimeSpan CriticalThreshold { get; set; } = TimeSpan.FromSeconds(15);

    private static readonly ConcurrentDictionary<string, OperationStats> Stats = new(StringComparer.Ordinal);

    private static readonly DateTimeOffset StartedAt = DateTimeOffset.Now;

    /// <summary>
    /// 记录一次操作。
    /// </summary>
    /// <param name="operation">操作名（通常是数据包名或指令名）。</param>
    /// <param name="elapsed">实际耗时。</param>
    /// <param name="success">是否成功。</param>
    /// <param name="detail">补充说明，例如服务器名。</param>
    public static void Record(string operation, TimeSpan elapsed, bool success, string? detail = null)
    {
        OperationStats stats = Stats.GetOrAdd(operation, _ => new OperationStats());
        stats.Add(elapsed, success);

        // 只对「慢」的留痕：正常调用每秒可能几十次，全打日志会淹掉真正有用的信息。
        string where = string.IsNullOrEmpty(detail) ? "" : $"（{detail}）";

        if (elapsed >= CriticalThreshold)
        {
            Message.Red($"[超时] {operation}{where} 耗时 {elapsed.TotalSeconds:0.0}s，超过 {CriticalThreshold.TotalSeconds:0}s。");
        }
        else if (elapsed >= SlowThreshold)
        {
            Message.Yellow($"[慢请求] {operation}{where} 耗时 {elapsed.TotalSeconds:0.0}s。");
        }
    }

    /// <summary>
    /// 生成汇总报告，按平均耗时倒序列出 TOP 操作。
    /// 供状态类指令 / 日志使用。
    /// </summary>
    public static string Report(int top = 10)
    {
        List<KeyValuePair<string, OperationStats>> all = Stats.ToList();
        if (all.Count == 0)
        {
            return "暂无调用统计。";
        }

        StringBuilder builder = new();
        builder.Append($"📊 调用统计（自 {StartedAt:HH:mm:ss} 起）\n");

        long totalCalls = all.Sum(item => item.Value.Count);
        long totalFailures = all.Sum(item => item.Value.Failures);
        builder.Append($"- 总调用：{totalCalls} 次，失败 {totalFailures} 次\n\n");

        IEnumerable<KeyValuePair<string, OperationStats>> slowest = all
            .OrderByDescending(item => item.Value.AverageMs)
            .Take(top);

        int rank = 1;
        foreach (KeyValuePair<string, OperationStats> item in slowest)
        {
            OperationStats stats = item.Value;
            builder.Append(
                $"{rank}. `{item.Key}`　{stats.Count} 次　" +
                $"平均 {stats.AverageMs / 1000.0:0.00}s　最大 {stats.MaxMs / 1000.0:0.00}s" +
                (stats.Failures > 0 ? $"　失败 {stats.Failures}" : "") +
                "\n");
            rank++;
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>清空统计（一般在运维排查完一轮之后调用）。</summary>
    public static void Reset()
    {
        Stats.Clear();
    }

    /// <summary>某个操作的累计数据。内部用，字段做简单加锁保证并发安全。</summary>
    private sealed class OperationStats
    {
        private readonly object gate = new();

        public long Count { get; private set; }

        public long Failures { get; private set; }

        public long TotalMs { get; private set; }

        public long MaxMs { get; private set; }

        /// <summary>平均耗时（毫秒）；无数据时为 0。</summary>
        public double AverageMs => Count == 0 ? 0 : (double)TotalMs / Count;

        public void Add(TimeSpan elapsed, bool success)
        {
            long ms = (long)elapsed.TotalMilliseconds;

            lock (gate)
            {
                Count++;
                TotalMs += ms;
                if (ms > MaxMs)
                {
                    MaxMs = ms;
                }

                if (!success)
                {
                    Failures++;
                }
            }
        }
    }
}
