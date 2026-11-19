"""生成 macOS 应用图标 Resources/DeskPal.icns：python3 make-icns.py（需要 Pillow）。
源图和 Windows 版共用 src/DeskPal.Client/Assets/icon-source.png（白底合成）。
和 make-icon.ps1 一样，只从四角向内去掉白边；再按 macOS 图标规范留出边距（内容约占 824/1024），
否则在程序坞和启动台里比其他图标大一圈。"""
from collections import deque
from pathlib import Path
from PIL import Image

here = Path(__file__).resolve().parent
source = here.parent.parent / "DeskPal.Client" / "Assets" / "icon-source.png"
target = here.parent / "Resources" / "DeskPal.icns"


def remove_white_fringe(img: Image.Image) -> Image.Image:
    img = img.convert("RGBA")
    n = img.width
    px = img.load()
    mid = n // 2
    fg = next((px[mid, y] for y in range(n) if max(px[mid, y][:3]) < 90), None)
    if fg is None:
        return img
    den = [max(1, 255 - c) for c in fg[:3]]
    seen = bytearray(n * n)
    queue = deque()
    for seed in (0, n - 1, (n - 1) * n, n * n - 1):
        seen[seed] = 1
        queue.append(seed)
    while queue:
        i = queue.popleft()
        x, y = i % n, i // n
        r, g, b, _ = px[x, y]
        a = [(255 - r) / den[0], (255 - g) / den[1], (255 - b) / den[2]]
        avg = sum(a) / 3
        if max(a) - min(a) > 0.10 or avg >= 0.88:
            continue
        if avg <= 0.05:
            px[x, y] = (0, 0, 0, 0)
        else:
            px[x, y] = (fg[0], fg[1], fg[2], round(min(1.0, avg) * 255))
        for nb in ((i - 1) if x > 0 else -1, (i + 1) if x < n - 1 else -1, (i - n) if y > 0 else -1, (i + n) if y < n - 1 else -1):
            if nb >= 0 and not seen[nb]:
                seen[nb] = 1
                queue.append(nb)
    return img


art = remove_white_fringe(Image.open(source))
art = art.crop(art.getbbox())
canvas = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
inner = art.resize((824, 824), Image.LANCZOS)
canvas.paste(inner, (100, 100), inner)
canvas.save(target, sizes=[(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)])
print(target)
