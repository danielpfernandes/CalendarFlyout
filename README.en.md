# CalendarFlyout — WPF / .NET 8

*[Leia em português](README.md)*

Windows 11 tray utility. Opens a compact agenda in the bottom-right corner of the monitor under the pointer, respecting the work area and DPI scaling. Reads the **primary calendar** of a Google account in read-only mode.

## Structure

```text
CalendarFlyout/
├── CalendarFlyout.csproj
├── App.xaml
├── App.xaml.cs                    # Hidden startup, NotifyIcon and shutdown
├── MainWindow.xaml               # WPF interface
├── MainWindow.xaml.cs            # Flyout, light dismiss and async operations
├── GlobalUsings.cs
├── app.manifest                  # Per-monitor DPI v2
├── Assets/calendar.ico           # Custom icon included
├── Native/WindowEffects.cs       # DWM, Acrylic, corners, position and activation
├── Services/
│   ├── CalendarClient.cs         # OAuth, automatic refresh and paginated API
│   ├── EncryptedDataStore.cs     # IDataStore with Windows DPAPI
│   ├── AgendaProjection.cs       # Ranges, timezone, all-day and grouping
│   └── ThemeService.cs           # Windows theme and high contrast
├── Tests/
│   ├── CalendarFlyout.Tests.csproj
│   └── Program.cs                # Checks runnable without an extra framework
├── .gitignore
├── README.md
└── README.en.md
```

## 1. Set up Google Cloud

1. Create or pick a project in the [Google Cloud Console](https://console.cloud.google.com/).
2. Enable the **Google Calendar API** on that project.
3. Configure the Google Auth Platform: Branding, Audience and Data Access. If the app is in testing mode, add the account you'll use under **Test users**.
4. Under Clients/Credentials, create an **OAuth client ID** of type **Desktop app**.
5. Download the JSON and save it as `client_secret.json` in the folder that contains `CalendarFlyout.csproj`.

The code validates the `installed` section of that file and only uses `client_id` and `client_secret`. The OAuth client uses the default browser and a local loopback callback managed by the SDK. Do not use Web or service account credentials.

Requested scopes:

```text
https://www.googleapis.com/auth/calendar.events.readonly
https://www.googleapis.com/auth/calendar.calendarlist.readonly
```

The actual credential does not ship with this project. The file is git-ignored and copied to the output on build/publish when present. You can build without it; the panel explains how to set it up when connecting.

## 2. Build and run

Requirements: Windows 11 and .NET 8 SDK or later; Visual Studio with .NET desktop development can also open the `.csproj` directly.

In PowerShell, inside the project folder:

```powershell
dotnet restore .\CalendarFlyout.csproj
dotnet build .\CalendarFlyout.csproj -c Release
Start-Process .\bin\Release\net8.0-windows\CalendarFlyout.exe
```

For a framework-dependent run, install the **.NET Desktop Runtime 8**. To distribute without requiring that runtime, publish for the target architecture:

```powershell
dotnet publish .\CalendarFlyout.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

For Windows ARM64, swap `win-x64` for `win-arm64`. Distribute the whole published folder; `client_secret.json` must sit next to the executable. There is no service installation, scheduling, or automatic run at Windows login.

### Automated release

The `.github/workflows/release.yml` workflow automatically publishes a self-contained `win-x64` build and attaches the zip to a GitHub Release. It runs when a tag in the `vX.Y.Z` format is pushed (`git tag v1.0.0 && git push origin v1.0.0`), or manually from the **Actions** tab (`workflow_dispatch`, providing the desired tag). `client_secret.json` is not included in the zip, since it isn't part of the repository.

## 3. Use it

- The app starts fully hidden, showing only a tray icon. Windows may place it in the hidden icons menu; pin it to the notification area if you'd like.
- Left-click the icon: opens the panel. There's also **Open agenda** in the right-click menu.
- On first use, click **Connect Google** and authorize in the browser. Reopen the panel from the tray once done. OAuth keeps running in the background even if the panel loses focus.
- Click outside, press Esc, or use the close button: the window hides and the process stays alive.
- **Refresh** fetches events again. Opening the panel also refreshes, as does the one-minute timer while it's visible.
- **Forget login** removes the local tokens and the displayed events. It does not revoke consent on Google nor end the browser session. To revoke, use [Google Account connections](https://myaccount.google.com/connections).
- Right-click the tray icon → **Exit** ends the process, removes the icon and releases resources.

The panel doesn't use a WebView, doesn't open a conventional window on startup, and doesn't appear in the taskbar or Alt+Tab. Opening a second instance in the same Windows session ends the new instance.

## Dates and refresh

- Time window: from today's local midnight to local midnight four days later, exclusive end.
- Recurrences are expanded via `SingleEvents`; all pages are read.
- Times with an offset are converted to the local Windows timezone. Actual start and end are shown; events that cross midnight appear on each day they span, with the times on the dates.
- All-day events use the original civil dates and the API's exclusive end. They receive no artificial timezone conversion.
- The agenda lists all four days, including days with no events. Cancelled events are omitted.
- On transient failures, the last result may stay visible along with a failure message; there is no on-disk event cache.

## Security and OAuth

`EncryptedDataStore` implements `IDataStore`. Both the access token and the refresh token are serialized and encrypted with `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` **before** being written to disk:

```text
%LOCALAPPDATA%\CalendarFlyout\Tokens\<hash>.bin
```

Writes use an already-encrypted temporary file and a replace in the same directory. The official library refreshes access tokens using the refresh token and persists the result through the same store. DPAPI ties protection to the Windows identity; other programs running as that same user can still access the data. The app does not log tokens or event contents.

No app can guarantee a permanent login: tokens can be revoked or expire. In external OAuth projects in **Testing** mode, refresh tokens for this scope typically expire in seven days. For distribution, configure whatever publishing/verification applies to your project. When authorization is refused, use **Forget login** and reconnect.

## Appearance and native behavior

`ShowInTaskbar=False`, `WindowStyle=None` and `WS_EX_TOOLWINDOW` keep the window behaving as a utility. The WPF `Deactivated` event calls `Hide()`, including during OAuth. There's no global mouse polling or keyboard hooks.

DWM rounds the corners. `DWMWA_SYSTEMBACKDROP_TYPE=DWMSBT_TRANSIENTWINDOW` requests **Desktop Acrylic** on Windows 11 22H2 / build 22621 or later. The window keeps `AllowsTransparency=False` and extends the frame to allow native composition. The content has a translucent layer for legibility. Earlier builds get a solid background; Windows may also reduce the effect based on accessibility, transparency or performance settings.

The theme follows `AppsUseLightTheme` and Windows preference changes. High contrast uses system colors and turns the backdrop off. It never changes Windows preferences.

## Validation

Validation performed at delivery: Release build with **zero errors and zero warnings**; the **11 agenda/date checks passed**. Running the DPAPI tests was blocked by the restricted validation environment, which returned a "Windows profile not loaded" error. Those tests are included to run in a normal Windows session. Real login, appearance and tray interactions require the manual validation below; they were not claimed as tested.

Run the date and storage checks on Windows:

```powershell
dotnet run --project .\Tests\CalendarFlyout.Tests.csproj -c Release
```

The program tests the four days, all-day exclusive end, midnight, timezone, daylight saving time, cancelled event, empty day, missing title, and DPAPI persist/refresh/delete. It only uses fake tokens in a temp folder.

Manual integration validation, which requires your own credential and account:

1. Start and confirm there's no window or taskbar button.
2. Open from the tray; click outside, press Esc, and close with Alt+F4; the process should stay alive.
3. Connect in the browser, reopen and check the agenda; restart the app and confirm the login is reused.
4. Create all-day, recurring, and midnight-crossing events on Google; compare the four days.
5. Switch light/dark theme; check another monitor with a different scale and the position above the taskbar.
6. Disconnect the network, refresh and check the message; test Forget login and Exit.

## Official references

- [OAuth in the Google .NET library](https://developers.google.com/api-client-library/dotnet/guide/aaa_oauth)
- [OAuth for desktop apps, loopback and expiration](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google Calendar: Events.list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list)
- [Google.Apis.Calendar.v3 package](https://www.nuget.org/packages/Google.Apis.Calendar.v3/1.75.0.4206)
- [DWM_SYSTEMBACKDROP_TYPE and requirements](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
