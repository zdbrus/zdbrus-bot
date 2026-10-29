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
3. **Download [ZdbrusBot.zip](https://github.com/zdbrus/zdbrus-bot/releases/latest/download/ZdbrusBot.zip)** and extract all of it into the Firestone folder (the one with `Firestone.exe`), replacing files when asked. It contains `Mods\ZdbrusBot.dll` plus three fixed MelonLoader files (`MelonLoader\net6\Il2CppInterop.*.dll`) that the current game needs.
4. **Start the game.** The bot starts by itself (Auto Start). Press **F1** to pick which features to turn on, and **F7** to stop or start it.

> Coming from Firebot? Delete `Mods\firebot.dll` so only one bot runs. Your old settings are moved over automatically.

## Keys

<img src="assets/panel.jpg" alt="Zdbrus Bot settings panel in game, Battle tab" width="380" align="right">


| Key | Action |
|-----|--------|
| **F1** | Settings panel (turn features on or off, set priorities) |
| **F2** | Task status (what runs next and when) |
| **F4** | War Machine Planner (opens in your browser) |
| **F6** | Hero auto-upgrade on / off (on by default: every minute it opens upgrades, buys to the next milestone or Max, and closes the menu again) |
| **F7** | Start / stop the bot |
| **F8** | Auto skills on / off |
| **F9** | Save a screen map (attach it to bug reports) |

## What it does

Every feature can be switched on or off in the F1 panel.

| Area | Automated |
|------|-----------|
| **Daily** | Daily rewards, check-in, free shop items, mail rewards, daily and weekly quests, battle pass free rewards, opens your chests (keeps the ones you choose) |
| **Town** | Firestone research (your priority order), meteorite research, alchemist, Oracle rituals and blessings, engineer tools, guardian training and enlightenment, exotic merchant (sell, upgrades, emblem chests), tavern (beer to tokens, cards), Scarab's game, Pharaoh's vault and milestones |
| **Guild** | Expeditions, free pickaxes, guild crystal, Chaos Rift (hits, eclipse stones, tomes, holy upgrades for the guardians you choose), personal tree, hero awakening, forbidden knowledge |
| **Map** | Map missions (claims, free speedups, skips when every squad is busy), Warfront loot, Liberation missions (keeps going after each win) |
| **Battle** | Arena (picks the easiest opponent, uses the free refresh, tries again later), war machine levels and blueprints for any machines you pick or your active squad, switches to your arena squad for arena fights and back, clicks coal miners and double-beer dragons the moment they appear |
| **Events** | Event challenge rewards, seasonal exchange shops (e.g. pumpkins for pickaxes and chests) |
| **Character** | Spends talent points the moment you earn them (and saves them) |
| **Progress** | Retries the stage boss after a lost fight so the run keeps climbing |
| **Prestige** | Temple prestige once your run stalls (no new highest stage for 3 hours, adjustable), never the paid 2x |

### War Machine Planner (F4)

Press **F4** in game and your browser opens the planner at `http://localhost:5577`. It reads your real war machines, crews, heroes and jewels from the game every few seconds.

- **Auto-build squad** picks your best 5 machines and crews for the campaign or the arena (each hero once, always a tank).
- **Drag heroes** between machines; the bench shows how much power each hero would add to the selected machine.
- **Game check** compares every number with the game's own, so you can trust the math.
- **Campaign target** shows the power a mission needs and the furthest mission your squad covers.
- **Apply to game** sets your planned squad in one click: the bot goes through Engineer, Squads, Edit and Select crew like a player would, saves, and confirms what stuck.
- **Squad 1, 2 or 3**: pick which of your squads Apply writes to. Campaign and arena remember their own (campaign squad 1, arena squad 2). Set **F1 > Battle > Arena > Arena Squad** to 2 and the bot switches to your arena squad for arena fights and back afterwards.
- Only heroes you have unlocked are used. It only saves when the squad on screen matches the plan, so a half-done change never replaces a working squad.
- **Hero faces** come from your own game (open Engineer > Select crew once).
- **Plan upgrades** by editing levels, rarities, blueprints or jewels.
- **Ethereal shard advisor** ranks the next jewel enchant for every hero in your squad by war machine power per shard. **Spend for me** does those enchants for you within a shard budget.

<img src="assets/planner.jpg" alt="War Machine Planner in the browser" width="100%">

### Works from the game's own signals

Tasks with a bubble or bell icon (Oracle, quests, mail and the rest) only run when that icon shows, so the bot does not open screens just to look. If a task's screen is already open, it carries on from there instead of walking back from town. Campaign, boss retries and chests run on their own timers.

## Safety

- **Never spends gems or real money.** Every purchase is skipped unless the game shows the button as available **and** you have enough of the exact currency it costs.
- If the game ever shows "not enough currency", the bot closes it and stops buying.
- It only presses buttons a player could press. It does not change game files or talk to the game servers.
- Open source: every line is in [`src/`](src).

> Using bots may be against the game's terms of service. Use at your own risk.

## Requirements

- Windows, Firestone from Steam or Epic, game version **9.1.3a** (the bot is updated when the game updates).
- MelonLoader **0.7.3** (newer nightly builds crash the current game).
- In the game settings, under **Gameplay**, set **Adventure buttons style** to **PC (Legacy)**.

## FAQ

**The game closes right after loading.** The three MelonLoader files from the zip are missing, usually because MelonLoader was reinstalled or updated. Extract ZdbrusBot.zip into the game folder again and replace files. Use MelonLoader 0.7.3 with "Enable Nightly builds" off.

**The bot doesn't start.** Check `MelonLoader\Latest.log` in the game folder for a line saying `Zdbrus Bot ready`. If it isn't there, MelonLoader isn't installed correctly or the DLL isn't in `Mods`.

**Do I need every war machine or guardian for it to work?** No. It works with whatever you own and skips what you don't have.

**Something is clicked wrong or a feature is missing.** Press **F9** on that screen and [open an issue](https://github.com/zdbrus/zdbrus-bot/issues/new/choose) with the file from `UserData\screenmaps` and your `MelonLoader\Latest.log`.

**Where are my settings?** `UserData\ZdbrusBotPreferences.cfg`. The F1 panel writes to it for you. See [ADVANCED.md](ADVANCED.md) for every option.

**The game updated and the bot broke.** Check [Releases](https://github.com/zdbrus/zdbrus-bot/releases) for a newer version. Watch or star this repo to get notified when one comes out.

**Nothing happens / no tasks run.** The bot is stopped. Press F7, or turn on F1 > Bot > Auto Start. The log says so when it is off.

## Credits

Zdbrus Bot stands on the work of two earlier Firestone bots. Thank you to both authors for making their code public.

- **[Firebot](https://github.com/danilogmoura/fire-bot)** by danilogmoura (MIT license). Zdbrus Bot is built on Firebot's code: the MelonLoader mod framework, task system, settings panel and many original tasks come from it.
- **[Deaeth85 Firestone Bot](https://github.com/Mendenj85/Deaeth85-Firestone-Bot)** by Mendenj85. An AutoHotkey bot whose feature list and game routines helped shape what Zdbrus Bot automates. No code was copied from it; Zdbrus Bot works inside the game instead of clicking the screen.

Thanks also to the [MelonLoader](https://github.com/LavaGang/MelonLoader) team.

<sub>Keywords: Firestone bot, Firestone Idle RPG bot, Firestone auto clicker, Firestone mod, Firestone automation, Firestone script, MelonLoader mod, idle game bot.</sub>
