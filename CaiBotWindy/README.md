# CaiBotWindy

用 **Windy**（QQ 官方机器人 SDK，.NET 10）实现的泰拉瑞亚服务器管理机器人，**自建 Bot 端**，兼容 **CaiBotLite** 的全部数据包接口。

它同时扮演两个角色：

1. **Windy 插件** —— 51 条 QQ 指令，覆盖服务器查询、地图下载、白名单、群管理、图鉴检索。
2. **Bot 端 HTTP / WebSocket 服务** —— 让 TShock 侧的 `CaiBotLite` 适配插件直接连进来，无需再依赖第三方 Bot 服务。

---

## 1. 架构

```
QQ 客户端
   │  ① 用户发指令（群聊 / 单聊）
   ▼
QQ 开放平台  ──WebSocket──▶  Windy 宿主
                               │  加载
                               ▼
                        CaiBotWindy.dll（本插件）
                               │
        ┌──────────────────────┼──────────────────────┐
        │  ② HTTP/WS 服务端     │  ③ 指令 + 渲染         │
        │  :22338              │   Markdown + 键盘      │
        └──────────────────────┼──────────────────────┘
                               ▲
              wss 反向长连接     │  ④ 服务端主动连出，无需开放入站端口
                               │
                     TShock + CaiBotLite 适配插件
                               │  ⑤ 读写世界 / 玩家 / 白名单
                               ▼
                        Terraria 1.4.5.8 服务端
```

关键点：**TShock 侧主动连出**，所以你不需要在 TShock 机器上开任何入站端口。

数据包信封（与适配插件 `Package.cs` 一一对应）：

```json
{
  "version": "2025.7.18",
  "direction": "to_server",
  "type": "player_list",
  "is_request": true,
  "request_id": "…",
  "payload": {}
}
```

---

## 2. 环境要求

| 项目 | 版本 | 说明 |
| --- | --- | --- |
| .NET SDK | **10.0** | 编译用。本机已验证 10.0.401 |
| .NET Runtime | 10.0 | Windy 宿主运行用 |
| Windy | 最新 | QQ 官方 Bot SDK 宿主 |
| TShock | 6.2.1.0 / Terraria 1.4.5.8 | 服务端 |
| CaiBotLite 适配插件 | 对应版本 | 装在 TShock 的 `ServerPlugins` |

---

## 3. 目录结构

```
CaiBotWindy/
├── CaiBotWindy.slnx                 解决方案（含宿主与自检工程）
├── data/                            图鉴数据（随产物复制到 Data/）
│   ├── item_id.json                 6196 条物品
│   ├── npc_id.json                  762 条生物
│   ├── project_id.json              1136 条弹幕
│   ├── buff_id.json                 400 条增益
│   └── prefix_id.json               98 条修饰语
├── menu/                            QQ 开放平台配置（随产物复制到 Menu/）
│   ├── menu.json                    自定义菜单（PUT /v2/menu 请求体）
│   └── panels.json                  7 个指令面板（POST /v2/panels 请求体数组）
├── scripts/
│   ├── gen_menu.py                  生成并校验 menu/ 下两个文件
│   └── check_menu.py                交叉校验：菜单引用 vs C# 已注册指令
├── src/CaiBotWindy/                 插件本体（25 个 .cs，约 5400 行）
│   ├── Plugin.cs                    插件入口
│   ├── App.cs                       共享状态
│   ├── PluginConfig.cs              配置模型
│   ├── Protocol/                    协议层：枚举 / 封包 / 二进制编解码
│   ├── Data/PluginData.cs           持久化存储
│   ├── Net/                         HTTP + WebSocket 服务端、会话、临时文件
│   ├── Services/                    图鉴数据、进度卡片渲染、界面渲染、白名单、权限
│   └── Commands/                    7 个文件、51 条指令
├── tests/ProtocolTest/              离线自检（68 项断言）
└── tests/ProgressPreview/           进度卡片渲染预览（输出 PNG，肉眼核对布局）
```

---

## 4. 构建

```bash
# SDK 装在工作区独立目录（不污染系统环境变量）
./dotnet-sdk/dotnet.exe build CaiBotWindy/CaiBotWindy.slnx -c Release
```

产物位置：

```
CaiBotWindy/src/CaiBotWindy/bin/Release/net10.0/
├── CaiBotWindy.dll      ← 要部署的插件
├── Data/                ← 图鉴数据（自动复制）
└── Menu/                ← 菜单面板配置（自动复制）
```

> 进度卡片与背包卡片渲染依赖 **SkiaSharp**（华为云 NuGet 镜像，见 `CaiBotWindy/NuGet.config`）。
> 需要额外部署 `SkiaSharp.dll` 与 `libSkiaSharp.dll`，见下一节。
> 渲染自测（一次输出进度 3 张 + 背包 2 张）：
> `dotnet run --project CaiBotWindy/tests/ProgressPreview -- CaiBotWindy/deploy/Asserts /tmp/card`
> → 生成 `/tmp/card-normal.png`、`/tmp/card-drunk.png`、`/tmp/card-zenith.png`、
> `/tmp/card-bag.png`（含虚空袋/钱罐/保险箱/熔炉的完整 350 格）、`/tmp/card-bag-offline.png`（仅主背包）。

拉取官方素材（进度卡片 + 图鉴图标）：

```bash
# 进度渲染必需：背景 / Boss 头像 / 世界图标 / 字体（约 70 个文件）
python CaiBotWindy/scripts/fetch_assets.py core

# 全部素材：含图鉴图标，共 8508 个文件 / 约 48 MB
python CaiBotWindy/scripts/fetch_assets.py all \
  --tree CaiBotWindy/scripts/.caibotlite-tree.json --workers 20
```

> 脚本走 `cdn.jsdelivr.net` 镜像逐文件高并发抓取（GitHub 直连与 tarball 在本机都拿不下来）。
> **已存在且非空的文件会跳过**，所以可以随时中断、反复重跑续抓；偶发 `HTTP 000` 是 CDN 限流，
> 降 `--workers` 到 4~8 再跑一遍即可（8508 个文件全量约 11 分钟，补齐失败项通常 <1 分钟）。
> `--tree` 指向本地缓存的 git tree（`api.github.com` 从 Python 走不通），不传则脚本自己去拉。
> 每个文件落盘时都会剥掉仓库里的 `assets/` 前缀，保证 HTTP 路由是 `/assets/images/items/Item_39.png`。

---

## 5. 部署到 Windy

把下面内容放到 **Windy 运行目录**（即 Windy 主程序所在目录，代码内称 `WindyRuntime.BasicPath`）：

```
<Windy 运行目录>/
├── Plugins/
│   └── CaiBotWindy.dll                   ← 从产物复制
├── SkiaSharp.dll                         ← 卡片渲染（托管）
├── libSkiaSharp.dll                      ← 卡片渲染（原生，win-x64）
├── Data/                                 ← 从产物复制（5 个 json）
├── Menu/                                 ← 从产物复制（2 个 json）；漏拷会自动补种
├── Config/
│   └── CaiBotWindy.json                  ← 首次启动自动生成
└── Asserts/                              ← 素材根目录，与 HTTP 路由 /assets/ 同源
    ├── fonts/LXGWWenKaiMono-Medium.ttf   ← 卡片字体
    └── images/
        ├── backgrounds/                  ← ProgressBackground.png（进度自绘底图）/ BagBackground.png（背包自绘底图）
        │                                    + 官方 Background_1~5.png（当前不再引用，仅留档）
        ├── bosses/                       ← 进度卡片 Boss 头像（name 与 progress 包 key 一致）
        ├── world_icon/                   ← 世界图标，文件名 = world_icon 字段
        ├── items/Item_<id>.png           ← 图鉴物品图标 + 进度锁图标 Item_5328.png
        ├── npcs/NPC_<id>.png
        ├── projectiles/Projectile_<id>.png
        └── buffs/Buff_<id>.png
```

> `SkiaSharp.dll` 由插件的程序集解析器从 Windy 运行目录加载；`libSkiaSharp.dll` 由插件启动时
> 按绝对路径预加载（宿主 `Windy.exe` 的 deps.json 里没有它，常规 RID 探测找不到）。
> 两者缺失时**不会崩**，只是进度查询自动退回文本列表。

启动 Windy，控制台应出现：

```
[CaiBotWindy] 图鉴数据已加载: 物品 6196 / 生物 762 / 弹幕 1136 / 增益 400 / 前缀 98
[CaiBotWindy] v2026.10.3 已就绪。监听 http://+:22338/，公网地址 …
```

> `Asserts/` 目录名沿用 Windy 生态惯例（注意是 **Asserts** 不是 Assets）。目录结构与官方
> `UnrealMultiple/CaiBotLite` 仓库的 `assets/` **逐字对应**（剥掉 `assets/` 前缀），用
> `scripts/fetch_assets.py` 可直接同步。素材缺失时图鉴仍可查询、进度查询退回文本，只是不出图。

> `Menu/` 下的两份配置**同时内嵌在 DLL 里**。若部署时漏拷该目录（或文件为空），插件启动会自动从内置资源补种，并打印
> `运行目录缺少菜单配置，已从内置资源补种 2 个文件到 Menu\ 目录。`
> 补种**只补缺失的文件、不覆盖已有内容**，因此你可以在运行目录里直接修改这两份 json 而不必重新编译。

### 卡片渲染（`/进度查询` 与 `/查背包`）

两个出图命令共用一套渲染底座，缺素材时**不崩、只退回文本**：

| 文件 | 职责 |
| --- | --- |
| `Services/RenderKit.cs` | 字体 / 素材加载与缓存、SkiaSharp 原生库预加载、画布、缩放、圆角面板、PNG 编码 |
| `Services/TextStyle.cs` | 字号封装 + **8 方向描边**（背景明暗不定时保证任何底色上都读得清） |
| `Services/ProgressRenderer.cs` | 进度卡片（1080×1440，底色星空自绘） |
| `Services/BagRenderer.cs` | 背包卡片（宽 976 × **高度动态**，按容器分区） |

#### 进度卡片的底图

底图是**自绘的夜色星空** `backgrounds/ProgressBackground.png`（1080×1440，3:4），三个世界共用一张。

- 原始产物 `CaiBotWindy/assets/ProgressBackground.source.png`（1024×1536）；处理脚本：
  `python scripts/prepare_card_background.py <源图> <输出> [top]` —— 取 3:4 裁剪窗口（`top` 避开底部「AI生成」水印）→ LANCZOS 缩放到 1080×1440。
- 换图后**不必动配色**：底图固定为暗色（实测均值亮度约 27），`Palette.ForScene()` 固定浅色字 + 8 方向描边。
  配色不再按世界类型切换 —— 原先官方三张底图亮度从 41（天顶）到 187（普通）跨度极大，才被迫维护两套。
- 官方 `Background_1~5` 仍留在素材目录（`Background_2/3/5` 是官方进度卡底图），当前不再引用。

#### 背包卡片的容器分区

`lookbag` 包的 `inventory` 数组**不是只有背包**：在线玩家（`LookOnline`）会把 Terraria 的
`NetItem.MaxInventory = 350` 格一次性发过来，各容器是固定下标段，与 `NetItem.*Index` 常量一一对应：

| 容器 | 下标 | 格数 |
| --- | --- | --- |
| 背包 | 0–58 | 59 |
| 装备与饰品（护甲 20 + 染料 10 + 宠物 5 + 宠物染料 5） | 59–98 | 40 |
| **钱罐**（Piggy Bank） | 99–138 | 40 |
| **保险箱**（Safe） | 139–178 | 40 |
| 垃圾桶 | 179 | 1 |
| **防御者熔炉**（Defender's Forge） | 180–219 | 40 |
| **虚空袋 / 虚空保险库**（Void Vault） | 220–259 | 40 |
| 装填 2 / 3 的装备与染料 | 260–349 | 90 |

渲染策略：

- 每个容器一个分区（标题 + 容器图标 + 件数），**只画到最后一个有内容的行**，空容器压成一行灰色提示 ——
  这样新号不会出现 250 格空格子，老玩家也不会因为容器多而把图拉得极长（高度动态计算）。
- **每个物品都标数量**（含 1）。官方 `lookbag.py` 只在 `>1` 时标，玩家会以为「没有数量」。
- 物品图标用 `Fit(..., allowUpscale: true)` 放大，否则 Terraria 那些 16×16 的小图标在 86px 格子里只占 1/3。
- 内容区铺一层统一深色底再画格子与文字，否则分区标题会压到背景的火把/木箱上读不清。
- **离线玩家例外**：`LookOffline` 只发主背包 59 格（TShock 的 `PlayerData` 不含银行数据），
  此时卡片右下角会标注「离线玩家存档：仅主背包可用」，避免被误读成「虚空袋是空的」。

> ⚠️ `RenderKit.Asset()` 返回的是**共享缓存位图，调用方绝对不能 `Dispose`**。释放后第二次命中缓存会拿到已销毁对象，
> 在 `SKImage.FromBitmap` 处直接 **0xC0000005 原生崩溃**（不是托管异常，抓不到）。需要改图先 `Copy()`。
> `Asset()` 内部已加 `Handle != IntPtr.Zero` 自愈检测，但不要依赖它。

**手机可读性的唯一决定因素**：缩略图上看到的字号 ≈ `(字号 / 画布宽) × 聊天窗口图片宽`（约 240pt）。
所以关键不是「字号绝对值」，而是**字号占画布宽的比例**：

| 画布 | 正文字号 | 占比 | 缩略后约 | 结论 |
| --- | --- | --- | --- | --- |
| 1920×1080（官方横版） | 46 | 0.024 | 5.8pt | 糊成一团，读不了 |
| **1080×1440（本地竖版）** | 44 | 0.041 | 9.8pt | 可读 |
| **1080×1440（Boss 标签）** | 52 | 0.048 | 11.5pt | **清晰** |

官方 16:9 横图缩到手机宽度后只剩 240×135pt，面积只有竖版（240×320pt）的 42%。**改版式时不要退回横版。**

---

## 6. 配置 `Config/CaiBotWindy.json`

```jsonc
{
  // HTTP / WebSocket 监听前缀，HttpListener 语法，+ 表示全部网卡
  "ListenPrefixes": ["http://+:22338/"],

  // 对外可访问基地址（不带结尾斜杠）。用于把本地图标转成 QQ 能拉取的 URL。
  // 留空则所有图片不发送。例："https://bot.example.com:22338"
  "PublicBaseUrl": "",

  // 机器人 AppID，用于 QQ 侧域名校验接口 /{AppId}.json
  "BotAppId": "",

  // 拥有全部权限的用户 OpenID
  "OwnerOpenIds": [],

  // 跨群生效的管理员 OpenID
  "Operators": [],

  "RequestTimeoutSeconds": 10,        // 普通 RPC 超时
  "FileRequestTimeoutSeconds": 60,    // 地图 / 世界文件类 RPC 超时
  "BindCodeLifetimeMinutes": 30,      // 绑定码有效期
  "DownloadFileLifetimeMinutes": 10,  // /download/{file_id} 临时文件保留时长

  // 单帧 JSON 软上限。适配插件接收缓冲区只有 1024 字节且只解析最后一帧，
  // 超过会记警告（详见第 12 节）。
  "MaxOutgoingFrameBytes": 1000,

  "MaxServersPerGroup": 5,            // 单群最多绑定服务器数
  "MaxPanels": 20,                    // QQ 平台面板数量上限

  "DataDirectory": "Data",            // 图鉴数据目录
  "AssetDirectory": "Asserts",        // 图标素材目录
  "StorageDirectory": "Config/CaiBotWindy",  // 运行数据（store.json / temp/）

  "Debug": false                      // 打印收到的每个数据包
}
```

### 关于 `PublicBaseUrl` 与图片显示

本项目有**两条完全不同的出图链路**，排障时先分清是哪一条：

| 链路 | 用在哪 | 机制 | 依赖 |
| --- | --- | --- | --- |
| **富媒体上传** | `/进度查询` 进度卡片、`/查背包` 背包卡片、`/查看地图` | 插件把 PNG 字节经 `msg_type=7` + `media.file_info` 上传到 `/v2/{users\|groups}/{id}/files` | 不需要 `PublicBaseUrl`，群/私聊均可用 |
| **Markdown 内联图** | 图鉴（`/si` `/sn` `/sp` `/sb`） | 输出 `![text #Wpx #Hpx](PublicBaseUrl/assets/...)`，由 **QQ 开放平台服务器主动来下载转存** | 必须有 `PublicBaseUrl` 且公网可达 |

官方文档原文：*"对于 markdown 消息内的图片资源，请使用可在公网访问的资源 url，开放平台会下载转存该资源。"*
因为是**服务端拉取**而非客户端渲染，所以 `http://` + 裸 IP + 非标准端口同样可用（实测 `http://<你的公网地址>:22338/assets/images/items/Item_39.png` → 200）。
若将来要换成域名或 HTTPS，只改这一项配置即可，无需重新编译。

> 排查"图不出来"的顺序：① `curl <PublicBaseUrl>/assets/images/items/Item_39.png` 是否 200；
> ② 对应 `Asserts/` 文件是否存在（缺图时本就只出文字）；③ 群里看机器人回复里有没有 `![](...)` 字样。

---

## 7. TShock 侧接线（必读）

这是整个链路里**唯一需要改第三方代码**的地方，务必看完。

现有的 `CaiBotLite` 适配插件把 Bot 地址写死在常量里，并且协议前缀也是写死的：

```csharp
// UnrealMultiple/TShockPlugin/src/CaiBotLite/WebsocketManager.cs
private const string BotServerUrl = "api.terraria.ink:22338";          // ← 第 16 行

var response = await client.GetAsync($"https://{BotServerUrl}/server/token/{...}");   // ← 第 66 行
await WebSocket.ConnectAsync(new Uri($"wss://{BotServerUrl}/server/ws/{...}/tshock/")); // ← 第 83 行
```

### 方案 A：改常量并重新编译（推荐）

把这三行改成你自己的地址；如果你没有给 Bot 端配 TLS，同时把 `https` / `wss` 换成 `http` / `ws`：

```csharp
private const string BotServerUrl = "你的域名或IP:22338";

var response = await client.GetAsync($"http://{BotServerUrl}/server/token/{...}");
await WebSocket.ConnectAsync(new Uri($"ws://{BotServerUrl}/server/ws/{...}/tshock/"));
```

然后重新编译适配插件，把新的 `CaiBotLite.dll` 放进 TShock 的 `ServerPlugins/` 目录。

### 方案 B：不改代码，用 TLS 反向代理

保留 `https://` / `wss://` 不动，在 Bot 端前面挂一个带**受信任证书**的反代（Caddy / nginx），并把 `api.terraria.ink` 通过 DNS 或 hosts 解析到你的反代。

> 注意：证书必须对该域名有效，否则 WebSocket 握手会因证书校验失败而中断。这一路径通常比方案 A 更麻烦，仅在你无法重新编译插件时考虑。

### 本插件已实现的 HTTP / WS 接口

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET` | `/ping` | 健康检查 |
| `GET` | `/server/token/{init_code}` | 服务端用绑定码换取连接令牌（返回 `token` + `group_open_id`） |
| `WS` | `/server/ws/{group_open_id}/{server_type}/` | 服务端长连接（`server_type` = `tshock` / `tModLoader` / `bukkit`） |
| `GET` | `/download/{file_id}` | 地图 / 小地图临时文件二次下载 |
| `GET` | `/plugin/{name}` | 下载 Windy `Plugins/` 下的插件文件 |
| `GET` | `/assets/{path}` | 图鉴图标（`images/items/Item_<id>.png` 等四类 + 进度卡片素材） |
| `GET` | `/{appid}.json` | QQ 侧域名校验 |

WebSocket 鉴权：请求头需带 `authorization: Bearer <token>`，`token` 在适配插件里即配置的 Token。鉴权失败按协议关闭，关闭码 `1008`（缺头）/ `4003`（Token 不匹配）。

---

## 8. 绑定服务器

1. 启动 TShock，控制台会打印：
   ```
   [CaiBotLite] 您的服务器绑定码为: 01234567
   ```
   （8 位数字。没看到可在游戏内执行 `/cbl code` 重新生成。）

2. 把机器人拉进 QQ 群，然后由**群主 / 群管理员**发送：
   ```
   /添加服务器 <IP> <端口> <绑定码>
   ```

3. 适配插件每 10 秒轮询一次令牌接口，换取成功后本群即可使用全部查询指令。

---

## 9. 指令总表

51 条指令，全部支持 `Group` / `GroupAt` 场景（标 ★ 的另支持单聊）。

### 服务器管理

| 指令 | 说明 |
| --- | --- |
| `/添加服务器 <IP> <端口> <绑定码>` | 绑定一台服务器（管理员） |
| `/修改服务器 <序号> <IP> <端口>` | 修改地址端口（管理员） |
| `/删除服务器 <序号>` | 删除已绑定服务器（管理员） |
| `/解绑服务器 [序号]` | 主动解除绑定（管理员） |
| `/服务器列表` | 查看本群绑定的服务器 |
| `/服务器信息 [序号]` | 服务器详细信息 |

### 查询

| 指令 | 说明 |
| --- | --- |
| `/在线 [序号]` | 在线玩家列表 |
| `/进度查询 [序号]` | 世界 Boss 进度（含击杀次数、进度锁） |
| `/查背包 <玩家名>` | 玩家背包、生命魔力、增益 |
| `/排行 <类型>` | 排行榜（Boss / 死亡 / 在线 / 钓鱼 / 金币） |
| `/插件列表 [序号]` | 服务器插件 / 模组列表 |
| `/远程指令 <指令> [序号]` | 在服务端执行指令（管理员） |
| `/自踢 [序号]` | 断开所有服务器连接（管理员） |

### 地图与文件

| 指令 | 说明 |
| --- | --- |
| `/查看地图 [序号]` | 世界地图预览图 |
| `/下载地图 [序号]` | 世界文件 `.wld` |
| `/下载小地图 [序号]` | 小地图文件 `.tmap` |

### 白名单

| 指令 | 别名 | 说明 |
| --- | --- | --- |
| `/添加白名单 <角色名>` | `绑定` | 绑定游戏角色 |
| `/修改白名单 <角色名>` | `重新绑定` | 重新绑定 |
| `/删除白名单` | | 解除绑定 |
| `/我的白名单` | | 查看自己的绑定信息 |
| `/登录 <验证码>` | | 批准新设备登录 |
| `/签到` | | 每日签到领金币 |
| `/查询金币` | | 金币余额 |
| `/查询玩家 <名字>` | | 按名字查白名单记录 |

### 图鉴检索

| 指令 | 别名 | 数据量 |
| --- | --- | --- |
| `/si <名字\|ID>` | `搜物品` | 6196 |
| `/sn <名字\|ID>` | `搜生物` | 762 |
| `/sp <名字\|ID>` | `搜弹幕` | 1136 |
| `/sb <名字\|ID>` | `搜增益` | 400 |
| `/sx <名字\|ID>` | `搜修饰` / `搜修饰语` | 98 |

检索策略：ID 精确 → 名称精确 → 前缀 → 包含 → Levenshtein 模糊（≥0.62 相似度）。命中过多时返回候选列表并提示改用 ID。

### 群管理

| 指令 | 说明 |
| --- | --- |
| `/获取群信息` | 本群 OpenID 等信息 |
| `/管理列表` | 列出本群机器人管理员 |
| `/添加管理 <OpenID>` / `/删除管理 <OpenID>` | 增删管理员（管理员） |
| `/绑定父群 <父群 OpenID>` / `/解绑父群` | 父群共享白名单（带环检测，管理员） |
| `/设置 <项> <开\|关>` | 群功能开关（别名 `群设置`，管理员） |
| `/黑名单列表` | 本群黑名单 |
| `/添加黑名单 <角色名>` / `/删除黑名单 <角色名>` | 封禁 / 解封（管理员） |
| `/全局黑名单` / `/全局封禁` / `/全局解封` | 机器人级黑名单（仅所有者） |
| `/权限请求` | 查询如何获得管理权限 |

### 帮助

`/菜单`（别名 `帮助`）、`/服务器管理`、`/快捷功能`、`/地图功能`、`/白名单菜单`、`/图鉴搜索菜单`、`/群管理`、`/菜单面板`

### 权限判定

`/设置`、`/添加服务器` 等管理类指令要求操作者满足其一：

- 在配置的 `OwnerOpenIds` / `Operators` 中；
- 是当前 QQ 群的**群主或管理员**；
- 在群记录 `Admins` 列表中。

---

## 10. 菜单与指令面板

`menu/` 下两个文件的内容**就是 QQ 开放平台的 API 请求体**，用 `scripts/publish_menu.mjs` 可直接推送：

```bash
NODE=node   # 本机为 C:\Users\Administrator\.workbuddy\binaries\node\versions\22.22.2-5\node.exe

# 只读查询平台现状（不改动任何配置）
QQ_BOT_APPID=<AppID> QQ_BOT_SECRET=<Secret> $NODE CaiBotWindy/scripts/publish_menu.mjs probe

# 推送自定义菜单 + 指令面板（同步策略：先清空该场景 → 再重建，可重复执行）
QQ_BOT_APPID=<AppID> QQ_BOT_SECRET=<Secret> $NODE CaiBotWindy/scripts/publish_menu.mjs push
```

| 文件 | 对应接口 | 内容 |
| --- | --- | --- |
| `menu.json` | `PUT /v2/menu` | 自定义菜单：9 个一级项 / 28 个子项 |
| `panels.json` | `POST /v2/panels` | 2 个指令面板（群聊 20 元素 + 单聊 9 元素） |

**注意**：QQ 的「自定义菜单」**只在单聊（c2c）生效且全局唯一**，群聊里看不到。群聊场景靠的是「指令面板」。

> ⚠ **同一 scope + target 下只能存在 1 个面板**，后建的直接替换先建的（实测确认）。
> 所以群聊与单聊各配 1 个面板，把高频入口压进这 20 个槽位；面板的前 7 个元素是「下钻入口」
> （点击 → 把指令填进输入框 → 发送 → 机器人回带按钮的子菜单），借此覆盖全部 51 条指令。

> [`ACaiCat/qq-bot-menu-panel`](https://github.com/ACaiCat/qq-bot-menu-panel) 是**可视化编辑器**，
> 只支持「连平台 → 拉取 → 编辑 → 存回」，**没有导入本地文件的功能**。
> 想可视化微调：先跑上面的 `push`，再启动它从平台拉取。

平台限制已在生成脚本里逐项校验（`scripts/gen_menu.py` 会因超限而报错退出）：

| 项 | 上限 |
| --- | --- |
| 一级菜单 | 10 个 |
| 菜单名称 | 10 字符（1 个中文按 2 字符计，即最多 5 个汉字） |
| 子菜单 | 5 个，且不可再嵌套 |
| 子菜单名称 | 14 字符 |
| 单面板元素 | 20 个 |
| 元素名称 / 描述 | 14 / 30 字符 |
| 面板总数 | 20 个 |
| **同一 scope + target 的面板数** | **1 个**（后建替换先建；实测结论，官方文档未提及） |

重新生成：

```bash
python scripts/gen_menu.py     # 生成 + 校验平台限制
python scripts/check_menu.py   # 交叉校验：面板引用的指令是否真实存在
```

`check_menu.py` 校验的是 `[Command(...)]` 的**别名表**。Windy 的 `CommandAttribute` 第 4 个参数 `params string[] parameters` 实际充当别名（匹配逻辑见 `CommandRegistry.ExecuteAsync`：`c.Parameters.Any(p => string.Equals(p, commandName, OrdinalIgnoreCase))`）。这一步很关键，因为指令面板元素点击后是把 **name 填进输入框**，如果 name 不是真指令，用户点一下就是一条无效输入。

在群里发送 `/菜单面板`（管理员）可让机器人直接把这两个文件发到群里。

---

## 11. 图鉴数据

数据来自 [`Cjx8848/TerraWiki`](https://github.com/Cjx8848/TerraWiki) 的 `Files/*.json`，字段名与本项目 C# 模型完全一致（`ItemId` / `NpcId` / `ProjId` / `BuffId` / `PrefixId`，价值为 `{Copper,Silver,Gold,Platinum}` 分币）。

`TerrariaID` 上游仓库只有导出工具、不含数据，因此数据源选的是 TerraWiki。

图标直接取自官方仓库，**不要手写文件名**——目录与命名必须严格按类目：

| 类目 | 落盘路径 | 图鉴字段 |
| --- | --- | --- |
| 物品 | `Asserts/images/items/Item_<id>.png` | `ItemId` |
| 生物 | `Asserts/images/npcs/NPC_<id>.png`（**大写 NPC**） | `NpcId` |
| 弹幕 | `Asserts/images/projectiles/Projectile_<id>.png` | `ProjId` |
| 增益 | `Asserts/images/buffs/Buff_<id>.png` | `BuffId` |

补图后无需重启：`MenuKit.AssetUrl` 在每次调用时 `File.Exists` 实时判定，只要 `PublicBaseUrl` 配置正确就被 QQ 拉取。
文件缺失时该项只渲染文字、不出破图（属于预期降级）。

**覆盖率现状**（2026-10-03 全量同步后）：物品 6195/6195 全覆盖；生物缺 4 张、弹幕缺 13 张、增益缺 12 张
（如 `Buff_389`~`Buff_400`）——这些**上游素材包本身就没有**（官方素材滞后于 `TerraWiki` 的图鉴数据），
上游补图后重跑 `fetch_assets.py all` 即可补齐。

---

## 12. 实现要点与已知限制

**协议版本号**按包类型区分（对齐适配插件 `PackageTypeExtension.GetVersion`）：默认 `2025.7.18`，`unbind_server` / `heartbeat` / `rank_data` / `plugin_list` / `shop_buy` / `shop_condition` 为 `2025.7.25`，`error` 为 `2026.2.14`。

**1024 字节单帧限制**（最大的坑）：适配插件接收缓冲区只有 1024 字节，而且**只用最后一帧解析**，不做拼接：

```csharp
var buffer = new byte[1024];
result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
var receivedData = Encoding.UTF8.GetString(buffer, 0, result.Count);   // 只有最后一帧
```

因此本插件侧：

- 所有出站 JSON 用 `Formatting.None` 紧凑序列化，禁止改成缩进格式；
- 读取入站消息时**累积多帧**再解析（对端实现不做这个，我们这边必须做）；
- 服务端 token 用 `Guid.NewGuid().ToString()`（36 字符）而非 `"N"`（32 字符），给 payload 多留余量；
- 超大响应（地图、世界文件、背包）走「写临时文件 + 返回 `/download/{file_id}` 链接」而不是直接塞进 payload。

**二进制编码**为两层：`bytes → Base64 → gzip(UTF-8) → Base64`，用于 `map_image` / `world_file` / `map_file` 的 `payload.base64`。已通过 Python 独立实现的交叉验证，双方输出完全一致。

**白名单**是单向通知（`is_request = false`），机器人回包同样是单向，不是 RPC。

**主动消息配额**：服务器上线通知属于主动推送，可能被 QQ 限流。发送失败只记日志、不影响主流程。

**未实现**：`shop_condition` / `shop_buy`（商店）两个包类型已在枚举与解析中支持，但没有对应的 QQ 指令入口。

---

## 13. 自检

```bash
./dotnet-sdk/dotnet.exe run --project CaiBotWindy/tests/ProtocolTest/ProtocolTest.csproj -c Release
```

68 项断言，覆盖：

| # | 内容 | 项数 |
| --- | --- | --- |
| 1 | `BinaryCodec` 往返（空 / 单字节 / 全 256 字节值 / 文本 / 1MB 随机） | 5 |
| 2 | 编码输出字符集校验 + 导出跨语言验证样本 | 1 |
| 3 | 各包类型协议版本号 | 3 |
| 4 | 枚举与线上字符串双向映射（含大小写不敏感） | 6 |
| 5 | `BotPacket` 封包结构（含 `request_id` 为 null 时仍写出该键） | 7 |
| 6 | 单帧 1024 字节约束与紧凑序列化 | 2 |
| 7 | `BotPacket.Parse` 容错（非 JSON / direction 缺失或非法 / payload 缺失） | 7 |
| 8 | `PayloadReader` 类型宽容（数字↔字符串↔布尔互认） | 19 |
| 9 | 图鉴数据加载（5 个文件的条目数） | 5 |
| 10 | 图鉴检索（ID 精确 / 名称 / 模糊 / 空值 / 不存在） | 9 |
| 11 | 货币格式化 | 3 |

当前结果：**68 / 68 通过**，解决方案 `0 错误 0 警告`。

跨语言编解码一致性可单独复核：

```python
import base64, gzip, pathlib
sample = pathlib.Path('tests/ProtocolTest/bin/Release/net10.0/codec-sample.txt').read_text('ascii')
raw = base64.b64decode(gzip.decompress(base64.b64decode(sample)).decode('utf-8'))
print(raw.decode('utf-8'))
```

---

## 14. 端口与防火墙

`ListenPrefixes` 决定监听端口（默认 `22338`）。两件事要做：

1. 放行 TCP 22338 入站（云安全组 + 系统防火墙）；
2. `HttpListener` 使用 `+` 通配前缀时需要 URL 保留权限，否则会启动失败：

```cmd
netsh http add urlacl url=http://+:22338/ user=Everyone
```

如果只想监听本机做反代，把配置改成 `["http://127.0.0.1:22338/"]` 即可免去 urlacl。

---

## 15. 与官方 CaiBot 的差异

| 项目 | 官方 CaiBot | 本项目 |
| --- | --- | --- |
| Bot 端语言 | Python（NoneBot2 + nonebot-adapter-qq） | C#（Windy SDK，.NET 10） |
| 协议 | CaiBotLite 数据包 | **完全兼容**，TShock 侧适配插件可直接复用 |
| Bot 服务 | `api.terraria.ink:22338` | 自建，数据落到自己的 `store.json` |
| 部署 | 需要 Python ≥3.13 环境 | 需要 .NET 10 运行时 + Windy 宿主 |
| 图鉴 | 独立插件 | 内置（5 类、8592 条） |
| 菜单面板 | 无 | 内置 `menu.json` / `panels.json`，可导入 qq-bot-menu-panel |

数据完全自持：服务器记录、群设置、白名单、用户与金币、登录尝试记录都写在 `Config/CaiBotWindy/store.json`。
