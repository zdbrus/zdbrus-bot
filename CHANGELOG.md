# Changelog

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
- The update check is switched off for now while it is tested for crashes.

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
