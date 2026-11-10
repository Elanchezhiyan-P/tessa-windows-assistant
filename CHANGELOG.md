# Changelog

## 2.2.0

### New
- **More AI choices.** Besides Gemini and a local model she now works with **Claude** (through Anthropic's official library; `claude-opus-5-5` by default, with chips for the cheaper Sonnet and Haiku) and with **any OpenAI-compatible service** such as OpenAI, OpenRouter, Groq or Together. Each keeps its own encrypted key, and `/model claude`, `/model openai gpt-4o-mini` and `/model local` switch from the chat. The "send this to ...?" question names the company the request is going to.
- **Several named chats and pinned messages.** New chat now starts another chat and *keeps* the old one. The chats button in the header lists them: open, rename or delete one. Chats are named after the first thing you type, and an old chat that you open again is read back to the model so you can carry on. Pin any message (or `/pin` her last reply); `/pins` and the Pinned tab list them from every chat and jump to the message. New commands: `/chats`, `/chat`, `/rename`, `/pin`, `/pins`; `/clear` empties only the open chat and `/export` saves only the open chat.
- **A floating mini timer.** A small always-on-top countdown for the focus session (with a progress bar), the break, or a reminder less than an hour away. It never takes focus, can be dragged (and remembers where), hides while a full-screen app or game is running, and offers to stop the session or hide itself on right-click. `/timer on|off|reset`, or Settings, Look and alerts.
- **Light theme and window size.** Dark, light, or match Windows (`/theme light`), with the accent darkened automatically on light so text stays readable, and Small, Normal, Large and Larger window and text sizes (`/size large`).
- **Backup and restore.** `/backup` (or Settings, PC & files) saves settings, memory, routines, saved plans, reminders and notes to one zip file; `/restore` shows what is inside, asks first and only adds to what you have. It never holds an API key or chat history and never restores *Run freely*, administrator commands, the "ask before sending" switch, clipboard history or a cloud service address.
- **Plans you can edit, save and schedule.** Before you run a plan you can reword, move, remove or add steps. **Save plan** (or `/plan save <name>`) keeps it; `/plan run <name>` shows it again; `/plan schedule <name> every weekday at 9am` brings it up on a schedule. A scheduled plan **never runs by itself**: it plays the reminder sound, shows the plan and waits for you to press Run plan. `/plans`, `/plan unschedule`, `/plan delete`.

### Changed
- "New chat" no longer erases the chat you were in. To remove chats, delete one from the header list, or `/chats delete all` (it asks first). The privacy page says so.
- The header's model label shows the chosen model; the voice-to-text option applies to Gemini only.
- Colours in the windows now follow the theme live (including the dropdowns, buttons and text boxes), so changing the look needs no restart.

### Tests
- 580+ unit tests, including new ones for the providers (against a fake server on this PC), the chat and pin store and its upgrade of an old database, backup safety (tampered files, unknown tools, foreign server addresses), plan editing and scheduling, the timer text and light-theme contrast for every theme.
- Not verified live in this release: Claude with a real key, the floating timer window on screen, and the new window layouts on the running app (checked in offscreen renders and unit tests only).

## 2.1.0

### New
- **Agent mode** (`/agent <goal>`): she writes a short plan, you approve it once, then she works through it step by step. Every step still asks before anything risky and is logged. A Stop button ends it; "New chat" also stops it.
- **Look after your PC:** `/slow` finds likely causes of a slow PC with a fix for each; `/startup` lists programs that start with Windows and switches them on or off (nothing is uninstalled); `/cleanup` removes old temporary files, internet cache, crash dumps and error reports after you approve the size.
- **Search your documents:** `/index add <folder>` reads PDF, Word, Excel, PowerPoint, HTML and text files in folders you choose; `/find` and plain questions ("find the invoice from March", "what does this contract say about notice?") search and read them. Uses the PdfPig library for PDFs.
- **Slash commands:** type `/` for a list. They run in the app, work without an API key, and include `/clear`, `/new`, `/export`, `/search`, `/undo`, `/briefing`, `/focus`, `/routine`, `/reminders`, `/clipboard`, `/snip`, `/ocr`, `/note`, `/nudge`, `/quiet`, `/health`, `/memory`, `/forget`, `/theme`, `/model`, `/voice`, `/system`, `/update`, `/diagnostics`, `/tour`, `/features`, `/about` and `/help`.
- **Replies stream in** as they are written, with a **Stop** button (Gemini and local servers that stream).
- **Results you can use:** `/startup` shows a switch per program, `/find` shows Open and Show in folder buttons, `/cleanup scan` shows a Clean up button.
- **Ctrl+Alt+A** asks about text selected in any app (explain, summarize, fix grammar, rewrite, shorten, translate). **Ctrl+Alt+T** (or `/snip`) drags a box on the screen and copies its text with Windows' built-in text recognition.
- **Health nudges** (water, posture, eye rest, stretch), **quiet hours**, and **low-disk and low-battery warnings**.
- **Themes** and a custom accent colour, a choice of Windows voice and speaking speed, and a **memory manager** that shows, adds and deletes what she remembers.
- **This PC:** hardware details and a local model that suits them; **Copy diagnostics** for bug reports; **Check for updates**; **undo** for file moves and tidy-ups; **chat search** (Ctrl+F).
- A three-step **first-run tour**.

### Changed
- Settings has new tabs: *Look & alerts*, and *This PC* became *PC & files*.
- Lists in chat are more compact and readable; dropdowns and sliders now match the dark theme.
- **Junk-file cleanup deletes permanently.** It is the one exception to "files go to the Recycle Bin". It only touches the temp folder, the internet cache, crash dumps and error reports, only files older than a day, never follows links, and always asks first.

### Safety fixes (found by an independent code review)
- The junk cleanup can no longer treat a folder of real data as temp when the `TEMP` variable points into the user profile.
- Indexing refuses the whole AppData folder (including `LocalLow`) and 8.3 short names; a removed folder can't be refilled by an update that was already running, and its documents never appear in a search.
- One unreadable, hostile or oversized document can no longer stop indexing: parts are size-limited, slide names are parsed safely, HTML parsing is time-limited, and any unexpected error is logged.
- "Ask about selected text" no longer borrows a clipboard that a password manager marked private or that holds rich text or a picture.
- Pressing "New chat" while a plan is being written no longer leaves a plan card behind.

### Tests
- 440+ unit tests, and the two PowerShell suites that drive the real app now cover the new tabs, the chat commands and agent mode (plan, cancel, run, approval, stop).

## 2.0.0
See the [README](README.md): Gemini voice, a local database for chat history, memory and reminders, quick capture, clipboard history, routines, focus mode, a daily briefing and screen text.
