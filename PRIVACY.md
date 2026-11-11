# Privacy

WinCompanion has **no telemetry, no analytics and no account**. This page says exactly what stays on your PC and what
leaves it.

## Stored on your PC

Everything is in `%AppData%\WinCompanion` (tray menu, *Data folder*):

| File | Contains |
|---|---|
| `apikey.bin`, `apikey.claude.bin`, `apikey.openai.bin` | Your Gemini, Claude and OpenAI-compatible service keys, each encrypted with Windows DPAPI (readable only by your Windows account on this PC) |
| `settings.json` | Preferences: model choice, server addresses, privacy switches, character, look and size, floating timer position |
| `wincompanion.db` | A local SQLite database with your chats and pinned messages, the facts you asked it to remember, pending and scheduled reminders, saved plans, and (only if you add folders with `/index add`) a search index holding the text of the documents in those folders |
| `Backups\` (in Documents\WinCompanion) | Backups you make, locked with your password (`.tessabak`) or plain (`.zip`) |
| `actions.log` | Each action it ran, when, whether you approved it, and a short result |
| `crash.log` | Error details if something failed (no chat content) |
| `pets\` | Characters you added yourself |

Notes you ask it to save go to `Documents\WinCompanion\notes.md`. Backups you make with `/backup` go to `Documents\WinCompanion\Backups` (or where you choose): one file with your settings, memory, routines, plans, reminders and notes, never an API key or chat history. It is encrypted when you give it a password (the box asks), and otherwise a plain zip that anyone with the file can read, so keep that kind somewhere private. Delete any of these at any time. "New chat" starts another chat and keeps the old one: delete a chat from the chats list in the header (this also removes it from search), or type `/chats delete all` to remove every chat and pinned message at once (it asks first).

## Sent to your chosen cloud model (Gemini, Claude or an OpenAI-compatible service)

Each request goes to the company whose model you chose in *Settings, AI model*, and includes your message, the recent conversation, the saved memories, the
results of any tools it ran (for example the output of a PowerShell command) and the list of what she can do. It does not look at which app you are
using. Screenshots, clipboard text and file contents are sent **only after you approve each one**, unless you turn that question off in *Settings, Privacy*;
the question names the company it is going to. When you open an old chat, its last few messages are given to the model again so you can carry on.

- **Gemini:** Google's terms for the Gemini API apply, including how data from unpaid ("free tier") use may be handled: <https://ai.google.dev/gemini-api/terms>.
- **Claude:** requests go to Anthropic through its official software library, with your key: <https://www.anthropic.com/legal/commercial-terms>.
- **OpenAI-compatible service:** requests and your key go only to the address you typed (for example api.openai.com or openrouter.ai), which must start with `https://`. That service's own terms apply; with an aggregator such as OpenRouter the model's maker may also see the request.

Don't put passwords, financial details or other people's private information into a cloud model.

## Not sent anywhere (local model)

With **Local**, requests go only to the server address you set (normally `localhost`). Nothing is sent to Google or to the author.

## Other network use

- **Voice:** when *Use a cloud service to understand my voice* is on (the default), each phrase you say after pressing the talk key or the wake word is recorded and sent to the service chosen under *Which service* to be turned into text: Gemini (Google), or an OpenAI-compatible service's speech model, using the key and address from *Settings, AI model*. *Automatic* picks your chosen model's own service when it can listen (Gemini or OpenAI-compatible); with Claude or a local model, Automatic uses Windows' recogniser and sends no audio anywhere, unless you pick a service on purpose. Nothing is recorded or sent until you start talking to her, and recordings are not saved. Turn the switch off (or choose *Windows only*) to use Windows' own speech recognition instead, which may also use Microsoft's online service.
- **Web lookup** (when the AI needs current facts) sends only the search words to <https://duckduckgo.com> and reads back the top results.
- **Weather** asks <https://wttr.in> for the city you name (or, with no city, your approximate location by IP address).
- **Daily briefing** (when you ask for it or turn it on) fetches the weather from <https://wttr.in> and, for each news topic you list, searches <https://duckduckgo.com> for "<topic> news". Only the city and the topic words leave your PC, and no AI model is involved.
- **Clipboard history** (off by default) keeps the last 100 pieces of text you copy in `wincompanion.db` on this PC, skipping copies password managers mark as private. If you ask the assistant to search it while Gemini is selected, you are asked before the matches are sent.
- **Screen text** ("copy the text on my screen", `/ocr`, and the drag-a-box *Snip text* on Ctrl+Alt+T) is read by Windows' built-in text recognition on this PC; nothing is sent anywhere. A snip is not kept as a picture, and the chat records only the first few words of what was copied.
- **Update check** (only when you press *Check for updates* or type `/update`) asks <https://api.github.com> for the latest release of this project. GitHub sees your IP address like any website you visit; nothing about you or your PC is sent, and nothing is downloaded or installed. It never runs on its own.
- **This PC and diagnostics** read your memory, processor, graphics and disk sizes from Windows to show them in Settings and suggest a local model. They stay on this PC. *Copy diagnostics* puts a report on your clipboard (version, hardware, a few settings and recent errors, with your Windows user name and PC name hidden); it is never sent anywhere unless you paste it yourself.
- **Document search** reads only the folders you add (never system or app-data folders), on this PC. The index is stored unencrypted in `wincompanion.db` like your chat, so it contains the text of those documents: remove a folder, run `/index clear`, or delete the file to forget it. With Gemini, document text goes to Google only for the matches or the document you ask about, and only after you approve (unless you turned that question off). With a local model nothing leaves the PC.
- **Agent mode** (`/agent`) sends only your goal to the model to write the plan. Each step is then an ordinary request, with the same approvals, privacy questions and log as if you had typed it. Saved plans stay on this PC; a **scheduled** plan only comes up for your approval at its time and runs nothing until you press Run plan.
- **Backup and restore** read and write one file on this PC and send nothing anywhere. With a password the file is encrypted (AES-256-GCM, key from PBKDF2-SHA256); the password is asked for in a box, is never saved or logged, and cannot be recovered. A restore only adds to your data and never switches on a risky setting.
- **The model's memory** is no longer saved in separate files: it is rebuilt from the open chat each time she starts or you open a chat, and old `context.*.json` files are deleted when she starts.
- **The floating button** and **commands that work with no model** run entirely on this PC.
- **The floating timer, chats and pinned messages, light theme and size** work entirely on this PC.
- **Start-up programs, junk files and the slow-PC check** read start-up entries, folder sizes and process load on this PC and send nothing anywhere. Switching a start-up program off only sets the on/off flag Task Manager uses. *Clean up junk files* permanently deletes old files in the temp folder, internet cache, crash dumps and error reports, after you approve the size.
- **Streaming replies** send exactly the same request as before, to the same model service (for Gemini, its streaming address instead of its one-piece address), and show the answer as it arrives. Nothing extra is sent, and the Stop button simply closes the connection.
- **Ask about selected text** (Ctrl+Alt+A) reads the text you have selected only when you press the key. It goes to the model only after you choose an action or type a question, and with Gemini you are asked first (unless you turned that off). It is not saved anywhere except in the chat if you ask about it.
- **Low-disk and battery warnings** read drive free space and the battery level on this PC every few minutes. Nothing is sent anywhere. Turn them off in *Settings, Look and alerts*.
- **What she remembers** is shown and editable in *Settings, You and her*. These facts are part of every request, so with Gemini they go to Google; delete any you don't want shared.
- **Web search** opens your browser at a search page; the app itself sends nothing.
- **Reminders, characters and the local parser** work without internet.
- **Voice** is understood by Gemini when that option is on with Gemini selected (see above), otherwise by Windows speech recognition. The microphone is used only while the wake word is on or while you are talking. Speech diagnostics (levels and outcomes, never the words or audio) go to `speech.log` in the data folder, which is capped at 1 MB.

## Control

Everything above can be switched off or removed: choose a local model, turn off the app-title and "ask before sending" options as you prefer,
remove your key (delete `apikey.bin`), and delete the data folder to erase it all.
