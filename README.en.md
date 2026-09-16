# CalendarFlyout — WPF / .NET 8

*[Leia em português](README.md)*

Windows 11 tray utility that shows, in a compact panel near the tray icon, your Google Calendar agenda from today through the next 3 days — read-only, for the calendars you choose.

## Features

- Runs hidden in the tray; no window, no taskbar entry.
- Sign in with your Google account via OAuth (the app never sees or stores your password).
- In **Settings**, pick which of your calendars show up in the panel.
- Refreshes itself every minute while the panel is open.
- Optional auto-start with Windows (per-user, no admin rights needed).
- Follows the Windows light/dark theme and high contrast mode.

## Requirements

- Windows 11.
- **.NET 8 SDK**, to build — or the **.NET Desktop Runtime 8**, to run an already-published build without the SDK.

## 1. Set up Google Calendar access

1. Create or pick a project in the [Google Cloud Console](https://console.cloud.google.com/) and enable the **Google Calendar API**.
2. Configure the OAuth consent screen (Google Auth Platform → Branding, Audience, Data Access). In testing mode, add your account under **Test users**.
3. Under Credentials, create an **OAuth client ID** of type **Desktop app** and download the JSON.
4. Save it as `client_secret.json` next to `CalendarFlyout.csproj` (or next to the executable, for a published build). It is not committed or shipped with the project.

The app only uses that file's `client_id`/`client_secret` and requests these scopes:

```text
https://www.googleapis.com/auth/calendar.events.readonly
https://www.googleapis.com/auth/calendar.calendarlist.readonly
```

In external OAuth projects in **Testing** mode, the refresh token usually expires in about 7 days; when that happens, use **Forget login** and reconnect.

## 2. Build and run

In PowerShell, inside the project folder:

```powershell
dotnet restore .\CalendarFlyout.csproj
dotnet build .\CalendarFlyout.csproj -c Release
Start-Process .\bin\Release\net8.0-windows\CalendarFlyout.exe
```

To distribute without requiring the .NET Desktop Runtime, publish a self-contained build:

```powershell
dotnet publish .\CalendarFlyout.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

Swap `win-x64` for `win-arm64` for Windows ARM64. Distribute the whole folder; `client_secret.json` must sit next to the executable.

### Automated release

The `.github/workflows/release.yml` workflow automatically publishes a self-contained `win-x64` build and attaches the zip to a GitHub Release. It runs when a `vX.Y.Z` tag is pushed (`git tag v1.0.0 && git push origin v1.0.0`), or manually from the repository's **Actions** tab.

## 3. Use it

1. On startup the app is fully hidden — only the tray icon shows.
2. Left-click the icon (or **Open agenda** in the right-click menu) opens the panel.
3. The first time, click **Connect Google**, authorize in the browser, and reopen the panel from the tray.
4. In **Settings**, choose which calendars to show and, if you want, turn on **Start with Windows**.
5. **Refresh** fetches events again; the panel also refreshes itself every minute while open.
6. **Forget login** removes the locally stored tokens (it does not revoke access on Google — for that, use [Google Account connections](https://myaccount.google.com/connections)).
7. Clicking outside, Esc, or the close button hides the panel without ending the app. **Exit**, in the tray menu, ends the process.

## Security and privacy

OAuth tokens are encrypted on disk with Windows DPAPI, tied to your Windows user, under `%LOCALAPPDATA%\CalendarFlyout\Tokens`. The app never logs tokens or event content, and sends no data to any server other than the Google Calendar API.

## Official references

- [OAuth in the Google .NET library](https://developers.google.com/api-client-library/dotnet/guide/aaa_oauth)
- [OAuth for desktop apps, loopback and expiration](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google Calendar: Events.list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list)
