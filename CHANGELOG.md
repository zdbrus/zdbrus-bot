# Changelog

## 0.7.39 (2026-10-30)
- Added opt-in General > Enable All On Launch (off by default). At each fresh game launch it enables Bot Auto Start, AutoUpgrade, AutoSkill and every task's Enable switch without changing priorities, reserves, combos or optional spending settings. Manual off choices remain off for the current process, including account switches.
- AutoUpgrade and AutoSkill stop on gameplay-scene unload and rebuild workers and cached targets when gameplay returns. Bot manual stop also survives scene/account transitions. Unrelated additive scenes do not interrupt automation.
- F6/F8 and F1 Enable switches now share the same desired state. Enabled AutoSkill starts automatically when gameplay is ready. Empty combos and worker failures turn the setting off with a warning instead of displaying a false on state or restarting every frame.
- Coroutine guarding now catches nested iterator and Current-getter exceptions, disposes nested iterators, and reports failure once. Scene changes cancel active upgrade holds and pending menu cleanup.
- Regression coverage exercises launch overrides, real Main scene callbacks, manual-off preservation, F1/hotkey reconciliation, nested failures, cancellation and pointer release. These fixes cover concrete failure paths; an account switch that does not reload the gameplay scene has not been live-verified.

## 0.7.38 (2026-10-30)
- F1 has a scrolling update notice and Releases button. When Auto Update is on, a separate helper checks for newer releases; the notice explains when to close the game, wait for the updater, and reopen. The installer waits for the game to exit and skips the swap if the game was restarted too early.
- Prestige has its own F1 tab with the last decision and run reading, exact boost and growth-threshold inputs, and clear disabled/stopped states. Its scheduled check has priority over ordinary tasks.
- Prestige checks only the observed free empower control, rechecks the run before resetting, supports the game's day/hour/minute/second run timer, rejects unreadable boost or run-age values, and starts fresh for an untracked or discontinuous run. Similar-age account switches cannot always be detected. The growth-rate rule remains an adjustable estimate, not a guaranteed optimum; the paid 2x option is never used.
- Priority chips now preserve and display the order you choose. Meteorite Research, Personal Tree, Forbidden Knowledge, Talents, Oracle Blessings, Exotic Merchant and War Machines fill available higher tiers before lower tiers; Event Exchange follows its selected order. A purchase cap or an unaffordable higher tier no longer causes those buyers to spend on a lower tier.
- AutoUpgrade and guardian holy-damage upgrades now offer separate **Balance** and **Focus first** strategies for their uncapped choices. Balance remains the default; Focus first saves for the highest available selected slot or guardian. Both use the chosen order.
- War-machine blueprint types can be ordered when upgrading all types. The bot opens the relevant workshop or blueprint tab before reading its corresponding currency, and keeps independent retry decisions for the two currencies.
- The Exotic Merchant reads upgrade names and levels from the game's upgrade components and skips level-locked entries. Other preview-based trees remember discovered names across launches, and verify an already-open preview still matches before spending; prices and completion state are not persisted.
- Firestone Research selects its documented one-based priority IDs and verifies preview and research-slot changes, including multiple open slots.
- Task status and persisted schedules now use the actual due time for bubble-based fallback checks. Map Missions, research, Alchemist, Engineer and Warfront use direct navigation where a bubble does not open the required screen; unreadable counters and timers are retried instead of being treated as success.
- Map Missions waits up to four seconds for the free-squad counter. AutoUpgrade reports common reasons for idle cycles and sends a balanced pointer-down, pointer-up and click sequence, including releasing a held button when stopped. These changes address observed code paths, not a confirmed diagnosis of issue #3's current report.

## 0.7.37 (2026-10-29)
- Community Discord server is live: https://discord.gg/U4APWwTa7F (announcements, install help, bug reports, suggestions). Linked from the website, README and release readme. No code changes.

## 0.7.36 (2026-10-29)
- Auto Update (F1 > General, on by default): when the game closes, a hidden helper downloads the newest GitHub release and swaps Mods\ZdbrusBot.dll; the new version loads on the next launch. No web code runs inside the game. The old DLL is kept as ZdbrusBot.dll.bak and the result is logged to UserData\ZdbrusBot_update.log. Everyone must install 0.7.36 by hand once; after that updates are automatic.

## 0.7.35 (2026-10-29)
- Mail: with 'Delete After Claim' on, mails that have nothing to claim (event announcements, already-claimed ones) are now deleted too. Before, the first such mail stopped the run and the mailbox was opened again every 30 minutes for nothing. A mail whose claim failed is kept. If a run claims and deletes nothing, the next one waits 3 hours.

## 0.7.34 (2026-10-29)
- Every task: a lit bubble that a run could not clear now backs off 3, 6, 12, 24, then 60 minutes instead of re-running every 3 minutes all day. The count resets when the bubble clears.

## 0.7.33 (2026-10-29)
- Arena only switches squads when there are battle tokens to spend, and runs all its fights under one switch instead of switching to the arena squad and back for every fight.

## 0.7.32 (2026-10-29)
- The 35-line task table is now printed at most once every 10 minutes instead of after every task (Boss Retry alone printed it every minute).

## 0.7.31 (2026-10-29)
- Every bot click now closes the 'locked guardian' and 'locked war machine' info popups the moment they appear, so no task (Chaos Rift holy upgrades, War Machine, any tile loop) can be blocked by one. Before, only Chaos Rift closed one, and only after the task ended.

## 0.7.30 (2026-10-29)
- Chaos Rift: confirmed live that Tomes of Power and holy upgrades work. Fixed the locked-guardian popup (Reach level 50 at the personal tree of life) staying open: the holy-upgrade loop clicked every guardian tile and never closed that popup for locked guardians. It now closes it and moves on.

## 0.7.29 (2026-10-29)
- Oracle Blessings no longer opens every blessing when you cannot afford it: unaffordable (or maxed) blessings are skipped for 6 hours, and the Oracle is not opened at all when all of them are skipped.
- Removed the once-a-second 'lvlUpButton/buyButton' debug log line.

## 0.7.28 (2026-10-29)
- Open Chests now closes the 'New items' (Equip) popup that gear drops leave open. Confirmed live in 0.7.27: all 10 chest types opened, but that popup stayed on screen.

## 0.7.27 (2026-10-29)
- Forbidden Knowledge and Talents priority chips now list every upgrade/talent name from the Firestone wiki (all three dark gods; all 46 talent tiers), not just the old defaults.

## 0.7.26 (2026-10-29)
- Settings: the typed lists (Event Exchange items, Personal Tree, Forbidden Knowledge, Talents and Meteorite Research priorities, and which Bubbles to open) are now tick-box chips in the panel instead of text. Ticked items are saved in the list's fixed order (the default priority order). Firestone Research priority stays typed because its order is yours to choose.

## 0.7.25 (2026-10-29)
- Open Chests: the 'Chests To Keep' text box is replaced by one checkbox per chest type (Common, Uncommon, Rare, Epic, Legendary, Wooden, Iron, Golden, Comet, Lunar), all ticked by default. Each is tied to the chest tile's game id, not its displayed name. Mystery boxes and Oracle gifts are never opened. Your old 'skip' setting is no longer used.

## 0.7.24 (2026-10-29)
- Open Chests: the chest tiles never opened their preview from the Button callback (logged 'Clicking Common did not open its preview' on every run). If the button click shows nothing, it now sends a real pointer down/up/click to the tile. Retries every 20 minutes instead of 2 hours. Not yet live-verified.

## 0.7.23 (2026-10-29)
- Chaos Rift: live log showed it finishing in 5s doing nothing while the Holy Upgrade bubble stayed lit. It now warns when the Chaos Rift screen does not open and then opens Holy Upgrade from its own bubble, and logs how many holy upgrades were confirmed and the orbs of light left.

## 0.7.22 (2026-10-29)
- Awaken Heroes waits up to 5s for the Awaken button to become clickable again after an awakening (live log showed it stopping after 1 of several affordable awakenings).

## 0.7.21 (2026-10-29)
- Awaken Heroes now always logs what it did: screen failed to open, button not clickable (with cost and crystals), or a confirmed count with the crystal balance. Before, several exits were silent.

## 0.7.20 (2026-10-29)
- War Machine Upgrade: fixed a bug where it read the currency counters on the garage list, where they are hidden, saw 0 and skipped every machine. It now opens each picked machine and decides on that machine's own screen, then also opens any other machine still showing its bell. If nothing could be bought it backs off for an hour.

## 0.7.19 (2026-10-29)
- War Machine Upgrade and Awaken Heroes no longer re-run every 3 minutes for a bubble they cannot clear: the log showed both looping with nothing to spend. They now leave that bubble alone for 1 hour and say why. The bubble itself can stay lit if it is for a machine or upgrade type that is not selected.

## 0.7.18 (2026-10-29)
- Bubble Rewards logs every lit bubble and which task answers it (or UNHANDLED) each run, so a bubble that stays lit can be traced by name.

## 0.7.17 (2026-10-29)
- Map missions, Oracle rituals, guardian training, Chaos Rift, Awakening, Holy Upgrade, Scarab milestones, Pharaoh's Vault, Event Exchange, Expeditions, Alchemist and Library research now verify a visible state change (currency, claim button, timer or slot) after each click and stop instead of looping when none is seen.
- Chaos Rift now owns its bubbles; Bubble Rewards and Scarab Game no longer overlap tasks that handle the same bubble.
- Miner, beer-dragon and pumpkin clicks are retried (up to 3 times) if the target is still on screen 3s later; Enlightenment is capped at 50 clicks per visit.
- Tree and research upgrade tasks (Personal Tree, Meteorite and other node trees) remember maxed, locked and unaffordable nodes and stop opening every node's preview each run.
- Temple Prestige adds a rate-based trigger: it samples the temple's gold boost hourly and empowers once growth per hour stays below a set share (default 75%) of this run's own average for two checks in a row, which needs no earlier prestige. The stall rule remains, and both respect the minimum gain and run age.
- Temple Prestige waits for a stage reset before resetting its stall tracker.
- Logs state when a result is only a click/control change and not independently confirmed reward receipt. Not yet live-tested in game.

## 0.7.16 (2026-10-29)
- Upgrade tasks now skip empty-currency preview scans, verify level/cost/currency changes after each purchase, and stop instead of repeating clicks when progress is not observed.
- Daily rewards, Oracle blessings, Engineer tools, free pickaxes and Warfront loot now check visible game-state changes; logs distinguish those changes from independently confirmed reward receipt.
- Fixed the Scarab free-token task reading the shop-sale countdown instead of the free-token reset timer.

## 0.7.15 (2026-10-29)
- Free reward bubbles now detect newly available claim buttons anywhere in menus, popups and events, including content opened inside an already-active screen.
- Bubble logs distinguish clicks and follow-up button state changes from independently verified reward collection.

## 0.7.14 (2026-10-29)
- Open Chests now recognizes only supported gear, jewel and celestial chests, respects the configured keep list, and waits for each reward screen and inventory update.
- The task verifies chest quantities decrease and continues until no eligible supported chests remain, with explicit reasons when it must stop or leave chests unopened.

## 0.7.13 (2026-10-29)
- Daily rewards, Warfront loot and Free Pickaxes now run as soon as their notification bubbles appear instead of waiting for their timers.
- The free-reward bubble scanner now includes Chaos Rift Supplies and Guardian Holy Upgrade; existing custom bubble lists are preserved.

## 0.7.12 (2026-10-29)
- The F1 settings and F2 status windows now have a brighter sci-fi HUD treatment with cyan/orange corner accents and clearer interactive section headers.
- Slightly smaller setting labels and wider section-title insets help keep long names readable.

## 0.7.11 (2026-10-29)
- The F4 planner now ranks the next war machine level and blueprint upgrades by power gained per resource, using separate rankings for tokens, components, blueprints and ethereal shards.
- Upgrade costs sampled in-game are shown directly; prices at unsampled levels are marked as estimates. The new advisor is read-only and does not spend resources.

## 0.7.10 (2026-10-29)
- Notification rewards now run as soon as a configured bubble appears, instead of waiting for the 10-minute sweep. Bubbles with no free reward still get a 3-hour cooldown.
- Meteorite Research now buys the first affordable upgrade instead of opening every upgrade preview before spending.
- The scheduler no longer checks the same hidden notification twice during one scan.

## 0.7.9 (2026-10-28)
- The planner can rebuild and apply the selected mode's squad every 2, 4, 6 or 12 hours when the estimated power gain meets a configurable threshold (3% by default). Off by default; the game and planner page must stay open.
- Build and release notes are consolidated in `RELEASING.md`. In particular, keep the three bundled Il2CppInterop files, stable MelonLoader 0.7.3, and the main-thread-only Unity access pattern.

## 0.7.8 (2026-10-28)
- **Fixed: auto-upgrade stopped buying (#2).** Since 0.7.6, a task waiting on its bubble looked like it was "about to run", so auto-upgrade stayed paused for good. It buys again.

## 0.7.7 (2026-10-28)
- Fixed the log filling up with "[FAILED] Path broken ... menus/Upgrades is missing" lines (issue #2). Those checks are normal when a menu is closed; each one is now logged once per session instead of every few seconds. One player's log had grown to 17 MB.

## 0.7.6 (2026-10-28)
- The bot now starts by itself when the game loads (Auto Start is on by default for new installs). If it is off, the log says clearly that nothing will run until you press F7. A stopped bot is why bubbles, boss retries and tasks never got done.
- Tasks with a bubble or bell icon (Oracle, quests, mail and the rest) now only run when that icon shows, instead of opening their screens on a timer to look. A 6-hour safety check still runs them in case a bubble is missed. Fewer wasted screens, more time farming.
- Open Chests is now on by default for new installs (every 2 hours it opens every chest except the ones you keep).

## 0.7.5 (2026-10-27)
- Auto upgrade: new "Max" option buys as many levels as you can afford with each press.

## 0.7.4 (2026-10-27)
- Fixed: Apply to game could leave the tank out of the front spot. The tank now always goes in front.

## 0.7.3 (2026-10-27)
- When a task starts and the game is already on that task's screen, the bot carries on from there instead of closing everything and walking back from town. The same goes for the planner's Apply to game and Spend for me.

## 0.7.2 (2026-10-27)
- New in the planner: **Ethereal shard advisor**. Lists the next jewel enchant for every hero in your squad, ranked by war machine power gained per shard, so you know exactly where your shards do the most.
- **Spend for me**: set a shard budget and the bot does the enchants for you through the Hall of Heroes (hero, Enchanting, Jewels), best value first. It stops at the first enchant the game won't allow, so it never spends past your budget or your shards.

## 0.7.1 (2026-10-27)
- Apply to game now works in game (tested with the campaign squad): it swaps only the machines that differ, sets crews through the crew picker, waits for the game's server to confirm the save, then checks the result.
- Safety: Apply only saves when the squad screen matches the plan exactly. Otherwise it leaves without saving and says what did not go in, so a working squad is never replaced by a half-done one.
- The planner only uses heroes you have unlocked (read the same way the game's crew picker does). It used to pick heroes you don't have yet, and the game refused them.
- Apply to squad 1, 2 or 3: pick the squad next to Apply to game. Campaign and arena each remember their own squad (campaign starts on 1, arena on 2), so applying an arena squad never overwrites your campaign squad.
- New Arena setting "Arena Squad" (1, 2 or 3): the bot sets that squad active before arena fights and switches back to your normal squad afterwards. Put your arena build in squad 2 with the planner, set Arena Squad to 2, and campaign keeps its own squad.
- War machine upgrades: pick any number of machines, or "Active squad" to always upgrade the machines you fight with (it follows your squad). It used to upgrade one machine only.
- Settings with many options wrap onto extra lines instead of being cut off (the war machine list showed only 4 of 13 machines).
- No more 10 second pause on the Squads screen when applying.
- New look for the F1 settings panel: tabs (Bot, Daily, Town, Guild, Battle, Events, Progress) instead of one long list, in the same dark teal and cyan style as the planner.

## 0.7.0 (2026-10-27)
- New: War Machine Planner. Press **F4** and your browser opens a planner at `localhost:5577` that reads your real war machines, crews, heroes and jewels straight from the game.
  - Auto-build picks the best 5 machines and crews (each hero used once, always keeps a tank), for the campaign or the arena.
  - Drag heroes between machines and see the power change; the bench shows how much each hero would add.
  - Shows the full stat math for every machine, and a Game check that compares the planner with the game's own numbers.
  - Campaign target: shows the power a mission needs and the furthest mission your squad covers.
  - **Apply to game**: one click and the bot sets your planned machines and crews through the Engineer screens (Squads, Edit, Select crew, Save), then reads the squad back and tells you if anything did not stick.
  - Hero faces taken from your own game (open Engineer > Select crew once).
  - Command-screen look: power gauge against the mission you target, the campaign power curve, ring gauges per machine.
  - Edit levels, rarities, blueprints or jewels to plan upgrades. Edits stay in the planner until you press Apply.
  - Only reachable from your own PC.
  - The planner server and roster reads stay on Unity's main thread; a background web thread previously crashed the game.
## 0.6.8 (2026-10-27)
- New: retries the stage boss. After a lost boss fight the game keeps farming the same stage until "Boss" is pressed; the bot now presses it every 4 minutes (adjustable), so the run keeps climbing. On by default.
## 0.6.7 (2026-10-27)
- Talent points now really stick: the game only keeps talent changes after "Save changes", which the bot now presses.
- The talents task checks your free points first and skips the whole tree when there are none (it used to walk every talent and time out).
## 0.6.6 (2026-10-26)
- New: spends talent points as soon as the "talent available" bubble shows up (Character > Talents), filling the talents you list first. Only talent points are spent.
- Pirate's prize free rewards are now claimed (the game names that button differently).
- Tasks that finish without a next time (for example expeditions with nothing running) now look again in 15 minutes instead of logging a failure.
## 0.6.5 (2026-10-26)
- Fixed new accounts getting stuck reopening Firestone research, the alchemist or map missions. The bot used to come back 3 minutes early for a free speed-up that new accounts don't have yet, then kept reopening the menu until the timer ran out. Now it checks whether the speed-up is really free and otherwise waits for the timer.
- Fixed a crash in Firestone research when a slot is empty or locked (it now looks again in 15 minutes).

## 0.6.4 (2026-10-26)
- Hero auto-upgrade no longer keeps the upgrades menu open. It opens the menu, buys up to the next milestone, closes it again and waits a minute (adjustable). Every other task, the notification bubbles and the miner/dragon collector keep working, with no more toggling F6 on and off.
- It also steps aside the moment a task is about to run.

## 0.6.3 (2026-10-26)
- Hero auto-upgrade now starts by itself when the game loads (it used to wait for F6), so heroes level up again right after a prestige.
- New Upgrade Mode setting, default "Next milestone" (also x100, x25, x10, x1).
- Notification bubbles that offer nothing free are left alone for 3 hours instead of being reopened every 10 minutes.

## 0.6.2 (2026-10-25)
- The download now includes the three fixed MelonLoader files (Il2CppInterop) the current game needs. Without them the game closes right after loading.
- The automatic update check is disabled because its background web request was suspected of crashing the game; keep it off until safely verified.

## 0.6.1 (2026-10-25)
- Temple prestige now waits until the run stalls: no new highest stage for 3 hours (adjustable), at least +100% gold boost and 1 hour into the run. Never uses the paid 2x empower.
- The bot checks GitHub once per start and tells you (log and F1 panel title) when a newer version is out. It never downloads anything by itself.

## 0.6.0 (2026-10-25)
- New: Meteorite research (priority list), Forbidden knowledge, Scarab's game (Noble's tokens only), Pharaoh's vault, Scarab milestones.
- New: Seasonal event exchange (buys the items you list with event currency).
- New: Battle pass free rewards; free rewards from other notification bubbles.
- New: Optional Temple prestige at a gold boost you choose (off by default).
- Purchases are now written to the log.

## 0.5.0 (2026-10-25)
- First Zdbrus Bot release, built on Firebot.
- Mail, tavern, exotic merchant, guild crystal, Chaos Rift, hero awakening, war machines, event challenges, Liberation missions, Arena, Oracle blessings, personal tree, quests.
- Coal miner and double-beer dragon collector, notification bubble watcher, F9 screen map, schedule memory across restarts.
- Faster map missions that skip when every squad is busy.
