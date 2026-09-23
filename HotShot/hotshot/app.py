"""HotShot 主程序：常驻小面板 + 全局热键截图。"""

from __future__ import annotations

import sys
import threading
import traceback

try:
    import tkinter as tk
except ImportError:  # pragma: no cover
    raise

from . import APP_NAME, __version__
from .capture import capture_screen
from .dpi import enable_dpi_awareness
from .editor import ScreenshotEditor
from .hotkey import GlobalHotkey, MOD_ALT, MOD_CONTROL


class HotShotApp:
    def __init__(self) -> None:
        enable_dpi_awareness()
        self.root = tk.Tk()
        self.root.title(APP_NAME)
        self.root.geometry("340x200+48+48")
        self.root.resizable(False, False)
        self.root.attributes("-topmost", True)
        self.root.configure(bg="#1E1E2E")

        self.busy = False
        self.status = tk.StringVar(value="就绪")

        self._build_ui()
        self.hotkey = GlobalHotkey(self._on_hotkey, MOD_CONTROL | MOD_ALT, 0x41)
        self.hotkey_ok = self.hotkey.start()

        if not self.hotkey_ok:
            self.status.set("热键注册失败（可能被占用）")
        else:
            self.status.set(f"就绪 · {self.hotkey.hotkey_label}")

        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

    def _build_ui(self) -> None:
        tk.Label(
            self.root,
            text="闪截 HotShot",
            bg="#1E1E2E",
            fg="#FFFFFF",
            font=("Segoe UI", 16, "bold"),
        ).pack(pady=(18, 2))
        tk.Label(
            self.root,
            text=f"独立截图 · 无需登录任何账号 · v{__version__}",
            bg="#1E1E2E",
            fg="#A0A4B8",
            font=("Segoe UI", 9),
        ).pack()

        tk.Label(
            self.root,
            textvariable=self.status,
            bg="#1E1E2E",
            fg="#00D4FF",
            font=("Segoe UI", 10),
        ).pack(padx=16, pady=10)

        btn_row = tk.Frame(self.root, bg="#1E1E2E")
        btn_row.pack(pady=(2, 8))

        def make_btn(text, cmd, primary=False):
            bg = "#00A8FF" if primary else "#343B4F"
            tk.Button(
                btn_row,
                text=text,
                command=cmd,
                bg=bg,
                fg="#FFFFFF",
                activebackground="#0090E0" if primary else "#454D63",
                activeforeground="#FFFFFF",
                relief="flat",
                bd=0,
                padx=14,
                pady=8,
                font=("Segoe UI", 10, "bold"),
                cursor="hand2",
            ).pack(side="left", padx=6)

        make_btn("立即截图", self._trigger_screenshot, primary=True)
        make_btn("退出", self._on_close)

        tk.Label(
            self.root,
            text="全局热键 Ctrl+Alt+A（无需微信）\n框选后：1矩形 2圆 3箭头 4画笔 5文字 6马赛克\nEnter 复制 · Ctrl+Enter 保存 · Esc 取消",
            bg="#1E1E2E",
            fg="#6C7386",
            font=("Segoe UI", 8),
            justify="center",
        ).pack(pady=(0, 12))

    def _on_hotkey(self) -> None:
        self.root.after(0, self._trigger_screenshot)

    def _trigger_screenshot(self) -> None:
        if self.busy:
            return
        self.busy = True
        self.status.set("截屏中…")
        self.root.update_idletasks()

        def work() -> None:
            try:
                img = capture_screen()
                self.root.after(20, lambda: self._open_editor(img))
            except Exception:
                traceback.print_exc()
                self.root.after(0, lambda: self._on_editor_done(None, None, False))

        threading.Thread(target=work, daemon=True).start()

    def _open_editor(self, img) -> None:
        try:
            self.root.withdraw()
            self.root.update_idletasks()
            editor = ScreenshotEditor(self.root, img, on_done=self._on_editor_done)
            editor.run()
        except Exception:
            traceback.print_exc()
            self.root.deiconify()
            self.busy = False
            self.status.set("截图失败")

    def _on_editor_done(self, image, saved_path, copied) -> None:
        self.root.deiconify()
        self.root.lift()
        self.busy = False
        if image is None:
            self.status.set("已取消")
        elif saved_path:
            self.status.set(f"已复制并保存 · {saved_path.name}")
        elif copied:
            self.status.set("已复制到剪贴板")
        else:
            self.status.set("完成")
        self.root.after(2500, self._reset_status)

    def _reset_status(self) -> None:
        if self.busy:
            return
        if self.hotkey_ok:
            self.status.set(f"就绪 · {self.hotkey.hotkey_label}")
        else:
            self.status.set("就绪（无全局热键）")

    def _on_close(self) -> None:
        try:
            self.hotkey.stop()
        except Exception:
            pass
        self.root.destroy()

    def run(self) -> None:
        self.root.mainloop()


def main() -> int:
    app = HotShotApp()
    app.run()
    return 0


if __name__ == "__main__":
    sys.exit(main())
