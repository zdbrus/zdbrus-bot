# Zdbrus Bot: Advanced Guide

This file covers everything that is not needed for normal use: manual configuration, recovery methods, and building from source.

For installation, quick start, and everyday usage, see the main [README](README.md).

---

## Configuration File Reference

Settings live in `Firestone/UserData/ZdbrusBotPreferences.cfg`, and the in-game panel (`F1`) writes to that same file.

> **Disclaimer:** the panel is the recommended way to change any setting. Editing the file by hand is supported, but do it with the game **closed** — the panel rewrites the file whenever a setting changes, so a change made while the game is running overwrites what you typed outside.

### When to edit the file

The panel covers everyday cases. Opening the file is worth it when you want to:

- keep your settings under version control, or copy them to another machine;
- configure Zdbrus Bot before its first launch;
- recover a setting by hand when the game does not open.

The file is read on the next launch, so a change takes effect only when you reopen the game.

### Format

```toml
[firebot_settings]      # global settings
shortcut_key = "F7"     # bot on/off hotkey; "None" frees the key

[alchemist]             # one section per task
enabled = true          # false = the task is skipped in the execution loop
resource_type = "0,1"   # comma-separated ids; empty = the task's default
```

- **The file documents itself.** The description of every option is written as a comment right above it, examples included, so the keys and their accepted values are always one line away and always current. The panel shows the short version of that same text, because the box at the bottom of the window holds about three lines — what does not fit there stays in the file.
- **Section names differ between the two surfaces.** The panel names a section after the feature it belongs to (`General`, `AutoSkill`, `Alchemist`), while the file keeps the identifier it always had (`[firebot_settings]`, `[auto_skill]`, `[alchemist]`). Renaming a section in the panel never renames it in the file, so an existing configuration keeps working.
- **A missing entry is added on the next launch**, which is how an older file keeps working after an update. There is no need to delete the file to get the new options.
- **An invalid value is never fatal.** The option is dropped and the task falls back to its default; the task that needs a value to know what to do, such as the alchemist resources, disables itself rather than guessing.
- **A task can be enabled and still not run.** Features the game only offers later are held back until then — Engineer tools and Free Pickaxes at level 50, Warfront Campaign at level 50, Alchemist at 120, Oracle at 200 — and everything else is available from level 1. This is a runtime condition and not a setting: the bot never rewrites your `enabled`, the task's own row on the status screen shows the level it is waiting for (`Locked (level)`), and it resumes by itself when the level arrives.

---

## Recovery Methods

Use these **only** if the game changes version and Zdbrus Bot stops working. If Zdbrus Bot is working normally, do not run any recovery method.

### Before You Start

1. Close the game completely.
2. Confirm your MelonLoader version is the stable **V0.7.3**. Nightly builds have crashed the current game. If you are not sure which version is installed, follow Method 2 (clean reinstall).
3. Back up `UserData/ZdbrusBotPreferences.cfg` if you customized it. Neither method below touches this file.
4. In the `Mods` folder, keep only one `firebot.dll`. Delete old or duplicated copies from previous downloads (for example `firebot (1).dll` or `firebot.old.dll`).
5. Optional: delete `MelonLoader/Latest.log` so the next launch generates a clean log for troubleshooting.

### Method 1: Assembly Cache Cleanup

Starting from a closed game:

1. Delete all contents inside `MelonLoader/Il2CppAssemblies`.
2. In `MelonLoader/Dependencies/Il2CppAssemblyGenerator`, keep only:
   - `Il2CppAssemblyGenerator.deps.json`
   - `Il2CppAssemblyGenerator.dll`
3. Delete `MelonLoader/Dependencies/AssemblyUnhollower` (if it exists).
4. Start the game again.

### Method 2: Reinstall MelonLoader

Starting from a closed game:

1. Delete the `MelonLoader` folder from the game root. This removes the old MelonLoader version, its generated assemblies and its logs.
2. Reinstall stable MelonLoader **V0.7.3** as described in the main README. Do not enable nightly builds.
3. After reinstalling, delete `MelonLoader/Dependencies/AssemblyUnhollower` if it exists (leftover from older MelonLoader versions).
4. Start the game again.

> **Important:** no matter which method you choose, always replace only `Mods/ZdbrusBot.dll` to update Zdbrus Bot.

---

## Finding your Firestone Folder

MelonLoader, the Zdbrus Bot zip and the `Mods/ZdbrusBot.dll` of an update all go to the folder that contains `Firestone.exe`. To open it without hunting through the disk:

- **Steam:** right-click Firestone in your library → **Manage** → **Browse local files**.
- **Epic Games:** in the **Library**, click the `...` on the Firestone card → **Manage** → **Open install location**.

The folder you want is the one containing `Firestone.exe`: that is where the MelonLoader installer points and where the Zdbrus Bot zip is extracted.

---

## Installation (From Source)

1. Clone the repository:

   ```bash
   git clone https://github.com/zdbrus/zdbrus-bot.git
   ```

2. Navigate to the project directory:

   ```bash
   cd zdbrus-bot
   ```

3. Install stable MelonLoader 0.7.3, launch the game once, then close it so `MelonLoader\Il2CppAssemblies` is generated.
4. Build the project with the folder containing `Firestone.exe`:

   ```powershell
   dotnet build src -c Release -p:GameRoot="C:\Program Files (x86)\Steam\steamapps\common\Firestone"
   ```

   See [RELEASING.md](RELEASING.md) for staging builds, package contents, and the draft-release checklist.

---

## Contributing

Contributions are welcome! Please submit a pull request or open an issue for suggestions or improvements.

---

## Bug Reporting & Feature Requests

Found a bug or have an idea for a new feature? Please open a ticket on our GitHub Issue Tracker.

**Before submitting a bug report:**

1. Check if the issue has already been reported.
2. Ensure you are using the latest version of Zdbrus Bot.
3. Attach your **MelonLoader log** file (`Firestone/MelonLoader/Latest.log`) if the game crashed or the bot failed.

[**Open a New Issue**](https://github.com/danilogmoura/firestone-bot/issues/new/choose)
