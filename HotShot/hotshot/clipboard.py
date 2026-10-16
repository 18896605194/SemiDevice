"""把 PIL 图片写入 Windows 剪贴板（CF_DIB）。"""

from __future__ import annotations

import ctypes
import io

from PIL import Image

CF_DIB = 8
GMEM_MOVEABLE = 0x0002

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

kernel32.GlobalAlloc.restype = ctypes.c_void_p
kernel32.GlobalAlloc.argtypes = [ctypes.c_uint, ctypes.c_size_t]
kernel32.GlobalLock.restype = ctypes.c_void_p
kernel32.GlobalLock.argtypes = [ctypes.c_void_p]
kernel32.GlobalUnlock.argtypes = [ctypes.c_void_p]
kernel32.GlobalFree.argtypes = [ctypes.c_void_p]
user32.SetClipboardData.restype = ctypes.c_void_p
user32.SetClipboardData.argtypes = [ctypes.c_uint, ctypes.c_void_p]
user32.OpenClipboard.argtypes = [ctypes.c_void_p]
user32.OpenClipboard.restype = ctypes.c_int
user32.EmptyClipboard.argtypes = []
user32.CloseClipboard.argtypes = []


def _image_to_dib(img: Image.Image) -> bytes:
    if img.mode != "RGB":
        img = img.convert("RGB")
    buf = io.BytesIO()
    img.save(buf, format="BMP")
    return buf.getvalue()[14:]


def copy_image_to_clipboard(img: Image.Image) -> bool:
    dib = _image_to_dib(img)
    if not dib:
        return False

    # Retry open a few times; clipboard may be busy
    opened = False
    for _ in range(10):
        if user32.OpenClipboard(None):
            opened = True
            break
        import time

        time.sleep(0.05)
    if not opened:
        return False

    handle = None
    try:
        user32.EmptyClipboard()
        handle = kernel32.GlobalAlloc(GMEM_MOVEABLE, len(dib))
        if not handle:
            return False
        locked = kernel32.GlobalLock(handle)
        if not locked:
            return False
        try:
            ctypes.memmove(locked, dib, len(dib))
        finally:
            kernel32.GlobalUnlock(handle)
        if not user32.SetClipboardData(CF_DIB, handle):
            return False
        # ownership transferred to clipboard on success
        handle = None
        return True
    finally:
        if handle:
            kernel32.GlobalFree(handle)
        user32.CloseClipboard()
