# Codex Monitor

Codex Monitor 是一个支持 Windows 和 WSL 的 Codex CLI 状态灯。使用它启动 Codex 后，Windows 桌面上会出现一个始终置顶的红、黄、绿三色悬浮窗，用于提示 Agent 正在工作、等待输入、已经完成或发生错误。

程序通过 Codex App Server 的结构化协议读取状态，不解析终端文字、不读取 Codex SQLite，也不记录提示词或回复内容。Codex 原有的 TUI、键盘输入和终端输出都会保留。

## 功能

- 无边框、始终置顶、可拖动，并记住上次位置
- 工作、等待输入、完成、中断和错误状态提示
- 等待批准、等待用户输入或执行出错时发送 Windows 通知
- 支持同时启动多个相互独立的 Codex 会话，悬浮窗会自动错开
- 可从系统托盘显示悬浮窗、切换置顶、切换静音或退出
- 仅监听本机回环地址 `127.0.0.1`

## 状态灯含义

| 状态灯 | 含义 | 常见情况 |
| --- | --- | --- |
| 黄灯闪烁 | Codex 正在工作 | 正在分析、生成内容或执行工具 |
| 红灯闪烁 | 需要关注 | 等待批准、等待输入、任务中断或执行出错 |
| 绿灯闪烁 | 本轮任务已完成 | Codex 已完成当前 turn |
| 全部变暗 | 当前空闲或正在建立连接 | 尚未启动任务，或监控刚刚启动 |

当多个状态同时存在时，显示优先级为：等待输入、错误、中断、工作中、已完成、空闲。

## 环境要求

- Windows 10/11 x64
- 已安装 Codex CLI，并且能在 PowerShell 中正常执行 `codex`
- 从源码构建时需要 .NET 8 SDK；仓库的 `global.json` 当前指定 `8.0.423`

在 WSL 中使用时还需要：

- WSL 2，并启用 Windows 程序互操作和默认的 `localhostForwarding`
- WSL 内安装 Codex CLI 和 .NET 8 SDK
- Windows PowerShell 可从 WSL 通过 `powershell.exe` 调用

可先检查环境：

```powershell
codex --version
dotnet --version
```

当前 App Server 协议在 `codex-cli 0.146.0` 上验证。App Server 仍是实验性接口；如果 Codex CLI 的协议发生不兼容，悬浮窗会显示错误，而不会回退到解析终端输出。

## 快速开始

### 1. 构建并运行检查

在仓库根目录打开 PowerShell：

```powershell
Set-Location D:\path\to\AIStatusLight
.\scripts\build.ps1
```

脚本会依次还原依赖、以 Release 配置构建解决方案，并运行自检。仓库内存在 `.tools\dotnet\dotnet.exe` 时会优先使用它，否则使用 `PATH` 中的 .NET SDK。

### 2. 生成可运行程序

```powershell
.\scripts\publish.ps1
```

默认发布为 Windows x64 自包含单文件程序，不要求目标电脑另外安装 .NET Runtime。产物位于 `artifacts\win-x64`：

- `CodexMonitor.exe`：悬浮窗及本地 Codex App Server 宿主
- `codex-watch.exe`：启动悬浮窗并在当前终端运行 Codex
- `codex.cmd`：将 `codex` 命令转发到 `codex-watch.exe` 的包装脚本

`CodexMonitor.exe` 和 `codex-watch.exe` 必须放在同一目录。也可以通过环境变量 `CODEX_MONITOR_APP` 指定 `CodexMonitor.exe` 的绝对路径。

### 3. 启动受监控的 Codex

最简单的方式是直接运行发布目录中的启动器：

```powershell
.\artifacts\win-x64\codex-watch.exe
```

它会自动完成以下操作：

1. 启动一个独立的 `CodexMonitor.exe` 悬浮窗。
2. 在本机随机端口启动 `codex app-server`。
3. 将连接地址作为 `--remote` 参数传给真正的 Codex CLI。
4. 在当前终端显示并操作原生 Codex TUI。
5. Codex 进程结束后，关闭与本次会话对应的悬浮窗和 App Server。

因此无需手动启动后台服务，也不要自行向 `codex-watch` 传入 `--remote`。

Codex 的其他参数会原样透传，例如：

```powershell
# 启动交互会话
.\artifacts\win-x64\codex-watch.exe

# 带初始任务启动
.\artifacts\win-x64\codex-watch.exe "检查项目并运行测试"

# 指定工作目录
.\artifacts\win-x64\codex-watch.exe -C D:\path\to\repo

# 组合 Codex 自身支持的参数
.\artifacts\win-x64\codex-watch.exe -C D:\path\to\repo "修复测试失败"
```

## 将监控接入日常 `codex` 命令

如果希望以后直接执行 `codex` 就自动显示状态灯，可在发布完成后运行：

```powershell
.\scripts\install-codex-wrapper.ps1
```

该脚本会：

- 找到原始 Codex CLI，并将其路径保存到用户环境变量 `CODEX_CLI_PATH`
- 将 `artifacts\win-x64` 加入当前用户的 `PATH`
- 让该目录中的 `codex.cmd` 转发到 `codex-watch.exe`

安装完成后关闭并重新打开 PowerShell 或 CMD，然后验证：

```powershell
Get-Command codex -All
codex --version
codex
```

此后可照常使用所有 Codex 参数：

```powershell
codex
codex "检查当前项目"
codex -C D:\path\to\repo
```

安装脚本不会覆盖或删除原来的 Codex CLI，也不会修改 PowerShell Profile。若发布目录不是默认位置，可以显式指定：

```powershell
.\scripts\install-codex-wrapper.ps1 -InstallDirectory D:\Tools\CodexMonitor
```

指定目录中必须已经包含 `CodexMonitor.exe`、`codex-watch.exe` 和 `codex.cmd`。

## 在 WSL 中使用

WSL 版本会在 WSL 内运行 Codex CLI 和 App Server，仅把悬浮窗放在 Windows 侧显示。因此 Codex 使用的是 WSL 的工作目录、Shell、工具和环境变量。

先在 WSL 中确认依赖：

```bash
codex --version
dotnet --version
/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe \
  -NoProfile -Command '$PSVersionTable.PSVersion'
```

然后在仓库根目录发布并安装：

```bash
bash scripts/publish-wsl.sh
bash scripts/install-wsl.sh
```

确保 `~/.local/bin` 已在 `PATH` 中，之后可直接使用：

```bash
codex-watch
codex-watch "检查项目并运行测试"
codex-watch -C /home/me/project "修复测试失败"
```

如需保留输入 `codex` 的习惯，可在 WSL 的 Shell 配置中加入别名：

```bash
alias codex=codex-watch
```

启动器会自动找到 WSL `PATH` 中原始的 `codex` 可执行文件。也可用 Linux 路径显式设置 `CODEX_CLI_PATH`。`CodexMonitor.exe` 默认与 `codex-watch` 放在同一目录；若分开放置，`CODEX_MONITOR_APP` 在 WSL 中应设置为该 exe 的 `/mnt/...` 路径。

每个 WSL 会话会建立一个只监听 `127.0.0.1` 的 App Server。WSL 启动器退出时，会结束 App Server，并通过共享生命周期文件通知 Windows 悬浮窗退出。

发布脚本会优先使用 `PATH` 中的 `powershell.exe`，找不到时自动尝试 Windows 的标准安装路径。因此关闭 WSL 的 Windows PATH 注入不会影响发布；但仍需启用 WSLInterop，并挂载 Windows 系统盘到 `/mnt/c`。

## 悬浮窗和托盘操作

- 单击悬浮窗，可以将本次 Codex 对应的 CMD、PowerShell 或 Windows Terminal 窗口切换到最前面。
- 按住悬浮窗任意位置并拖动，可以调整位置；拖动不会切换终端窗口。
- 鼠标悬停时会在悬浮窗上方显示 `×` 按钮，点击后隐藏悬浮窗，不会结束 Codex CLI。
- 使用 `Alt+F4` 关闭悬浮窗时，程序会隐藏到系统托盘，而不是结束监控。
- 单击等待输入或执行出错通知，可以将对应的终端窗口切换到最前面。
- Windows Terminal 的多个标签页共享同一个顶层窗口；程序可以激活该窗口，但不会自动切换其中的标签页。
- 双击托盘图标可重新显示悬浮窗。
- 右键托盘图标可以选择“显示悬浮窗”“切换置顶”“切换静音”或“退出”。
- “静音”只关闭 Windows 气泡通知，不影响状态灯。
- 正常通过 `codex-watch` 启动时，退出 Codex TUI 后，对应的监控程序也会自动退出。

窗口位置、置顶和静音设置保存在：

```text
%LOCALAPPDATA%\CodexMonitor\settings.json
```

每次受监控会话的临时连接信息保存在 `%LOCALAPPDATA%\CodexMonitor\sessions`。其中只包含本地 WebSocket 地址、进程 ID 和启动时间，不包含认证信息、提示词或回复内容；失效记录会在后续启动时自动清理。

## 单独启动悬浮窗

通常不需要直接运行 `CodexMonitor.exe`，因为 `codex-watch.exe` 会管理它的完整生命周期。直接运行时，程序只启动悬浮窗和 App Server，不会自动把当前终端中的普通 `codex` 会话接入该服务。

此模式主要用于检查启动错误。若启动器提示“悬浮窗启动超时”，可以运行：

```powershell
.\artifacts\win-x64\CodexMonitor.exe
```

然后查看状态灯和可能出现的 Windows 错误通知；完成排查后从托盘菜单选择“退出”。

## 开发命令

### 重新构建和自检

```powershell
.\scripts\build.ps1
```

也可只运行自检项目：

```powershell
dotnet run --project .\tests\CodexMonitor.SelfTest\CodexMonitor.SelfTest.csproj -c Release
```

### 发布其他 Windows 运行时

发布脚本接受 .NET Runtime Identifier：

```powershell
.\scripts\publish.ps1 -Runtime win-arm64
```

对应产物会写入 `artifacts\win-arm64`。该目标需要相应 Windows 设备进行实际运行验证。

## 常见问题

### 提示“找不到 Codex CLI”

先确认新终端中能找到原始 Codex：

```powershell
Get-Command codex -All
```

如果系统只找到了不适合直接启动的 `.cmd` shim，请把 `CODEX_CLI_PATH` 指向 `codex.ps1` 或原生 `codex.exe`：

```powershell
[Environment]::SetEnvironmentVariable(
    'CODEX_CLI_PATH',
    'C:\path\to\codex.ps1',
    'User'
)
```

重新打开终端后再试。不要将 `CODEX_CLI_PATH` 指向本项目生成的 `codex.cmd`，否则会形成递归调用。

### 提示“找不到 CodexMonitor.exe”

确认 `CodexMonitor.exe` 与 `codex-watch.exe` 位于同一目录。若必须分开放置，设置：

```powershell
[Environment]::SetEnvironmentVariable(
    'CODEX_MONITOR_APP',
    'D:\Tools\CodexMonitor\CodexMonitor.exe',
    'User'
)
```

### 安装包装器后 `codex` 仍未启动状态灯

关闭所有旧终端并重新打开，再执行 `Get-Command codex -All`。输出的首个匹配项应是发布目录中的 `codex.cmd`。如果不是，请检查发布目录是否已加入 `PATH`，以及是否有其他同名命令排在它前面。

### 状态灯显示协议或连接错误

执行 `codex --version` 检查 Codex CLI 版本，并尝试更新或切换到已验证版本。由于 App Server 是实验性接口，Codex CLI 升级后可能需要同步更新本项目的协议适配。

### 没有收到 Windows 通知

确认托盘菜单没有开启静音，并检查 Windows 的“通知”设置是否允许 `Codex Monitor` 发送通知。状态灯本身不受通知设置影响。

## 状态来源

| 显示状态 | App Server 状态或事件 |
| --- | --- |
| 工作中 | `active`、`turn/started`、`inProgress` |
| 等待输入 | `waitingOnApproval`、`waitingOnUserInput` |
| 已完成 | `turn/completed` / `completed` |
| 已中断 | `interrupted` |
| 错误 | `failed`、`systemError`、不可重试的 `error` |

监控窗口仅连接由自身启动、监听在 `127.0.0.1` 随机端口上的 App Server。
