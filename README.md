# Tessa (WinCompanion)

Tessa is a desktop AI companion for Windows. Press a hotkey (or say "Hey Tessa"), type or talk, and she can answer questions
about your PC, set reminders, open and arrange windows, take screenshots, rewrite what you copied, and run PowerShell for
you. She works with **Google Gemini** (free API key) or a **local model** such as Qwen running in Ollama or LM Studio, so
you can keep everything on your own machine.

> **Version 1.0.0, the first release.** Windows 10 and 11, 64-bit. It has been tested thoroughly on the author's PC (Windows 11) and
> has had little testing on other setups, so please report problems: see [CONTRIBUTING.md](CONTRIBUTING.md).

## Features

### Ask about your PC, or have her do things
Ask in plain language: *"how much free space is on each drive?"*, *"which program is using the most memory?"*, *"what's my IP address?"*.
Tessa writes a PowerShell command, runs it, and explains the real result. Read-only commands run on their own; anything that changes
something shows an approval card (**Allow / Deny**) first. For tasks no built-in tool covers (installing an app with winget, checking a
service, network queries) she falls back to PowerShell, so there is very little she can't attempt.

### Reminders that work without AI
*"Remind me in 10 minutes to stretch"*, *"at 5pm call mom"*, *"tomorrow at 9"*, *"on Friday at 14:30"*. Reminders are understood by Tessa
herself, so they work offline, survive restarts, and cost no API calls. When one is due, an animated character walks or flies onto your
screen with the message and **Done** and **Snooze** buttons (it stays out of the way during full-screen apps). You can also ask
*"what reminders do I have?"* or *"cancel the stretch reminder"*.

### Recurring reminders and timers
*"Remind me every day at 9am to stretch"*, *"every weekday at 8:30am for standup"*, *"every Monday at 10"*, *"every 2 hours to drink water"*.
Repeating reminders stay in your list (shown as "repeats every day") until you cancel them. *"Set a timer for 25 minutes"*, *"10 minute timer for
pasta"* works the same way, offline, with the character announcing it when time is up.

### Web lookup
For current or unknown facts (*"who won the match last night?"*, *"what's the capital of Australia?"*) she can search the web and answer from
the top results. Only the search words are sent, to DuckDuckGo; she treats what comes back as information, never as instructions.

### Animated reminder characters
Pick who delivers your reminders: real-footage animals (cat, dog, heron, seagull), cartoon animals, pixel-art animals, or three original
superheroes (**Captain Comet**, **Mecha Spark**, **Midnight Fox**). They turn to face the way they walk, and you can add your own.

### Open apps
Say *"open Notepad"*, *"open Spotify"* or *"open the Downloads folder"*. She opens programs, Store apps, files, folders, web addresses and Settings pages.

### Windows and media control
Snap windows left or right, minimise, maximise, focus or close them; change the volume, play or pause media, switch dark mode, adjust
brightness, put the PC to sleep or restart it (power actions always ask first).

### Screenshots
*"Take a screenshot and save it to my Desktop"* saves an image to a folder you choose. *"What's on my screen?"* lets the AI look at it,
but only after you approve sending it.

### Clipboard helpers
Summarise, translate, fix the grammar of, shorten, or make more polite whatever you just copied.

### Files and folders
Find, read, move and rename files, create folders, tidy up Downloads, and send things to the Recycle Bin. Nothing is ever deleted permanently.

### Notes and memory
Say *"note: buy milk"* to add to a Markdown notes file. Tell her a lasting fact (*"my project folder is D:\Work"*) and she remembers it for
next time.

### Voice
Talk to her with push-to-talk or the hands-free wake phrase, and she can read replies aloud with a Windows voice.

### Modern, compact window
A translucent overlay that follows your Windows theme, with persisted chat history, markdown rendering, a tray icon with a quick menu,
and an **Action log** of everything she ran and when.

## Install

Run `WinCompanion-Setup-1.0.0.exe`. It installs for your user only (no admin rights needed) and includes everything it
needs, so you don't have to install .NET. Windows SmartScreen may warn that the publisher is unknown, because the installer
isn't signed with a paid certificate yet; choose **More info, Run anyway** if you trust the source.

## First run

The first time Tessa starts she asks a few quick things: your name, a short name she should call you, what *her* name is (it's also her wake word, so
the default is "Hey Tessa" but you can rename her to anything), and whether you want a female or male voice. You can skip it and change all of this later in
*Settings, About you and her*.

1. Press **Ctrl+Alt+Space** (or click the tray icon) to open the window.
2. Open **Settings** (the gear) and choose a model:
   - **Gemini (cloud):** paste a free key from <https://aistudio.google.com/apikey>. It is stored encrypted for your Windows account.
   - **Local (Qwen):** install [Ollama](https://ollama.com), run `ollama pull qwen2.5:7b` (or any model that supports tool calling), then choose
     *Local*, press **Ollama** and **Detect**, and **Save**. Nothing leaves your PC.
3. Ask something: *"what's the free space on my drives?"*, *"remind me in 10 minutes to stretch"*, *"what's on my screen?"*.

## Options

| Where | Option | What it does |
|---|---|---|
| Settings | **Model** | Gemini (free key) or any OpenAI-compatible local server (Ollama, LM Studio, llama.cpp), with a separate optional vision model |
| Settings | **About you and her** | Your name, what she calls you, her name (and wake word), and a female or male voice |
| Header + Settings | **Mute voice** | The speaker icon in the window header (or the switch in Settings) silences her spoken replies and reminders |
| Settings | **About** | Version, author, website, email, GitHub and a "Report a problem" link |
| Settings | **Gemini API key** | Paste a new key and Save to replace it, or **Remove saved key** to delete it |
| Settings | **Privacy** | Ask before sending a screenshot, clipboard text or file to Gemini (on by default) |
| Settings | **Reminder character** | Choose a character, or *None*; **Preview** shows it |
| Settings | **PowerShell commands** | *Ask unless read-only* (default) or *Run freely* |
| Settings | **Allow administrator commands** | Off by default; see below |
| Tray menu | **Start with Windows** | Launch Tessa when you sign in |
| Tray menu | **Wake word "Hey Tessa"** | Hands-free listening (uses the microphone while on) |
| Tray menu | **Action log / Data folder** | See what she ran, or open her data folder |

| Shortcut | Does |
|---|---|
| Ctrl+Alt+Space | Show or hide the window |
| Ctrl+Alt+V | Talk (push-to-talk) |
| "Hey Tessa" | Talk, hands-free |

### Administrator commands (optional)
Tessa runs PowerShell with your normal permissions. If you want her to do things that need administrator rights, turn on
*Settings, PowerShell commands, Allow administrator commands*. She then asks Windows for elevation (the UAC prompt) each time,
and every such command also needs your approval in the chat first. Leave it off and those commands simply fail with "access denied".

## Safety

- **Approval for anything risky.** PowerShell that isn't read-only, deleting, moving, power actions and force-quitting all ask first.
  *Run freely* lets safe-looking commands run without asking, but delete, overwrite, install, download-and-run, registry and shutdown
  commands still ask. A pattern list can't catch everything, so keep the default if unsure.
- **Everything she runs is logged:** tray menu, *Action log*.
- **She runs as you**, never as administrator, unless you turn on the option above.

## Privacy

See [PRIVACY.md](PRIVACY.md). In short: with Gemini, your messages and tool results go to Google. With a screenshot, clipboard text or file, you
are asked first (you can turn that off). With a local model nothing leaves your PC. There is no telemetry.

## Characters

Pick one in *Settings, Reminder character* (try **Preview**). Add your own by running `tools/video_to_pet.py` on a clip of an
animal, or by dropping a folder into `%AppData%\WinCompanion\pets\`. Credits and the format are in [Assets/Pets/CREDITS.md](Assets/Pets/CREDITS.md).

## Build from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
dotnet run                       # run it
build\build-release.ps1          # publish a self-contained build and make the installer (needs Inno Setup 6)
```

`tools/` holds the scripts that generate the character packs (Python 3 with Pillow; `video_to_pet.py` also needs `rembg`).

## Troubleshooting

- **Speech isn't converted to the right words:** turn on *Online speech recognition* in Windows Settings, Privacy & security, Speech, and set your
  speech language under Time & language, Speech. She uses the same recogniser as Windows voice typing, which copes with accents much better
  than the basic one (used only if that setting is off). Push-to-talk (Ctrl+Alt+V) is more reliable than the wake phrase.
- **Nothing happens on Ctrl+Alt+Space:** another program may own the shortcut; use the tray icon. A message appears when this happens.
- **"Can't reach the local model server":** start Ollama / LM Studio, then press **Detect** in Settings.
- **Local model is slow or ignores tools:** use a model that supports tool calling and has enough memory; 3B models work but are limited.
- **Something broke:** errors are written to `%AppData%\WinCompanion\crash.log`; the tray menu opens the data folder.
- **Reset everything:** exit the app and delete `%AppData%\WinCompanion`.

## Contributing

This is an open-source project and contributions are welcome: bug reports, fixes, new tools, new characters, translations.
See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build it, run the tests and send a pull request.

## Licence

[MIT](LICENSE), copyright Elanchezhiyan P, for the code. Characters and other assets have their own licences: see [Assets/Pets/CREDITS.md](Assets/Pets/CREDITS.md).

## Author

Made by **Elanchezhiyan P**: [codebyelan.in](https://codebyelan.in) · [elanche97@gmail.com](mailto:elanche97@gmail.com)
