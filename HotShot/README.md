# HotShot 闪截

独立桌面截图工具，**无需登录微信或任何账号**。按全局快捷键即可截图。

## 功能

- 全局热键 **Ctrl+Alt+A** 一键截图（默认微信同款）
- 框选区域后可标注：矩形 / 圆 / 箭头 / 画笔 / 文字 / 马赛克
- **Enter** 复制到剪贴板
- **Ctrl+Enter** 复制并保存到 `图片\HotShot\`
- **Esc** 取消 · **Ctrl+Z** 撤销
- 主面板常驻，随时点「立即截图」

## 使用

### 首次安装（创建桌面快捷方式）

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Code\HotShot\install_shortcut.ps1"
```

### 直接运行

```powershell
& "$env:MIMO_PYTHON" "D:\Code\HotShot\main.py"
```

或双击 `D:\Code\HotShot\HotShot.bat`。

## 快捷键

| 按键 | 作用 |
|------|------|
| Ctrl+Alt+A | 全局截图 |
| 1–6 | 切换标注工具 |
| Enter | 完成并复制 |
| Ctrl+Enter / Ctrl+S | 完成并保存 |
| Ctrl+Z | 撤销 |
| Esc | 取消 |

## 说明

- 截图保存目录：`%USERPROFILE%\Pictures\HotShot\`
- 若 Ctrl+Alt+A 被其它软件占用，改 `hotshot/app.py` 里的热键定义即可
- 关闭主面板即退出程序（停止全局热键）
