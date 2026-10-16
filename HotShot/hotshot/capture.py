"""全屏截图。"""

from PIL import Image, ImageGrab


def capture_screen() -> Image.Image:
    """抓取整块虚拟屏幕（支持多显示器）。"""
    try:
        return ImageGrab.grab(all_screens=True, include_layered_windows=True)
    except TypeError:
        return ImageGrab.grab(all_screens=True)
    except Exception:
        return ImageGrab.grab()
