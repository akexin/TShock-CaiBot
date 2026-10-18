using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CaiBotWindy.Services;

/// <summary>
/// 卡片渲染的统一闸门：并发限流 + 短期结果缓存。
///
/// <para><b>为什么限流</b>：SkiaSharp 渲染是纯 CPU 活，<c>/进度查询</c> 要画 1080×1440、
/// <c>/查背包</c> 要按容器动态算高度。群里几个人同时点，CPU 会被打满，进而拖慢
/// 同一进程里的 HTTP 收发。这里把并发压到 CPU 核数的一半。</para>
///
/// <para><b>为什么缓存</b>：同一玩家在几十秒内重复查同一张卡很常见（刷新、撤回后重发），
/// 而进度 / 背包数据在这个时间尺度上基本不变。缓存的是最终 PNG 字节，命中时零渲染开销。</para>
/// </summary>
internal static class RenderGate
{
    /// <summary>同时渲染的卡片数上限：CPU 核数的一半，至少 1。</summary>
    private static readonly SemaphoreSlim Gate = new(Math.Max(1, Environment.ProcessorCount / 2));

    private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.Ordinal);

    private static readonly long TtlMs = 60_000;
    private const int MaxEntries = 64;

    private readonly record struct Entry(byte[] Png, long AtMs);

    /// <summary>
    /// 执行一次渲染。相同 <paramref name="scope"/> + <paramref name="payload"/> 的请求
    /// 在 60 秒内直接复用上次的结果。
    /// </summary>
    /// <param name="scope">用途标识（如 <c>progress</c> / <c>bag</c>），避免不同卡片撞 key。</param>
    /// <param name="payload">原始数据包 JSON —— 它就是这张卡的内容指纹。</param>
    /// <param name="render">真正的渲染动作，返回 PNG 字节；失败返回 <c>null</c>。</param>
    public static byte[]? Run(string scope, string payload, Func<byte[]?> render)
    {
        string key = BuildKey(scope, payload);

        if (TryGet(key, out byte[]? cached))
        {
            return cached;
        }

        Gate.Wait();
        try
        {
            // 排队期间可能已经有人画完了同一张卡。
            if (TryGet(key, out cached))
            {
                return cached;
            }

            byte[]? png = render();
            if (png is not null)
            {
                Store(key, png);
            }

            return png;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>清空缓存（数据源变化、测试时用）。</summary>
    public static void Clear()
    {
        Cache.Clear();
    }

    private static bool TryGet(string key, out byte[]? png)
    {
        if (Cache.TryGetValue(key, out Entry entry) && Environment.TickCount64 - entry.AtMs < TtlMs)
        {
            png = entry.Png;
            return true;
        }

        png = null;
        return false;
    }

    private static void Store(string key, byte[] png)
    {
        if (Cache.Count >= MaxEntries)
        {
            // 丢掉最早的一半，避免缓存无界增长。
            foreach (KeyValuePair<string, Entry> pair in Cache.OrderBy(item => item.Value.AtMs).Take(MaxEntries / 2).ToArray())
            {
                Cache.TryRemove(pair.Key, out _);
            }
        }

        Cache[key] = new Entry(png, Environment.TickCount64);
    }

    /// <summary>键 = scope + 载荷 SHA-256 前 16 字节，避免把几 KB 的背包 JSON 直接当字典键。</summary>
    private static string BuildKey(string scope, string payload)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return scope + ":" + Convert.ToHexString(hash, 0, 16);
    }
}
