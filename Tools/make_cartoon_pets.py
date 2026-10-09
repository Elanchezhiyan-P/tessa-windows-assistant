"""
Draws the cartoon characters (cat, dog, bird) as smooth sprite sheets.

Run from the repository root:  python tools/make_cartoon_pets.py
Writes Assets/Pets/<id>/sheet.png and pet.json (one animation per row, equal-sized cells).
Everything is drawn at 4x and shrunk, which gives clean anti-aliased edges. All characters face left.
"""
import json
import math
import os

from PIL import Image, ImageChops, ImageDraw

S = 4                 # supersampling factor
W, H = 160, 120       # final cell size
GROUND = 116          # y of the ground line inside a cell (the sprite is bottom-aligned on the taskbar)
OUT = (58, 42, 34, 255)   # outline colour


class Canvas:
    def __init__(self, w=W, h=H):
        self.w, self.h = w, h
        self.im = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.im)

    @staticmethod
    def _p(pts):
        return [(x * S, y * S) for x, y in pts]

    def ellipse(self, cx, cy, rx, ry, fill, outline=2.5):
        box = lambda e: [(cx - rx - e) * S, (cy - ry - e) * S, (cx + rx + e) * S, (cy + ry + e) * S]
        if outline:
            self.d.ellipse(box(outline), fill=OUT)
        self.d.ellipse(box(0), fill=fill)

    def poly(self, pts, fill, outline=2.5):
        if outline:  # a wide round-jointed line centred on the edge gives an even outline
            self.d.line(self._p(pts + [pts[0], pts[1]]), fill=OUT, width=int(2 * outline * S), joint="curve")
            for x, y in pts:
                r = outline * S
                self.d.ellipse([x * S - r, y * S - r, x * S + r, y * S + r], fill=OUT)
        self.d.polygon(self._p(pts), fill=fill)

    def stroke(self, pts, width, fill, outline=2.5):
        """A thick line with round ends and joints (legs, tails)."""
        for w, color in ((width + 2 * outline, OUT), (width, fill)):
            self.d.line(self._p(pts), fill=color, width=int(w * S), joint="curve")
            for x, y in pts:
                r = w / 2 * S
                self.d.ellipse([x * S - r, y * S - r, x * S + r, y * S + r], fill=color)

    def line(self, pts, width, fill):
        self.d.line(self._p(pts), fill=fill, width=int(width * S), joint="curve")

    def clipped(self, mask_draw, draw):
        """Draw with `draw(canvas)` but only where `mask_draw(draw_on_L_image)` painted (e.g. stripes inside a body)."""
        mask = Image.new("L", self.im.size, 0)
        mask_draw(ImageDraw.Draw(mask))
        layer = Canvas(self.w, self.h)
        draw(layer)
        layer.im.putalpha(ImageChops.multiply(layer.im.split()[3], mask))
        self.im.alpha_composite(layer.im)

    def finish(self):
        return self.im.resize((self.w, self.h), Image.LANCZOS)


def ell_mask(cx, cy, rx, ry):
    return lambda d: d.ellipse([(cx - rx) * S, (cy - ry) * S, (cx + rx) * S, (cy + ry) * S], fill=255)


def eye(c, x, y, look=-1.5, blink=False, r=5.5):
    if blink:
        c.line([(x - r, y), (x + r, y + 0.5)], 2, OUT)
        return
    c.ellipse(x, y, r, r + 1.2, (255, 255, 255, 255), outline=1.8)
    c.ellipse(x + look, y + 0.6, r * 0.55, r * 0.85, (30, 24, 24, 255), outline=0)
    c.ellipse(x + look - 1.2, y - 1.6, 1.4, 1.4, (255, 255, 255, 255), outline=0)


def rotate(pts, origin, degrees):
    a = math.radians(degrees)
    ox, oy = origin
    return [(ox + (x - ox) * math.cos(a) - (y - oy) * math.sin(a),
             oy + (x - ox) * math.sin(a) + (y - oy) * math.cos(a)) for x, y in pts]


# ------------------------------------------------------------------------------------------------
# Cat
# ------------------------------------------------------------------------------------------------
def cat(phase, walking=True, blink=False, meow=False, tail=0.0):
    c = Canvas()
    fur, dark, belly, pink = (244, 162, 89, 255), (205, 120, 46, 255), (255, 228, 190, 255), (255, 150, 160, 255)
    bob = 2.2 * abs(math.sin(phase)) if walking else 0
    swing = 30 * math.sin(phase) if walking else 0
    hip = GROUND - 24 - bob

    def leg(x, theta, color):
        t = math.radians(theta)
        foot = (x + 24 * math.sin(t), min(GROUND, hip + 24 * math.cos(t)))
        c.stroke([(x, hip), foot], 9, color)
        c.ellipse(foot[0] - 1.5, foot[1] - 3, 5.5, 4, belly, outline=2)

    # far legs (darker), then near legs, drawn first so the body covers the hips
    leg(64, -swing, dark); leg(110, swing, dark)
    leg(74, swing, fur); leg(120, -swing, fur)

    # tail
    sway = 5 * math.sin(phase * 1.0 + tail)
    c.stroke([(124, 76 - bob), (140, 68 - bob), (150 + sway, 50), (146 + sway * 1.4, 32)], 8, fur)
    c.stroke([(150 + sway, 50), (146 + sway * 1.4, 32)], 8, dark)

    # body with stripes and belly
    c.ellipse(90, 78 - bob, 38, 21, fur)
    body_mask = ell_mask(90, 78 - bob, 38, 21)

    def marks(k):
        for x in (74, 88, 102, 116):
            k.line([(x, 54 - bob), (x + 4, 70 - bob)], 5, dark)
        k.d.ellipse([60 * S, (90 - bob) * S, 120 * S, (104 - bob) * S], fill=belly)

    c.clipped(body_mask, marks)

    # head
    hy = 62 - bob * 0.6
    c.poly([(29, hy - 14), (27, hy - 40), (47, hy - 24)], fur)
    c.poly([(51, hy - 24), (64, hy - 40), (66, hy - 12)], fur)
    c.poly([(32, hy - 18), (32, hy - 32), (42, hy - 24)], pink, outline=0)
    c.poly([(54, hy - 24), (61, hy - 33), (62, hy - 16)], pink, outline=0)
    c.ellipse(46, hy, 21, 19, fur)
    c.clipped(ell_mask(46, hy, 21, 19), lambda k: [k.line([(x, hy - 19), (x, hy - 10)], 3, dark) for x in (40, 46, 52)])
    eye(c, 38, hy - 3, blink=blink)
    eye(c, 54, hy - 3, blink=blink)
    c.poly([(23, hy + 2), (32, hy + 2), (27.5, hy + 8)], pink, outline=1.8)           # nose
    if meow:
        c.ellipse(27.5, hy + 14, 5, 5.5, (200, 60, 70, 255), outline=1.8)
    else:
        c.line([(27.5, hy + 8), (27.5, hy + 11), (22, hy + 13)], 1.6, OUT)
        c.line([(27.5, hy + 11), (33, hy + 13)], 1.6, OUT)
    for dy, dx in ((-3, -17), (3, -17), (9, -14)):                                      # whiskers
        c.line([(24, hy + 6 + dy / 2), (24 + dx, hy + 6 + dy)], 1.3, OUT)
    return c.finish()


# ------------------------------------------------------------------------------------------------
# Dog
# ------------------------------------------------------------------------------------------------
def dog(phase, walking=True, wag=0.0, tongue=False, bark=False):
    c = Canvas()
    fur, dark, snout = (205, 150, 92, 255), (140, 90, 44, 255), (244, 214, 170, 255)
    bob = 2.0 * abs(math.sin(phase)) if walking else 0
    swing = 28 * math.sin(phase) if walking else 0
    hip = GROUND - 25 - bob

    def leg(x, theta, color):
        t = math.radians(theta)
        foot = (x + 25 * math.sin(t), min(GROUND, hip + 25 * math.cos(t)))
        c.stroke([(x, hip), foot], 10, color)
        c.ellipse(foot[0] - 1.5, foot[1] - 3, 6, 4, snout, outline=2)

    leg(68, -swing, dark); leg(114, swing, dark)
    leg(78, swing, fur); leg(124, -swing, fur)

    # tail (wags)
    tx = 6 * math.sin(wag)
    c.stroke([(132, 72 - bob), (142, 60 - bob), (146 + tx, 44)], 9, fur)

    # body + saddle patch
    c.ellipse(96, 80 - bob, 40, 20, fur)
    c.clipped(ell_mask(96, 80 - bob, 40, 20), lambda k: k.ellipse(104, 66 - bob, 18, 11, dark, outline=0))

    # collar and tag
    c.poly([(60, 66 - bob), (70, 62 - bob), (74, 88 - bob), (64, 92 - bob)], (220, 70, 70, 255), outline=2)
    c.ellipse(68, 96 - bob, 4.5, 4.5, (255, 205, 70, 255), outline=1.8)

    # head, snout, ear
    hy = (56 if bark else 62) - bob * 0.6
    c.ellipse(46, hy, 21, 19, fur)
    c.ellipse(27, hy + 7, 15, 10.5, snout)
    c.ellipse(15, hy + 3, 6, 4.5, (30, 24, 24, 255), outline=1.5)                      # nose
    c.ellipse(14, hy + 2, 1.6, 1.2, (255, 255, 255, 255), outline=0)
    eye(c, 43, hy - 5, look=-2)
    c.line([(36, hy - 13), (50, hy - 11)], 2.4, OUT)                                  # eyebrow
    if bark or tongue:
        c.line([(16, hy + 14), (38, hy + 15)], 1.8, OUT)
        c.ellipse(28, hy + 20, 5.5, 7.5 if bark else 6, (255, 130, 150, 255), outline=1.8)
    else:
        c.line([(17, hy + 13), (36, hy + 15)], 1.8, OUT)
    c.poly([(52, hy - 14), (70, hy - 16), (74, hy + 12), (60, hy + 16)], dark)        # floppy ear
    return c.finish()


# ------------------------------------------------------------------------------------------------
# Bird
# ------------------------------------------------------------------------------------------------
def bird(phase):
    c = Canvas()
    blue, deep, light, white = (88, 168, 255, 255), (52, 124, 214, 255), (128, 192, 255, 255), (244, 250, 255, 255)
    beak, bob = (255, 186, 66, 255), 14 + 2.0 * math.sin(phase)   # sits low in the cell so raised wings fit
    flap = -18 - 48 * math.cos(phase)           # wing angle: about -66 (up) .. +30 (down)
    wing = [(x * 0.8, y * 0.8) for x, y in [(0, 0), (14, -14), (50, -26), (72, -10), (52, 6), (16, 10)]]

    # far wing behind the body
    c.poly(rotate([(x + 78, y + 56 + bob) for x, y in wing], (78, 56 + bob), flap + 12), deep)
    # tail fan
    c.poly([(108, 68 + bob), (142, 58 + bob), (148, 70 + bob), (142, 84 + bob), (108, 78 + bob)], deep)
    c.line([(114, 70 + bob), (140, 66 + bob)], 1.6, OUT)
    c.line([(114, 74 + bob), (140, 78 + bob)], 1.6, OUT)
    # feet tucked up
    for x in (80, 94):
        c.stroke([(x, 90 + bob), (x - 2, 100 + bob)], 3.2, beak, outline=1.6)
    # body, belly, head
    c.ellipse(84, 68 + bob, 31, 24, blue)
    c.clipped(ell_mask(84, 68 + bob, 31, 24), lambda k: k.ellipse(72, 80 + bob, 22, 17, white, outline=0))
    c.ellipse(54, 48 + bob, 18, 17, blue)
    c.poly([(54, 34 + bob), (60, 18 + bob), (66, 34 + bob)], deep, outline=2)          # crest
    c.poly([(40, 44 + bob), (20, 51 + bob), (40, 58 + bob)], beak, outline=2)          # beak
    c.line([(24, 51 + bob), (38, 51 + bob)], 1.2, OUT)
    eye(c, 49, 44 + bob, look=-1.8, r=5.5)
    c.ellipse(55, 56 + bob, 4, 3, (255, 170, 180, 255), outline=0)                     # cheek
    # near wing in front
    c.poly(rotate([(x + 86, y + 56 + bob) for x, y in wing], (86, 56 + bob), flap), light)
    return c.finish()


# ------------------------------------------------------------------------------------------------
def write(pet_id, name, kind, rows, scale, sort=10, credit="Drawn for WinCompanion (cartoon style)"):
    """rows: {animation: (fps, [frames])}"""
    folder = os.path.join("Assets", "Pets", pet_id)
    os.makedirs(folder, exist_ok=True)
    columns = max(len(frames) for _, frames in rows.values())
    W, H = next(iter(rows.values()))[1][0].size
    sheet = Image.new("RGBA", (columns * W, len(rows) * H), (0, 0, 0, 0))
    animations = {}
    for row, (anim, (fps, frames)) in enumerate(rows.items()):
        for i, frame in enumerate(frames):
            sheet.paste(frame, (i * W, row * H))
        animations[anim] = {"row": row, "frames": len(frames), "fps": fps}
    sheet.save(os.path.join(folder, "sheet.png"))
    meta = {"name": name, "kind": kind, "facing": "left", "frameWidth": W, "frameHeight": H, "scale": scale,
            "smooth": True, "sort": sort, "sheet": "sheet.png",
            "credit": credit, "animations": animations}
    with open(os.path.join(folder, "pet.json"), "w") as f:
        json.dump(meta, f, indent=2)
    print(pet_id, sheet.size, {k: v["frames"] for k, v in animations.items()})


def main():
    n = 8
    ph = [2 * math.pi * i / n for i in range(n)]
    write("cat", "Cartoon Cat", "walker", {
        "walk": (12, [cat(p) for p in ph]),
        "idle": (3, [cat(0, False, tail=t) for t in (0, 1.2, 2.4, 3.6)[:3]] + [cat(0, False, blink=True)]),
        "alert": (3, [cat(0, False, meow=True, tail=0.5), cat(0, False, tail=1.5)]),
    }, 0.85)
    write("dog", "Cartoon Dog", "walker", {
        "walk": (12, [dog(p, wag=p * 2) for p in ph]),
        "idle": (6, [dog(0, False, wag=w, tongue=True) for w in (0, 1.6, 3.2, 4.8)]),
        "alert": (5, [dog(0, False, wag=0.5, bark=True), dog(0, False, wag=2.5)]),
    }, 0.85)
    flap = [bird(p) for p in ph]
    write("bird", "Cartoon Bird", "flyer", {"fly": (14, flap), "idle": (14, flap[:1])}, 0.85)


if __name__ == "__main__":
    main()
