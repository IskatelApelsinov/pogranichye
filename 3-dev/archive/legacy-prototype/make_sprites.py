"""
Генератор изометрических СПРАЙТОВ-ЗАГЛУШЕК (SVG) под 2.5D-вид сверху.
Это временные «кубики и ромбики», чтобы сцена ожила уже сейчас; потом художник
(или ИИ) заменит их на финальные арты, сохранив имена файлов.
    python3 make_sprites.py   →  пишет assets/*.svg
"""
import os

OUT = os.path.join(os.path.dirname(__file__), "assets")
TILE_W, TILE_H = 64, 32          # изо-ромб 2:1


def _svg(w, h, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
            f'viewBox="0 0 {w} {h}">{body}</svg>')


def tile(top, side):
    """Ромб-тайл земли (изометрия)."""
    pts = f"{TILE_W/2},0 {TILE_W},{TILE_H/2} {TILE_W/2},{TILE_H} 0,{TILE_H/2}"
    body = (f'<polygon points="{pts}" fill="{top}" stroke="{side}" stroke-width="1"/>'
            f'<polygon points="{pts}" fill="none" stroke="rgba(255,255,255,.12)"/>')
    return _svg(TILE_W, TILE_H, body)


def cube(front, right, top, roof=None, mark=""):
    """Изо-кубик строения на тайле (ромбовый верх + две грани)."""
    w, h = 64, 72
    bx, by = 32, 20         # центр верхней грани
    hw, hh = 32, 16         # половинки ромба верха
    wall = 26               # высота стен
    top_pts = f"{bx},{by-hh} {bx+hw},{by} {bx},{by+hh} {bx-hw},{by}"
    left_pts = f"{bx-hw},{by} {bx},{by+hh} {bx},{by+hh+wall} {bx-hw},{by+wall}"
    right_pts = f"{bx},{by+hh} {bx+hw},{by} {bx+hw},{by+wall} {bx},{by+hh+wall}"
    body = (f'<polygon points="{left_pts}" fill="{front}"/>'
            f'<polygon points="{right_pts}" fill="{right}"/>'
            f'<polygon points="{top_pts}" fill="{top}"/>')
    if roof:
        body += f'<polygon points="{bx},{by-hh-14} {bx+hw},{by} {bx},{by+hh} {bx-hw},{by}" fill="{roof}" opacity="0.95"/>'
    if mark:
        body += f'<text x="{bx}" y="{by+6}" font-size="14" text-anchor="middle">{mark}</text>'
    return _svg(w, h, body)


def pawn(color, ring="#1b1b21", hat=None, mark=""):
    """Токен-персонаж: простой «3D» столбик с головой; hat — цвет шапки, mark — значок ремесла."""
    w, h = 28, 40
    body = (f'<ellipse cx="14" cy="36" rx="10" ry="4" fill="rgba(0,0,0,.35)"/>'
            f'<rect x="7" y="14" width="14" height="18" rx="6" fill="{color}" stroke="{ring}"/>'
            f'<circle cx="14" cy="11" r="7" fill="{color}" stroke="{ring}"/>')
    if hat:
        body += f'<path d="M7 9 Q14 2 21 9 Z" fill="{hat}" stroke="{ring}" stroke-width="0.7"/>'
    if mark:
        body += f'<text x="14" y="26" font-size="9" text-anchor="middle">{mark}</text>'
    return _svg(w, h, body)


def tree(trunk="#6e5234", crown="#3f7d3a", snow=False):
    """Пропс-дерево (крона-эллипсоид на стволе)."""
    w, h = 44, 56
    body = (f'<ellipse cx="22" cy="52" rx="12" ry="4" fill="rgba(0,0,0,.3)"/>'
            f'<rect x="19" y="30" width="6" height="18" fill="{trunk}"/>'
            f'<ellipse cx="22" cy="24" rx="17" ry="20" fill="{crown}"/>'
            f'<ellipse cx="22" cy="18" rx="11" ry="12" fill="rgba(255,255,255,.08)"/>')
    if snow:
        body += '<ellipse cx="22" cy="16" rx="13" ry="7" fill="rgba(255,255,255,.7)"/>'
    return _svg(w, h, body)


def pine(snow=False):
    """Ёлка-конус (для зимнего/лесного колорита)."""
    w, h = 40, 58
    c = "#2f6b39"
    body = (f'<ellipse cx="20" cy="54" rx="11" ry="4" fill="rgba(0,0,0,.3)"/>'
            f'<rect x="17" y="42" width="6" height="12" fill="#5e4630"/>'
            f'<polygon points="20,6 33,28 7,28" fill="{c}"/>'
            f'<polygon points="20,18 36,44 4,44" fill="{c}"/>')
    if snow:
        body += ('<polygon points="20,6 27,18 13,18" fill="rgba(255,255,255,.8)"/>'
                 '<polygon points="20,18 29,30 11,30" fill="rgba(255,255,255,.6)"/>')
    return _svg(w, h, body)


def rock():
    w, h = 40, 34
    body = ('<ellipse cx="20" cy="30" rx="14" ry="4" fill="rgba(0,0,0,.3)"/>'
            '<polygon points="8,28 14,12 24,10 33,22 30,28" fill="#8d8f96"/>'
            '<polygon points="14,12 24,10 22,20 15,22" fill="#a7a9b0"/>')
    return _svg(w, h, body)


def bush(berries=False):
    w, h = 36, 30
    body = ('<ellipse cx="18" cy="27" rx="12" ry="3" fill="rgba(0,0,0,.25)"/>'
            '<ellipse cx="12" cy="18" rx="9" ry="9" fill="#3c6e36"/>'
            '<ellipse cx="24" cy="18" rx="9" ry="9" fill="#468141"/>'
            '<ellipse cx="18" cy="14" rx="9" ry="9" fill="#4f8f49"/>')
    if berries:
        body += ('<circle cx="13" cy="17" r="2" fill="#c0392b"/>'
                 '<circle cx="22" cy="20" r="2" fill="#c0392b"/>'
                 '<circle cx="19" cy="13" r="2" fill="#c0392b"/>')
    return _svg(w, h, body)


def well():
    w, h = 48, 54
    body = ('<ellipse cx="24" cy="50" rx="15" ry="4" fill="rgba(0,0,0,.3)"/>'
            '<polygon points="24,30 39,38 24,46 9,38" fill="#6e5234"/>'
            '<polygon points="9,38 24,46 24,52 9,46" fill="#55402a"/>'
            '<polygon points="39,38 24,46 24,52 39,46" fill="#4a3824"/>'
            '<ellipse cx="24" cy="34" rx="10" ry="5" fill="#2a3a4a"/>'
            '<rect x="11" y="8" width="4" height="26" fill="#5e4630"/>'
            '<rect x="33" y="8" width="4" height="26" fill="#5e4630"/>'
            '<polygon points="7,10 41,10 24,0" fill="#8a3a2a"/>')
    return _svg(w, h, body)


def icon():
    """Иконка приложения (PWA) — простой герб приграничья."""
    body = ('<rect width="128" height="128" rx="24" fill="#191920"/>'
            '<polygon points="64,20 104,44 104,92 64,116 24,92 24,44" '
            'fill="#2a2a34" stroke="#d8b968" stroke-width="3"/>'
            '<text x="64" y="86" font-size="56" text-anchor="middle">🏰</text>')
    return _svg(128, 128, body)


SPRITES = {
    "tile_grass": tile("#6a9a4a", "#527a38"),
    "tile_snow":  tile("#dce6f0", "#b7c6dc"),
    "tile_dirt":  tile("#8a6a44", "#6e5234"),
    "tile_water": tile("#3a6a8a", "#2a5070"),
    "tile_field": tile("#b89a4a", "#8a7030"),
    "house":      cube("#7a5a3a", "#5e4630", "#9a7048", roof="#8a3a2a"),
    "tavern":     cube("#6a4a7a", "#503860", "#8a68a0", roof="#c0392b", mark="🍺"),
    "forge":      cube("#4a4a52", "#36363c", "#5e5e68", roof="#2a2a30", mark="🔥"),
    "barn":       cube("#8a6a3a", "#6e5230", "#a78a48", roof="#6a4a2a", mark="🌾"),
    "tree":       tree(),
    "tree_snow":  tree(snow=True),
    "pine":       pine(),
    "pine_snow":  pine(snow=True),
    "rock":       rock(),
    "bush":       bush(),
    "bush_berry": bush(berries=True),
    "well":       well(),
    "player":     pawn("#e1b12c"),                       # ты — золотой
    "villager":   pawn("#9bb0c8"),                       # житель — серо-голубой
    "woodcutter": pawn("#8a6a3a", hat="#5e4630", mark="🪓"),
    "smith":      pawn("#6a6a72", hat="#36363c", mark="🔨"),
    "cook":       pawn("#c86a4a", hat="#f0e0c0", mark="🍲"),
    "fisher":     pawn("#4a8aa0", hat="#2a5a70", mark="🐟"),
    "hunter":     pawn("#5a7a4a", hat="#3a5230", mark="🏹"),
    "herbalist":  pawn("#6a9a5a", hat="#4a7038", mark="🌿"),
    "tavernkeeper": pawn("#9a6a8a", hat="#6a4060", mark="🍺"),
    "icon":       icon(),
}


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, svg in SPRITES.items():
        with open(os.path.join(OUT, name + ".svg"), "w", encoding="utf-8") as f:
            f.write(svg)
    print(f"записано {len(SPRITES)} спрайтов в {OUT}")


if __name__ == "__main__":
    main()
