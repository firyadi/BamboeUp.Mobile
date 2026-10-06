# BamboeUp.Mobile

.NET MAUI **Blazor Hybrid** client for BamboeUp (Android + iOS + Windows).

## Architecture

| Layer | Role |
|-------|------|
| **Shell** (`Shell/`) | Auth → BamboeUp.Api, SecureStorage session, device (camera / GPS / biometric), HttpClient |
| **UI shell** (`Components/`) | Dark dashboard, bottom nav, login, profile |
| **Modules** (`Modules/`) | SchemaStudio-generated features (same idea as `BamboeUp.AppModules`) |

## Run

1. Start BamboeUp.Api (default `http://localhost:5000`).
2. Edit `Resources/Raw/appsettings.json` → `ApiSettings:BaseUrl`:
   - **Android emulator (default di repo):** `http://10.0.2.2:5000/`  
     (`localhost` di emulator = HP virtual sendiri, **bukan** PC host)
   - Windows / iOS simulator: `http://localhost:5000/`
   - Physical device: your LAN IP, e.g. `http://192.168.x.x:5000/`
3. After installing Android Studio, set **User** environment variables (then **reopen terminal**):
   - `ANDROID_HOME` = `%LOCALAPPDATA%\Android\Sdk`
   - `JAVA_HOME` = **JDK 21** — e.g. `C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot`
     - JBR Android Studio (JDK 25) **tidak** didukung MAUI → `winget install Microsoft.OpenJDK.21`
   - First time: install platform API 36:
     `dotnet build -t:InstallAndroidDependencies -f net10.0-android "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" "-p:AcceptAndroidSDKLicenses=true"`
4. Start an emulator (or plug in a phone with USB debugging), then:
   `dotnet build -t:Run -f net10.0-android`
   - AVD created for this machine: `BamboeUp_API36`
   - Start: `"%LOCALAPPDATA%\Android\Sdk\emulator\emulator.exe" -avd BamboeUp_API36`
   - Wait until `adb devices` shows `device` (not `offline`), then Run again.

One-off without relying on env:

```powershell
dotnet build -f net10.0-android `
  -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" `
  -p:JavaSdkDirectory="C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot"
```

## SchemaStudio

Set `UiPlatform=Mobile` and `MobileOutputPath` to this project folder. Generated files land under `Modules/` (see `TemplateUi1Table.Mobile.md`).
