# Building the ACPollerForAPS installer (NSIS)

## Prerequisites
- NSIS 3.x installed (provides `makensis.exe`).
- The **advsplash** plugin (bundled with NSIS) for the launch splash. If you don't
  want the splash, remove the `.onInit` function from the .nsi.

## Folder layout expected by the script
```
nsis\
  ACPollerForAPS.nsi
  assets\
    acpoller.ico
    welcome.bmp      (164x314)
    header.bmp       (150x57)
    splash.bmp
  dist\              <-- put your BUILT binaries here (Release)
    ACPollerForAPS.Service.exe
    ACPollerForAPS.UI.exe
    ACPollerForAPS.Core.dll
    Newtonsoft.Json.dll
    NLog.dll
    NLog.config
    FluentFTP.dll
    AWSSDK.Core.dll
    AWSSDK.S3.dll
    MahApps.Metro.dll
    ControlzEx.dll
```

> The exact list of third-party DLLs depends on your build. After compiling in
> Release, copy everything from the service's and UI's `bin\Release` output into
> `dist\`, then trim the `File` lines in the .nsi to match. Missing a DLL here is
> the most common cause of a runtime failure after install.

## Compile
```
makensis ACPollerForAPS.nsi
```
Produces `ACPollerForAPS-Setup-2.0.0.exe`.

## What the installer does
- Asks for the install directory (defaults to `C:\Program Files\ACPollerForAPS`).
- Lets the user choose components:
  - **Service** — copies the service, installs it via InstallUtil (which also
    creates the Windows Event Log source), and starts it.
  - **Configuration interface (UI)** — copies the UI and creates Start Menu
    shortcuts.
- Requires administrator rights (service + Event Log).
- Registers an Add/Remove Programs entry and writes an uninstaller.

## Uninstall
Stops and removes the service (InstallUtil `/u`, which also removes the Event Log
source), deletes the binaries and shortcuts, and cleans the registry.
It intentionally **keeps** `logs\`, `providers\` and `settings.json` so
configuration and history survive an uninstall/upgrade. Remove them manually if a
full wipe is wanted.

## Notes
- The service is installed via
  `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe`. This matches the
  existing `ProjectInstaller.cs` (service registration + Event Log source), so the
  installer stays consistent with the manual .bat method.
- If you install the **UI without the service**, uncomment the FluentFTP / AWSSDK
  lines in the UI section (the UI's "Test connection" buttons need them).
- BMP images must stay at the MUI2 dimensions (welcome 164x314, header 150x57) or
  they will look wrong.
