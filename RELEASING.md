# Building and preparing a release

This project is a Windows MelonLoader mod targeting .NET 6. The DLL references assemblies generated in the installed game folder, so a plain checkout is not enough to build it.

## Build and test

1. Install the stable MelonLoader **0.7.3** in the Firestone game folder. Do not use a nightly build; newer nightlies have made the game crash at startup.
2. Launch Firestone once through Steam or Epic, then close it. MelonLoader generates `MelonLoader\Il2CppAssemblies`, which the project references.
3. Install a .NET SDK, then build with the actual folder containing `Firestone.exe`:

   ```powershell
   dotnet build src -c Release -p:GameRoot="C:\Program Files (x86)\Steam\steamapps\common\Firestone"
   ```

   The build writes `src\bin\Release\net6.0\ZdbrusBot.dll`. The post-build target also copies it to `<GameRoot>\Mods`. For a staging build that does not overwrite the installed mod, override the destination:

   ```powershell
   dotnet build src -c Release `
     -p:GameRoot="C:\Program Files (x86)\Steam\steamapps\common\Firestone" `
     -p:ModsDir="C:\path\to\staging\Mods"
   ```

   Use `-p:GameRoot="..."` for a custom Steam library or Epic install. The default Steam path is set in `src\Directory.Build.props`.
4. Install the build in the game and test the changed feature. Check `MelonLoader\Latest.log`; the startup line `Zdbrus Bot ready` confirms the mod loaded.

## Package the player download

Start from the latest published `ZdbrusBot.zip` so the compatibility files, logo, license, and third-party notices stay intact. Extract it to a new, version-specific staging folder, replace `Mods\ZdbrusBot.dll` with the tested build and replace the bundled `README.txt` with `release\README.txt`, then re-create `ZdbrusBot.zip` with the package contents at its root:

```powershell
$package = Join-Path $env:TEMP "ZdbrusBot-0.7.9-package"
Expand-Archive -Path "C:\path\to\latest\ZdbrusBot.zip" -DestinationPath $package
Copy-Item "src\bin\Release\net6.0\ZdbrusBot.dll" (Join-Path $package "Mods\ZdbrusBot.dll") -Force
Copy-Item "release\README.txt" (Join-Path $package "README.txt") -Force
Compress-Archive -Path "$package\*" -DestinationPath "C:\path\to\output\ZdbrusBot.zip" -Force
```

Verify that it contains (zip entries on Windows use backslashes):

- `Mods\ZdbrusBot.dll`
- `MelonLoader\net6\Il2CppInterop.Common.dll`
- `MelonLoader\net6\Il2CppInterop.Runtime.dll`
- `MelonLoader\net6\Il2CppInterop.HarmonySupport.dll`
- `README.txt` describing the current release

Those three Interop DLLs are intentionally bundled: the current game can close during startup when they are missing. Do not replace them with arbitrary copies from a different MelonLoader build.

## GitHub release

1. Set `Brand.Version` in `src\Core\Brand.cs` and add a dated section for that version at the top of `CHANGELOG.md`.
2. Commit and push the verified source changes. The repository has no bot build/release workflow; the binary zip must be built and attached manually.
3. Create a GitHub **draft release** with a matching `v<version>` tag, based on the commit containing the release source. Upload the tested package as `ZdbrusBot.zip`.
4. Before publishing, verify the release page reports the correct tag and attached asset, download the asset once, and inspect its four required DLL paths above. Keep the release as a draft until that check is complete; **Publish release** is the final public step.

## Crash-prevention notes

- Keep to stable MelonLoader 0.7.3. Nightly builds have crashed the current game.
- Keep the three bundled Il2CppInterop files in the download. Their absence has caused the game to close immediately after loading.
- The automatic update check is disabled; its background web request was suspected of crashing the game. Do not re-enable it without isolating and testing the cause.
- Do not move Unity or Il2Cpp work onto a background thread. The planner server and roster reads are deliberately serviced from the game's main-thread `Tick`; a background web thread previously crashed the game.
- A closed-menu screen-map path is not by itself a failure; log it once instead of repeatedly filling the log. See the 0.7.7 changelog entry.
