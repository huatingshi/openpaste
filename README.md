# openpaste

Windows 小工具：把文本粘贴进终端，再自动输入到当前光标处。  
适合网页禁止 Ctrl+V 的输入框（验证码、表单等）。

## 别人怎么用

**一行安装（PowerShell）：**

```powershell
irm https://raw.githubusercontent.com/huatingshi/openpaste/main/install.ps1 | iex
```

关掉终端，重新打开，输入：

```text
openpaste
```

1. 把要输入的内容粘贴到提示处，回车  
2. 3 秒内点一下目标输入框  
3. 自动把字打进去（不是 Ctrl+V）

## 克隆后本地安装

```powershell
git clone https://github.com/huatingshi/openpaste.git
cd openpaste
.\install.ps1
```

## 要求

- Windows
- PowerShell 5.1+
