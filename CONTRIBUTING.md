# Contributing to WinCompanion

Thanks for helping! This is an open-source project (MIT licence) and anyone can contribute: bug reports, fixes, new tools,
new characters, documentation, translations. You don't need to ask first for small fixes; for bigger changes, open an
issue so we can agree on the approach before you spend time on it.

## Ground rules

- Be kind: see "Code of conduct" at the end of this file.
- **Safety first.** Anything that deletes, overwrites, installs, sends data off the PC or changes system settings must ask the user
  first. New tools that do those things need `needsConfirmation: true` (see `Tools/`), and a test.
- **Privacy.** Don't add telemetry. Anything that sends content to a cloud model must go through `ToolContext.AllowCloudAsync`.
- **No copyrighted characters.** Characters must be your own work, public domain / CC0, or have a licence that allows redistribution
  in an MIT-licensed project (record the source in `Assets/Pets/CREDITS.md`). Please don't submit artwork of trademarked characters
  (superheroes, cartoon or game characters and so on).

## Build and run

You need Windows 10 or 11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build
dotnet run
```

Useful start-up options for testing: `--show` (open the chat), `--settings`, `--flyout` (open the tray menu), `--pet <id>` (preview a character).
Set `WINCOMPANION_DEBUG_LLM=1` to log every request to and reply from the model in `%AppData%\WinCompanion\llm-debug.log`.

## Tests

The tests drive the real app, so run them on a PC you aren't using. They back up and restore your settings.

| Test | What it covers |
|---|---|
| `tests\ToolSuite.ps1` | Every tool, through the real window, using a scripted fake model (`tests\fake_llm.py`): approval cards, file moves, windows, clipboard, reminders... Needs Python 3. |
| `tests\UiSuite.ps1` | Every tray-menu item and Settings control. |

Run `powershell -File tests\ToolSuite.ps1` and `powershell -File tests\UiSuite.ps1`. Add a case when you add a tool or a setting.

## Where things are

| Folder | Contents |
|---|---|
| `Services/` | The AI clients (Gemini, local OpenAI-compatible), reminders and their parser, settings, privacy, logging |
| `Tools/` | Everything the assistant can do. One class per area; add a tool by adding a `DelegateTool` |
| `UI/`, `*.xaml` | Windows and styles |
| `Assets/Pets/` | Characters (a sprite sheet and `pet.json` each) |
| `tools/` | Python scripts that generate characters (`video_to_pet.py` cuts them out of video) |
| `installer/`, `build/` | Inno Setup script, release build and signing scripts |

## Adding a character

Run `tools/video_to_pet.py` on a short clip of an animal (see the top of the script), or draw one and add a `pet.json`. The format and
the licences of the built-in characters are in [Assets/Pets/CREDITS.md](Assets/Pets/CREDITS.md). Check the result with
`dotnet run -- --pet <id>`.

## Sending a pull request

1. Fork, create a branch, make your change, and build with no warnings.
2. Run the two test scripts above.
3. Describe what you changed and why, and what you tested. Screenshots help for UI changes.
4. By submitting a pull request you agree that your contribution is licensed under the MIT licence of this project.

## Reporting problems

Open an issue with your Windows version, what you did, what happened, and (for crashes) the contents of `%AppData%\WinCompanion\crash.log`.
Please remove any private information first. For security problems, don't post details publicly: see [SECURITY.md](SECURITY.md).

## Code of conduct

We want Tessa to be a welcoming project for everyone, whatever their experience, background or identity.

**Expected:** be respectful and patient, give and accept feedback graciously, assume good intent, and focus on what is best for the project and its users.

**Not acceptable:** harassment, insults, personal attacks, discrimination, publishing others' private information, or other conduct that would be
inappropriate in a professional setting.

Maintainers may edit or remove comments, commits and issues that don't follow this code, and may ban people who repeatedly break it.
To report a problem, contact the maintainers privately through the repository's contact details.

This is a short summary in the spirit of the [Contributor Covenant 2.1](https://www.contributor-covenant.org/version/2/1/code_of_conduct/).
