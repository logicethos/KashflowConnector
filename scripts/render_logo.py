#!/usr/bin/env python3
"""Draw the Kashflow plugin mark: a ledger, a coin, and a connector. No text."""

from pathlib import Path

BG = "#102E36"
PAPER = "#F6F1E6"
RULE = "#D2C8B8"
SPINE = "#1A6E68"
COIN = "#E0B34A"
COIN_INNER = "#F4D78A"
COIN_RIM = "#8C5E22"
LINK = "#D7F4F0"
NODE = "#F7FBFB"
NODE_EDGE = "#1C7A72"


def svg() -> str:
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512" role="img">
  <rect width="512" height="512" fill="{BG}"/>
  <rect x="88" y="120" width="300" height="280" rx="22" fill="{PAPER}"/>
  <rect x="108" y="148" width="16" height="224" rx="4" fill="{SPINE}"/>
  <path d="M148 180 H360 M148 220 H360 M148 260 H360 M148 300 H360 M148 340 H360" stroke="{RULE}" stroke-width="8" stroke-linecap="round"/>
  <circle cx="372" cy="372" r="86" fill="{COIN_RIM}"/>
  <circle cx="372" cy="372" r="74" fill="{COIN}"/>
  <circle cx="372" cy="372" r="46" fill="{COIN_INNER}"/>
  <circle cx="372" cy="372" r="28" fill="none" stroke="{COIN_RIM}" stroke-width="6"/>
  <line x1="312" y1="96" x2="424" y2="96" stroke="{LINK}" stroke-width="12" stroke-linecap="round"/>
  <circle cx="312" cy="96" r="18" fill="{NODE}" stroke="{NODE_EDGE}" stroke-width="6"/>
  <circle cx="424" cy="96" r="18" fill="{NODE_EDGE}"/>
  <circle cx="424" cy="96" r="6" fill="{LINK}"/>
</svg>
"""


def png(path: Path) -> None:
    from PIL import Image, ImageDraw

    image = Image.new("RGB", (512, 512), BG)
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((88, 120, 388, 400), radius=22, fill=PAPER)
    draw.rounded_rectangle((108, 148, 124, 372), radius=4, fill=SPINE)
    for y in (180, 220, 260, 300, 340):
        draw.line((148, y, 360, y), fill=RULE, width=8)
    draw.ellipse((372 - 86, 372 - 86, 372 + 86, 372 + 86), fill=COIN_RIM)
    draw.ellipse((372 - 74, 372 - 74, 372 + 74, 372 + 74), fill=COIN)
    draw.ellipse((372 - 46, 372 - 46, 372 + 46, 372 + 46), fill=COIN_INNER)
    draw.ellipse((372 - 28, 372 - 28, 372 + 28, 372 + 28), outline=COIN_RIM, width=6)
    draw.line((312, 96, 424, 96), fill=LINK, width=12)
    draw.ellipse((312 - 18, 96 - 18, 312 + 18, 96 + 18), fill=NODE, outline=NODE_EDGE, width=6)
    draw.ellipse((424 - 18, 96 - 18, 424 + 18, 96 + 18), fill=NODE_EDGE)
    draw.ellipse((424 - 6, 96 - 6, 424 + 6, 96 + 6), fill=LINK)
    image.save(path, format="PNG")


def main() -> None:
    root = Path(__file__).resolve().parents[1] / "assets"
    root.mkdir(parents=True, exist_ok=True)
    (root / "logo.svg").write_text(svg())
    png(root / "logo.png")


if __name__ == "__main__":
    main()
