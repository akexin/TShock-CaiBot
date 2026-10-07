using System.Threading.Channels;
using Windy.SDK;

namespace CaiBotWindy.Infrastructure;

/// <summary>
/// 出站消息队列 —— 把「突发的一堆发送请求」摊平成一串匀速的发送动作。
///
/// <para><b>解决什么问题</b>：QQ 官方机器人对主动消息有频率限制。服务器上线、事件广播、
/// 子群活动回流这类场景会在<b>同一瞬间</b>催生出好几条发送，全部并发打出去轻则被限频拒收、
/// 重则整批失败。逐个 <c>await</c> 又会把业务线程拖住（尤其在 WebSocket 读取线程上）。</para>
///
/// <para><b>做法</b>：调用方只管「投递」，不用等；后台单个消费者按顺序发出去，
/// 每条之间留一个最小间隔，失败自动重试几次。既平滑了速率，也让发送失败不再影响主流程。</para>
///
/// <para><b>用在哪些地方</b>：<b>通知类</b>消息（事件广播、上线通知、子群回流、登录确认卡片）。
/// 指令的<b>直接回复不走队列</b> —— 用户发指令是等着看的，必须立刻回，多等一秒体验就差了。</para>
/// </summary>
public sealed class MessageQueue : IDisposable
{
    /// <summary>队列中「发送动作 + 用途描述」的最小单元。</summary>
    private sealed record Job(Func<CancellationToken, Task> Send, string Description);

    /// <summary>
    /// 队列容量。满了就丢弃<b>最旧的</b> —— 通知类消息丢一条旧的，
    /// 远比把新事件挤掉、或让内存无限增长要好。
    /// </summary>
    private const int Capacity = 512;

    private readonly Channel<Job> queue = Channel.CreateBounded<Job>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly CancellationTokenSource shutdown = new();
    private readonly Task worker;

    private long sentCount;
    private long failedCount;
    private long droppedCount;
    private int pendingCount;

    public MessageQueue()
    {
        worker = Task.Run(RunAsync, CancellationToken.None);
    }

    /// <summary>两次发送之间的最小间隔。默认 300ms（约 3 条/秒），留足余量。</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>单条消息失败后的最大重试次数（不含首次）。</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>重试之间的基础等待，按次数倍增。</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>等待发送的消息数。</summary>
    public int Pending => Volatile.Read(ref pendingCount);

    /// <summary>累计发送成功数。</summary>
    public long Sent => Interlocked.Read(ref sentCount);

    /// <summary>累计最终失败数（重试耗尽）。</summary>
    public long Failed => Interlocked.Read(ref failedCount);

    /// <summary>累计因队列满被丢弃数。</summary>
    public long Dropped => Interlocked.Read(ref droppedCount);

    /// <summary>
    /// 投递一条消息。立即返回，不等待实际发送。
    /// </summary>
    /// <param name="send">真正的发送动作。</param>
    /// <param name="description">用途描述，失败时打进日志好定位。</param>
    /// <returns>是否成功入队；队列满时为 false（此时旧消息会被丢弃）。</returns>
    public bool Enqueue(Func<CancellationToken, Task> send, string description = "")
    {
        ArgumentNullException.ThrowIfNull(send);

        if (shutdown.IsCancellationRequested)
        {
            return false;
        }

        if (queue.Writer.TryWrite(new Job(send, description)))
        {
            Interlocked.Increment(ref pendingCount);
            return true;
        }

        Interlocked.Increment(ref droppedCount);
        Message.Yellow($"[消息队列] 队列已满（{Capacity}），丢弃一条旧消息：{description}");
        return false;
    }

    private async Task RunAsync()
    {
        CancellationToken token = shutdown.Token;

        try
        {
            while (await queue.Reader.WaitToReadAsync(token))
            {
                while (queue.Reader.TryRead(out Job? job))
                {
                    await SendWithRetryAsync(job, token);
                    Interlocked.Decrement(ref pendingCount);

                    // 节流：两条之间至少隔 MinInterval。
                    // 放在发送之后而不是之前，避免给「第一条」也平白加延迟。
                    if (MinInterval > TimeSpan.Zero)
                    {
                        await Task.Delay(MinInterval, token);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
        catch (Exception ex)
        {
            Message.Red($"[消息队列] 消费循环异常退出: {ex.Message}");
        }
    }

    /// <summary>带重试地发送一条；重试仍失败则记一次 Failed 并放弃。</summary>
    private async Task SendWithRetryAsync(Job job, CancellationToken token)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await job.Send(token);
                Interlocked.Increment(ref sentCount);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                bool last = attempt == MaxRetries;
                if (last)
                {
                    Interlocked.Increment(ref failedCount);
                    Message.Yellow($"[消息队列] 发送失败（已重试 {MaxRetries} 次，放弃）：{job.Description} —— {ex.Message}");
                    return;
                }

                // 退避：第 n 次等 RetryDelay * 2^n，避免限频时越重试越堵。
                TimeSpan delay = TimeSpan.FromMilliseconds(RetryDelay.TotalMilliseconds * (1 << attempt));
                Message.Yellow($"[消息队列] 发送失败，{delay.TotalSeconds:0.0}s 后重试（{attempt + 1}/{MaxRetries}）：{job.Description} —— {ex.Message}");
                await Task.Delay(delay, token);
            }
        }
    }

    /// <summary>一句话运行状况，供状态类指令展示。</summary>
    public string Describe()
    {
        return $"待发 {Pending} / 已发 {Sent} / 失败 {Failed} / 丢弃 {Dropped}";
    }

    public void Dispose()
    {
        try
        {
            shutdown.Cancel();
            queue.Writer.TryComplete();
            worker.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // 关闭阶段的异常不影响退出。
        }
        finally
        {
            shutdown.Dispose();
        }
    }
}
