# 任务交接（HANDOFF）

> 交接时间：2026-11-12 10:20 GMT+8
> 上一轮工作：跨平台重构 + macOS 壳 + 服务器（106.14.173.90）→ 本机全量迁移
> 本文档面向**新对话接手**：只写"现在是什么状态、下一步做什么、哪里会踩坑"，历史推演过程见 `.workbuddy/memory/2026-11-11.md`。

---

## 0. 一句话现状

macOS 版 OpenClaw Debugger 已能双击运行并完整操作本机 OpenClaw（记忆/表情包/模型/插件/技能/备份），服务器三类内容（记忆、数据、配置）已全量搬到本机并校验通过。**代码改动仍在 `refactor/split-core-shell` 分支，未合并、未推送**；剩余工作都是环境/部署层，不是数据搬运。

---

## 1. 两个"服务器"不要混淆

| 名称 | 含义 | 状态 |
|---|---|---|
| 阿里云服务器 | `admin@106.14.173.90:22`，Ubuntu 24.04，openclaw 2026.9.4 + node 24.21.0 | 仍在运行，gateway 由 user systemd 常驻 |
| 本机 macOS | `chlors@127.0.0.1:22`（开启远程登录后的 sshd） | 已接管，openclaw 2026.11.3 + node 26.13.0 |

应用的 `settings.json` 现已指向 `chlors@127.0.0.1`（本机）。服务器配置留在同目录 `settings.server.json`，需要切回时直接替换即可。
应用走 **SSH**，不连 18789 的 Gateway HTTP 面板。

---

## 2. 环境事实（照抄，别重新探测）

| 项 | 值 |
|---|---|
| 仓库 | `/Volumes/Atri/PROJECT/OpenClaw-Debugger` |
| 分支 | `refactor/split-core-shell`，HEAD `426078b`（4 个提交，**未推送**） |
| main | `072de01`，等于 `origin/main`（未动） |
| 远端 | `https://github.com/Chlors-Creator/OpenClaw-Debugger.git` |
| .NET SDK | 10.0.404，装在 `~/.dotnet`，**不在 PATH**，需 `export PATH="$HOME/.dotnet:$PATH"` |
| node（登录 shell） | `/usr/local/bin/node` **v26.13.0**（官方 pkg，npm 11.20.0） |
| openclaw | `/usr/local/bin/openclaw`，**2026.11.3**（847611d） |
| dotnet 构建 | mac 上靠 `EnableWindowsTargeting=true` 交叉编译 WPF 壳，用于回归 |
| Xcode | 已装 27.2 于 `/Applications/Xcode.app`，但 `xcode-select -p` 指向 CommandLineTools；需要时 `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer` |
| 应用 | `/Applications/OpenClawDebugger.app`（1.0.0，ad-hoc 签名，134MB） |
| 私有运行目录 | `~/Documents/ChatGPT/Openclaw/OpenClaw-Debugger-Private/`（settings/logs/缩略图缓存） |

**AI 执行环境里 `node -v` 可能是 v22.22.2** —— 那是 WorkBuddy 内置运行时（`~/.workbuddy/binaries/node/...`），只注入沙箱，**不影响用户终端与 openclaw**。不要拿它当结论，也不要据此"修复" node。

---

## 3. 已完成且已验证的三块工作

### 3.1 跨平台重构（提交 `ccc9781`）

单工程 WPF → 六工程结构：

```
src/OpenClawDebugger.Core          net10.0    桥接路由/SSH/业务服务/平台抽象
src/OpenClawDebugger.Platform.Windows  DPAPI、凭据管理器、ssh 探测、WPF 缩略图
src/OpenClawDebugger.Platform.Mac      Keychain(security CLI)、AES-GCM、sips、/usr/bin/open
src/OpenClawDebugger.Shell.Windows     WPF + WebView2
src/OpenClawDebugger.Shell.Mac         Avalonia 12.1.6 + NativeWebView(WKWebView)
shared/WebUi                           界面唯一真源，两个壳 Content Include + Link
tools/OpenClawDebugger.ContractGen     替代已删除的 Generate-BridgeContract.ps1
tests/OpenClawDebugger.Tests           改为 net10.0，mac 可直接跑
```

关键决策（改动时别推翻）：
- 全部工程 `RootNamespace = OpenClawDebugger`，71 个搬家文件 **using 与命名空间零改动**。
- 六个平台抽象：`IBridgeTransport` / `ICredentialStore` / `ISecretProtector` / `ISshExecutableLocator` / `IThumbnailEncoder` / `IShellLauncher`。
- `SshExecutableLocator.Current` 是静态注入点，Windows 壳在 `MainWindow` 构造函数注册。
- 快照后缀保留 `.dpapi`（历史快照兼容）。
- 嵌入 py 资源用 `<LogicalName>OpenClawDebugger.*.py</LogicalName>`。
- 手写经典 `.sln`（`dotnet new sln` 默认出 `.slnx`）。

### 3.2 macOS 壳（提交 `f74f5d1` `44681f0` `d49063c`）

- 壳形态定案：**Avalonia 而非原生 AppKit**。两者都跑 WKWebView，风险在桥接/Keychain/文件通道，不在壳。再评估条件：上 Mac App Store / 要菜单栏常驻与自动更新 / 包体敏感 / Avalonia WebView 拖拽或输入法出问题。
- `shared/WebUi/host-shim.js`（在 `bridge.js` 之前加载）把 `window.chrome.webview` 映射到 `invokeCSharpAction`，前端 5 处调用零改动。
- `LocalWebUiServer`：HttpListener 绑 127.0.0.1 随机端口服务 WebUi，绕开 WKWebView 的 `file://` fetch/CORS 限制，带路径穿越防护。
- `AvaloniaBridgeTransport`：回复走 `InvokeScript("window.__openclawHostDeliver(<json>)")`，跨线程统一 `Dispatcher.UIThread.Post`。
- `BridgeDispatcher` 增加可选构造参数 `allowedOrigins`（默认仍 `https://openclaw.local`），比较改为 scheme+host+port 全等。
- `NativeWebView` 事件**只给 Body、无来源 URI**（反射确认）；靠壳声明来源 + `NavigationCompleted` 校验 `location.href` 兜底；`NewWindowRequested` 一律 `Handled = true`。
- 诊断开关：`OPENCLAW_BRIDGE_LOG=1`（双向消息）、`OPENCLAW_SMOKE=1`（探针后 6 秒退出）。
- 打包：`build/build-mac-app.sh`（publish → 从 Windows .ico 提最大帧 → sips+iconutil 出 .icns → 组 bundle → ad-hoc 签名），`--install` 装到 /Applications。`Info.plist` 必须写 `NSPrincipalClass=NSApplication` 与 `CFBundleIconFile`。

**验收基线**：全解决方案 0 错误 0 警告，13 项测试通过，`success=True scripts=39 styles=11 body=17`，`initialize → ok:true` 带回完整设置。

### 3.3 服务器 → 本机迁移

| 类别 | 结果 |
|---|---|
| 记忆/表情包 | workspace 四个 md（AGENTS 7454 / USER 6420 / IDENTITY 3103 / SOUL 1570）+ `memory/` 6 日记 + `.dreams` 17 语料 + `stickers/` 10 图 + catalog.json，逐文件 SHA-256 一致 |
| 数据 | state 库 108→110 表；`agents/main` 23MB schema 19（2 会话 / 347 记忆来源 / 101 索引段）；recall **438 条**；qqbot 7 文件；skills-data 1；workspace/media 44；embedding gguf 313MB 哈希一致 |
| 配置 | 两个自定义 provider（teamorouter / sjtu）、primary + 2 fallback、3 个 alias、`main` 的 identity(以塘🍃)/modelPolicy、`channels.qqbot` 全量、`channels.a2a`、`bindings`、`tools.web.search=searxng` |

实测：sjtu 1.6s / teamorouter 2.0s 均 HTTP 200；`channels status` → `QQ Bot 4018338744 (Helios_qq): enabled, configured`；应用 24 条日志零失败（connect 222ms、getModels 3.0s）。

**刻意保留本机值**：gateway token / port 18789 / bind loopback / tailscale（机器级密钥，搬过来没收益还会让本机客户端失效）。

完整迁移矩阵与证据链见 `~/Documents/ChatGPT/Openclaw/server-export-2026-11-12/MIGRATION-GAPS.md`（21 项逐条）。导出原件：同目录 `workspace/` `plugins/` `MANIFEST.md`。

---

## 4. 备份与回滚点（不要删）

| 路径 | 内容 |
|---|---|
| `~/.openclaw/_backup-before-restore-20261112-001600/`（26M） | 恢复前原 state/agents/qqbot + `agent-main-original.sqlite` + `state-after-import.sqlite` + `state-before-repair.sqlite` |
| `~/.openclaw/_config-merge-20261112/` | `local-before.json` / `server.json` / `local-before-merge.json`（600） |
| `~/.openclaw/openclaw.json.bak*`（5 个） | 合并各阶段快照 |
| `~/Documents/ChatGPT/Openclaw/server-export-2026-11-12/` | 服务器导出的原件（8.9MB / 205 文件） |
| `~/.openclaw/workspace/.git` | 本机 workspace 首次提交 `085cdc4` |

`~/.openclaw/openclaw.json` 内含 **apiKey / clientSecret 明文**（权限 600）。交接文档、提交信息、日志里一律不复制这些值。

---

## 5. 待办（按优先级，含已决策终止项）

### P0 — 合并推送（唯一阻塞主线的事）

`refactor/split-core-shell` 4 个提交未推送。合并前必须**在 Windows 上跑一次回归**（`pwsh build/build-win.ps1`），因为 mac 侧只验证了交叉编译通过，没验证 WebView2 运行时。合并后推送 `main`。

### P1 — QQ 机器人抢线（切 Mac 前必做）

两端同 appId `1905609051` / 账号 `4018338744`，同时跑 gateway 会互踢。切 Mac 前先停服务器：

```bash
ssh admin@106.14.173.90 'systemctl --user stop openclaw-gateway'
```

要长期在 Mac 上跑，还得给服务器加 `systemctl --user disable`，或接受"只有一边在线"。

### P2 — macOS 常驻（LaunchAgent）

**当前实测未安装**：`openclaw gateway status` → `Service: LaunchAgent (not loaded)`、`connect ECONNREFUSED 127.0.0.1:18789`。gateway 现在没跑。
服务器那边是 user systemd + `Linger=yes`；Mac 需要等价 LaunchAgent（`openclaw gateway install` 会生成）。装之前先解决 P1，否则一登录就抢服务器线。

### P3 — searxng 搜索后端

插件与配置（`tools.web.search.provider=searxng`）已就位，但**本机没有容器运行时**，搜索会失败。代理脚本 `~/.local/bin/searxng-on-demand-proxy.py`（纯标准库）已取回。补齐步骤：

```bash
docker run -d --name searxng -p 127.0.0.1:8888:8080 searxng/searxng
nohup python3 ~/.local/bin/searxng-on-demand-proxy.py &   # :8889，闲置 5 分钟自动停容器
curl -s "http://127.0.0.1:8889/search?q=test&format=json" | head -c 200
```

### P4 — 两条插件告警（可不修）

`openclaw update status` 报 `deepseek`、`llama-cpp` settings 升级未完成 —— 因搬来的 state 库记录着旧 `/home/admin/...` 路径。官方策略"existing settings kept"，两插件**实际加载正常**。`openclaw update repair` 试过两次，都打开 state 维护锁后失败；**收益不抵风险，已停手**。彻底解决需卸载重装这两个插件。另有 59 行 `workspaceDir` 残留旧路径，不影响 recall。

### P5 — 仓库侧旧待办（承接上一轮）

1. 备份快照上传通道 `BridgeFile`（mac 无 `postMessageWithAdditionalObjects` 对等能力），贴纸上传要改宿主文件对话框或 base64 通道。
2. 正式分发需 Developer ID 签名 + `notarytool` 公证（现在 ad-hoc，仅本机可跑）。
3. 正式发布前把 `Platform.Mac` 的 `security` CLI 换成 Security.framework（避免 secret 短暂出现在进程参数表）。
4. `shared/WebUi/index.html` 仍有硬编码 Windows 文案（「DPAPI 加密」「在资源管理器中打开」），Mac 上语义错误。
5. 核实 `Avalonia.Controls.WebView` 授权条款（文档挂在 Accelerate 商业线，nuspec 声明 MIT）。

### 已终止 — node 升级

**用户 2026-11-12 明确决定：不升级。** 当前 v26.13.0 满足 openclaw engines `>=24.16.0 <25 || >=26.1.0`；v26.13.1 仅补丁级安全修复，无功能影响。安装包留在 `~/Downloads/node-v26.13.1.pkg`（SHA-256 与签名已校验），**不要再提议升级，也不要再尝试 sudo**。

---

## 6. 踩过的坑（重复率最高的部分）

1. **不要 `cp` 活着的 sqlite 库**。gateway 常驻会 WAL 撕裂。用 Python `sqlite3.Connection.backup()` 做一致性快照（服务器无 sqlite3 CLI）。
2. **recall 按路径哈希寻址**。`plugin_state_entries` 键为 `<sha256(workspace路径)>:<后缀>`，`/home/admin` 与 `/Users/chlors` 哈希不同，直接导入显示 0 条。已改写 466 行键恢复 438 条。搬数据到第三台机器时同样要改。
3. **多 agent 必须 `agents.ownership="explicit"`**，且 legacy `default:true` 会直接报配置错误。
4. **`channels.qqbot.enabled=false` 会让 qqbot 插件整体判 disabled**，不是"只停渠道"。
5. **Windows 侧 SSH**：Git Bash 自带 ssh 处理中文用户名会 GBK 编码错乱、读不到 `~/.ssh`。必须用 `%WINDIR%\System32\OpenSSH\ssh.exe`。
6. **macOS sshd 非交互会话 PATH 只有 `/usr/bin:/bin:/usr/sbin:/sbin`**，`openclaw` 在 `/usr/local/bin` → `getModels` 报「找不到 openclaw 命令」。解法：`~/.zshenv` 里仅当 `$SSH_CONNECTION` 非空时补 PATH。
7. **GUI 报 `RenderTimer -6661` 不是代码问题**，是锁屏/休眠导致无法建 display link。`caffeinate -d -u` 唤醒即可；截图全白同理。
8. **别用 `rm -rf` 动 .NET SDK 的 `sdk-manifests`**。上轮误删 emscripten 清单导致 `MSB4242` 整机构建失效，靠装干净副本比对才复原。workload 失败优先 `dotnet workload repair` 或另装独立 SDK 目录。
9. **沙箱里 sudo 不可用**，且锁屏时图形授权框无法确认。凡是需要管理员权限的操作，交给用户在终端手动执行，不要硬试。
10. **`openclaw doctor --fix` 会开 state 维护锁**。跑之前确认没有 gateway 在用；上次 `update repair` 两次失败留下的锁已自然释放，但别重复踩。

---

## 7. 常用命令

```bash
# 构建 + 测试（macOS，含 Windows 壳交叉编译）
export PATH="$HOME/.dotnet:$PATH"
/Volumes/Atri/PROJECT/OpenClaw-Debugger/build/build-mac.sh

# 打包 / 安装
/Volumes/Atri/PROJECT/OpenClaw-Debugger/build/build-mac-app.sh --install

# 带桥接日志启动
OPENCLAW_BRIDGE_LOG=1 /Applications/OpenClawDebugger.app/Contents/MacOS/OpenClawDebugger

# OpenClaw 自检
openclaw gateway status
openclaw channels status
openclaw memory status --agent main
openclaw memory search "洛茜"
openclaw update status

# 连接自检（应用同款通道，BatchMode 免密）
ssh chlors@127.0.0.1 'command -v openclaw && ls ~/.openclaw/workspace'

# 服务器
ssh admin@106.14.173.90 'systemctl --user status openclaw-gateway --no-pager'
```

---

## 8. 接手建议顺序

1. 读本文档 → 跑一次 `build/build-mac.sh` 确认基线（0 错误、13 测试通过）。
2. 问用户先做哪条：合并推送（P0，需 Windows 机器）／常驻（P2）／搜索（P3）／仓库旧待办（P5）。
3. 动 `openclaw.json` 前先 `cp` 一份带时间戳的备份 —— 里面是生产密钥，改坏了本机助手直接停摆。
4. 任何"看起来像 bug"的现象，先按第 6 节核对，多数是环境问题不是代码问题。
