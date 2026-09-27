"""Значки и шкалы HUD поездки (T47): белые фигуры на прозрачном фоне, цвет задаёт Unity по теме.

Запуск: python tools/build_ui_icons.py  → Assets/DrivingSchool/Art/UI/Hud/*.png
Рисуем с 4-кратным запасом и уменьшаем — края сглажены. Руками PNG не править: меняем скрипт.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "DrivingSchool", "Art", "UI", "Hud")
FONT = os.path.join(ROOT, "Assets", "DrivingSchool", "Art", "Fonts", "GolosText", "GolosText-Bold.ttf")
SS = 4
W = (255, 255, 255, 255)


def canvas(size):
    img = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img), size * SS


def save(img, name, size):
    os.makedirs(OUT, exist_ok=True)
    img.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


def erase(img, draw_fn):
    """Вырезать фигуру (альфа = 0): рисуем маску и вычитаем её из альфы."""
    mask = Image.new("L", img.size, 0)
    draw_fn(ImageDraw.Draw(mask))
    a = img.getchannel("A")
    a.paste(0, mask=mask)
    img.putalpha(a)


# ---------- значки 128 px ----------

def turn():
    img, d, s = canvas(128)
    m = s * 0.12
    d.rectangle([m, s * 0.38, s * 0.55, s * 0.62], fill=W)
    d.polygon([(s * 0.45, s * 0.14), (s - m, s * 0.5), (s * 0.45, s * 0.86)], fill=W)
    save(img, "icon_turn", 128)


def beam(high):
    img, d, s = canvas(128)
    # Корпус фары: «D», плоская сторона слева, выпуклая справа.
    x0, x1, y0, y1 = s * 0.46, s * 0.92, s * 0.18, s * 0.82
    d.pieslice([x0 - (x1 - x0), y0, x1, y1], -90, 90, fill=W)
    d.rectangle([x0 - s * 0.02, y0, x0 + s * 0.02, y1], fill=W)
    lw = int(s * 0.07)
    for i in range(5):
        y = s * (0.24 + i * 0.13)
        if high:
            d.line([(s * 0.06, y), (s * 0.38, y)], fill=W, width=lw)
        else:
            d.line([(s * 0.10, y + s * 0.10), (s * 0.38, y)], fill=W, width=lw)
    save(img, "icon_highbeam" if high else "icon_lowbeam", 128)


def handbrake():
    img, d, s = canvas(128)
    lw = int(s * 0.075)
    c, r = s / 2, s * 0.30
    d.ellipse([c - r, c - r, c + r, c + r], outline=W, width=lw)
    R = s * 0.45
    d.arc([c - R, c - R, c + R, c + R], 135, 225, fill=W, width=lw)
    d.arc([c - R, c - R, c + R, c + R], -45, 45, fill=W, width=lw)
    f = ImageFont.truetype(FONT, int(s * 0.36))
    d.text((c, c + s * 0.01), "P", font=f, fill=W, anchor="mm")
    save(img, "icon_handbrake", 128)


def seatbelt():
    img, d, s = canvas(128)
    d.ellipse([s * 0.36, s * 0.06, s * 0.60, s * 0.30], fill=W)                  # голова
    d.rounded_rectangle([s * 0.26, s * 0.34, s * 0.70, s * 0.78], radius=s * 0.12, fill=W)  # туловище
    d.rounded_rectangle([s * 0.26, s * 0.70, s * 0.86, s * 0.90], radius=s * 0.07, fill=W)  # колени (сидит)
    belt = [(s * 0.66, s * 0.34), (s * 0.30, s * 0.74)]
    erase(img, lambda m: m.line(belt, fill=255, width=int(s * 0.16)))
    d = ImageDraw.Draw(img)
    d.line(belt, fill=W, width=int(s * 0.07))
    save(img, "icon_seatbelt", 128)


def battery():
    img, d, s = canvas(128)
    lw = int(s * 0.075)
    d.rounded_rectangle([s * 0.10, s * 0.30, s * 0.90, s * 0.82], radius=s * 0.05, outline=W, width=lw)
    d.rectangle([s * 0.20, s * 0.20, s * 0.34, s * 0.30], fill=W)
    d.rectangle([s * 0.66, s * 0.20, s * 0.80, s * 0.30], fill=W)
    lw2 = int(s * 0.06)
    d.line([(s * 0.20, s * 0.56), (s * 0.38, s * 0.56)], fill=W, width=lw2)
    d.line([(s * 0.29, s * 0.47), (s * 0.29, s * 0.65)], fill=W, width=lw2)
    d.line([(s * 0.62, s * 0.56), (s * 0.80, s * 0.56)], fill=W, width=lw2)
    save(img, "icon_battery", 128)


def warning():
    img, d, s = canvas(128)
    pts = [(s * 0.5, s * 0.13), (s * 0.92, s * 0.86), (s * 0.08, s * 0.86)]
    d.polygon(pts, fill=W)
    d.line(pts + [pts[0]], fill=W, width=int(s * 0.08), joint="curve")
    erase(img, lambda m: (m.rounded_rectangle([s * 0.45, s * 0.34, s * 0.55, s * 0.64], radius=s * 0.04, fill=255),
                          m.ellipse([s * 0.44, s * 0.70, s * 0.56, s * 0.81], fill=255)))
    save(img, "icon_warning", 128)


def instructor():
    img, d, s = canvas(128)
    d.ellipse([s * 0.32, s * 0.12, s * 0.68, s * 0.48], fill=W)
    d.pieslice([s * 0.12, s * 0.54, s * 0.88, s * 1.30], 180, 360, fill=W)
    # козырёк кепки инструктора
    d.chord([s * 0.30, s * 0.08, s * 0.70, s * 0.32], 180, 360, fill=W)
    d.rounded_rectangle([s * 0.28, s * 0.20, s * 0.80, s * 0.25], radius=s * 0.02, fill=W)
    save(img, "icon_instructor", 128)


# ---------- шкалы 512 px ----------
# Дуга шкалы: от −120° до +120° по часовой от верха (зазор 120° внизу под передачу).

def ring(name, inner):
    img, d, s = canvas(512)
    d.ellipse([0, 0, s - 1, s - 1], fill=W)
    r = s * inner / 2
    c = s / 2
    erase(img, lambda m: m.ellipse([c - r, c - r, c + r, c + r], fill=255))
    save(img, name, 512)


def ticks():
    img, d, s = canvas(512)
    c = s / 2
    for kmh in range(0, 201, 10):
        a = math.radians(-120 + 240 * kmh / 200)
        major = kmh % 20 == 0
        r0, r1 = s * (0.335 if major else 0.355), s * 0.39
        w = int(s * (0.012 if major else 0.006))
        p0 = (c + r0 * math.sin(a), c - r0 * math.cos(a))
        p1 = (c + r1 * math.sin(a), c - r1 * math.cos(a))
        d.line([p0, p1], fill=W, width=w)
    save(img, "dial_ticks", 512)


def dial_bg():
    size = 512
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = img.load()
    c = size / 2
    for y in range(size):
        for x in range(size):
            r = math.hypot(x + 0.5 - c, y + 0.5 - c) / c
            if r > 1:
                continue
            a = 0.80 + 0.18 * r * r            # темнее к краю
            edge = max(0.0, min(1.0, (1 - r) * c))   # сглаженный край
            px[x, y] = (255, 255, 255, int(255 * a * edge))
    img.save(os.path.join(OUT, "dial_bg.png"))


def rounded():
    img, d, s = canvas(64)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=s * 0.3, fill=W)
    save(img, "rounded", 64)


def shadow():
    size = 96
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([24, 24, size - 25, size - 25], radius=14, fill=(255, 255, 255, 170))
    img = img.filter(ImageFilter.GaussianBlur(10))
    img.save(os.path.join(OUT, "shadow.png"))


def vignette():
    size = 256
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = img.load()
    c = size / 2
    for y in range(size):
        for x in range(size):
            r = math.hypot(x + 0.5 - c, y + 0.5 - c) / c
            if r > 1:
                continue
            a = max(0.0, (r - 0.55) / 0.45) ** 2
            px[x, y] = (255, 255, 255, int(255 * a))
    img.save(os.path.join(OUT, "vignette.png"))


if __name__ == "__main__":
    turn(); beam(False); beam(True); handbrake(); seatbelt(); battery(); warning(); instructor()
    ring("ring_thick", 0.86); ring("ring_thin", 0.94); ticks(); dial_bg(); rounded(); shadow(); vignette()
    print("UI_ICONS_BUILT", OUT)
