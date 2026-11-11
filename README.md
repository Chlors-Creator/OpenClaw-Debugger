# OpenClaw Debugger

OpenClaw Debugger 是一款桌面工具，用于管理 OpenClaw 工作区及常用扩展内容。提供 Windows 与 macOS 两个版本，界面与业务逻辑共用同一份代码。

## 功能

- **概览**：查看连接状态、服务器信息和备份进度。
- **记忆**：浏览、编辑和保存记忆文档，并检查版本冲突。
- **表情包**：上传和预览常见图片格式，管理名称、标签与权重，并支持筛选、暂停、取消和失败重试。
- **模型**：管理文本与图像识别模型链，查看输入能力和延迟，并调整模型顺序。
- **插件**：浏览和搜索社区插件，查看介绍、元数据、Stars 与下载量，并提供下载部署和安全检查入口。页面也展示按时间范围统计的热门插件。
- **技能**：查看已安装技能、详情和远程更新状态。
- **自定义**：将自己制作的插件和技能 ZIP 包加入本机自定义库，查看元数据并移除；自定义列表与社区下载项分开保存。
- **依赖**：查看当前依赖、详情和远程更新状态。
- **主题**：切换主题并调整界面外观。
- **服务器备份**：创建服务器文件系统归档，查看进度、速度与剩余时间，并支持暂停、继续、取消和传输中断后续传。

远程操作由统一队列管理，并显示等待、运行、暂停、取消、失败和完成状态。

## 仓库结构

单仓库多工程结构，同一份跨平台核心由两个平台的壳共享：

| 路径 | 说明 |
| --- | --- |
| `src/OpenClawDebugger.Core` | `net10.0` 跨平台核心：桥接路由、SSH 客户端、业务服务、平台抽象接口 |
| `src/OpenClawDebugger.Platform.Windows` | Windows 实现：凭据管理器、DPAPI、ssh 路径探测、WPF 缩略图编码 |
| `src/OpenClawDebugger.Platform.Mac` | macOS 实现：Keychain、AES-GCM 快照加密、sips 缩略图 |
| `src/OpenClawDebugger.Shell.Windows` | WPF + WebView2 外壳 |
| `src/OpenClawDebugger.Shell.Mac` | Avalonia + NativeWebView（WKWebView）外壳 |
| `shared/WebUi` | 界面资源唯一真源，两个平台的壳都以复制方式引用 |
| `tools/OpenClawDebugger.ContractGen` | 桥接契约生成器，替代原 PowerShell 脚本 |
| `tests/OpenClawDebugger.Tests` | `net10.0` 测试套件，Windows 与 macOS 都能直接运行 |

## 构建

Windows 发布：

```powershell
pwsh build/build-win.ps1
```

macOS 上编译整个解决方案并运行测试（Windows 壳会交叉编译，用于确认核心改动没有破坏 Windows 侧）：

```bash
./build/build-mac.sh
```

macOS 打包成可双击运行的 `.app`：

```bash
./build/build-mac-app.sh              # 生成到 artifacts/OpenClawDebugger.app
./build/build-mac-app.sh --install    # 生成并安装到 /Applications
```

产物为自包含 arm64 应用，采用 ad-hoc 签名，只能在本机运行。对外分发需要 Apple Developer ID 签名并通过 `notarytool` 公证，否则 Gatekeeper 会拦截。

## 运行要求

- Windows 10 或更高版本。
- Windows OpenSSH Client。
- Microsoft WebView2 Runtime。
- 远端环境已安装 OpenClaw；使用备份功能时还需要 Python 3、tar 和 gzip。

macOS 额外要求：

- macOS 11 或更高版本。
- 开启「系统设置 → 通用 → 共享 → 远程登录」，因为本工具通过 SSH 管理目标机器（包括连本机）。
- 免密登录：程序使用 `BatchMode=yes`，只接受密钥认证，不读取密码。目标机器需已配置 `~/.ssh/authorized_keys`，且主机指纹已在 `known_hosts` 中。

### 连接本机时提示「找不到 openclaw 命令」

macOS 的 sshd 给非交互会话只提供 `PATH=/usr/bin:/bin:/usr/sbin:/sbin`，而 Node 版 `openclaw` 通常装在 `/usr/local/bin`。在目标机器的 `~/.zshenv` 中补充即可（只影响 SSH 会话）：

```zsh
if [[ -n "$SSH_CONNECTION" ]]; then
  export PATH="/usr/local/bin:/opt/homebrew/bin:$PATH"
fi
```

可用 `ssh <user>@<host> 'command -v openclaw'` 自查是否已生效。

## 使用方法

1. 从项目发布页获取并解压程序包。
2. 启动 OpenClaw Debugger。
3. 在“设置”页面完成服务器连接设置，然后连接服务器。
4. 通过顶部选项卡使用记忆、表情包、模型、插件、技能和依赖等功能。
5. 创建快照时，在“概览”页面启动服务器备份。

## 备份说明

备份是在线文件级归档。服务器文件在读取期间可能发生变化，因此归档不等同于磁盘或卷级原子快照。恢复前请使用适合的运维工具检查归档内容。

## 开源许可

当前仓库尚未指定开源许可证。
