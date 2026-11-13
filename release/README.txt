ZDBRUS BOT 0.7.61 - auto-play mod for Firestone: Idle Clicker Online RPG
Website: https://zdbrus.github.io/zdbrus-bot/
Downloads and release notes: https://github.com/zdbrus/zdbrus-bot/releases
Discord (help, bugs, suggestions): https://discord.gg/U4APWwTa7F
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
exposes a reliable receipt. Liberation Missions can run Easy (1 star), Normal (2 stars), or both.
Battle Pass skips scrolling when its rewards marker is present but hidden.
Mystery Box claims are retried when the offer or claim cannot be confirmed. Boxes are opened only
when their amount is readable and the opening control shows no price; otherwise they are kept.
Free Pickaxes waits at least ten minutes before retrying an unreadable quantity or an unconfirmed claim
even if its notification remains lit or the game restarts. Notification cooldowns and separate
Chaos Rift and Mystery Box retries also continue after a restart. Like other task schedules, these
waits are shared across accounts on the same installation, so switching accounts can carry one over.
F1 > Prestige shows the last decision and controls. Growth efficiency is the primary free-prestige
trigger: two hourly samples below the configured share of average growth can trigger without waiting
for a timer (75% by default; 0 disables growth). If it does not trigger, repeated boss attempts at one
stage can trigger next (3 by default; choose 2–5); the no-progress fallback is selectable at 30 minutes
or 1 hour. All triggers require the minimum boost and run age. The growth rule is an estimate; unreadable
stage/boost values or a run age the bot cannot read (including the game's minutes:seconds clock) stop
the reset. A +0% boost is a valid zero sample and is checked hourly instead of reopening the Temple on
every task check. Stage checks use game data, so opening the Temple and
hiding the battle HUD does not cancel a valid reset. The paid 2x is never used. The fallback timer starts
fresh for untracked runs. Similar-age account switches cannot always be detected; turn prestige off when
switching accounts until you have checked its settings.
Only the free-empower confirmation opened by the bot is confirmed; existing or unrecognized prompts
are left untouched. After a confirmed reset, the bot waits briefly for the completion screen and
presses Start now when available.
Scarab uses Noble's tokens by default. Turn on "Use Pharaoh's Tokens" to allow those tokens too.
It uses Auto at x1, guards each spin's token permission and cost, and never buys tokens or spends gems.
F1 > Guild > Awaken Heroes > Awaken Amount offers x1, x2, x5, x10 and x20 (default x1).
Awakening uses Auto for every affordable selected batch, without dropping to a smaller amount or
buying crystals. Both Auto activities turn off when finished or when the bot stops. Large balances
may be split across visits to respect Max Task Runtime.
Compatible town and guild tasks reuse their hub. Popup cleanup remains, and the bot returns to
battle for battle tasks and idle upgrade windows. AutoUpgrade is a separate worker, not the last task.
With Balance selected, it uses gaps between tasks even when the next check is less than 30 seconds away,
then finishes its short pass before handing over. Ready and running tasks still take priority;
Focus still pauses as soon as a task is approaching.
Numbered priority chips keep your chosen order: tap to remove an item, then tap it to add it last.
Clear empties a list; Recommended puts back the guide order (Battle cry and Fate first for talents).
Upgrade buyers hold for an unaffordable higher tier rather than spending on lower tiers; level-locked
Exotic upgrades are skipped until unlocked. Previously unknown preview-based names may be read once,
then remembered between launches. Prices and completion state are not persisted.
Talents and Meteorite Research read the live tree directly and open only eligible priority previews.
Meteorite Research's "Meteorites To Keep" is the minimum left after each purchase, not a target to
reach before spending. It buys at x1 using the game's actual balance and cost, rechecks the latest
reserve before each purchase, and waits for both the level and exact debit before buying again.
Changed or unreadable values stop spending. Raising the reserve cannot undo an already-sent purchase.
For hero gold and guardian holy upgrades, Balance tries all selected targets in order; Focus first
saves for the first available target and can leave lower priorities waiting indefinitely.
When War Machine Blueprints is set to All, separate priority chips order damage, health and armor.
Auto-build squad picks machines and crews together, so a machine can make the squad because of the heroes
that boost it. It includes a tank and a healer whenever you own them, in both Campaign and Arena.
The planner can auto-apply a stronger squad on a schedule when enabled; it is off by default.
The planner's read-only upgrade advisor shows the best machine to level and the best blueprint to buy, and
checks each machine's own screws, cogs and metal. Hover over planner numbers (or tap ?) for explanations.
The planner's tier list ranks every war machine S to C for long-term investment and marks a goal squad.
F4 has a Void Crystal advisor for hero gear. It automatically compares your roster; choosing one hero
is optional. Pick damage, health, armor, gold or gear power. It uses the game's current gear, next
boost and price, and estimates party gains using the heroes currently fighting. Gear on benched heroes
counts when it helps everyone; gear power also considers their personal gear. Gear power helps unlock
tiers and missions, but is not hero damage. These are next-upgrade estimates, not a combat simulation
or a promise of the best entire budget plan. Enter a budget and confirm Spend for me to buy the best
affordable next upgrade, re-checking before each enchant. Stop spending cancels further buys; a pending
enchant may still finish. Each request stops after 40 enchants or an uncertain result. It never buys
crystals, unlocks tiers or spends other currencies. Void Crystals improve heroes, not war machines.
Crystal and shard spending now use the correct unlocked hero's button, fixing the black screen and
locked-hero selections. They wait for the hero to load and stop if the selection cannot be confirmed.

WAR MACHINE TIPS
Full guide: https://github.com/zdbrus/zdbrus-bot/blob/v0.7.60/WAR-MACHINES.md
- Only jewel chests (Wooden, Iron, Golden, Diamond and up) give war machine parts and the conduits that
  unlock new machines. Gear chests (Common to Mythic) don't.
- You can't pick which machine unlocks next. Machines 1-2 are single-target attackers, 3 is a tank,
  4 hits several enemies, 5 is a healer, and the rest are random.
- Campaign: level one main machine and never stop. Blueprints: damage first, then health.
- Spend blueprints on S and A tier machines; go easy on B and C, since blueprints are lost if you replace them.
- Levels and rarity on any machine help them all, so they're never wasted.
- Never buy Opal chests with expedition tokens; buy Diamond chests and transmute them.
- Chest timing trick (3-5 machines): save Iron and Golden chests and open 50 just before the next machine
  unlocks, so the parts go to machines you already have. Untick Open Wooden, Iron and Golden Chests in
  F1 > Daily > Open Chests while you do this, or the bot opens them every 20 minutes.

ARENA
Arena picks the lowest readable opponent power within your F1 power limit and rechecks the opponent's
name, rank and power plus your squad power in the preview. Power is not a win prediction. Unreadable or
changed values and unconfirmed fights stop the attempt. If One Fight Per Day is enabled, after 20:00
local time it may attempt the weakest opponent once above the limit when all three opponents are
readable and refresh is unavailable; an unconfirmed attempt still counts for that day.
The configured Arena squad must be verified. If it is confirmed empty, the bot warns once and fights
with your current squad instead. If the squad cannot be read, the bot skips the fight rather than
guessing. The bot allows time to restore the previous squad and logs a failure, but stopping the bot,
changing accounts or closing the game mid-task can interrupt restoration. Check the active squad
afterward. If the runtime is too short to switch, raise F1 > General > Max Task Runtime.

MATCHUP RECORDING
F1 > Battle > Arena > Record Arena Matchups is off by default, even with Enable All On Launch.
When enabled, confirmed bot-started wins and losses are appended locally to
UserData\ZdbrusBotArenaMatchups.jsonl without player names or account IDs. Manual fights and
unconfirmed results are not recorded. Missing values remain null; preview stats are not yet a
validated win prediction. Recording does not change fights. The file grows until you delete it.

KEYS
F1 settings   F2 status (click a task's ON/OFF switch)   F4 planner   F6 auto-upgrade   F7 bot on/off   F8 auto-skill   F9 screen map

STARTUP AND ACCOUNT SWITCHES
F1 > General > Enable All On Launch is off by default. Opt in to turn on Bot Auto Start,
AutoUpgrade, AutoSkill and every task's Enable switch at each fresh game launch. Priorities,
reserves, combos and optional spending settings are not changed. Check prestige and spending
settings before opting in. Manual off choices survive account switches for the current process;
the next game launch turns the switches back on.
AutoUpgrade and AutoSkill rebuild their workers when the gameplay scene reloads. F6/F8 and
their F1 Enable switches control the same state. A worker error disables its switch and logs
the reason; enable it once or press its hotkey to retry. An empty skill combo cannot start.

TROUBLESHOOTING
If the game closes during startup, verify the three bundled Il2CppInterop files are present and use
stable MelonLoader 0.7.3. Attach MelonLoader\Latest.log to a bug report if the bot fails.
With F1 > General > Auto Update enabled, the F1 notice checks for newer releases. Close Firestone,
wait for UserData\ZdbrusBot_update.log to report the result, then reopen. The separate updater skips
installation if the game restarts too early. The old DLL is kept as a .bak. If Auto Update is off
or the check fails, use the Releases button to download the update and install it while the game is closed.

SAFETY
The bot skips purchases unless the game shows an available button and enough of the exact currency.
It never spends gems or real money. Using bots may break the game's terms of service; use at your own risk.
