using System.Diagnostics;
using Windy.SDK;

namespace CaiBotWindy.Infrastructure;

/// <summary>一次并行查询里单个目标的结果。</summary>
/// <typeparam name="T">查询成功时的数据类型。</typeparam>
public sealed class FanOutItem<T>
{
    private FanOutItem(string key, bool success, T? value, string? error, bool timedOut, TimeSpan elapsed)
    {
        Key = key;
        Success = success;
        Value = value;
        Error = error;
        TimedOut = timedOut;
        Elapsed = elapsed;
    }

    /// <summary>目标标识（服务器名等），用于日志与展示。</summary>
    public string Key { get; }

    public bool Success { get; }

    public T? Value { get; }

    /// <summary>失败原因；成功时为 null。</summary>
    public string? Error { get; }

    /// <summary>是否因独立超时而失败（区别于「服务器报错」）。</summary>
    public bool TimedOut { get; }

    /// <summary>本次查询实际耗时。</summary>
    public TimeSpan Elapsed { get; }

    public static FanOutItem<T> Ok(string key, T value, TimeSpan elapsed) =>
        new(key, success: true, value, error: null, timedOut: false, elapsed);

    public static FanOutItem<T> Timeout(string key, TimeSpan elapsed) =>
        new(key, success: false, value: default, error: "查询超时", timedOut: true, elapsed);

    public static FanOutItem<T> Fail(string key, string error, TimeSpan elapsed) =>
        new(key, success: false, value: default, error, timedOut: false, elapsed);
}

/// <summary>
/// 向多个目标并行发起同一类查询，<b>每个目标各自独立超时</b>。
///
/// <para><b>解决什么问题</b>：早期「在线总览」是一条 <c>foreach</c> 里串行 <c>await</c>，
/// 绑了 5 台服务器、每台都卡到超时的话，用户要等 5 个超时<b>累加</b>的时间才看到结果 ——
/// 而且任何一台先抛异常都可能打断后面几台。</para>
///
/// <para><b>改法</b>：全部同时发起，各自带独立超时。并行后总耗时≈最慢的那台，
/// 而不是所有台之和；单台超时只会让自己那条记为失败，其余照常返回。</para>
///
/// <para>超时用 <c>Task.WaitAsync</c> 而非 <c>CancellationTokenSource</c>：
/// 前者只取消等待、不影响已发出的请求，正符合「超时了就先不等它」的语义。</para>
/// </summary>
public static class FanOut
{
    /// <summary>记录超过该耗时的目标，便于事后发现「哪台服务器慢」。</summary>
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 并行执行全部查询。
    /// </summary>
    /// <param name="items">每个元素为「标识 + 真正的查询委托」。</param>
    /// <param name="perItemTimeout">单个目标的超时；超时只影响该目标。</param>
    /// <param name="cancellationToken">整体取消（如机器人关闭）。</param>
    /// <returns>与输入顺序一致的结果数组（<c>Task.WhenAll</c> 保证顺序）。</returns>
    public static async Task<FanOutItem<T>[]> RunAsync<T>(
        IEnumerable<(string Key, Func<CancellationToken, Task<T>> Run)> items,
        TimeSpan perItemTimeout,
        CancellationToken cancellationToken = default)
    {
        List<Task<FanOutItem<T>>> tasks = items
            .Select(item => RunOneAsync(item.Key, item.Run, perItemTimeout, cancellationToken))
            .ToList();

        // WhenAll 会等全部结束（含失败项），且结果顺序与输入一致 —— 展示时不需要再对齐。
        return await Task.WhenAll(tasks);
    }

    private static async Task<FanOutItem<T>> RunOneAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> run,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Stopwatch watch = Stopwatch.StartNew();

        try
        {
            T value = await run(cancellationToken).WaitAsync(timeout);
            watch.Stop();

            if (watch.Elapsed >= SlowThreshold)
            {
                Message.Yellow($"[FanOut] {key} 响应偏慢：{watch.Elapsed.TotalSeconds:0.0}s");
            }

            return FanOutItem<T>.Ok(key, value, watch.Elapsed);
        }
        catch (TimeoutException)
        {
            watch.Stop();
            Message.Yellow($"[FanOut] {key} 查询超时（{timeout.TotalSeconds:0}s），已跳过。");
            return FanOutItem<T>.Timeout(key, watch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 个别实现对取消与超时不做区分，兜底按超时处理。
            watch.Stop();
            return FanOutItem<T>.Timeout(key, watch.Elapsed);
        }
        catch (Exception ex)
        {
            watch.Stop();
            Message.Yellow($"[FanOut] {key} 查询失败：{ex.Message}");
            return FanOutItem<T>.Fail(key, ex.Message, watch.Elapsed);
        }
    }
}
