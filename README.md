# TShock-CaiBot

TShock（Terraria 服务端）的 QQ 官方机器人项目，包含机器人本体和 TShock 侧适配插件。

## 组成

| 路径 | 说明 |
| --- | --- |
| `CaiBotWindy/` | 机器人本体。基于 Windy（QQ 官方机器人 SDK，C# / .NET 10）自建 Bot 端，兼容 CaiBotLite 的全部数据包接口 |
| `adapter/` | TShock 侧适配插件。上游 CaiBotLite 的本地化改造版（`BotServerUrl` 改为配置项、按需 TLS、修复多帧解析），并新增 `server_file` 包以支持机器人点播服务端文件；附本地编译工程 |
| `CaiBotWindy/deploy/Asserts/` | 图鉴素材（物品 / 生物 / 弹幕 / 增益图标、世界图标、Boss 头像、字体、背景图） |
| `Start-WindyBot.bat` | 机器人启动脚本 |
| `Start-TShockServer.bat` | TShock 服务端启动脚本 |

## 能力

- QQ 指令集：服务器查询、地图下载、服务端文件下发、白名单、群管理、图鉴检索、运维诊断（群聊 / 私聊双场景）
- 图鉴查询 `/si` `/sn` `/sp` `/sb`：物品 6196、生物 762、弹幕 1136、增益 400、修饰 98
- 卡片渲染（SkiaSharp 自绘，窄画布加大字号，手机聊天窗口里不点开也读得清）
  - `/进度查询`：1080×1440 竖版进度卡，含 Boss 网格、入侵事件、击杀次数和夜色星空底图
  - `/查背包`：按容器分区（背包 / 虚空袋 / 钱罐 / 保险箱 / 防御者熔炉 / 装备与饰品）的动态高度卡片
- 服务器管理：白名单（需两侧开关同时打开）、经济数据、Boss 锁、地图导出、签到
- 服务端文件下发（管理员，仅群聊）：`/发文件` 取任意文件、`/看文件` 看目录与文本、`/存档` 取世界存档、`/插件打包` 打包插件目录；`/日志` 支持分页 / 行号区间 / 单份下载 / 全量打包四种看法

## 快速开始

1. 装 .NET 10 SDK（机器人侧）
2. 编译机器人：
   ```bash
   dotnet build CaiBotWindy/CaiBotWindy.slnx -c Release
   ```
3. 自检、出图预览：
   ```bash
   dotnet run --project CaiBotWindy/tests/ProtocolTest                              # 协议自检 75 项
   dotnet run --project CaiBotWindy/tests/ProgressPreview -- CaiBotWindy/deploy/Asserts ./out/card
   ```
4. 部署与配置见 [`CaiBotWindy/README.md`](CaiBotWindy/README.md)

> NuGet 走华为云镜像（`CaiBotWindy/NuGet.config`）；`api.nuget.org` 不通的环境可直接用。

## 仓库说明

仓库不含任何凭据与运行日志。以下内容已在 `.gitignore` 中排除，切勿放开：

- `CaiBotWindy/deploy/Config/`：机器人 AppID / ClientSecret / 绑定令牌 / 群 OpenID
- `CaiBotWindy/deploy/logs/`：运行日志（含群 OpenID、玩家记录）
- `TShock-Server/`：服务端本体与配置（含连接密码）
- 第三方 SDK 二进制（`CaiBotWindy/refs/`）、各 `bin` / `obj`、运行目录产物

`CaiBotWindy/refs/` 是从上游 Windy 源码复制来的一份副本，本地对其 SDK 做过若干改动，
改动内容以补丁形式保存在 `CaiBotWindy/docs/windy-sdk-local-changes.patch`，
重建步骤见 `CaiBotWindy/docs/Windy-SDK-本地改动.md`。

首次运行会自动生成 `Config/` 下的配置，按 `CaiBotWindy/README.md` 填入自己的 AppID 与密钥即可。

## 素材与数据来源

| 内容 | 来源 |
| --- | --- |
| 图鉴图标 / 世界图标 / Boss 头像 / 字体 | [`UnrealMultiple/TShockPlugin`](https://github.com/UnrealMultiple/TShockPlugin) 的 `CaiBotLite/assets/`，用 `CaiBotWindy/scripts/fetch_assets.py` 同步 |
| 图鉴数据（物品 / 生物 / 弹幕 / 增益 / 修饰） | [`Cjx8848/TerraWiki`](https://github.com/Cjx8848/TerraWiki) 的 `Files/*.json` |
| `adapter/CaiBotLite/` | 上游 CaiBotLite 的本地化改造版，版权归原作者 |
| `ProgressBackground.png` / `BagBackground.png` | 本项目自绘 |

第三方素材与代码的版权归各自作者，遵循其原仓库的许可条款。
