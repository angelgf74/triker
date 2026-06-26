# -*- coding: utf-8 -*-
"""Genera los recursos graficos de la ficha de Google Play para Triker:
   - icon-512.png            (512x512, icono de alta resolucion)
   - feature-graphic.png     (1024x500, grafico de funcion)
Coherente con la identidad de la app (fondo #1A1A2E, acento #F0C040,
cartas con los colores reales del juego)."""

import os
import math
from PIL import Image, ImageDraw, ImageFont, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "graphics")
os.makedirs(OUT, exist_ok=True)

FONT_BD = "C:/Windows/Fonts/arialbd.ttf"

BG_DARK   = (26, 26, 46)      # #1A1A2E
BG_DARK2  = (38, 38, 70)      # un tono mas claro para degradado
ACCENT    = (240, 192, 64)    # #F0C040
WHITE     = (255, 255, 255)

# Colores reales de las cartas (Triker.Web/css/app.css)
RED     = (229, 57, 53)
GREEN   = (67, 160, 71)
BLUE    = (30, 136, 229)
BLACK   = (33, 33, 33)
MAGENTA = (156, 39, 176)
CYAN    = (0, 172, 193)

SS = 3  # supersampling


def font(px):
    return ImageFont.truetype(FONT_BD, int(px * SS))


def vertical_gradient(w, h, top, bottom):
    base = Image.new("RGB", (w, h), top)
    top = [c for c in top]
    bottom = [c for c in bottom]
    px = base.load()
    for y in range(h):
        t = y / max(1, h - 1)
        r = int(top[0] + (bottom[0] - top[0]) * t)
        g = int(top[1] + (bottom[1] - top[1]) * t)
        b = int(top[2] + (bottom[2] - top[2]) * t)
        for x in range(w):
            px[x, y] = (r, g, b)
    return base


def draw_figure(d, box, kind, color):
    """Dibuja una figura de naipe dentro de la caja (x0,y0,x1,y1)."""
    x0, y0, x1, y1 = box
    w = x1 - x0
    h = y1 - y0
    cx = (x0 + x1) / 2
    cy = (y0 + y1) / 2
    if kind == "circle":
        d.ellipse(box, fill=color)
    elif kind == "square":
        d.rounded_rectangle(box, radius=w * 0.12, fill=color)
    elif kind == "triangle":
        d.polygon([(cx, y0), (x1, y1), (x0, y1)], fill=color)
    elif kind == "diamond":
        d.polygon([(cx, y0), (x1, cy), (cx, y1), (x0, cy)], fill=color)
    elif kind == "star":
        pts = []
        for i in range(10):
            ang = -math.pi / 2 + i * math.pi / 5
            r = (w / 2) if i % 2 == 0 else (w / 2) * 0.45
            pts.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
        d.polygon(pts, fill=color)
    elif kind == "heart":
        r = w / 4
        d.ellipse([x0, y0, x0 + 2 * r, y0 + 2 * r], fill=color)
        d.ellipse([x1 - 2 * r, y0, x1, y0 + 2 * r], fill=color)
        d.polygon([(x0, y0 + r), (x1, y0 + r), (cx, y1)], fill=color)
    elif kind == "club":
        r = w / 4
        d.ellipse([cx - r, y0, cx + r, y0 + 2 * r], fill=color)
        d.ellipse([x0, cy - r * 0.5, x0 + 2 * r, cy + r * 1.5], fill=color)
        d.ellipse([x1 - 2 * r, cy - r * 0.5, x1, cy + r * 1.5], fill=color)
        d.rectangle([cx - r * 0.35, cy, cx + r * 0.35, y1], fill=color)


def make_card(w, h, letter, color, figure):
    """Crea una carta blanca (RGBA) con letra grande y figuras de naipe."""
    w, h = int(w * SS), int(h * SS)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    radius = int(w * 0.14)
    # cuerpo
    d.rounded_rectangle([0, 0, w - 1, h - 1], radius=radius, fill=(255, 255, 255, 255))
    # borde tenue del color
    d.rounded_rectangle([0, 0, w - 1, h - 1], radius=radius,
                        outline=color + (255,), width=max(2, int(w * 0.02)))
    # letra central
    f = ImageFont.truetype(FONT_BD, int(h * 0.5))
    bbox = d.textbbox((0, 0), letter, font=f)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    d.text(((w - tw) / 2 - bbox[0], (h - th) / 2 - bbox[1]), letter, font=f, fill=color)
    # figuras en esquinas (naipe)
    fs = int(w * 0.16)
    m = int(w * 0.10)
    draw_figure(d, (m, m, m + fs, m + fs), figure, color)
    # esquina inferior derecha (rotada visualmente con misma figura)
    draw_figure(d, (w - m - fs, h - m - fs, w - m, h - m), figure, color)
    return img


def shadow_for(card, blur=14, alpha=140, off=(0, 8)):
    w, h = card.size
    pad = blur * SS * 2
    sh = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    solid = Image.new("RGBA", card.size, (0, 0, 0, alpha))
    sh.paste(solid, (pad + off[0] * SS, pad + off[1] * SS), card)
    sh = sh.filter(ImageFilter.GaussianBlur(blur * SS))
    return sh, pad


def paste_rotated(base, card, cx, cy, angle, with_shadow=True):
    """Pega 'card' centrada en (cx,cy) en coords ya escaladas, con rotacion y sombra."""
    if with_shadow:
        sh, pad = shadow_for(card)
        sh = sh.rotate(angle, expand=True, resample=Image.BICUBIC)
        base.alpha_composite(sh, (int(cx - sh.width / 2), int(cy - sh.height / 2)))
    rot = card.rotate(angle, expand=True, resample=Image.BICUBIC)
    base.alpha_composite(rot, (int(cx - rot.width / 2), int(cy - rot.height / 2)))


# ----------------------------------------------------------------------------
# ICONO 512x512
# ----------------------------------------------------------------------------
def build_icon():
    W = H = 512
    base = vertical_gradient(W * SS, H * SS, BG_DARK2, BG_DARK).convert("RGBA")

    cw, ch = 150, 210
    # cartas laterales
    left = make_card(cw, ch, "R", BLUE, "square")
    right = make_card(cw, ch, "K", MAGENTA, "star")
    paste_rotated(base, left, (W / 2 - 78) * SS, (H / 2 + 6) * SS, 15)
    paste_rotated(base, right, (W / 2 + 78) * SS, (H / 2 + 6) * SS, -15)

    # carta central amarilla (firma de marca)
    center = Image.new("RGBA", (int(cw * 1.12 * SS), int(ch * 1.12 * SS)), (0, 0, 0, 0))
    d = ImageDraw.Draw(center)
    cwS, chS = center.size
    rad = int(cwS * 0.14)
    d.rounded_rectangle([0, 0, cwS - 1, chS - 1], radius=rad, fill=ACCENT + (255,))
    f = ImageFont.truetype(FONT_BD, int(chS * 0.56))
    bbox = d.textbbox((0, 0), "T", font=f)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    d.text(((cwS - tw) / 2 - bbox[0], (chS - th) / 2 - bbox[1]), "T", font=f, fill=BG_DARK)
    fs = int(cwS * 0.15); m = int(cwS * 0.09)
    draw_figure(d, (m, m, m + fs, m + fs), "circle", BG_DARK)
    draw_figure(d, (cwS - m - fs, chS - m - fs, cwS - m, chS - m), "circle", BG_DARK)
    paste_rotated(base, center, (W / 2) * SS, (H / 2 - 4) * SS, 0)

    icon = base.convert("RGB").resize((W, H), Image.LANCZOS)
    icon.save(os.path.join(OUT, "icon-512.png"))
    print("icon-512.png OK")


# ----------------------------------------------------------------------------
# FEATURE GRAPHIC 1024x500
# ----------------------------------------------------------------------------
def build_feature():
    W, H = 1024, 500
    base = vertical_gradient(W * SS, H * SS, BG_DARK2, BG_DARK).convert("RGBA")
    d = ImageDraw.Draw(base)

    # cartas TRIKER en arco
    spec = [("T", RED, "circle"), ("R", BLUE, "square"), ("I", GREEN, "triangle"),
            ("K", MAGENTA, "star"), ("E", CYAN, "heart"), ("R", BLACK, "club")]
    cw, ch = 118, 165
    n = len(spec)
    gap = 128
    total = (n - 1) * gap
    x0 = W / 2 - total / 2
    cy = H / 2 - 28
    for i, (ltr, col, fig) in enumerate(spec):
        card = make_card(cw, ch, ltr, col, fig)
        cx = x0 + i * gap
        angle = (i - (n - 1) / 2) * -5  # leve arco
        dy = abs(i - (n - 1) / 2) * 7
        paste_rotated(base, card, cx * SS, (cy + dy) * SS, angle)

    # tagline
    f1 = font(40)
    txt = "Combina  LETRAS · FIGURAS · COLORES"
    bbox = d.textbbox((0, 0), txt, font=f1)
    tw = bbox[2] - bbox[0]
    ty = (H - 70) * SS
    d.text(((W * SS - tw) / 2 - bbox[0], ty), txt, font=f1, fill=ACCENT)

    feat = base.convert("RGB").resize((W, H), Image.LANCZOS)
    feat.save(os.path.join(OUT, "feature-graphic.png"))
    print("feature-graphic.png OK")


if __name__ == "__main__":
    build_icon()
    build_feature()
