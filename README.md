# Codex Monitor

一个在 Windows 本地运行的 Codex CLI 状态悬浮窗。它通过 Codex App Server 的结构化协议显示 Agent 是否正在工作、等待输入、完成、中断或发生错误。

## 特性

- 始终置顶、可拖拽、位置记忆的无边框悬浮窗
- 系统托盘、静音和展开会话列表
- 多会话汇总，优先显示等待输入和错误
- 等待批准或用户输入时发送 Windows 通知
- 不解析终端文字，不读取 Codex SQLite，不记录提示词或回复内容
- `codex-watch` 保留原生 Codex TUI、键盘输入和终端输出

## 环境要求

- Windows 10/11 x64
- Codex CLI 已安装并可通过 `codex` 命令运行
- 当前协议在 `codex-cli 0.146.0` 上验证

App Server 仍是 Codex 的实验性接口。程序启动时使用标准 `initialize` 握手；协议不兼容时会在悬浮窗显示错误，而不会回退到解析终端文本。

## 构建

仓库内 `.tools/dotnet` 存在时脚本优先使用它，否则使用 PATH 中的 .NET 8 SDK。

```powershell
.\scripts\build.ps1
```

## 发布

```powershell
.\scripts\publish.ps1
```

产物位于 `artifacts\win-x64`：

- `CodexMonitor.exe`：WPF 悬浮窗和本地 App Server 宿主
- `codex-watch.exe`：在当前终端启动受监控的 Codex TUI

把两个 EXE 放在同一目录，并把该目录加入 PATH。然后使用：

```powershell
codex-watch
codex-watch "检查项目并运行测试"
codex-watch -C D:\path\to\repo
```

第一版不会修改 PowerShell Profile，也不会覆盖原始 `codex.exe`。

## 状态来源

| 显示状态 | App Server 状态或事件 |
|---|---|
| 工作中 | `active`、`turn/started`、`inProgress` |
| 等待输入 | `waitingOnApproval`、`waitingOnUserInput` |
| 已完成 | `turn/completed` / `completed` |
| 已中断 | `interrupted` |
| 错误 | `failed`、`systemError`、不可重试的 `error` |

监控窗口只监听 `127.0.0.1` 上由自身启动的 App Server。运行时端点写入 `%LOCALAPPDATA%\CodexMonitor\runtime.json`，供启动器发现，不包含认证信息。
