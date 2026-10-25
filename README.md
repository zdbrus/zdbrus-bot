<div align="center">

<img src="assets/logo.png" alt="Zdbrus Bot logo" width="180">

# Zdbrus Bot

### Free auto-play bot for **Firestone: Idle Clicker Online RPG** (Steam / Epic)

Runs inside the game as a MelonLoader mod. No pixel clicking, so screen size and resolution never matter.

[![Download](https://img.shields.io/badge/DOWNLOAD-latest%20release-orange?style=for-the-badge&logo=windows)](https://github.com/zdbrus/zdbrus-bot/releases/latest/download/ZdbrusBot.zip)

[![Downloads](https://img.shields.io/github/downloads/zdbrus/zdbrus-bot/total?label=downloads&color=success)](https://github.com/zdbrus/zdbrus-bot/releases)
[![Latest release](https://img.shields.io/github/v/release/zdbrus/zdbrus-bot?label=version)](https://github.com/zdbrus/zdbrus-bot/releases/latest)
[![Stars](https://img.shields.io/github/stars/zdbrus/zdbrus-bot?style=flat&color=yellow)](https://github.com/zdbrus/zdbrus-bot/stargazers)
[![Game version](https://img.shields.io/badge/Firestone-9.1.3a-red)](#requirements)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

**[Website](https://zdbrus.github.io/zdbrus-bot/)** · **[Install guide](#install-in-4-steps)** · **[Features](#what-it-does)** · **[FAQ](#faq)** · **[Report a bug](https://github.com/zdbrus/zdbrus-bot/issues/new/choose)**

</div>

---

## Install in 4 steps

1. **Install MelonLoader 0.7.3 or newer** on Firestone using the official installer: [MelonLoader releases](https://github.com/LavaGang/MelonLoader/releases/latest). Point it at `Firestone.exe`.
   (Steam: right-click Firestone > Manage > Browse local files to find the folder.)
2. **Start the game once and close it.** MelonLoader finishes setting itself up on the first run.
3. **Download [ZdbrusBot.zip](https://github.com/zdbrus/zdbrus-bot/releases/latest/download/ZdbrusBot.zip)** and extract it into the Firestone folder (the one with `Firestone.exe`). It contains `Mods\ZdbrusBot.dll`.
4. **Start the game**, press **F1** to pick which features to turn on, then press **F7** to start the bot.

> Coming from Firebot? Delete `Mods\firebot.dll` so only one bot runs. Your old settings are moved over automatically.

## Keys

<img src="assets/panel.jpg" alt="Zdbrus Bot settings panel in game" width="420" align="right">


| Key | Action |
|-----|--------|
| **F1** | Settings panel (turn features on or off, set priorities) |
| **F2** | Task status (what runs next and when) |
| **F7** | Start / stop the bot |
| **F6** | Auto upgrade heroes on / off |
| **F8** | Auto skills on / off |
| **F9** | Save a screen map (attach it to bug reports) |

## What it does

Every feature can be switched on or off in the F1 panel.

| Area | Automated |
|------|-----------|
| **Daily** | Daily rewards, check-in, free shop items, mail rewards, daily and weekly quests, battle pass free rewards |
| **Town** | Firestone research (your priority order), meteorite research, alchemist, Oracle rituals and blessings, engineer tools, guardian training and enlightenment, exotic merchant (sell, upgrades, emblem chests), tavern (beer to tokens, cards), Scarab's game, Pharaoh's vault and milestones |
| **Guild** | Expeditions, free pickaxes, guild crystal, Chaos Rift (hits, eclipse stones, tomes, holy upgrades for the guardians you choose), personal tree, hero awakening, forbidden knowledge |
| **Map** | Map missions (claims, free speedups, skips when every squad is busy), Warfront loot, Liberation missions (keeps going after each win) |
| **Battle** | Arena (picks the easiest opponent, uses the free refresh, tries again later), war machine levels and blueprints for the machine you pick (all 13 supported), clicks coal miners and double-beer dragons the moment they appear |
| **Events** | Event challenge rewards, seasonal exchange shops (e.g. pumpkins for pickaxes and chests) |
| **Optional** | Temple prestige once the gold boost reaches the percent you choose (off by default) |

It also reads the notification bubbles on the left of the screen, so it reacts the moment something is ready instead of waiting for a timer.

## Safety

- **Never spends gems or real money.** Every purchase is skipped unless the game shows the button as available **and** you have enough of the exact currency it costs.
- If the game ever shows "not enough currency", the bot closes it and stops buying.
- It only presses buttons a player could press. It does not change game files or talk to the game servers.
- Open source: every line is in [`src/`](src).

> Using bots may be against the game's terms of service. Use at your own risk.

## Requirements

- Windows, Firestone from Steam or Epic, game version **9.1.3a** (the bot is updated when the game updates).
- MelonLoader **0.7.3 or newer** (older versions will not load it).
- In the game settings, under **Gameplay**, set **Adventure buttons style** to **PC (Legacy)**.

## FAQ

**The bot doesn't start.** Check `MelonLoader\Latest.log` in the game folder for a line saying `Zdbrus Bot ready`. If it isn't there, MelonLoader isn't installed correctly or the DLL isn't in `Mods`.

**Do I need every war machine or guardian for it to work?** No. It works with whatever you own and skips what you don't have.

**Something is clicked wrong or a feature is missing.** Press **F9** on that screen and [open an issue](https://github.com/zdbrus/zdbrus-bot/issues/new/choose) with the file from `UserData\screenmaps` and your `MelonLoader\Latest.log`.

**Where are my settings?** `UserData\ZdbrusBotPreferences.cfg`. The F1 panel writes to it for you. See [ADVANCED.md](ADVANCED.md) for every option.

**The game updated and the bot broke.** Watch or star this repo to get notified of the fix release.

## Credits

Zdbrus Bot is built on [Firebot](https://github.com/danilogmoura/fire-bot) by danilogmoura (MIT license). Thanks to the MelonLoader team.

<sub>Keywords: Firestone bot, Firestone Idle RPG bot, Firestone auto clicker, Firestone mod, Firestone automation, Firestone script, MelonLoader mod, idle game bot.</sub>
