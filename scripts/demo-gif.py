#!/usr/bin/env python3
"""Renders a short demo GIF of Radial Convert: a mock Explorer window, the real radial menu
graphics (from `RadialConvert preview`), a flick per file type and the result.

    python3 scripts/demo-gif.py <menus-dir> <out.gif>

<menus-dir> holds img/aud/vid{-1,0,1,2,3}.png made with:
    RadialConvert preview <dir>/img0.png 0 png,webp,pdf,gif JPG   (and so on)
scripts/demo-gif.sh does all of it. Needs Pillow and ffmpeg.
"""
import math, os, subprocess, sys, tempfile
from PIL import Image, ImageDraw, ImageFont, ImageFilter

W, H, FPS = 720, 450, 20
F = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FB = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
font = lambda s, b=False: ImageFont.truetype(FB if b else F, s)

menus_dir, out_gif = sys.argv[1], sys.argv[2]
MENU = {}
for k in ("img", "aud", "vid"):
    for s in (-1, 0, 1, 2, 3):
        MENU[k, s] = Image.open(os.path.join(menus_dir, f"{k}{s}.png")).convert("RGBA").resize((240, 240), Image.LANCZOS)

# ---------- static scene ----------
WIN = (40, 26, 680, 372)
SLOTS = [(80, 120), (220, 120), (360, 120), (500, 120), (80, 250), (220, 250)]


def background():
    bg = Image.new("RGB", (W, H))
    px = bg.load()
    for y in range(H):
        for x in range(W):
            t = (x / W) * 0.6 + (y / H) * 0.4
            px[x, y] = (int(18 + 40 * t), int(24 + 20 * t), int(58 + 60 * (1 - t)))
    d = ImageDraw.Draw(bg)
    # window shadow + body
    sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((WIN[0] + 4, WIN[1] + 10, WIN[2] + 4, WIN[3] + 10), 12, fill=(0, 0, 0, 130))
    bg.paste(sh.filter(ImageFilter.GaussianBlur(10)), (0, 0), sh.filter(ImageFilter.GaussianBlur(10)))
    d.rounded_rectangle(WIN, 10, fill=(25, 25, 28), outline=(60, 60, 66))
    d.rounded_rectangle((WIN[0], WIN[1], WIN[2], WIN[1] + 40), 10, fill=(36, 36, 40))
    d.rectangle((WIN[0], WIN[1] + 30, WIN[2], WIN[1] + 40), fill=(36, 36, 40))
    d.text((WIN[0] + 16, WIN[1] + 12), "Downloads", font=font(13), fill=(225, 225, 230))
    for i, c in enumerate([(255, 95, 87), (255, 189, 46), (40, 200, 64)][::-1]):
        d.text((WIN[2] - 30 - i * 26, WIN[1] + 10), "—□×"[2 - i], font=font(14), fill=(170, 170, 176))
    d.rounded_rectangle((WIN[0] + 12, WIN[1] + 50, WIN[2] - 12, WIN[1] + 76), 6, fill=(38, 38, 43))
    d.text((WIN[0] + 24, WIN[1] + 55), "This PC  ›  Nick  ›  Downloads", font=font(12), fill=(170, 170, 178))
    return bg


def icon(kind):
    im = Image.new("RGBA", (72, 72), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    if kind in ("jpg", "png"):
        for y in range(72):
            d.line((4, y, 67, y), fill=(int(250 - y * 1.2), int(150 + y * 0.6), int(90 + y * 1.8)))
        d.ellipse((42, 12, 58, 28), fill=(255, 236, 160))
        d.polygon([(4, 66), (26, 32), (44, 66)], fill=(40, 70, 90))
        d.polygon([(26, 66), (48, 40), (68, 66)], fill=(30, 52, 70))
        m = Image.new("L", (72, 72), 0)
        ImageDraw.Draw(m).rounded_rectangle((4, 8, 67, 66), 8, fill=255)
        im.putalpha(m)
    elif kind in ("wav", "m4a"):
        d.rounded_rectangle((8, 6, 64, 66), 10, fill=(124, 77, 255) if kind == "wav" else (0, 170, 140))
        for i, h in enumerate([10, 22, 34, 18, 28, 12, 24]):
            x = 16 + i * 6.5
            d.rounded_rectangle((x, 36 - h / 2, x + 3.5, 36 + h / 2), 2, fill=(255, 255, 255))
    elif kind in ("mov", "gif"):
        d.rounded_rectangle((8, 6, 64, 66), 10, fill=(230, 60, 70) if kind == "mov" else (245, 160, 0))
        if kind == "mov":
            d.polygon([(28, 22), (28, 50), (50, 36)], fill=(255, 255, 255))
        else:
            d.text((18, 25), "GIF", font=font(16, True), fill=(255, 255, 255))
    return im


def tile(img, slot, name, kind, selected=False, alpha=1.0):
    x, y = slot
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    if selected:
        d.rounded_rectangle((x - 14, y - 10, x + 100, y + 104), 8, fill=(45, 80, 120, 255), outline=(90, 150, 220, 255))
    ic = icon(kind)
    layer.paste(ic, (x + 7, y), ic)
    tw = d.textlength(name, font=font(12))
    d.text((x + 43 - tw / 2, y + 80), name, font=font(12), fill=(230, 230, 235))
    if alpha < 1:
        a = layer.getchannel("A").point(lambda v: int(v * alpha))
        layer.putalpha(a)
    img.alpha_composite(layer)


def cursor(img, p):
    x, y = p
    pts = [(0, 0), (0, 20), (5, 15), (9, 24), (12, 23), (8, 14), (15, 14)]
    d = ImageDraw.Draw(img)
    d.polygon([(x + a, y + b) for a, b in pts], fill=(255, 255, 255), outline=(0, 0, 0))


def chips(img, shift, rmb, caption):
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, H - 58, W, H), 0, fill=(10, 10, 16, 235))
    d.text((24, H - 38), caption, font=font(17, True), fill=(240, 242, 248))
    on, off = (0, 145, 255), (55, 58, 70)
    x = W - 150
    d.rounded_rectangle((x, H - 44, x + 64, H - 14), 7, fill=on if shift else off)
    d.text((x + 13, H - 37), "Shift", font=font(13, True), fill=(255, 255, 255))
    mx = x + 80
    d.rounded_rectangle((mx, H - 48, mx + 30, H - 8), 14, fill=off)
    d.pieslice((mx, H - 48, mx + 30, H - 20), 270, 360, fill=on if rmb else (80, 84, 98))
    d.line((mx + 15, H - 48, mx + 15, H - 32), fill=(20, 20, 26), width=2)


def toast(img, title, sub, t):
    if t <= 0:
        return
    k = min(1, t)
    x0 = W - 300 + int((1 - k) * 60)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.rounded_rectangle((x0, 300, x0 + 280, 368), 10, fill=(32, 33, 40, int(245 * k)), outline=(70, 72, 84, int(255 * k)))
    ic = MENU["img", 0].resize((40, 40), Image.LANCZOS)
    layer.paste(ic, (x0 + 12, 314), ic)
    d.text((x0 + 62, 312), title, font=font(14, True), fill=(255, 255, 255, int(255 * k)))
    d.text((x0 + 62, 336), sub, font=font(12), fill=(190, 194, 206, int(255 * k)))
    img.alpha_composite(layer)


def menu(img, key, sel, centre, scale=1.0):
    m = MENU[key, sel]
    if scale != 1.0:
        s = max(1, int(240 * scale))
        m = m.resize((s, s), Image.LANCZOS)
        a = m.getchannel("A").point(lambda v: int(v * min(1, scale + 0.2)))
        m.putalpha(a)
    img.alpha_composite(m, (int(centre[0] - m.width / 2), int(centre[1] - m.height / 2)))


# ---------- animation ----------
BG = background().convert("RGBA")
files = [("holiday.jpg", "jpg"), ("beat.wav", "wav"), ("clip.mov", "mov")]
made = []                                  # (name, kind, born_frame)
frames = []
state = {"cur": (600, 330), "sel": None, "shift": False, "rmb": False, "menu": None, "toast": None, "cap": ""}
DIRS = [(0, -1), (1, 0), (0, 1), (-1, 0)]


def ease(t):
    return t * t * (3 - 2 * t)


def draw():
    img = BG.copy()
    for i, (n, k) in enumerate(files):
        tile(img, SLOTS[i], n, k, selected=(state["sel"] == i))
    for j, (n, k, born) in enumerate(made):
        tile(img, SLOTS[3 + j], n, k, alpha=min(1, (len(frames) - born) / 6))
    if state["menu"]:
        key, sel, c, sc = state["menu"]
        menu(img, key, sel, c, sc)
    if state["toast"]:
        ti, su, born = state["toast"]
        toast(img, ti, su, (len(frames) - born) / 5)
    cursor(img, state["cur"])
    chips(img, state["shift"], state["rmb"], state["cap"])
    frames.append(img.convert("RGB"))


def hold(n):
    for _ in range(n):
        draw()


def move(to, n):
    a = state["cur"]
    for i in range(1, n + 1):
        t = ease(i / n)
        state["cur"] = (a[0] + (to[0] - a[0]) * t, a[1] + (to[1] - a[1]) * t)
        if state["menu"]:
            key, _, c, sc = state["menu"]
            dx, dy = state["cur"][0] - c[0], state["cur"][1] - c[1]
            s = -1 if dx * dx + dy * dy < 22 * 22 else ((1 if dx > 0 else 3) if abs(dx) > abs(dy) else (0 if dy < 0 else 2))
            state["menu"] = (key, s, c, sc)
        draw()


def scene(i, key, direction, out_name, out_kind, fast=False):
    sx, sy = SLOTS[i]
    state["cap"] = "Select a file"
    move((sx + 46, sy + 40), 8 if fast else 12)
    state["sel"] = i
    hold(4 if fast else 8)
    empty = (440 + i * 30, 238)
    move(empty, 6 if fast else 10)
    state["cap"] = "Hold Shift + right-click"
    state["shift"] = True
    hold(3 if fast else 6)
    state["rmb"] = True
    for sc in (0.55, 0.75, 0.92, 1.0):
        state["menu"] = (key, -1, empty, sc)
        draw()
    hold(3 if fast else 8)
    state["cap"] = "Flick toward a format, let go"
    dx, dy = DIRS[direction]
    move((empty[0] + dx * 80, empty[1] + dy * 80), 6)
    hold(6 if fast else 10)
    state["rmb"] = False
    state["shift"] = False
    state["menu"] = None
    made.append((out_name, out_kind, len(frames)))
    state["toast"] = (f"Converted to {out_name.split('.')[-1].upper()}", out_name, len(frames))
    state["cap"] = "Done, saved next to the original"
    hold(18 if fast else 26)
    state["toast"] = None
    state["sel"] = None


hold(6)
scene(0, "img", 0, "holiday.png", "png")
scene(1, "aud", 3, "beat.m4a", "m4a", fast=True)
scene(2, "vid", 2, "clip.gif", "gif", fast=True)
state["cap"] = "Radial Convert: Shift + right-click, flick, done."
hold(34)

# ---------- encode ----------
with tempfile.TemporaryDirectory() as tmp:
    for n, f in enumerate(frames):
        f.save(os.path.join(tmp, f"f{n:04d}.png"))
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-framerate", str(FPS),
                    "-i", os.path.join(tmp, "f%04d.png"),
                    "-vf", "split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle",
                    "-loop", "0", out_gif], check=True)
print(f"wrote {out_gif}: {len(frames)} frames, {len(frames) / FPS:.1f}s, {os.path.getsize(out_gif) // 1024} KB")
