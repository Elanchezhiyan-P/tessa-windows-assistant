"""
Draws three ORIGINAL superhero characters for WinCompanion (chibi style, facing left).

Run from the repository root:  python tools/make_hero_pets.py
  comet  - Captain Comet: teal suit, orange cape, gold comet emblem
  spark  - Mecha Spark:   chunky blue/silver robot suit with a glowing cyan visor and chest dial
  fox    - Midnight Fox:  indigo suit, fox-ear hood, silver mask, bushy orange tail

These are our own designs and are not based on any existing character.
"""
import math
import sys
import os

sys.path.insert(0, os.path.dirname(__file__))
import make_cartoon_pets as m
from make_cartoon_pets import Canvas, OUT, rotate, eye, write

HW, HH = 150, 150
G = 146  # ground line
SKIN = (255, 214, 176, 255)
WHITE = (255, 255, 255, 255)


def C(r, g, b):
    return (r, g, b, 255)


STYLES = {
    "comet": dict(name="Captain Comet", suit=C(30, 170, 175), dark=C(18, 110, 120), accent=C(255, 196, 40),
                  cape=C(240, 110, 40), cape2=C(200, 80, 30), boots=C(200, 80, 30), head="hero"),
    "spark": dict(name="Mecha Spark", suit=C(70, 120, 215), dark=C(40, 80, 160), accent=C(90, 235, 255),
                  cape=None, cape2=None, boots=C(170, 180, 195), head="mech"),
    "fox": dict(name="Midnight Fox", suit=C(60, 55, 130), dark=C(38, 34, 90), accent=C(240, 140, 50),
                cape=None, cape2=None, boots=C(38, 34, 90), head="fox"),
}


def hero(key, phase=0.0, walking=True, blink=False, wave=0.0, sway=0.0):
    s = STYLES[key]
    c = Canvas(HW, HH)
    bob = abs(math.sin(phase)) * -3 if walking else math.sin(sway) * 1.2
    swing = math.sin(phase) * 24 if walking else 0
    hipx, hipy = 52, 100 + bob
    sx, sy = 52, 76 + bob           # shoulder
    fl = math.sin(sway * 2) * 2       # cape flutter

    # tail (fox) behind everything
    if key == "fox":
        t = math.sin(phase + 1) * 6 if walking else math.sin(sway) * 5
        c.stroke([(hipx + 8, hipy - 4), (hipx + 28, hipy - 10 + t), (hipx + 40, hipy - 34 + t), (hipx + 36, hipy - 54 + t * 1.3)],
                 15, s["accent"])
        c.ellipse(hipx + 36, hipy - 55 + t * 1.3, 8, 9, WHITE)

    # cape streams behind (trailing to the right since the hero faces left)
    if s["cape"]:
        lift = (14 if walking else 4) + fl
        pts = [(sx + 2, sy - 2), (sx + 12, sy + 2), (sx + 40 + lift, sy + 28 - lift * 0.6), (sx + 46 + lift, sy + 52 - lift * 0.3),
               (sx + 18, hipy + 14), (hipx - 4, hipy + 6)]
        c.poly(pts, s["cape"])
        c.poly([(sx + 12, sy + 4), (sx + 38 + lift, sy + 30 - lift * 0.6), (sx + 22, hipy + 4)], s["cape2"], outline=0)

    # back leg + arm
    def leg(theta, shade):
        top = (hipx, hipy)
        foot = rotate([(hipx, hipy + 30)], top, theta)[0]
        c.stroke([top, foot], 11, shade)
        c.ellipse(foot[0] - 3, min(foot[1] + 1, G - 4), 9, 5.5, s["boots"])
    leg(-swing, s["dark"])
    c.stroke([(sx + 2, sy + 4), rotate([(sx + 2, sy + 26)], (sx + 2, sy + 4), swing * 0.8)[0]], 9, s["dark"])

    # torso
    c.poly([(sx - 15, sy - 4), (sx + 15, sy - 4), (sx + 12, hipy + 6), (sx - 12, hipy + 6)], s["suit"])
    c.ellipse(hipx, hipy + 4, 14, 6, s["dark"], outline=2)  # belt
    # emblem
    if key == "comet":
        c.ellipse(sx - 1, sy + 12, 7, 7, s["accent"], outline=1.5)
        c.poly([(sx - 6, sy + 12), (sx - 22, sy + 8), (sx - 8, sy + 16)], s["accent"], outline=0)
    elif key == "spark":
        glow = 0.5 + 0.5 * math.sin(sway * 3 + phase)
        c.ellipse(sx - 1, sy + 12, 8, 8, C(30, 50, 90), outline=1.5)
        c.ellipse(sx - 1, sy + 12, 5, 5, (int(60 + 100 * glow), 235, 255, 255), outline=0)
        c.poly([(sx - 15, sy - 4), (sx - 6, sy - 4), (sx - 6, sy + 28), (sx - 12, sy + 28)], s["dark"], outline=0)
    else:
        c.poly([(sx - 8, sy + 4), (sx + 8, sy + 4), (sx, sy + 20)], s["accent"], outline=1.5)

    # front leg and arm
    leg(swing, s["suit"])
    arm_top = (sx - 8, sy + 2)
    ang = -swing * 0.8 if not wave else -140 + math.sin(wave) * 18
    hand = rotate([(sx - 8, sy + 26)], arm_top, ang)[0]
    c.stroke([arm_top, hand], 10, s["suit"])
    c.ellipse(hand[0], hand[1], 6.5, 6.5, s["accent"] if key == "spark" else SKIN, outline=2)

    # head
    hx, hy = 45, 48 + bob
    if key == "fox":
        # ears behind hood
        for ex, flip in ((hx - 4, 0), (hx + 18, 1)):
            c.poly([(ex - 9, hy - 14), (ex + 2 + flip * 4, hy - 42), (ex + 12, hy - 14)], s["suit"])
            c.poly([(ex - 4, hy - 16), (ex + 2 + flip * 4, hy - 34), (ex + 8, hy - 16)], s["accent"], outline=0)
    if key == "spark":
        c.ellipse(hx + 4, hy, 25, 24, C(190, 198, 212), outline=2.5)                 # helmet
        c.ellipse(hx - 6, hy + 2, 17, 13, C(30, 40, 70), outline=2)                  # visor
        glow = 0.6 + 0.4 * math.sin(sway * 3)
        vis = (int(70 + 60 * glow), 235, 255, 255)
        if blink:
            c.line([(hx - 20, hy + 2), (hx + 4, hy + 2)], 3, vis)
        else:
            c.ellipse(hx - 12, hy + 2, 5, 6, vis, outline=0)
            c.ellipse(hx + 1, hy + 2, 5, 6, vis, outline=0)
        c.stroke([(hx + 6, hy - 22), (hx + 6, hy - 34)], 3, C(190, 198, 212), outline=1.5)   # antenna
        c.ellipse(hx + 6, hy - 36, 4, 4, s["accent"], outline=1.5)
    else:
        c.ellipse(hx + 4, hy, 24, 23, SKIN)
        if key == "comet":
            c.poly([(hx - 20, hy - 6), (hx - 14, hy - 24), (hx + 4, hy - 30), (hx + 22, hy - 22), (hx + 30, hy - 6),
                    (hx + 18, hy - 12), (hx + 2, hy - 18), (hx - 12, hy - 8)], C(70, 45, 30), outline=2)
            c.poly([(hx - 18, hy - 4), (hx + 2, hy - 1), (hx + 2, hy + 9), (hx - 18, hy + 8)], s["suit"], outline=1.8)  # eye mask
        else:
            c.ellipse(hx + 4, hy - 4, 25, 22, s["suit"])                              # hood
            c.ellipse(hx - 2, hy + 6, 17, 15, SKIN, outline=2)
            c.poly([(hx - 20, hy - 2), (hx + 8, hy - 2), (hx + 6, hy + 10), (hx - 18, hy + 9)], C(190, 195, 215), outline=1.8)  # mask
        ex = hx - 8
        if blink:
            c.line([(ex - 5, hy + 3), (ex + 5, hy + 3)], 2, OUT)
        else:
            c.ellipse(ex, hy + 3, 5, 6, WHITE, outline=1.5)
            c.ellipse(ex - 1.5, hy + 4, 2.8, 4, (30, 24, 24, 255), outline=0)
        c.line([(hx - 14, hy + 15), (hx - 7, hy + 17)], 2, (200, 90, 80, 255))        # mouth
        c.ellipse(hx - 17, hy + 12, 3, 2, (255, 160, 150, 255), outline=0)             # cheek
    return c.finish()


def main():
    n = 8
    ph = [2 * math.pi * i / n for i in range(n)]
    for key in STYLES:
        st = STYLES[key]
        write(key, st["name"], "walker", {
            "walk": (12, [hero(key, p, sway=p) for p in ph]),
            "idle": (4, [hero(key, 0, False, sway=t) for t in (0, 1.2, 2.4, 3.6)[:3]] + [hero(key, 0, False, blink=True, sway=0)]),
            "alert": (6, [hero(key, 0, False, wave=w, sway=w) for w in (0, 1.6, 3.2, 4.8)]),
        }, 0.8, sort=11, credit="Original character drawn for WinCompanion (not based on any existing character)")


if __name__ == "__main__":
    main()
