"""全屏截图选择 + 标注编辑器（仿微信截图交互）。"""

from __future__ import annotations

import math
import time
from pathlib import Path
from typing import List, Optional, Tuple

from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageTk

try:
    import tkinter as tk
    from tkinter import colorchooser, simpledialog
except ImportError:  # pragma: no cover
    raise

Point = Tuple[int, int]
Box = Tuple[int, int, int, int]

SAVE_DIR = Path.home() / "Pictures" / "HotShot"
TOOLS = ("rect", "ellipse", "arrow", "pen", "text", "mosaic")
TOOL_LABELS = {
    "rect": "矩形",
    "ellipse": "圆",
    "arrow": "箭头",
    "pen": "画笔",
    "text": "文字",
    "mosaic": "马赛克",
}
DEFAULT_COLOR = "#FF4D4F"
PEN_WIDTH = 3


class ScreenshotEditor:
    """全屏遮罩上框选区域，支持矩形/圆/箭头/画笔/文字/马赛克。"""

    def __init__(self, root: tk.Tk, image: Image.Image, on_done=None):
        self.root = root
        self.on_done = on_done
        self.src = image.convert("RGB")
        self.result: Optional[Image.Image] = None
        self.saved_path: Optional[Path] = None

        self.tool = "rect"
        self.color = DEFAULT_COLOR
        self.pen_width = PEN_WIDTH

        # selection region (capture crop) — frozen after first drag
        self._sel_start: Optional[Point] = None
        self._sel_end: Optional[Point] = None
        # in-progress annotation stroke
        self._drag_start: Optional[Point] = None
        self._drag_end: Optional[Point] = None
        self._drawing = False
        self._pen_points: List[Point] = []
        self._layers: List[Image.Image] = []
        self._history: List[dict] = []
        self._mode = "select"  # select → annotate

        self._toolbar: Optional[tk.Toplevel] = None
        self._color_label: Optional[tk.Label] = None
        self._sel_tk = None
        self._preview_id = None

        self._build_chrome()

    def _build_chrome(self) -> None:
        self.win = tk.Toplevel(self.root)
        self.win.withdraw()
        self.win.overrideredirect(True)
        self.win.attributes("-topmost", True)

        w, h = self.src.size
        self.canvas = tk.Canvas(
            self.win,
            width=w,
            height=h,
            highlightthickness=0,
            borderwidth=0,
            cursor="tcross",
            bg="#000000",
        )
        self.canvas.pack(fill="both", expand=True)

        dark = self.src.convert("RGBA")
        veil = Image.new("RGBA", dark.size, (0, 0, 0, 150))
        dark = Image.alpha_composite(dark, veil)
        self._dark_tk = ImageTk.PhotoImage(dark)
        self.canvas.create_image(0, 0, anchor="nw", image=self._dark_tk)

        self._sel_img_id = self.canvas.create_image(0, 0, anchor="nw", state="hidden")
        self._sel_rect_id = self.canvas.create_rectangle(
            0, 0, 0, 0, outline="#00D4FF", width=2, state="hidden"
        )

        self.win.bind("<Escape>", lambda e: self._cancel())
        self.win.bind("<Return>", lambda e: self._finish(save=False))
        self.win.bind("<Control-Return>", lambda e: self._finish(save=True))
        self.win.bind("<Control-s>", lambda e: self._finish(save=True))
        self.win.bind("<Control-c>", lambda e: self._finish(save=False))
        self.win.bind("Control+z", lambda e: self._undo())
        for i, tool in enumerate(TOOLS, start=1):
            self.win.bind(str(i), lambda e, t=tool: self._apply_tool(t))

        self.canvas.bind("<ButtonPress-1>", self._on_press)
        self.canvas.bind("<B1-Motion>", self._on_drag)
        self.canvas.bind("<ButtonRelease-1>", self._on_release)

        self.win.geometry(f"{w}x{h}+0+0")
        self.win.deiconify()
        self.win.focus_force()
        self.win.lift()

    def _clamp(self, x: int, y: int) -> Point:
        w, h = self.src.size
        return max(0, min(w - 1, int(x))), max(0, min(h - 1, int(y)))

    def _norm_box(self, p1: Point, p2: Point) -> Box:
        x1, y1 = p1
        x2, y2 = p2
        left, right = sorted((x1, x2))
        top, bottom = sorted((y1, y2))
        return left, top, right, bottom

    def _sel_box(self) -> Optional[Box]:
        if not self._sel_start or not self._sel_end:
            return None
        return self._norm_box(self._sel_start, self._sel_end)

    def _drag_box(self) -> Optional[Box]:
        if not self._drag_start or not self._drag_end:
            return None
        return self._norm_box(self._drag_start, self._drag_end)

    def _has_selection(self) -> bool:
        box = self._sel_box()
        return bool(box and box[2] - box[0] >= 4 and box[3] - box[1] >= 4)

    # ----- mouse -----
    def _on_press(self, event) -> None:
        pos = self._clamp(event.x, event.y)
        if self._mode == "annotate" and self.tool == "text":
            self._add_text(pos)
            return
        self._drawing = True
        self._drag_start = pos
        self._drag_end = pos
        if self._mode == "select":
            self._sel_start = pos
            self._sel_end = pos
        if self.tool == "pen" and self._mode == "annotate":
            self._pen_points = [pos]
        self._refresh()

    def _on_drag(self, event) -> None:
        if not self._drawing:
            return
        pos = self._clamp(event.x, event.y)
        self._drag_end = pos
        if self._mode == "select":
            self._sel_end = pos
        if self._mode == "annotate" and self.tool == "pen":
            self._pen_points.append(pos)
        self._refresh()

    def _on_release(self, event) -> None:
        if not self._drawing:
            return
        self._drawing = False
        pos = self._clamp(event.x, event.y)
        self._drag_end = pos
        if self._mode == "select":
            self._sel_end = pos
            if self._has_selection():
                self._mode = "annotate"
                self._ensure_toolbar()
            else:
                self._sel_start = self._sel_end = None
            self._drag_start = self._drag_end = None
            self._refresh()
            return

        if self.tool == "pen":
            if len(self._pen_points) >= 2:
                self._commit_pen()
            else:
                self._pen_points = []
            self._drag_start = self._drag_end = None
            self._refresh()
            return

        if self.tool in ("rect", "ellipse", "arrow"):
            box = self._drag_box()
            if box and box[2] - box[0] >= 2 and box[3] - box[1] >= 2:
                self._commit_shape(self.tool, box)
            self._drag_start = self._drag_end = None
            self._refresh()
            return

        if self.tool == "mosaic":
            box = self._drag_box() or self._sel_box()
            if box and box[2] - box[0] >= 2 and box[3] - box[1] >= 2:
                self._apply_mosaic(box)
            self._drag_start = self._drag_end = None
            self._refresh()
            return

        self._drag_start = self._drag_end = None
        self._refresh()

    # ----- display -----
    def _hex_rgba(self, alpha: int = 255) -> Tuple[int, int, int, int]:
        c = self.color.lstrip("#")
        return int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16), alpha

    def _composed_full(self) -> Image.Image:
        result = self.src.convert("RGBA")
        for layer in self._layers:
            result = Image.alpha_composite(result, layer)
        return result.convert("RGB")

    def _refresh(self) -> None:
        if self._preview_id is not None:
            self.canvas.delete(self._preview_id)
            self._preview_id = None

        sel = self._sel_box()
        if not sel or not self._has_selection():
            self.canvas.itemconfigure(self._sel_rect_id, state="hidden")
            self.canvas.itemconfigure(self._sel_img_id, state="hidden")
            self._hide_toolbar()
            return

        left, top, right, bottom = sel
        self.canvas.coords(self._sel_rect_id, left, top, right, bottom)
        self.canvas.itemconfigure(self._sel_rect_id, state="normal")

        full = self._composed_full()
        region = full.crop((left, top, right, bottom))
        self._sel_tk = ImageTk.PhotoImage(region)
        self.canvas.coords(self._sel_img_id, left, top)
        self.canvas.itemconfigure(self._sel_img_id, image=self._sel_tk, state="normal")
        self._place_toolbar(left, top, right, bottom)

        if self._drawing and self._mode == "annotate":
            self._draw_live_preview()

    def _draw_live_preview(self) -> None:
        if self.tool == "pen" and len(self._pen_points) >= 2:
            pts = [c for p in self._pen_points for c in p]
            self._preview_id = self.canvas.create_line(
                *pts, fill=self.color, width=self.pen_width + 1, smooth=True
            )
            return
        box = self._drag_box()
        if not box:
            return
        left, top, right, bottom = box
        if self.tool == "rect":
            self._preview_id = self.canvas.create_rectangle(
                left, top, right, bottom, outline=self.color, width=self.pen_width + 2
            )
        elif self.tool == "ellipse":
            self._preview_id = self.canvas.create_oval(
                left, top, right, bottom, outline=self.color, width=self.pen_width + 2
            )
        elif self.tool == "arrow":
            self._preview_id = self.canvas.create_line(
                left, top, right, bottom, fill=self.color, width=self.pen_width + 2, arrow="last"
            )

    # ----- toolbar -----
    def _ensure_toolbar(self) -> None:
        if self._toolbar is not None:
            return
        bar = tk.Toplevel(self.win)
        bar.overrideredirect(True)
        bar.attributes("-topmost", True)
        bar.configure(bg="#2F3542")
        self._toolbar = bar

        def mk(text, cmd, bg="#2F3542"):
            lbl = tk.Label(
                bar,
                text=text,
                bg=bg,
                fg="#FFFFFF",
                font=("Segoe UI", 10),
                padx=10,
                pady=6,
                cursor="hand2",
            )
            lbl.pack(side="left", padx=1, pady=2)
            lbl.bind("<Button-1>", lambda e, c=cmd: c())
            lbl.bind("<Enter>", lambda e, w=lbl: w.configure(bg="#4A5568"))
            lbl.bind("<Leave>", lambda e, w=lbl: w.configure(bg=bg))
            return lbl

        for tool in TOOLS:
            mk(TOOL_LABELS[tool], lambda t=tool: self._apply_tool(t))
        self._color_label = mk("●", self._pick_color)
        self._color_label.configure(fg=self.color, font=("Segoe UI", 14, "bold"))
        mk("撤销", self._undo)
        mk("✕", self._cancel, bg="#8B3A3A")
        mk("保存", lambda: self._finish(save=True), bg="#2E7D4F")
        mk("完成", lambda: self._finish(save=False), bg="#00A8FF")

    def _place_toolbar(self, left: int, top: int, right: int, bottom: int) -> None:
        if self._toolbar is None:
            return
        self._toolbar.update_idletasks()
        bw = self._toolbar.winfo_reqwidth()
        bh = self._toolbar.winfo_reqheight()
        x = max(0, (left + right) // 2 - bw // 2)
        y = bottom + 8
        screen_h = self.root.winfo_screenheight()
        if y + bh > screen_h:
            y = max(0, top - bh - 8)
        self._toolbar.geometry(f"+{int(x)}+{int(y)}")
        self._toolbar.lift()

    def _hide_toolbar(self) -> None:
        if self._toolbar is not None:
            self._toolbar.destroy()
            self._toolbar = None
            self._color_label = None

    def _pick_color(self) -> None:
        color = colorchooser.askcolor(color=self.color, title="选择颜色", parent=self.win)
        if color and color[1]:
            self.color = color[1]
            if self._color_label is not None:
                self._color_label.configure(fg=self.color)

    def _apply_tool(self, tool: str) -> None:
        self.tool = tool
        if tool == "mosaic" and self._mode == "annotate":
            box = self._drag_box() or self._sel_box()
            if box:
                self._apply_mosaic(box)

    # ----- annotations -----
    def _new_layer(self) -> Image.Image:
        return Image.new("RGBA", self.src.size, (0, 0, 0, 0))

    def _push_layer(self, layer: Image.Image) -> None:
        self._layers.append(layer)
        self._history.append({"type": "layer"})

    def _commit_shape(self, kind: str, box: Box) -> None:
        left, top, right, bottom = box
        layer = self._new_layer()
        draw = ImageDraw.Draw(layer)
        fill = self._hex_rgba()
        width = self.pen_width + 2
        if kind == "rect":
            draw.rectangle([left, top, right, bottom], outline=fill, width=width)
        elif kind == "ellipse":
            draw.ellipse([left, top, right, bottom], outline=fill, width=width)
        elif kind == "arrow":
            self._draw_arrow(draw, (left, top), (right, bottom), fill, width + 1)
        self._push_layer(layer)

    def _draw_arrow(self, draw: ImageDraw.ImageDraw, p1: Point, p2: Point, fill, width: int) -> None:
        draw.line([p1, p2], fill=fill, width=width)
        x1, y1 = p1
        x2, y2 = p2
        angle = math.atan2(y2 - y1, x2 - x1)
        size = 14 + width * 2
        a1 = angle + math.pi / 7
        a2 = angle - math.pi / 7
        tip = (x2, y2)
        wing1 = (x2 - size * math.cos(a1), y2 - size * math.sin(a1))
        wing2 = (x2 - size * math.cos(a2), y2 - size * math.sin(a2))
        draw.polygon([tip, wing1, wing2], fill=fill)

    def _commit_pen(self) -> None:
        if len(self._pen_points) < 2:
            return
        layer = self._new_layer()
        draw = ImageDraw.Draw(layer)
        draw.line(
            self._pen_points,
            fill=self._hex_rgba(),
            width=self.pen_width + 1,
            joint="curve",
        )
        self._push_layer(layer)
        self._pen_points = []

    def _add_text(self, pos: Point) -> None:
        text = simpledialog.askstring("文字标注", "输入文字：", parent=self.win)
        if not text:
            return
        layer = self._new_layer()
        draw = ImageDraw.Draw(layer)
        draw.text(pos, text, fill=self._hex_rgba(), font=self._font(22))
        self._push_layer(layer)
        self._refresh()

    def _font(self, size: int) -> ImageFont.ImageFont:
        for name in ("msyh.ttc", "msyhbd.ttc", "simhei.ttf", "arial.ttf"):
            try:
                return ImageFont.truetype(name, size)
            except Exception:
                continue
        return ImageFont.load_default()

    def _apply_mosaic(self, box: Box) -> None:
        left, top, right, bottom = box
        if right - left < 4 or bottom - top < 4:
            return
        full = self._composed_full()
        region = full.crop((left, top, right, bottom))
        factor = 12
        small = region.resize(
            (max(1, region.width // factor), max(1, region.height // factor)),
            Image.Resampling.BILINEAR,
        )
        pixel = small.resize(region.size, Image.Resampling.NEAREST)
        pixel = pixel.filter(ImageFilter.GaussianBlur(0.4))
        layer = self._new_layer()
        layer.paste(pixel.convert("RGBA"), (left, top))
        self._push_layer(layer)

    def _undo(self) -> None:
        if not self._history:
            return
        self._history.pop()
        if self._layers:
            self._layers.pop()
        self._refresh()

    # ----- finish -----
    def _finish(self, save: bool) -> None:
        if not self._has_selection() and not self._layers:
            self._cancel()
            return
        self.result = self._composed_full()
        box = self._sel_box()
        if box:
            left, top, right, bottom = box
            self.result = self.result.crop((left, top, right, bottom))

        from .clipboard import copy_image_to_clipboard

        try:
            copy_image_to_clipboard(self.result)
        except Exception:
            pass

        if save:
            SAVE_DIR.mkdir(parents=True, exist_ok=True)
            path = SAVE_DIR / f"HotShot_{time.strftime('%Y%m%d_%H%M%S')}.png"
            self.result.save(path, "PNG")
            self.saved_path = path
        else:
            self.saved_path = None

        self._teardown(copied=True, saved=bool(save))

    def _cancel(self) -> None:
        self.result = None
        self.saved_path = None
        self._teardown(copied=False, saved=False)

    def _teardown(self, copied: bool, saved: bool) -> None:
        self._hide_toolbar()
        try:
            self.win.destroy()
        except Exception:
            pass
        if self.on_done:
            self.on_done(self.result, self.saved_path if saved else None, copied)

    def run(self) -> None:
        self.win.grab_set()
        self.root.wait_window(self.win)
