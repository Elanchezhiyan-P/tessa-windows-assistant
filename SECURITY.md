# Security

WinCompanion can run PowerShell, move and delete files, and talk to AI services, so security reports are taken seriously.

## Reporting a vulnerability

Please **don't** open a public issue for a security problem. Use the repository's private vulnerability reporting
(*Security, Report a vulnerability* on GitHub) and include what you found, how to reproduce it, and the version.
If GitHub reporting isn't available, email elanche97@gmail.com instead. You'll get a reply as soon as the maintainers can, and credit in the fix if you want it.

## What counts

- A way to make the app run a command or change files **without the approval card** it should have shown
  (see `Tools/SafeCommands.cs` and how `NeedsConfirmation` is used).
- A way to read your API key, chat, or files without you approving it, or to send data off the PC when the setting says not to.
- Text on a web page, document or screenshot that makes the assistant act on instructions you didn't give it.

## Good to know

- The Gemini key is stored encrypted with Windows DPAPI, readable only by your Windows account on that PC.
- The "Run freely" PowerShell mode relies on a pattern list of dangerous commands. It is a safety net, not a sandbox,
  and the default mode asks before anything that isn't read-only.
- Only the latest release is supported.
