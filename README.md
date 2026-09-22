# openpaste

Windows 临时终端工具：复制文字，点击目标输入框，按快捷键自动输入。
适合不接受 Ctrl+V、但接受模拟键盘输入的网页和应用。

**运行时才监听快捷键；关闭终端或按 Ctrl+C 就结束。没有托盘、后台服务或开机自启动。**

## 安装

在 PowerShell 中执行：

```powershell
irm https://raw.githubusercontent.com/huatingshi/openpaste/main/install.ps1 | iex
```

重新打开终端后运行：

```powershell
openpaste
```

也可以克隆后直接运行，或安装到用户的 `bin` 目录：

```powershell
git clone https://github.com/huatingshi/openpaste.git
cd openpaste
.\openpaste.cmd
# 可选：安装命令
.\install.ps1
```

## 快捷键模式（默认）

1. 在终端运行 `openpaste`，保持该终端会话打开。
2. 复制需要输入的文本。
3. 点击目标输入框，按 **Ctrl+Alt+V**，然后松开按键。
4. 输入过程中按 **Esc** 可取消本次输入，程序继续等待下一次触发。
5. 回到终端按 **Ctrl+C**，或关闭该终端会话，结束程序并释放快捷键。

默认每个字符间隔 10 毫秒；等待 Ctrl / Alt / Shift / Win 全部松开后才开始输入。
输入过程中切换前台窗口，或按下 Ctrl / Alt / Shift / Win，会停止剩余输入。
正在输入时重复触发快捷键不会再排队输入一遍。

```powershell
# 修改快捷键（占用时程序会报错）
openpaste -Hotkey Ctrl+Alt+P

# 调慢输入速度，适应处理较慢的输入框
openpaste -IntervalMs 30

# 保留剪贴板换行：每个换行会发送 Enter
openpaste -Multiline

# 查看参数
openpaste -Help
```

快捷键支持 Ctrl、Alt、Shift、Win 与 A-Z、0-9、F1-F24 的组合，必须包含 Ctrl、Alt 或 Win。
Esc 仅在读取/发送一次文本期间被临时占用。Esc 已被其他程序注册时，本次输入会中止。

**默认把换行和制表符转为空格，保留其余空白。** `-Multiline` 会发送 Enter；某些聊天框或表单可能因此提交内容，请只在需要的目标中启用。制表符始终转为空格。

## 手动模式

保留原来的“终端粘贴 → 倒计时 → 点击输入框”操作：

```powershell
openpaste -Manual
openpaste -Manual -DelaySeconds 5 -IntervalMs 20
```

在提示处粘贴一行文本并回车，倒计时结束后开始输入。空回车退出，Esc 取消倒计时或本次输入。
该模式逐行读取；包含多行的文本请使用默认剪贴板快捷键模式。

## 要求与限制

- Windows，Windows PowerShell 5.1；启动器自动使用 STA 模式运行。
- 使用 Windows 自带的 .NET Framework / Windows Forms，无需安装其他运行时。
- 前台窗口保护不能识别同一窗口内切换输入框的情况；输入期间请保持目标输入框焦点。
- 部分安全输入框、远程桌面和管理员权限应用可能拒绝模拟输入。程序检查 Windows 接受的事件数量；“Text sent”表示事件已发送，不保证目标软件最终接收了每个字符。
- 文本只在本次会话内处理，不上传，不写入日志，不修改剪贴板。

## 开发验证

```powershell
powershell -STA -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1
```

测试覆盖取消、焦点变化、快捷键释放、重复触发、剪贴板重试、失败反馈和 Unicode / 多行处理。
使用模拟桌面，不读取真实剪贴板或向其他程序发送键盘输入。

安装测试在临时目录执行，模拟用户 PATH，不修改真实安装或环境变量。
有交互式 Windows 桌面时，还可以验证真实终端生命周期：

```powershell
powershell -STA -NoProfile -ExecutionPolicy Bypass -File .\tests\console-smoke.ps1
```

该测试创建独立的隐藏测试终端，验证 Ctrl+C 退出及快捷键释放、关闭终端后进程结束。
只使用测试快捷键 Ctrl+Alt+Shift+F24，不读取剪贴板或发送文字。
