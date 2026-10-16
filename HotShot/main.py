"""HotShot 入口。"""

from hotshot.dpi import enable_dpi_awareness

enable_dpi_awareness()

from hotshot.app import main  # noqa: E402

if __name__ == "__main__":
    raise SystemExit(main())
