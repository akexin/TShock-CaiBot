using CaiBotWindy.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 协议编解码测试。
///
/// <para><b>为什么优先测这里</b>：编解码是机器人与服务端之间唯一的通道，
/// 一个字节错位或类型判断失误，表现是「某条指令偶发失败 / 服务器端崩溃」，
/// 而且双方都是自己写的，出错时很难判断该怀疑哪一端。这里把边界钉死。</para>
/// </summary>
public class ProtocolCodecTests
{
    // ── BinaryCodec：bytes → base64 → gzip → base64 ──────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(64 * 1024)]
    public void BinaryCodec_RoundTrip_PreservesBytes(int size)
    {
        byte[] original = new byte[size];
        for (int i = 0; i < size; i++)
        {
            original[i] = (byte)(i * 31 % 256);
        }

        byte[] restored = BinaryCodec.Decode(BinaryCodec.Encode(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void BinaryCodec_RoundTrip_PreservesAllByteValues()
    {
        byte[] original = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        byte[] restored = BinaryCodec.Decode(BinaryCodec.Encode(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void BinaryCodec_Encode_ProducesBase64NotRawGzip()
    {
        // 外层必须是可放进 JSON 字符串的 Base64 —— 换成原始 gzip 会在 JSON 序列化时坏掉。
        string encoded = BinaryCodec.Encode([1, 2, 3]);

        Assert.NotEmpty(encoded);
        Assert.DoesNotContain("\n", encoded);
        Assert.True(Convert.TryFromBase64String(encoded, new byte[encoded.Length], out _));
    }

    [Fact]
    public void BinaryCodec_Encode_CompressesRepeatedContent()
    {
        // 全零数据在 Base64 后仍是 2/3 长度，gzip 必须把它压下去；否则「地图/存档」这类
        // 大块数据会撑爆单帧预算。
        byte[] zeros = new byte[64 * 1024];

        string encoded = BinaryCodec.Encode(zeros);

        Assert.True(
            encoded.Length < zeros.Length,
            $"压缩后长度 {encoded.Length} 不小于原始 {zeros.Length}，压缩环节可能失效");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BinaryCodec_Decode_EmptyInput_ReturnsEmptyArray(string input)
    {
        Assert.Empty(BinaryCodec.Decode(input));
    }

    [Fact]
    public void BinaryCodec_Encode_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => BinaryCodec.Encode(null!));
    }

    [Fact]
    public void BinaryCodec_Decode_InvalidBase64_Throws()
    {
        Assert.Throws<FormatException>(() => BinaryCodec.Decode("this-is-not-base64!!"));
    }

    // ── BotPacket：请求 / 通知包的字段契约 ───────────────────────────────────

    [Fact]
    public void RequestPacket_KeepsRequestIdAndDirection()
    {
        BotPacket packet = BotPacket.Request(PackageType.Whitelist, "req-42", new JObject { ["a"] = 1 });

        Assert.True(packet.IsRequest);
        Assert.Equal("req-42", packet.RequestId);
        Assert.Equal(PackageDirection.ToServer, packet.Direction);
        Assert.Equal(PackageType.Whitelist, packet.Type);
    }

    [Fact]
    public void NotifyPacket_WritesExplicitNullRequestId()
    {
        // 适配插件按「字段存在」判断解析分支，所以非请求包必须显式写 null，
        // 而不是干脆不写这个键。
        JObject json = BotPacket.Notify(PackageType.Heartbeat).ToJsonObject();

        Assert.True(json.ContainsKey("request_id"));
        Assert.Equal(JTokenType.Null, json["request_id"]!.Type);
        Assert.False(json.Value<bool>("is_request"));
    }

    [Fact]
    public void ToJson_IsCompact_NoNewlines()
    {
        // 适配插件单帧缓冲区只有 1024 字节，缩进会白吃掉几十字节预算。
        string json = BotPacket.Notify(PackageType.Heartbeat).ToJson();

        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        Assert.Null(BotPacket.Parse("{not json"));
        Assert.Null(BotPacket.Parse(""));
        Assert.Null(BotPacket.Parse("   "));
    }

    [Fact]
    public void Parse_MissingFields_FallsBackToDefaults()
    {
        BotPacket? packet = BotPacket.Parse("{}");

        Assert.NotNull(packet);
        Assert.Equal(ProtocolVersions.Default, packet!.Version);
        Assert.Equal(PackageDirection.ToBot, packet.Direction);
        Assert.False(packet.IsRequest);
        Assert.Null(packet.RequestId);
        Assert.Empty(packet.Payload.Properties());
    }

    [Fact]
    public void Parse_NotificationWithRequestId_DropsTheId()
    {
        // 非请求包带着 request_id 说明对端发错了；保留它会让 pending 表里留下幽灵条目。
        BotPacket? packet = BotPacket.Parse("""
            {"version":"2025.7.18","direction":"to_bot","type":"heartbeat","is_request":false,"request_id":"x"}
            """);

        Assert.NotNull(packet);
        Assert.Null(packet!.RequestId);
    }

    [Fact]
    public void Parse_Request_KeepsRequestId()
    {
        BotPacket? packet = BotPacket.Parse("""
            {"version":"2025.7.18","direction":"to_bot","type":"player_list","is_request":true,"request_id":"req-7","payload":{"k":"v"}}
            """);

        Assert.NotNull(packet);
        Assert.True(packet!.IsRequest);
        Assert.Equal("req-7", packet.RequestId);
        Assert.Equal("v", packet.Payload.GetString("k"));
    }

    [Fact]
    public void Parse_PayloadNotObject_FallsBackToEmpty()
    {
        BotPacket? packet = BotPacket.Parse("""
            {"type":"heartbeat","payload":"i-am-a-string"}
            """);

        Assert.NotNull(packet);
        Assert.Empty(packet!.Payload.Properties());
    }

    // ── PayloadReader：类型不符时给默认值而不是抛异常 ────────────────────────

    [Fact]
    public void GetString_NonStringToken_ConvertsInsteadOfThrowing()
    {
        // 适配插件曾在个别字段里把字符串写成数字，这里必须宽容。
        JObject payload = new() { ["num"] = 123, ["nil"] = JValue.CreateNull() };

        Assert.Equal("123", payload.GetString("num"));
        Assert.Equal("fallback", payload.GetString("nil", "fallback"));
        Assert.Equal("fallback", payload.GetString("missing", "fallback"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"not-a-bool\"", false)]
    public void GetBool_AcceptsBoolIntegerAndString(string tokenJson, bool expected)
    {
        JObject payload = new() { ["flag"] = JToken.Parse(tokenJson) };

        Assert.Equal(expected, payload.GetBool("flag"));
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("42.9", 42)]
    [InlineData("\"42\"", 42)]
    [InlineData("\"abc\"", 0)]
    [InlineData("true", 1)]
    [InlineData("false", 0)]
    public void GetInt_AcceptsNumberStringAndBool(string tokenJson, int expected)
    {
        JObject payload = new() { ["num"] = JToken.Parse(tokenJson) };

        Assert.Equal(expected, payload.GetInt("num"));
    }

    [Fact]
    public void GetStringList_SkipsBlankEntries()
    {
        JObject payload = new() { ["names"] = new JArray("a", "", "  ", "b", 123) };

        Assert.Equal(["a", "b", "123"], payload.GetStringList("names"));
    }

    [Fact]
    public void GetStringList_WrongType_ReturnsEmpty()
    {
        JObject payload = new() { ["names"] = "not-an-array" };

        Assert.Empty(payload.GetStringList("names"));
    }

    [Fact]
    public void GetIntList_SkipsUnparsableEntries()
    {
        JObject payload = new() { ["ids"] = new JArray(1, "2", "abc", 3.9) };

        Assert.Equal([1, 2], payload.GetIntList("ids"));
    }

    [Fact]
    public void GetItemSlots_ReadsPairsAndToleratesShortSlots()
    {
        // [[id, stack], [id]] —— 只有 id 的格子按 stack=0 处理。
        JObject payload = new()
        {
            ["inventory"] = new JArray(
                new JArray(39, 5),
                new JArray("40", "7"),
                new JArray(41)),
        };

        List<(int ItemId, int Stack)> slots = payload.GetItemSlots("inventory");

        Assert.Equal(3, slots.Count);
        Assert.Equal((39, 5), slots[0]);
        Assert.Equal((40, 7), slots[1]);
        Assert.Equal((41, 0), slots[2]);
    }

    [Fact]
    public void GetBoolMap_IsCaseInsensitive()
    {
        JObject payload = new() { ["process"] = new JObject { ["EyeOfCthulhu"] = true, ["Skeletron"] = false } };

        Dictionary<string, bool> map = payload.GetBoolMap("process");

        Assert.True(map["eyeofcthulhu"]);
        Assert.False(map["Skeletron"]);
    }

    [Fact]
    public void GetRank_ReadsTitleAndLines()
    {
        JObject payload = new()
        {
            ["rank"] = new JObject
            {
                ["title"] = "金币排行",
                ["rank_lines"] = new JObject { ["1"] = "alice 100" },
            },
        };

        (string title, Dictionary<string, string> lines) = payload.GetRank();

        Assert.Equal("金币排行", title);
        Assert.Equal("alice 100", lines["1"]);
    }

    [Fact]
    public void GetPluginList_ReadsPascalCaseFields()
    {
        JObject payload = new()
        {
            ["plugins"] = new JArray(new JObject
            {
                ["Name"] = "CaiBotLite",
                ["Version"] = "2026.10.3.2",
                ["Author"] = "Cai",
                ["Description"] = "适配插件",
            }),
        };

        List<TerrariaPluginInfo> plugins = payload.GetPluginList();

        TerrariaPluginInfo plugin = Assert.Single(plugins);
        Assert.Equal("CaiBotLite", plugin.Name);
        Assert.Equal("2026.10.3.2", plugin.Version);
    }
}
