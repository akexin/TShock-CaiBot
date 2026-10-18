using System.Text;
using Newtonsoft.Json;

namespace CaiBotWindy.Infrastructure;

/// <summary>
/// 结构化审计日志：把敏感操作写成 JSON Lines，供事后追责与排查。
///
/// <para>每天一个文件（<c>audit-YYYY-MM-DD.jsonl</c>），天然按天轮转，不会像运行日志那样无限膨胀。
/// 每行一条 JSON，字段：<c>ts / actor / group / action / detail / server / result</c>。</para>
///
/// <para>写失败只吞异常 —— 审计是旁路能力，不该反过来拖垮业务。未调用 <see cref="Configure"/>
/// 时所有写入都是空操作，方便在测试 / 预览环境安全使用。</para>
/// </summary>
public static class AuditLog
{
    private static readonly object Gate = new();
    private static string _directory = "";

    /// <summary>指定审计日志目录（绝对路径）。调用后审计才会真正落盘。</summary>
    public static void Configure(string directory)
    {
        _directory = directory;
    }

    /// <summary>
    /// 记一条审计。
    /// </summary>
    /// <param name="action">动作标识，如 <c>file_download</c> / <c>whitelist_check</c> / <c>whitelist_approve</c>。</param>
    /// <param name="detail">关键参数（路径 / 角色名 / 触发原因等）。</param>
    /// <param name="actor">操作者（QQ 号或玩家名）。</param>
    /// <param name="group">群 OpenID（私聊或服务端场景可为空）。</param>
    /// <param name="server">服务器名。</param>
    /// <param name="result">结果（ok / denied / need_login / not_found …）。</param>
    public static void Write(
        string action,
        string detail,
        string? actor = null,
        string? group = null,
        string? server = null,
        string result = "ok")
    {
        if (_directory.Length == 0)
        {
            return;
        }

        try
        {
            string line = JsonConvert.SerializeObject(new
            {
                ts = DateTime.UtcNow.ToString("O"),
                actor = actor ?? "",
                group = group ?? "",
                action,
                detail,
                server = server ?? "",
                result,
            });

            string file = Path.Combine(_directory, $"audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            lock (Gate)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(file, line + "\n", Encoding.UTF8);
            }
        }
        catch
        {
            // 审计失败不影响业务流程。
        }
    }
}
