# Privacy

WinCompanion has **no telemetry, no analytics and no account**. This page says exactly what stays on your PC and what
leaves it.

## Stored on your PC

Everything is in `%AppData%\WinCompanion` (tray menu, *Data folder*):

| File | Contains |
|---|---|
| `apikey.bin` | Your Gemini key, encrypted with Windows DPAPI (readable only by your Windows account on this PC) |
| `settings.json` | Preferences: model choice, local server address, privacy switches, character |
| `wincompanion.db` | A local SQLite database with your chat history, the facts you asked it to remember, and pending reminders |
| `context.*.json` | The model's recent conversation context |
| `actions.log` | Each action it ran, when, whether you approved it, and a short result |
| `crash.log` | Error details if something failed (no chat content) |
| `pets\` | Characters you added yourself |

Notes you ask it to save go to `Documents\WinCompanion\notes.md`. Delete any of these at any time; "New chat" clears the chat and context.

## Sent to Google (only when the model is Gemini)

Each request includes your message, the recent conversation, the saved memories, the results of any tools it ran
(for example the output of a PowerShell command). It does not look at which app you are using. Screenshots, clipboard text and file contents are sent **only after you approve each one**, unless you turn
that question off in *Settings, Privacy*.

Google's terms for the Gemini API apply, including how data from unpaid ("free tier") use may be handled. Read them before sending
anything sensitive: <https://ai.google.dev/gemini-api/terms>. Don't put passwords, financial details or other people's private
information into a cloud model.

## Not sent anywhere (local model)

With **Local**, requests go only to the server address you set (normally `localhost`). Nothing is sent to Google or to the author.

## Other network use

- **Voice (Gemini selected):** when *Use Gemini to understand my voice* is on (the default), each phrase you say after pressing the talk key or the wake word is recorded and sent to Google's Gemini to be turned into text. Nothing is recorded or sent until you start talking to her, and recordings are not saved. Turn it off in *Settings, You and her* to use Windows' own speech recognition instead (which may also use Microsoft's online service). With a local model, voice never goes to Gemini.
- **Web lookup** (when the AI needs current facts) sends only the search words to <https://duckduckgo.com> and reads back the top results.
- **Weather** asks <https://wttr.in> for the city you name (or, with no city, your approximate location by IP address).
- **Daily briefing** (when you ask for it or turn it on) fetches the weather from <https://wttr.in> and, for each news topic you list, searches <https://duckduckgo.com> for "<topic> news". Only the city and the topic words leave your PC, and no AI model is involved.
- **Clipboard history** (off by default) keeps the last 100 pieces of text you copy in `wincompanion.db` on this PC, skipping copies password managers mark as private. If you ask the assistant to search it while Gemini is selected, you are asked before the matches are sent.
- **Screen text** ("copy the text on my screen") is read by Windows' built-in text recognition on this PC; nothing is sent anywhere.
- **Update check** (only when you press *Check for updates* or type `/update`) asks <https://api.github.com> for the latest release of this project. GitHub sees your IP address like any website you visit; nothing about you or your PC is sent, and nothing is downloaded or installed. It never runs on its own.
- **This PC and diagnostics** read your memory, processor, graphics and disk sizes from Windows to show them in Settings and suggest a local model. They stay on this PC. *Copy diagnostics* puts a report on your clipboard (version, hardware, a few settings and recent errors, with your Windows user name and PC name hidden); it is never sent anywhere unless you paste it yourself.
- **Web search** opens your browser at a search page; the app itself sends nothing.
- **Reminders, characters and the local parser** work without internet.
- **Voice** is understood by Gemini when that option is on with Gemini selected (see above), otherwise by Windows speech recognition. The microphone is used only while the wake word is on or while you are talking. Speech diagnostics (levels and outcomes, never the words or audio) go to `speech.log` in the data folder, which is capped at 1 MB.

## Control

Everything above can be switched off or removed: choose a local model, turn off the app-title and "ask before sending" options as you prefer,
remove your key (delete `apikey.bin`), and delete the data folder to erase it all.
