# PcUptimeTelegramTracker

A lightweight Windows Service that tracks how long your PC stays on (and how long it spends asleep), then reports session summaries to a Telegram bot — including which processes consumed the most CPU time.

## Features

- **Accurate uptime tracking**, even with Windows Fast Startup enabled (uses `Kernel-Boot` Event ID 27 and `User32` Event ID 1074 instead of the unreliable `EventLog` 6005/6006 pair).
- **Event-driven, not polling** — subscribes to the Windows Event Log via `EventLogWatcher`, so it consumes virtually no CPU while idle.
- **Per-session Telegram summary**, sent automatically on the next startup after a shutdown/restart (no reliance on catching the shutdown moment itself, which is unreliable due to how little time Windows gives a service to act during shutdown).
- **Top 5 resource-heavy apps** per session, based on lightweight periodic CPU sampling (every 60 seconds).
- **Weekly summary report**, aggregating the past 7 days of sessions and app usage.
- Local SQLite storage — no external database server required.

## How it works

1. On startup, the service reads the Windows Event Log to reconstruct the previous session's timeline (start time, end time, awake/asleep duration) — even if the service itself wasn't running during part of that session.
2. It sends a Telegram message summarizing that session, then starts a live `EventLogWatcher` subscription to track the current session going forward.
3. Every 60 seconds, it samples running processes and accumulates CPU time per process name into a local SQLite database.
4. Once a day, it checks whether 7 days have passed since the last weekly report and sends one if due.

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/) (to build/publish)
- A Telegram bot (see setup below)

## Setup

### 1. Create a Telegram bot

1. Open Telegram and message [@BotFather](https://t.me/BotFather).
2. Send `/newbot` and follow the prompts to choose a name and username.
3. BotFather will give you a **Bot Token** — save it.
4. Send any message to your new bot, then visit:
   `https://api.telegram.org/bot<YOUR_TOKEN>/getUpdates`
   and find your **Chat ID** in the response (`"chat":{"id": ...}`).

### 2. Configure the app

Create `PcUptimeTelegramTracker.Worker/appsettings.Local.json` (this file is git-ignored, so your token never gets committed):

```json
{
  "Telegram": {
    "BotToken": "your-bot-token-here",
    "ChatId": "your-chat-id-here"
  }
}
```

### 3. Install as a Windows Service

Run the included script from an **Administrator** PowerShell prompt, from the repo root:

```powershell
.\install-service.ps1
```

This publishes the project in Release mode, copies your local config into the publish folder, and registers/starts the service (auto-start on boot).

Alternatively, do it manually:

```powershell
dotnet publish PcUptimeTelegramTracker.Worker -c Release -r win-x64 --self-contained false -o C:\Services\PcUptimeTelegramTracker

# copy appsettings.Local.json into C:\Services\PcUptimeTelegramTracker manually

sc.exe create PcUptimeTelegramTracker binPath= "C:\Services\PcUptimeTelegramTracker\PcUptimeTelegramTracker.Worker.exe" start= auto
Start-Service PcUptimeTelegramTracker
```

### Uninstalling

```powershell
Stop-Service PcUptimeTelegramTracker
sc.exe delete PcUptimeTelegramTracker
```

## Tech stack

- .NET 10 Worker Service
- [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot)
- Microsoft.Data.Sqlite
- Windows Event Log (`System.Diagnostics.Eventing.Reader`)

## A note on language

This project was built for personal use, so **log output and Telegram messages are in Turkish**. Code, comments, and commit messages are kept in English for readability.

## License

Personal project — no license specified yet.