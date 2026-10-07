using System.Security.Cryptography;
using System.Text;
using CaiBotWindy.Protocol;
using CaiBotWindy.Services;
using Newtonsoft.Json.Linq;

namespace ProtocolTest;

/// <summary>
/// 协议层与数据层的离线自检。运行方式：
/// <code>dotnet run --project tests/ProtocolTest -c Release</code>
/// 参数：可选传入图鉴数据目录，默认 <c>../../data</c>（相对仓库根）。
/// </summary>
internal static class Program
{
    private static int passed;
    private static int failed;
    private const int FrameLimit = 1024;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 忽略 --nologo 之类的开关，只把非 '-' 开头的参数当作数据目录。
        string[] positional = args.Where(a => !a.StartsWith('-')).ToArray();
        string dataDirectory = positional.Length > 0
            ? positional[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data"));

        Console.WriteLine("=== 1. BinaryCodec 往返 ===");
        BinaryCodecRoundTrip("空", []);
        BinaryCodecRoundTrip("单字节", [0x00]);
        BinaryCodecRoundTrip("全字节值", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
        BinaryCodecRoundTrip("文本", Encoding.UTF8.GetBytes("泰拉瑞亚 Terraria 1.4.5.8"));
        BinaryCodecRoundTrip("1MB 随机", RandomNumberGenerator.GetBytes(1024 * 1024));

        Console.WriteLine("\n=== 2. BinaryCodec 导出样本（供 Python 侧交叉验证）===");
        string samplePath = Path.Combine(AppContext.BaseDirectory, "codec-sample.txt");
        byte[] sample = Encoding.UTF8.GetBytes("CaiBotWindy <-> CaiBotLite 协议自检 payload 0123456789");
        string encoded = BinaryCodec.Encode(sample);
        File.WriteAllText(samplePath, encoded, Encoding.ASCII);
        Check("编码结果只含 Base64 字符", encoded.All(c => char.IsLetterOrDigit(c) || c is '+' or '/' or '='));
        Console.WriteLine($"   样本已写出: {samplePath}");
        Console.WriteLine($"   原文 {sample.Length} 字节 -> 编码 {encoded.Length} 字符");

        Console.WriteLine("\n=== 3. 协议版本映射 ===");
        Check("hello 版本 = 2025.7.18", ProtocolVersions.For(PackageType.Hello) == "2025.7.18");
        Check("unbind_server 版本 = 2025.7.25", ProtocolVersions.For(PackageType.UnbindServer) == "2025.7.25");
        Check("error 版本 = 2026.2.14", ProtocolVersions.For(PackageType.Error) == "2026.2.14");
        Check("ping 线名 = \"ping\"", PackageType.Ping.ToWire() == "ping");
        Check("ping 可反向解析", ProtocolNames.ParseType("ping") == PackageType.Ping);
        Check("server_file 版本 = 2026.10.3.2", ProtocolVersions.For(PackageType.ServerFile) == "2026.10.3.2");
        Check("server_file 线名 = \"server_file\"", PackageType.ServerFile.ToWire() == "server_file");
        Check("server_file 可反向解析", ProtocolNames.ParseType("server_file") == PackageType.ServerFile);
        Check("not_registered 线名正确", WhitelistResult.NotRegistered.ToWire() == "not_registered");
        Check("not_registered 可反向解析", ProtocolNames.ParseWhitelistResult("not_registered") == WhitelistResult.NotRegistered);

        Console.WriteLine("\n=== 4. 枚举线上字符串双向映射 ===");
        bool allWireRoundTrip = true;
        foreach (PackageType type in Enum.GetValues<PackageType>())
        {
            string wire = type.ToWire();
            if (type != PackageType.Unknown && ProtocolNames.ParseType(wire) != type)
            {
                allWireRoundTrip = false;
                Console.WriteLine($"   !! {type} -> {wire} -> {ProtocolNames.ParseType(wire)}");
            }
        }

        Check("全部 PackageType 线名可往返", allWireRoundTrip);
        Check("未知类型归入 Unknown", ProtocolNames.ParseType("no_such_packet") == PackageType.Unknown);
        Check("大小写不敏感解析", ProtocolNames.ParseType("PLAYER_LIST") == PackageType.PlayerList);
        Check("ServerType tmodloader 小写可解析", ProtocolNames.TryParseServerType("tmodloader", out ServerType tml) && tml == ServerType.TModLoader);
        Check("ServerType 线名 tModLoader", ServerType.TModLoader.ToWire() == "tModLoader");
        bool allWhitelistRoundTrip = Enum.GetValues<WhitelistResult>()
            .Where(r => r != WhitelistResult.Unknown)
            .All(r => ProtocolNames.ParseWhitelistResult(r.ToWire()) == r);
        Check("全部 WhitelistResult 可往返", allWhitelistRoundTrip);

        Console.WriteLine("\n=== 5. BotPacket 封包结构 ===");
        BotPacket request = BotPacket.Request(PackageType.PlayerList, "req-1");
        JObject json = request.ToJsonObject();
        Check("direction = to_server", json.Value<string>("direction") == "to_server");
        Check("type = player_list", json.Value<string>("type") == "player_list");
        Check("is_request = true", json.Value<bool>("is_request"));
        Check("request_id = req-1", json.Value<string>("request_id") == "req-1");
        Check("四个必需键齐全", json.ContainsKey("version") && json.ContainsKey("direction")
            && json.ContainsKey("type") && json.ContainsKey("is_request"));

        BotPacket notify = BotPacket.Notify(PackageType.Whitelist, new JObject { ["exist"] = true });
        JObject notifyJson = notify.ToJsonObject();
        Check("通知包 is_request = false", !notifyJson.Value<bool>("is_request"));
        Check("通知包 request_id 键存在且为 null", notifyJson.ContainsKey("request_id") && notifyJson["request_id"]!.Type == JTokenType.Null);

        Console.WriteLine("\n=== 6. 单帧 1024 字节约束 ===");
        BotPacket frame = BotPacket.Request(PackageType.CallCommand, Guid.NewGuid().ToString("N"),
            new JObject { ["command"] = "/give 4956 999" });
        int frameBytes = Encoding.UTF8.GetByteCount(frame.ToJson());
        Check($"典型指令包 {frameBytes} 字节 <= {FrameLimit}", frameBytes <= FrameLimit);
        Check("紧凑输出不含缩进换行", !frame.ToJson().Contains('\n'));

        // request_id 用 Guid "N"（32 字符）时，payload 预算还剩多少。
        int budget = FrameLimit - Encoding.UTF8.GetByteCount(
            BotPacket.Request(PackageType.Unknown, new string('x', 32)).ToJson());
        Console.WriteLine($"   request_id 取 32 字符 Guid 时，payload 可用预算约 {budget} 字节");

        Console.WriteLine("\n=== 7. BotPacket.Parse 容错 ===");
        Check("空串返回 null", BotPacket.Parse("") is null);
        Check("非 JSON 返回 null", BotPacket.Parse("not json") is null);
        BotPacket? missingDirection = BotPacket.Parse("""{"type":"hello","is_request":false,"payload":{}}""");
        Check("direction 缺失按 to_bot 处理", missingDirection?.Direction == PackageDirection.ToBot);
        BotPacket? badDirection = BotPacket.Parse("""{"direction":"侧向","type":"hello","payload":{}}""");
        Check("direction 非法按 to_bot 处理", badDirection?.Direction == PackageDirection.ToBot);
        BotPacket? isRequestTrue = BotPacket.Parse("""{"direction":"to_bot","type":"hello","is_request":true,"request_id":"abc","payload":{"a":1}}""");
        Check("is_request=true 时保留 request_id", isRequestTrue?.RequestId == "abc");
        BotPacket? isRequestFalse = BotPacket.Parse("""{"direction":"to_bot","type":"hello","is_request":false,"request_id":"abc","payload":{}}""");
        Check("is_request=false 时清空 request_id", isRequestFalse?.RequestId is null);
        BotPacket? nullPayload = BotPacket.Parse("""{"type":"hello"}""");
        Check("payload 缺失时给空对象", nullPayload?.Payload.Count == 0);

        Console.WriteLine("\n=== 8. PayloadReader 类型宽容 ===");
        JObject p = JObject.Parse("""
        {
          "name": "ACai",
          "count": 12,
          "exists_int": 1,
          "exists_bool": true,
          "exists_str": "true",
          "absent": null,
          "inventory": [[4956, 1], [74, 99], ["166", "5"]],
          "buffs": [1, 2, "3"],
          "process": { "Eye of Cthulhu": true, "Moon Lord": false },
          "ranks": { "ACai": 42, "Bob": "7" },
          "names": { "a": "x", "b": 5 },
          "rank": { "title": "金币排行", "rank_lines": { "ACai": "100" } },
          "plugins": [ { "Name": "TShock", "Version": "6.2.1.0", "Author": "Nyx", "Description": "d" } ]
        }
        """);

        Check("GetString 取字符串", p.GetString("name") == "ACai");
        Check("GetString 对数字转文本", p.GetString("count") == "12");
        Check("GetString 缺失用兜底", p.GetString("nope", "?") == "?");
        Check("GetString 遇到 null 用兜底", p.GetString("absent", "?") == "?");
        Check("GetBool 整型非 0 为真", p.GetExists("exists_int"));
        Check("GetBool 布尔直读", p.GetBool("exists_bool"));
        Check("GetBool 字符串解析", p.GetBool("exists_str"));
        Check("GetBool 缺失用兜底", p.GetBool("nope") == false);
        Check("GetInt 缺失用兜底", p.GetInt("nope", -1) == -1);

        List<(int ItemId, int Stack)> slots = p.GetItemSlots("inventory");
        Check("GetItemSlots 三条", slots.Count == 3);
        Check("GetItemSlots 数字对", slots[0] == (4956, 1));
        Check("GetItemSlots 字符串对也认", slots[2] == (166, 5));
        Check("GetIntList 混合类型", p.GetIntList("buffs").SequenceEqual([1, 2, 3]));
        Check("GetBoolMap 真假正确", p.GetBoolMap("process")["Eye of Cthulhu"] && !p.GetBoolMap("process")["Moon Lord"]);
        Check("GetIntMap 数字与字符串", p.GetIntMap("ranks")["ACai"] == 42 && p.GetIntMap("ranks")["Bob"] == 7);
        Check("GetStringMap 数字转文本", p.GetStringMap("names")["b"] == "5");
        (string title, Dictionary<string, string> lines) = p.GetRank();
        Check("GetRank 解析 title", title == "金币排行");
        Check("GetRank 解析 lines", lines["ACai"] == "100");
        Check("GetPluginList 解析 PascalCase", p.GetPluginList()[0].Name == "TShock");

        Console.WriteLine("\n=== 9. 图鉴数据加载 ===");
        Console.WriteLine($"   数据目录: {dataDirectory}");
        if (!Directory.Exists(dataDirectory))
        {
            Check("数据目录存在", false);
        }
        else
        {
            TerrariaData.Load(dataDirectory);
            Check($"物品 6196 条（实际 {TerrariaData.Items.Count}）", TerrariaData.Items.Count == 6196);
            Check($"生物 762 条（实际 {TerrariaData.Npcs.Count}）", TerrariaData.Npcs.Count == 762);
            Check($"弹幕 1136 条（实际 {TerrariaData.Projects.Count}）", TerrariaData.Projects.Count == 1136);
            Check($"增益 400 条（实际 {TerrariaData.Buffs.Count}）", TerrariaData.Buffs.Count == 400);
            Check($"修饰语 98 条（实际 {TerrariaData.Prefixes.Count}）", TerrariaData.Prefixes.Count == 98);

            Console.WriteLine("\n=== 10. 图鉴检索 ===");
            ItemInfo? byId = TerrariaData.GetItemById(4956);
            Check("ID 4956 能取到物品", byId is not null);
            Console.WriteLine($"   ID 4956 -> {byId?.Name}（伤害 {byId?.Damage}，最大堆叠 {byId?.MaxStack}，价值 {byId?.MonetaryValue.Format()}）");

            List<ItemInfo> byName = TerrariaData.SearchItems("泰拉刃");
            Check($"按名称检索「泰拉刃」命中 {byName.Count} 条", byName.Count > 0);
            if (byName.Count > 0)
            {
                Console.WriteLine($"   最高分结果 -> {byName[0].Name}（ID {byName[0].ItemId}）");
            }

            List<ItemInfo> byNumeric = TerrariaData.SearchItems("4956");
            Check("按数字串检索优先走 ID 精确匹配", byNumeric.Count == 1 && byNumeric[0].ItemId == 4956);

            NpcInfo? boss = TerrariaData.SearchNpcs("月亮领主").FirstOrDefault();
            Check("能搜到「月亮领主」", boss is not null);
            Console.WriteLine($"   月亮领主 -> ID {boss?.NpcId}，生命 {boss?.LifeMax}，伤害 {boss?.Damage}");

            Check("弹幕 ID 1 名为木箭", TerrariaData.GetProjectileById(1)?.Name == "木箭");
            Check("增益 ID 1 名为黑曜石皮", TerrariaData.GetBuffById(1)?.Name == "黑曜石皮");
            Check("修饰语 ID 1 名为「大」", TerrariaData.GetPrefixById(1)?.Name == "大");

            // 模糊匹配：故意少打一个字
            List<ItemInfo> fuzzy = TerrariaData.SearchItems("泰拉");
            Check($"模糊检索「泰拉」命中 {fuzzy.Count} 条", fuzzy.Count > 0);
            Check("空查询返回空结果", TerrariaData.SearchItems("   ").Count == 0);
            Check("不存在的名称返回空结果", TerrariaData.SearchItems("zzzzzzzzzz").Count == 0);

            Console.WriteLine("\n=== 11. 货币格式化 ===");
            Check("零价值显示无价之宝", new CoinValue().Format() == "无价之宝");
            Check("铂金优先", new CoinValue { Platinum = 1, Gold = 2, Silver = 3, Copper = 4 }.Format() == "1铂 2金 3银 4铜");
            Check("跳过高位零值", new CoinValue { Silver = 5, Copper = 6 }.Format() == "5银 6铜");
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 48));
        Console.WriteLine($"通过 {passed} 项，失败 {failed} 项");
        return failed == 0 ? 0 : 1;
    }

    private static void BinaryCodecRoundTrip(string name, byte[] data)
    {
        string encoded = BinaryCodec.Encode(data);
        byte[] decoded = BinaryCodec.Decode(encoded);
        bool ok = decoded.AsSpan().SequenceEqual(data);
        Check($"往返一致：{name}（{data.Length} 字节）", ok);
    }

    private static void Check(string label, bool condition)
    {
        if (condition)
        {
            passed++;
            Console.WriteLine($"  [OK] {label}");
        }
        else
        {
            failed++;
            Console.WriteLine($"  [FAIL] {label}");
        }
    }
}
