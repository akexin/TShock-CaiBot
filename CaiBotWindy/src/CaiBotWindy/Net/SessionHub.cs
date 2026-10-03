using System.Collections.Concurrent;
using CaiBotWindy.Data;
using Windy.SDK;

namespace CaiBotWindy.Net;

/// <summary>
/// 在线连接表。以令牌为键保存 <see cref="ServerSession"/>，并提供按群 / 序号解析的能力。
/// </summary>
public sealed class SessionHub
{
    private readonly ConcurrentDictionary<string, ServerSession> sessions = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ServerSession> All => sessions.Values.ToList();

    public int Count => sessions.Count;

    public void Add(ServerSession session)
    {
        // 同一令牌重复连接时，先关掉旧连接，避免双份会话争抢响应。
        if (sessions.TryGetValue(session.Token, out ServerSession? existing) && !ReferenceEquals(existing, session))
        {
            _ = existing.CloseAsync("被新的连接取代");
        }

        sessions[session.Token] = session;
    }

    public void Remove(ServerSession session)
    {
        if (sessions.TryGetValue(session.Token, out ServerSession? current) && ReferenceEquals(current, session))
        {
            sessions.TryRemove(session.Token, out _);
        }
    }

    public ServerSession? FindByToken(string token)
    {
        return sessions.TryGetValue(token, out ServerSession? session) ? session : null;
    }

    /// <summary>取某个群下最近建立的一条连接。</summary>
    public ServerSession? FindByGroup(string groupOpenId)
    {
        return sessions.Values
            .Where(session => session.GroupOpenId == groupOpenId)
            .OrderByDescending(session => session.ConnectedAtUtc)
            .FirstOrDefault();
    }

    public List<ServerSession> FindAllByGroup(string groupOpenId)
    {
        return sessions.Values
            .Where(session => session.GroupOpenId == groupOpenId)
            .OrderBy(session => session.ConnectedAtUtc)
            .ToList();
    }

    /// <summary>
    /// 按群内展示序号解析服务器（序号与 <c>/服务器列表</c> 一致，1 起）。
    /// 若序号缺省（0）则取该群唯一 / 最近的一台。
    /// </summary>
    public bool TryResolve(
        string groupOpenId,
        int displayIndex,
        out ServerSession session,
        out ServerRecord record)
    {
        session = null!;
        record = null!;

        List<ServerRecord> servers = DataStore.GetServers(groupOpenId);
        if (servers.Count == 0)
        {
            return false;
        }

        ServerRecord? target = displayIndex > 0
            ? servers.FirstOrDefault(item => item.DisplayIndex == displayIndex)
            : servers.Count == 1
                ? servers[0]
                : servers.FirstOrDefault(item => FindByToken(item.Token) is not null);

        if (target is null)
        {
            return false;
        }

        ServerSession? found = FindByToken(target.Token);
        if (found is null || !found.IsOpen)
        {
            record = target;
            return false;
        }

        session = found;
        record = target;
        return true;
    }

    /// <summary>断开某个服务器并清空其令牌，等待服务端重新生成绑定码。</summary>
    public async Task<bool> UnbindAsync(ServerRecord record, string reason)
    {
        ServerSession? session = FindByToken(record.Token);
        if (session is not null)
        {
            try
            {
                await session.NotifyAsync(Protocol.PackageType.UnbindServer,
                    new Newtonsoft.Json.Linq.JObject { ["reason"] = reason }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Message.Yellow($"[CaiBotWindy] 发送解绑通知失败（可忽略）: {ex.Message}");
            }

            await session.CloseAsync("机器人主动解绑");
        }

        return DataStore.RemoveServer(record);
    }
}
