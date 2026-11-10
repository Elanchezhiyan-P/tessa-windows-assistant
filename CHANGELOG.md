# Changelog

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
