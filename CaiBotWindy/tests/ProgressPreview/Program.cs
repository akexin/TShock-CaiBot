using CaiBotWindy.Services;

// 卡片渲染自测：用样例数据渲染进度卡片（普通 / 醉酒 / 天顶三种世界）与背包卡片，
// 方便肉眼核对布局、字号与素材是否齐全。
//
//   dotnet run --project CaiBotWindy/tests/ProgressPreview -- [素材目录] [输出png] [真实payload.json]
//
// 第三个参数可选：传一个 look_bag 包的 payload JSON 文件，会额外渲染一张 -real.png，
// 用于拿线上真实数据（而不是样例）排障。

string assetRoot = args.Length > 0
    ? args[0]
    : Path.Combine("CaiBotWindy", "deploy", "Asserts");
string output = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "card-preview.png");
string? realJsonPath = args.Length > 2 ? args[2] : null;

if (!Directory.Exists(assetRoot))
{
    Console.Error.WriteLine($"素材目录不存在: {Path.GetFullPath(assetRoot)}");
    return 1;
}

RenderKit.Configure(assetRoot);
if (RenderKit.UnavailableReason is string reason)
{
    Console.Error.WriteLine($"渲染不可用: {reason}");
    return 1;
}

string directory = Path.GetDirectoryName(output) ?? ".";
string stem = Path.GetFileNameWithoutExtension(output);

foreach (string scenario in new[] { "normal", "drunk", "zenith" })
{
    ProgressSnapshot snapshot = ProgressRenderer.FromJson(SampleProgress(scenario));
    byte[]? png = ProgressRenderer.Render(snapshot);
    if (png is null)
    {
        Console.Error.WriteLine($"[进度/{scenario}] 渲染失败");
        return 1;
    }

    string path = Path.Combine(directory, $"{stem}-{scenario}.png");
    File.WriteAllBytes(path, png);
    Console.WriteLine($"[进度/{scenario}] {png.Length / 1024} KB -> {path}");
}

byte[]? bag = BagRenderer.Render(BagRenderer.FromJson(SampleBag()));
if (bag is null)
{
    Console.Error.WriteLine("[背包] 渲染失败");
    return 1;
}

string bagPath = Path.Combine(directory, $"{stem}-bag.png");
File.WriteAllBytes(bagPath, bag);
Console.WriteLine($"[背包] {bag.Length / 1024} KB -> {bagPath}");

// 离线玩家只发主背包（59 格），验证「仅主背包可用」的标注是否出现
byte[]? offlineBag = BagRenderer.Render(BagRenderer.FromJson(SampleBag(full: false)));
if (offlineBag is null)
{
    Console.Error.WriteLine("[背包/离线] 渲染失败");
    return 1;
}

string offlinePath = Path.Combine(directory, $"{stem}-bag-offline.png");
File.WriteAllBytes(offlinePath, offlineBag);
Console.WriteLine($"[背包/离线] {offlineBag.Length / 1024} KB -> {offlinePath}");

// 可选：拿线上真实 payload 出图（排障用 —— 先分清是「数据没有」还是「渲染没用」）
if (realJsonPath is not null)
{
    if (!File.Exists(realJsonPath))
    {
        Console.Error.WriteLine($"真实数据文件不存在: {realJsonPath}");
        return 1;
    }

    byte[]? real = BagRenderer.Render(BagRenderer.FromJson(File.ReadAllText(realJsonPath)));
    if (real is null)
    {
        Console.Error.WriteLine("[背包/真实数据] 渲染失败");
        return 1;
    }

    string realPath = Path.Combine(directory, $"{stem}-real.png");
    File.WriteAllBytes(realPath, real);
    Console.WriteLine($"[背包/真实数据] {real.Length / 1024} KB -> {realPath}");
}

return 0;

static string SampleProgress(string scenario)
{
    bool drunk = scenario == "drunk";
    bool zenith = scenario == "zenith";
    string icon = zenith ? "IconHallowCorruptionEverything" : drunk ? "IconCrimsonNotTheBees" : "IconCorruption";

    // 三种世界共用同一份「打了一半」的进度，便于横向对比配色与布局
    return $$"""
    {
      "process": {
        "King Slime": true,
        "Eye of Cthulhu": true,
        "Eater of Worlds or Brain of Cthulhu": false,
        "Eater of Worlds": true,
        "Brain of Cthulhu": true,
        "Queen Bee": true,
        "Deerclops": true,
        "Skeletron": true,
        "Wall of Flesh": true,
        "Queen Slime": true,
        "The Destroyer": false,
        "The Twins": false,
        "Skeletron Prime": true,
        "Plantera": false,
        "Golem": false,
        "Duke Fishron": false,
        "Empress of Light": false,
        "Lunatic Cultist": false,
        "Tower Solar": true,
        "Tower Nebula": true,
        "Tower Vortex": true,
        "Tower Stardust": false,
        "Moon Lord": false,
        "Pillars": false,
        "Goblins": true,
        "Pirates": false,
        "Frost": true,
        "Frost Moon": false,
        "Pumpkin Moon": false,
        "Martians": false,
        "DD2InvasionT1": true,
        "DD2InvasionT2": true,
        "DD2InvasionT3": false
      },
      "kill_counts": {
        "King Slime": 1, "Eye of Cthulhu": 3, "Eater of Worlds": 5, "Brain of Cthulhu": 66,
        "Queen Bee": 3, "Deerclops": 4, "Skeletron": 32, "Wall of Flesh": 34, "Queen Slime": 4,
        "The Destroyer": 3, "The Twins": 4, "Skeletron Prime": 5, "Plantera": 7, "Golem": 5,
        "Duke Fishron": 4, "Empress of Light": 4, "Lunatic Cultist": 5, "Moon Lord": 9
      },
      "boss_lock": {
        "Queen Bee": "明天05:44", "Wall of Flesh": "明天11:44", "Duke Fishron": "明天23:44"
      },
      "world_name": "TShockWorld",
      "drunk_world": {{(drunk ? "true" : "false")}},
      "zenith_world": {{(zenith ? "true" : "false")}},
      "world_icon": "{{icon}}"
    }
    """;
}

static string SampleBag(bool full = true)
{
    // full=true 模拟在线玩家（LookOnline 会拼出 350 格）；
    // full=false 模拟离线玩家存档（LookOffline 只发主背包 59 格）。
    (int Id, int Stack)[] slots = new (int, int)[full ? 350 : 59];

    // 主背包 0-58（71~74 = 铜/银/金/铂金币，是正常正 id）
    (int Id, int Stack)[] bag =
    [
        (71, 248), (72, 36), (73, 7), (3507, 1), (3505, 1), (3506, 1), (8, 99), (29, 3),
        (1291, 12), (4956, 1), (706, 10), (2589, 10), (3330, 4), (499, 1484), (188, 51),
        (361, 9999), (965, 35), (307, 7), (4934, 1), (27, 2), (28, 2), (5, 7), (75, 1),
        (2364, 1), (2426, 3), (3000, 1), (1305, 1), (218, 1), (1231, 1),
    ];
    Array.Copy(bag, slots, Math.Min(bag.Length, slots.Length));

    if (full)
    {
        // 装备与饰品 59-98（20 护甲 + 10 染料 + 5 宠物 + 5 宠物染料）
        (int Id, int Stack)[] gear =
        [
            (4956, 1), (4934, 1), (499, 1), (188, 1), (1291, 1), (3330, 1),
            (3000, 1), (1305, 1), (1231, 1), (218, 1), (2426, 1), (75, 1),
        ];
        Array.Copy(gear, 0, slots, 59, gear.Length);

        // 钱罐 99-138
        (int Id, int Stack)[] piggy = [(87, 1), (71, 999), (72, 500), (73, 100), (74, 20)];
        Array.Copy(piggy, 0, slots, 99, piggy.Length);

        // 保险箱 139-178
        (int Id, int Stack)[] safe = [(88, 1), (188, 99), (499, 99), (361, 300)];
        Array.Copy(safe, 0, slots, 139, safe.Length);

        // 防御者熔炉 180-219（179 是垃圾桶，跳过）
        (int Id, int Stack)[] forge = [(3813, 1), (965, 500)];
        Array.Copy(forge, 0, slots, 180, forge.Length);

        // 虚空袋 220-259：放 23 件，验证多行分区与数量显示
        (int Id, int Stack)[] vault =
        [
            (4131, 1), (75, 1), (218, 1), (1231, 1), (1305, 1), (965, 50), (307, 20),
            (29, 5), (8, 200), (706, 30), (2589, 25), (3330, 8), (3000, 3), (2426, 6),
            (27, 10), (28, 10), (5, 15), (2364, 2), (1291, 40), (499, 700), (188, 88),
            (361, 1000), (4934, 1),
        ];
        Array.Copy(vault, 0, slots, 220, vault.Length);
    }

    string slotJson = string.Join(", ", slots.Select(slot => $"[{slot.Id}, {slot.Stack}]"));

    return $$"""
    {
      "name": "星",
      "exist": 1,
      "life": "420/420",
      "mana": "200/200",
      "quests_completed": 12,
      "inventory": [{{slotJson}}],
      "buffs": [1, 2, 3, 5, 8, 11, 16, 26],
      "enhances": [29, 109, 1291],
      "economic": {
        "Coins": "12 金 34 银 56 铜",
        "LevelName": "Lv.7 矿工",
        "Skill": "挖矿 III"
      }
    }
    """;
}
