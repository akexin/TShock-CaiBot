# Windy SDK 的本地改动

`CaiBotWindy/refs/` 不在仓库里，它是 Windy 上游源码的本地副本
（`github.com/Cjx8848/Windy`，含 `Windy` 宿主与 `Windy.SDK` 两个项目）。
`CaiBotWindy.slnx` 直接引用其中的 csproj，没有这份副本，解决方案连还原都做不到。

副本与上游并非完全一致：上游 SDK 缺少本插件需要的能力，改动只存在于本地磁盘上，
靠 `.gitignore` 排除在版本管理之外。所以换一台机器克隆本仓库后，必须按下面两步重建 `refs/`，
否则编译报的会是「找不到 Windy.SDK」这类看不出根因的错。

## 第一步：取上游源码

```bash
git clone --depth 1 https://github.com/Cjx8848/Windy.git /tmp/windy
mkdir -p CaiBotWindy/refs
cp -r /tmp/windy/Windy.SDK /tmp/windy/Windy CaiBotWindy/refs/
```

`refs/` 里不要留 `bin/` 与 `obj/`，上游带过来的是 Linux 构建的残留，混进来会干扰还原。

## 第二步：套上本地补丁

```bash
# 在仓库根目录执行
git apply --directory=CaiBotWindy/refs CaiBotWindy/docs/windy-sdk-local-changes.patch
```

已实测：套用后 `refs/Windy.SDK` 与维护机上的版本逐字节一致，解决方案可正常编译。
若 `git apply` 报 `patch does not apply`，多半是上游 main 已经改动，
按下面的「补丁内容」逐条手工合并，不要用 `-3` 强行三方合并。

## 补丁内容

八处改动，按作用分组。行数来自 `git diff --stat`，仅供参考位置。

| 文件 | 改动 |
| --- | --- |
| `Adaptor/Adaptor.cs` | `OnGroupAtNoCommand` 事件改名为 `OnNoCommand`，触发范围从「群 AT 未匹配」放宽到群、群 AT、私聊三种场景 |
| `Hooks/HookRegistry.cs` | 上面那个事件的注册/执行方法同步改名（`RegisterNoCommand` / `ExecuteNoCommandAsync`），并删掉「只有群 AT 才触发」的场景判断 |
| `Plugin/WindyPlugin.cs` | 暴露 `Commands` 属性；新增 `OnCommandsRegistered()` 虚方法；`AddNoCommandHandler` 等内部入口改名 |
| `Plugin/PluginManager.cs` | 指令扫描注册完成后回调 `plugin.OnCommandsRegistered()` |
| `Command/CommandDispatchResult.cs` | 新增。定义 `DispatchStatus`（`Handled` / `NotFound` / `Failed`）与结果对象 |
| `Command/CommandRegistry.cs` | 新增 `DispatchAsync()`，把「没这条指令」和「指令执行出错」分开；重名与签名错误不再抛异常，改为跳过并记入 `Skipped`；新增 `BuildSummary()` 打印注册清单 |
| `Adaptor/QQOfficial/QQOfficialAdaptor.cs` | 补一批平台 API：入群申请列表、群禁言设置、入群审批策略增删改查、群成员查询 |
| `Main.cs` | 消息分派改用 `DispatchAsync`；指令匹配到但内部报错时回一条明确提示，不再落进未匹配兜底；配套改掉钩子调用名 |

两个设计动机值得记下来，因为它们解释了为什么要动上游代码而不是绕开：

- **原版的 `ExecuteAsync` 把「指令不存在」和「指令执行抛异常」都返回 `false`**。
  上层拿到 `false` 只能回一句「不知道你想做什么」，指令内部出错被伪装成用户打错字。
  `DispatchAsync` 就是为区分这两种情况加的。
- **原版指令重名直接 `throw`**，异常冒到插件加载处会让机器人整个起不来 ——
  而代价仅仅是两条指令撞名。改成跳过问题指令、其余照常注册，问题在启动日志里照样看得见。

## 重新生成补丁

以后本地又改了 SDK，用这套流程刷新补丁，保证 `refs/` 与补丁不会漂移：

```bash
git clone --depth 1 https://github.com/Cjx8848/Windy.git /tmp/windy-up
# 把本地 refs 的 .cs / .csproj 覆盖进 /tmp/windy-up，排除 bin、obj
cd /tmp/windy-up && git add -A Windy.SDK && git diff --cached --binary -- Windy.SDK \
  > "$OLDPWD/CaiBotWindy/docs/windy-sdk-local-changes.patch"
```

补丁只覆盖 `Windy.SDK`。`Windy` 宿主与上游一致，没有本地差异。
