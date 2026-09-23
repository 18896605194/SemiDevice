"""Windows 全局热键（RegisterHotKey），默认 Ctrl+Alt+A。"""

from __future__ import annotations

import ctypes
import threading
from ctypes import wintypes
from typing import Callable, Optional

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

WM_HOTKEY = 0x0312
WM_QUIT = 0x0012
MOD_ALT = 0x0001
MOD_CONTROL = 0x0002
MOD_SHIFT = 0x0004
MOD_WIN = 0x0008
MOD_NOREPEAT = 0x4000

HOTKEY_ID = 1

# Some Python builds omit these aliases in ctypes.wintypes
HCURSOR = wintypes.HANDLE
HICON = wintypes.HANDLE
HBRUSH = wintypes.HANDLE
HINSTANCE = wintypes.HINSTANCE
HWND = wintypes.HWND

user32.RegisterClassW.restype = wintypes.ATOM
user32.RegisterClassW.argtypes = [ctypes.c_void_p]
user32.CreateWindowExW.restype = HWND
user32.CreateWindowExW.argtypes = [
    wintypes.DWORD,
    wintypes.LPCWSTR,
    wintypes.LPCWSTR,
    wintypes.DWORD,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    HWND,
    wintypes.HMENU,
    HINSTANCE,
    ctypes.c_void_p,
]
user32.RegisterHotKey.argtypes = [HWND, ctypes.c_int, ctypes.c_uint, ctypes.c_uint]
user32.RegisterHotKey.restype = wintypes.BOOL
user32.UnregisterHotKey.argtypes = [HWND, ctypes.c_int]
user32.GetMessageW.argtypes = [ctypes.c_void_p, HWND, ctypes.c_uint, ctypes.c_uint]
user32.GetMessageW.restype = ctypes.c_int
user32.PostThreadMessageW.argtypes = [wintypes.DWORD, ctypes.c_uint, ctypes.c_size_t, ctypes.c_size_t]
user32.DefWindowProcW.argtypes = [HWND, ctypes.c_uint, ctypes.c_size_t, ctypes.c_size_t]
user32.DefWindowProcW.restype = ctypes.c_size_t
kernel32.GetModuleHandleW.restype = HINSTANCE
kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]


class POINT(ctypes.Structure):
    _fields_ = [("x", wintypes.LONG), ("y", wintypes.LONG)]


class MSG(ctypes.Structure):
    _fields_ = [
        ("hwnd", HWND),
        ("message", wintypes.UINT),
        ("wParam", ctypes.c_size_t),
        ("lParam", ctypes.c_size_t),
        ("time", wintypes.DWORD),
        ("pt", POINT),
    ]


class WNDCLASS(ctypes.Structure):
    _fields_ = [
        ("style", ctypes.c_uint),
        ("lpfnWndProc", ctypes.c_void_p),
        ("cbClsExtra", ctypes.c_int),
        ("cbWndExtra", ctypes.c_int),
        ("hInstance", HINSTANCE),
        ("hIcon", HICON),
        ("hCursor", HCURSOR),
        ("hbrBackground", HBRUSH),
        ("lpszMenuName", wintypes.LPCWSTR),
        ("lpszClassName", wintypes.LPCWSTR),
    ]


class GlobalHotkey:
    def __init__(
        self,
        callback: Callable[[], None],
        modifiers: int = MOD_CONTROL | MOD_ALT,
        vk: int = 0x41,
    ):
        self.callback = callback
        self.modifiers = modifiers | MOD_NOREPEAT
        self.vk = vk
        self._thread: Optional[threading.Thread] = None
        self._hwnd = None
        self._registered = False
        self._tid = 0

    @property
    def hotkey_label(self) -> str:
        parts = []
        mods = self.modifiers & ~MOD_NOREPEAT
        if mods & MOD_CONTROL:
            parts.append("Ctrl")
        if mods & MOD_ALT:
            parts.append("Alt")
        if mods & MOD_SHIFT:
            parts.append("Shift")
        if mods & MOD_WIN:
            parts.append("Win")
        if 0x30 <= self.vk <= 0x5A:
            ch = chr(self.vk).upper()
        else:
            ch = f"VK{self.vk:02X}"
        parts.append(ch)
        return "+".join(parts)

    def start(self) -> bool:
        ready = threading.Event()
        state = {"registered": False, "error": None}

        def run() -> None:
            self._tid = kernel32.GetCurrentThreadId()
            hinstance = kernel32.GetModuleHandleW(None)
            class_name = "HotShotHotkeyMsgWindow"

            wc = WNDCLASS()
            wc.lpfnWndProc = ctypes.cast(user32.DefWindowProcW, ctypes.c_void_p)
            wc.hInstance = hinstance
            wc.lpszClassName = class_name

            if not user32.RegisterClassW(ctypes.byref(wc)):
                state["error"] = f"RegisterClass failed: {ctypes.GetLastError()}"
                ready.set()
                return

            hwnd = user32.CreateWindowExW(
                0,
                class_name,
                "HotShotHotkey",
                0,
                0,
                0,
                0,
                0,
                HWND(-3),  # HWND_MESSAGE
                None,
                hinstance,
                None,
            )
            self._hwnd = hwnd
            if not hwnd:
                state["error"] = f"CreateWindowEx failed: {ctypes.GetLastError()}"
                ready.set()
                return

            registered = bool(user32.RegisterHotKey(hwnd, HOTKEY_ID, self.modifiers, self.vk))
            self._registered = registered
            state["registered"] = registered
            if not registered:
                state["error"] = f"RegisterHotKey failed: {ctypes.GetLastError()}"
                ready.set()
                return

            ready.set()

            msg = MSG()
            while True:
                ret = user32.GetMessageW(ctypes.byref(msg), None, 0, 0)
                if ret in (0, -1):
                    break
                if msg.message == WM_HOTKEY and msg.wParam == HOTKEY_ID:
                    try:
                        self.callback()
                    except Exception:
                        pass
                user32.TranslateMessage(ctypes.byref(msg))
                user32.DispatchMessageW(ctypes.byref(msg))

            user32.UnregisterHotKey(hwnd, HOTKEY_ID)
            user32.DestroyWindow(hwnd)
            self._registered = False

        self._thread = threading.Thread(target=run, name="HotShotHotkey", daemon=True)
        self._thread.start()
        ready.wait(timeout=3.0)
        if not state["registered"] and state.get("error"):
            print("hotkey error:", state["error"])
        return state["registered"]

    def stop(self) -> None:
        if self._tid:
            user32.PostThreadMessageW(self._tid, WM_QUIT, 0, 0)
        self._registered = False


kernel32.GetCurrentThreadId.restype = wintypes.DWORD
kernel32.GetCurrentThreadId.argtypes = []
user32.DestroyWindow.argtypes = [HWND]
user32.TranslateMessage.argtypes = [ctypes.c_void_p]
user32.DispatchMessageW.argtypes = [ctypes.c_void_p]
