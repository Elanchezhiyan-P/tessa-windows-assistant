"""
Turns a short video of a real animal into a WinCompanion character pack.

    python tools/video_to_pet.py VIDEO --id realcat --name "Real Cat" --kind walker --facing right [options]

What it does:
  1. Reads frames from the video (optionally a time range) and cuts the animal out with rembg (AI background removal).
  2. Cleans each cut-out: keeps only the main animal, removes thin lines (a dog's lead), trims colour fringes.
  3. Holds the animal still in the frame (removes the walking/panning drift) so it walks "on the spot".
  4. Finds the stretch of the clip that loops best, so the walk cycle repeats without a visible jump.
  5. Writes Assets/Pets/<id>/sheet.png and pet.json.

Needs: pip install "rembg[cpu]" pillow numpy opencv-python-headless   (the first run downloads a ~170 MB model)
Only use footage you have the right to use. The built-in packs use clips under the Pexels free licence.
"""
import argparse
import json
import os
import sys

import cv2
import numpy as np
from PIL import Image
from scipy import ndimage as ndi


def read_frames(path, start, end, step, max_height):
    cap = cv2.VideoCapture(path)
    if not cap.isOpened():
        sys.exit(f"Can't open {path}")
    fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    first = int(start * fps)
    last = min(total, int(end * fps)) if end else total
    cap.set(cv2.CAP_PROP_POS_FRAMES, first)
    frames = []
    for index in range(first, last):
        ok, frame = cap.read()
        if not ok:
            break
        if (index - first) % step:
            continue
        if frame.shape[0] > max_height:
            scale = max_height / frame.shape[0]
            frame = cv2.resize(frame, (round(frame.shape[1] * scale), max_height), interpolation=cv2.INTER_AREA)
        frames.append(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
    return frames, fps / step


def cut_out(rgb, session):
    from rembg import remove
    return np.array(remove(Image.fromarray(rgb), session=session))  # RGBA


def clean(rgb, rgba, open_size):
    """Keep the main animal only, drop thin lines, soften the edge and replace fringe colours."""
    alpha = rgba[..., 3]
    mask = (alpha > 128).astype(np.uint8)
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (open_size, open_size))
    opened = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)       # thin things (a lead, whiskers of sky) vanish
    count, labels, stats, _ = cv2.connectedComponentsWithStats(opened)
    if count <= 1:
        return None
    main = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))         # the largest blob is the animal
    keep = cv2.dilate((labels == main).astype(np.uint8), kernel)   # grow back what the opening shaved off the edge
    alpha = np.where(keep > 0, alpha, 0).astype(np.uint8)

    alpha = cv2.erode(alpha, np.ones((3, 3), np.uint8))            # pull the edge in off the old background
    alpha = cv2.GaussianBlur(alpha, (0, 0), 0.9)

    # Edge pixels still carry some of the old background; paint them with the nearest solid animal colour instead.
    solid = alpha > 235
    nearest = ndi.distance_transform_edt(~solid, return_distances=False, return_indices=True)
    colour = np.where((alpha <= 235)[..., None], rgb[nearest[0], nearest[1]], rgb)
    return np.dstack([colour, alpha])


def bbox_and_centroid(rgba):
    alpha = rgba[..., 3]
    ys, xs = np.where(alpha > 20)
    weights = alpha[ys, xs].astype(np.float64)
    return (xs.min(), xs.max() + 1, ys.min(), ys.max() + 1,
            np.average(xs, weights=weights), np.average(ys, weights=weights))


def local_linear(values, half):
    """For each index, the value of a straight line fitted to the neighbours within +-half (handles clip ends too)."""
    out = np.empty(len(values))
    for i in range(len(values)):
        lo, hi = max(0, i - half), min(len(values), i + half + 1)
        t = np.arange(lo, hi)
        slope, intercept = np.polyfit(t, values[lo:hi], 1) if hi - lo >= 2 else (0.0, values[i])
        out[i] = slope * i + intercept
    return out


def round_cut_top(rgba, min_width=12, max_width=140):
    """
    If a narrow part (a tail) is cut flat by the top of the video frame, give it a rounded end.
    The missing tip can't be recovered, so the cap borrows the colours of the last row of the tail.
    """
    solid = rgba[..., 3] > 128
    cols = np.where(solid[0])[0]
    if len(cols) == 0:
        return rgba
    # the widest unbroken run of solid pixels on the top row
    runs = np.split(cols, np.where(np.diff(cols) > 1)[0] + 1)
    run = max(runs, key=len)
    width = len(run)
    if not min_width <= width <= max_width:
        return rgba

    radius = width // 2
    centre = (run[0] + run[-1]) / 2
    top_colour = rgba[:3, run[0]:run[-1] + 1, :3].mean(axis=0)       # average of the first rows, per column
    padded = np.zeros((rgba.shape[0] + radius + 2, rgba.shape[1], 4), np.uint8)
    padded[radius + 2:] = rgba
    for d in range(1, radius + 1):                                    # d = rows above the cut
        half = np.sqrt(max(radius * radius - d * d, 0))
        x0, x1 = int(round(centre - half)), int(round(centre + half))
        for x in range(max(x0, run[0]), min(x1, run[-1]) + 1):
            padded[radius + 2 - d, x, :3] = top_colour[x - run[0]]
            padded[radius + 2 - d, x, 3] = 255
    padded[..., 3] = cv2.GaussianBlur(padded[..., 3], (0, 0), 0.8)
    return padded


def touches_border(rgba, margin=2, limit=80):
    """True if a real part of the animal (not just a tail tip) is sliced off by the edge of the video frame."""
    solid = rgba[..., 3] > 128
    edge = solid[:margin].sum() + solid[-margin:].sum() + solid[:, :margin].sum() + solid[:, -margin:].sum()
    return edge > limit


def align(frames, fps, flyer):
    """Crop every frame to a window that follows the animal's smoothed position, so it stays in place."""
    info = [bbox_and_centroid(f) for f in frames]
    x0, x1, y0, y1, cx, cy = (np.array(v, dtype=np.float64) for v in zip(*info))

    # Follow the animal's steady travel with a straight line fitted over about one stride around each frame. The gait
    # itself (legs, bobbing) stays in the picture; only the drift is removed. A moving average would lag behind a runner.
    half = max(2, int(fps * 0.45))
    xref = local_linear(cx, half)
    yref = local_linear(cy, half)

    left, right = np.min(x0 - xref), np.max(x1 - xref)
    top = np.min(y0 - yref) if flyer else np.min(y0 - yref)
    bottom = np.max(y1 - yref)

    crops = []
    for i, frame in enumerate(frames):
        l, r = int(round(xref[i] + left)) - 2, int(round(xref[i] + right)) + 2
        t, b = int(round(yref[i] + top)) - 2, int(round(yref[i] + bottom)) + 2
        crops.append(crop(frame, l, t, r, b))
    return crops


def crop(rgba, l, t, r, b):
    h, w = rgba.shape[:2]
    out = np.zeros((b - t, r - l, 4), np.uint8)
    sl, st, sr, sb = max(l, 0), max(t, 0), min(r, w), min(b, h)
    if sr > sl and sb > st:
        out[st - t:sb - t, sl - l:sr - l] = rgba[st:sb, sl:sr]
    return out


def thumbnail(rgba, height=72):
    image = Image.fromarray(rgba)
    width = max(1, round(image.width * height / image.height))
    small = np.array(image.resize((width, height), Image.BILINEAR), dtype=np.float32) / 255.0
    small[..., :3] *= small[..., 3:4]   # premultiply so invisible pixels don't count
    return small


def best_loop(crops, fps, min_seconds, max_seconds, bad):
    """The start and length whose last frame flows back into its first with the least visible jump."""
    thumbs = [thumbnail(c) for c in crops]
    width = min(t.shape[1] for t in thumbs)
    thumbs = [t[:, :width] for t in thumbs]
    n = len(thumbs)
    diff = lambda a, b: float(np.abs(thumbs[a] - thumbs[b]).mean())

    best = None
    for length in range(max(2, round(min_seconds * fps)), min(n - 2, round(max_seconds * fps)) + 1):
        for start in range(1, n - length - 1):
            if any(bad[start - 1:start + length + 2]):
                continue  # never loop over frames where the animal is cut off by the video edge
            # Compare the join, and the frames around it, so the motion (not just the pose) matches.
            score = (diff(start, start + length)
                     + 0.5 * (diff(start - 1, start + length - 1) + diff(start + 1, start + length + 1)))
            if best is None or score < best[0]:
                best = (score, start, length)
    if best is None:
        sys.exit("No usable loop: the clip is too short, or the animal is cut off by the video edge in every stretch.\n"
                 "Try a different --start/--end, or lower --min-loop.")
    print(f"best loop: start frame {best[1]}, {best[2]} frames ({best[2] / fps:.2f}s), score {best[0]:.4f}")
    return best[1], best[2]


def build_sheet(frames, idle, cell_height):
    height = cell_height
    width = max(1, round(frames[0].shape[1] * height / frames[0].shape[0]))
    resize = lambda a: Image.fromarray(a).resize((width, height), Image.LANCZOS)
    sheet = Image.new("RGBA", (width * len(frames), height * (1 if idle is None else 2)), (0, 0, 0, 0))
    for i, frame in enumerate(frames):
        sheet.paste(resize(frame), (i * width, 0))
    if idle is not None:
        sheet.paste(resize(idle), (0, height))
    return sheet, width, height


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("video")
    ap.add_argument("--id", required=True, help="folder name under Assets/Pets")
    ap.add_argument("--name", required=True)
    ap.add_argument("--kind", choices=["walker", "flyer", "sitter"], required=True,
                    help="sitter: stays put and loops its idle animation (it rises into view from the bottom of the screen)")
    ap.add_argument("--facing", choices=["left", "right"], required=True, help="the way the animal faces in the video")
    ap.add_argument("--start", type=float, default=0)
    ap.add_argument("--end", type=float, default=0, help="seconds; 0 = to the end")
    ap.add_argument("--step", type=int, default=2, help="use every Nth frame (speed)")
    ap.add_argument("--max-height", type=int, default=720, help="working height of the video frames")
    ap.add_argument("--cell-height", type=int, default=220, help="height of each sprite frame in the sheet")
    ap.add_argument("--frames", type=int, default=16, help="most frames in the loop")
    ap.add_argument("--min-loop", type=float, default=0.4)
    ap.add_argument("--max-loop", type=float, default=1.3)
    ap.add_argument("--round-top", action="store_true", help="round off a tail the video frame cuts flat at the top")
    ap.add_argument("--open", type=int, default=5, help="remove features thinner than this many pixels")
    ap.add_argument("--display-height", type=float, default=130, help="how tall to show it on screen (DIPs)")
    ap.add_argument("--model", default="u2net", help="rembg model (u2net, isnet-general-use, ...)")
    ap.add_argument("--credit", default="")
    ap.add_argument("--cache", help="folder to keep cut-outs so re-runs are fast")
    ap.add_argument("--preview", help="write a contact sheet PNG here to inspect the result")
    a = ap.parse_args()

    from rembg import new_session
    session = new_session(a.model)

    frames, fps = read_frames(a.video, a.start, a.end, a.step, a.max_height)
    print(f"{len(frames)} frames at {fps:.1f} fps")

    cutouts = []
    for i, rgb in enumerate(frames):
        # The key names the clip and settings too, so a cache can never hand back frames from a different video.
        key = f"{a.id}_{os.path.splitext(os.path.basename(a.video))[0]}_s{a.start}_x{a.step}_h{a.max_height}_o{a.open}"
        cached = os.path.join(a.cache, f"{key}_{i:04d}.png") if a.cache else None
        if cached and os.path.exists(cached):
            cutouts.append(np.array(Image.open(cached).convert("RGBA")))
            continue
        result = clean(rgb, cut_out(rgb, session), a.open)
        if result is None:
            sys.exit(f"Frame {i}: nothing left after cleaning; try a smaller --open or a different --model")
        if cached:
            os.makedirs(a.cache, exist_ok=True)
            Image.fromarray(result).save(cached)
        cutouts.append(result)
        print(f"  cut out {i + 1}/{len(frames)}", end="\r")
    print()

    if a.round_top:
        cutouts = [round_cut_top(c) for c in cutouts]
    bad = [touches_border(c) for c in cutouts]
    print(f"{sum(bad)} of {len(bad)} frames have the animal cut off by the video edge")
    crops = align(cutouts, fps, a.kind == "flyer")
    start, length = best_loop(crops, fps, a.min_loop, a.max_loop, bad)
    # Re-crop using only the loop's frames, so the window hugs the animal instead of covering the whole clip's travel.
    loop = align(cutouts[start:start + length], fps, a.kind == "flyer")

    count = min(a.frames, length)
    picks = [loop[int(i * length / count)] for i in range(count)]
    seconds = length / fps
    out_fps = round(count / seconds, 2)

    # "Standing" frame for waiting: the narrowest pose (legs together / wings tucked).
    idle = None if a.kind == "sitter" else min(picks, key=lambda f: bbox_and_centroid(f)[1] - bbox_and_centroid(f)[0])

    sheet, cell_w, cell_h = build_sheet(picks, idle, a.cell_height)
    folder = os.path.join("Assets", "Pets", a.id)
    os.makedirs(folder, exist_ok=True)
    sheet.save(os.path.join(folder, "sheet.png"), optimize=True)

    move = {"walker": "walk", "flyer": "fly", "sitter": "idle"}[a.kind]
    meta = {"name": a.name, "kind": a.kind, "facing": a.facing, "frameWidth": cell_w, "frameHeight": cell_h,
            "scale": round(a.display_height / cell_h, 3), "smooth": True, "glow": False, "sort": 0, "sheet": "sheet.png",
            "credit": a.credit,
            "animations": {move: {"row": 0, "frames": count, "fps": out_fps}}}
    if idle is not None:
        meta["animations"]["idle"] = {"row": 1, "frames": 1, "fps": 1}
    with open(os.path.join(folder, "pet.json"), "w") as f:
        json.dump(meta, f, indent=2)
    print(f"wrote {folder}: {count} frames of {cell_w}x{cell_h}, {out_fps} fps, sheet {sheet.size}")

    if a.preview:
        tiles = [Image.fromarray(p).resize((cell_w // 2, cell_h // 2), Image.LANCZOS) for p in picks]
        cols = min(8, len(tiles))
        rows = -(-len(tiles) // cols)
        board = Image.new("RGBA", (cols * tiles[0].width, rows * tiles[0].height), (120, 130, 150, 255))
        for i, tile in enumerate(tiles):
            board.alpha_composite(tile, ((i % cols) * tile.width, (i // cols) * tile.height))
        board.convert("RGB").save(a.preview)


if __name__ == "__main__":
    main()
