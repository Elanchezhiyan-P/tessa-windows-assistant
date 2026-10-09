# Credits and third-party notices

## Code and runtime

- **.NET 8, WPF, Windows Forms** (Microsoft, MIT): included in the installer so you don't need to install .NET.
- **System.Speech** (Microsoft, MIT): speech recognition and voice output.

## Characters

## Real (cat, dog, heron, sitting cat, puppy, seagull)
Cut out from real stock video with `tools/video_to_pet.py` (background removal by rembg, motion held in place, best-looping
stretch of the clip chosen). The footage is licensed under the [Pexels free licence](https://www.pexels.com/license/): free to use
and modify, no attribution required (given here anyway). Don't resell the clips themselves.

| Character | Footage |
|---|---|
| Real Cat (ginger, walks) | "Cat Walking Around" by Roman Odintsov, https://www.pexels.com/video/cat-walking-around-6543588/ |
| Real Dog (shaggy dog, walks) | "Lonely dog walking on rural road in sunny day", https://www.pexels.com/video/lonely-dog-walking-on-rural-road-in-sunny-day-36040950/ |
| Real Heron (flaps) | "Great Blue Heron Taking Off in Slow Motion", https://www.pexels.com/video/great-blue-heron-taking-off-in-slow-motion-39008201/ |
| Real Cat (sitting; rises from the bottom of the screen) | "An Orange and White Cat Wearing a Collar" by Line Riedel, https://www.pexels.com/video/an-orange-and-white-cat-wearing-a-collar-13279436/ |
| Real Puppy (running) | "Side View of a Black Dog Running by the Sea", https://www.pexels.com/video/side-view-of-a-black-dog-running-by-the-sea-11967567/ |
| Real Seagull (glides) | "Close Up Footage Of The Bird Flying" by Michael Scott, https://www.pexels.com/video/close-up-footage-of-the-bird-flying-3826855/ |

The commands used (from the repository root, with rembg installed, see the top of the script):

```
python tools/video_to_pet.py cat.mp4      --id realcat    --name "Real Cat"           --kind walker --facing right --round-top --min-loop 1.0 --max-loop 2.0 --frames 24
python tools/video_to_pet.py dog.mp4      --id realdog    --name "Real Dog"           --kind walker --facing right --start 0.3 --end 6.2 --step 2 --max-height 900 --min-loop 0.8 --max-loop 1.8 --frames 20
python tools/video_to_pet.py heron.mp4    --id realheron  --name "Real Heron"         --kind flyer  --facing left  --start 6.0 --end 10.5 --step 2 --max-height 1000 --min-loop 0.5 --max-loop 1.6 --frames 20 --display-height 150
python tools/video_to_pet.py sitting.mp4  --id realcatsit --name "Real Cat (sitting)" --kind sitter --facing right --start 0 --end 11 --step 2 --min-loop 2.5 --max-loop 7.0 --frames 36 --display-height 150
python tools/video_to_pet.py puppy.mp4    --id realpuppy  --name "Real Puppy"         --kind walker --facing left  --start 0 --end 3 --step 1 --open 7 --min-loop 0.3 --max-loop 0.8
python tools/video_to_pet.py seagull.mp4  --id realbird   --name "Real Seagull"       --kind flyer  --facing right --start 6 --end 10.5 --open 3 --min-loop 0.5 --max-loop 1.5 --display-height 105
```
(`speed`, `playback` and `sort` in each pet.json were then tuned by hand so the feet don't slide.)

A `sitter` character stays in one place: it rises into view from the bottom of the screen, loops its idle animation, then sinks away.

## Cartoon (Cartoon Cat, Cartoon Dog, Cartoon Bird)
Drawn for WinCompanion by `tools/make_cartoon_pets.py`. Edit that script and re-run it to change colours or motion.

## Pixel packs (Pixel Cat, Pixel Dog, Pixel Dove, Pixel Falcon)
Released under **CC0 (public domain)** by their artists. Credit is not required but is appreciated.
Built from the artists' original GIFs by `tools/make_pixel_pets.py` (background colour made transparent, frames laid out one animation per row).

| Character | Artist | Source |
|---|---|---|
| Pixel Cat | Shepardskin | https://opengameart.org/content/cat-sprites |
| Pixel Dog | Shepardskin | https://opengameart.org/content/dog-sprites |
| Pixel Dove, Pixel Falcon | MoikMellah | https://opengameart.org/content/animated-birds-32x32 |

## Adding your own

Run `tools/video_to_pet.py` on a clip of your own, or create a folder in `%AppData%\WinCompanion\pets\<name>\` containing
`sheet.png` (one animation per row, equal-sized cells, transparent background) and `pet.json`. Copy any folder here as a starting point:

- `kind`: `walker` (paces along the taskbar) or `flyer` (glides across the top of the screen)
- `facing`: which way the art faces (`left` or `right`); it is mirrored when moving the other way
- `frameWidth`, `frameHeight`, `scale`: cell size in the sheet and how big to show it
- `smooth`: `true` for photos and smooth art, otherwise hard pixel edges; `glow`: light outline for dark art
- `speed`: travel speed in DIPs/second (match it to the stride); `playback`: animation speed multiplier; `sort`: picker order
- `animations`: `walk` or `fly`, `idle`, and optionally `run` and `alert`, each with `row`, `frames`, `fps`

A folder with the same name as a built-in character replaces it.

## Original superheroes (comet, spark, fox)
Captain Comet, Mecha Spark and Midnight Fox were drawn for WinCompanion by the project (`tools/make_hero_pets.py`),
released under the project's MIT licence. They are original designs and are not based on any existing character.

## Used to build the characters (not distributed)
 the characters (not distributed)

- **rembg** (MIT) with the **U²-Net** model (Apache-2.0) for background removal.
- **Pillow** (HPND), **NumPy** (BSD), **SciPy** (BSD), **OpenCV** (Apache-2.0).

## Installer

Built with **Inno Setup** (Jordan Russell, free licence).
