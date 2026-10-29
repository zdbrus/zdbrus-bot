ZDBRUS BOT 0.7.21 - auto-play mod for Firestone: Idle Clicker Online RPG
Website: https://zdbrus.github.io/zdbrus-bot/
Downloads and release notes: https://github.com/zdbrus/zdbrus-bot/releases
Based on Firebot by danilogmoura (MIT).

INSTALL
1. Install stable MelonLoader 0.7.3. Nightly builds have crashed the current game.
2. Launch Firestone once through Steam or Epic, then close it.
3. Extract this whole zip into the folder containing Firestone.exe and replace files.
   Keep the three Il2CppInterop DLLs in MelonLoader\net6; without them the game can close at startup.
4. Start Firestone through Steam or Epic. If you used Firebot before, remove Mods\firebot.dll.

FEATURES
See the release notes for the full list. Highlights include notification-bubble tasks, daily rewards,
research and town automation, map missions, arena, talents, boss retries, and the F4 War Machine Planner.
For bubbles without a dedicated task, the bot looks for free claim/collect buttons on activated screens.
Its log distinguishes button clicks and later button state changes from independently verified rewards. Upgrades, missions, rituals, training, vault and research actions also stop when no visible change follows a click.
Upgrade tasks skip preview scans when the relevant currency is at or below its reserve, verify level,
cost, currency or inventory changes after purchases, and stop when progress cannot be confirmed.
Reward logs report observed state changes without claiming a reward was granted unless the game
exposes a reliable receipt.
Temple prestige triggers on a stalled run, or once the gold boost grows slower than 75% of this run's own average rate for two checks in a row (adjustable, 0 = off); it never uses the paid 2x.
The planner can auto-apply a stronger squad on a schedule when enabled; it is off by default.
The planner's read-only upgrade advisor ranks machine levels and blueprints by power gained per resource.

KEYS
F1 settings   F2 status   F4 planner   F6 auto-upgrade   F7 bot on/off   F8 auto-skill   F9 screen map

TROUBLESHOOTING
If the game closes during startup, verify the three bundled Il2CppInterop files are present and use
stable MelonLoader 0.7.3. Attach MelonLoader\Latest.log to a bug report if the bot fails.
The automatic update check remains disabled because its background request was suspected of crashing the game.

SAFETY
The bot skips purchases unless the game shows an available button and enough of the exact currency.
It never spends gems or real money. Using bots may break the game's terms of service; use at your own risk.
