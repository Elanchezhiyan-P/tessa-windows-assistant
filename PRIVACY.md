# Privacy

WinCompanion has **no telemetry, no analytics and no account**. This page says exactly what stays on your PC and what
leaves it.

## Stored on your PC

Everything is in `%AppData%\WinCompanion` (tray menu, *Data folder*):

| File | Contains |
|---|---|
| `apikey.bin` | Your Gemini key, encrypted with Windows DPAPI (readable only by your Windows account on this PC) |
| `settings.json` | Preferences: model choice, local server address, privacy switches, character |
| `transcript.json`, `context.*.json` | Your chat history and the model's conversation context |
| `memory.json` | Facts you asked it to remember |
| `reminders.json` | Pending reminders |
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

- **Web lookup** (when the AI needs current facts) sends only the search words to <https://duckduckgo.com> and reads back the top results.
- **Weather** asks <https://wttr.in> for the city you name (or, with no city, your approximate location by IP address).
- **Web search** opens your browser at a search page; the app itself sends nothing.
- **Reminders, characters, voice and the local parser** work without internet.
- **Voice** uses Windows speech recognition. The microphone is used only while the wake word is on or while you are talking.

## Control

Everything above can be switched off or removed: choose a local model, turn off the app-title and "ask before sending" options as you prefer,
remove your key (delete `apikey.bin`), and delete the data folder to erase it all.
