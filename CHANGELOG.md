# Changelog

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
