"""
Builds the retro pixel characters (pixelcat, pixeldog, dove, falcon) from the original CC0 downloads.

Usage (from the repository root):
    python tools/make_pixel_pets.py <folder containing: cat sprite/, dog/, birds.png>

Sources (all CC0):
    Cat and dog by Shepardskin: opengameart.org/content/cat-sprites, opengameart.org/content/dog-sprites
    Birds by MoikMellah:        opengameart.org/content/animated-birds-32x32
The artists' files use a solid background colour; it is made transparent here and the frames are laid out
one animation per row.
"""
import json
import os
import sys

from PIL import Image, ImageSequence

OUT = os.path.join("Assets", "Pets")


def keyed(frame, bg=None):
    """RGBA copy of a frame with its solid background colour made transparent."""
    f = frame.convert("RGBA")
    px = f.load()
    if bg is None:
        bg = px[0, 0]
    for y in range(f.height):
        for x in range(f.width):
            if px[x, y][:3] == bg[:3]:
                px[x, y] = (0, 0, 0, 0)
    return f


def gif_frames(src, path):
    return [f.copy() for f in ImageSequence.Iterator(Image.open(os.path.join(src, path)))]


def bottom_center(img, cw, ch):
    cell = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    cell.paste(img, ((cw - img.width) // 2, ch - img.height), img)
    return cell


def build(pet_id, meta, rows, cw, ch):
    folder = os.path.join(OUT, pet_id)
    os.makedirs(folder, exist_ok=True)
    sheet = Image.new("RGBA", (cw * max(len(f) for _, f in rows.values()), ch * len(rows)), (0, 0, 0, 0))
    animations = {}
    for r, (name, (fps, frames)) in enumerate(rows.items()):
        for i, fr in enumerate(frames):
            sheet.paste(bottom_center(fr, cw, ch), (i * cw, r * ch))
        animations[name] = {"row": r, "frames": len(frames), "fps": fps}
    sheet.save(os.path.join(folder, "sheet.png"))
    meta = dict(meta, facing="left", glow=True, sort=20, frameWidth=cw, frameHeight=ch, sheet="sheet.png", animations=animations)
    with open(os.path.join(folder, "pet.json"), "w") as f:
        json.dump(meta, f, indent=2)
    print(pet_id, sheet.size, {k: v["frames"] for k, v in animations.items()})


def main(src):
    cat_walk = [keyed(f) for f in gif_frames(src, "cat sprite/catwalkx2.gif")]
    cat_run = [keyed(f) for f in gif_frames(src, "cat sprite/catrunx2.gif")]
    build("pixelcat", {"name": "Pixel Cat", "kind": "walker", "scale": 4,
                       "credit": "Cat sprites by Shepardskin (opengameart.org/content/cat-sprites), CC0"},
          {"walk": (6, cat_walk), "run": (10, cat_run), "idle": (1, cat_walk[:1])}, 40, 34)

    dog_walk = [keyed(f) for f in gif_frames(src, "dog/dog_walkx1.gif")]
    dog_look = [keyed(f) for f in gif_frames(src, "dog/dog_stand_lookx1.gif")]
    dog_bark = [keyed(f) for f in gif_frames(src, "dog/dog_stand_barkx1.gif")]
    build("pixeldog", {"name": "Pixel Dog", "kind": "walker", "scale": 9,
                       "credit": "Dog sprites by Shepardskin (opengameart.org/content/dog-sprites), CC0"},
          {"walk": (8, dog_walk), "idle": (2, dog_look), "alert": (4, dog_bark)}, 16, 10)

    sheet = Image.open(os.path.join(src, "birds.png")).convert("RGBA")
    for pet_id, name, row in (("dove", "Pixel Dove", 0), ("falcon", "Pixel Falcon", 1)):
        frames = [keyed(sheet.crop((i * 32, row * 32, i * 32 + 32, row * 32 + 32)), bg=(255, 0, 255)) for i in range(5)]
        build(pet_id, {"name": name, "kind": "flyer", "scale": 3,
                       "credit": "Bird sprites by MoikMellah (opengameart.org/content/animated-birds-32x32), CC0"},
              {"fly": (10, frames), "idle": (10, frames[:1])}, 32, 32)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
