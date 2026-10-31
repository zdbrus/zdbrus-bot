# Roadmap

## Bugs reported 2026-10-27 (fix first)
- **Boss retry doesn't work.** Check the Boss button path and whether the stage state is read right.
- **Auto upgrade has no "x All / Max" option.** Add it to the multiplier choices (next to x100, x25, x10, x1).
- **Left-side notification bubbles never get finished.** Arena tokens (10), war machine/engineer tools, Pharaoh's pyramid, map/treasure, and the one below. Rule: every bubble that shows must end with its task completed, not just opened. Start from UserData/screenmaps/unhandled_bubbles.txt and the log.
- **"Christmas-treeing"**: the bot sometimes opens a menu, flips through it and leaves without doing anything. Every task should either act or log exactly why it did nothing (no tokens, timer running, nothing affordable).
- **Verify in game:** 0.7.3 "start from the current screen", 0.7.4 tank-in-front, Spend for me via Town > Hall of Heroes.

Ideas for the next updates. Nothing here is promised; it is the list we work from.

## Next up
- **Upgrade advisor (read-only rankings implemented).** The planner ranks the next machine level and blueprint upgrades separately by their required resources; the existing Ethereal shard advisor ranks jewel upgrades separately. No level or blueprint spending is automated. In-game cost samples: machine levels 1, 13 and 15; blueprint levels 0, 4 and 20. Unsampled prices are marked as estimates; validate additional cost changes before connecting any ranking to automation.
- **Arena smart picks.** Read each opponent's squad from the battle preview and only fight the ones the math says you beat. The power-based selection and preview checks in 0.7.40 are safeguards, not this squad-based predictor.

## Hero optimizer (new)
- "What's my next big war machine boost from heroes?" For each hero on a crew: what the next jewel upgrade, enchant or rarity step adds to squad power, and what it costs.
- Note from the planner math: crew heroes boost war machines through their jewels and through matching the machine's specialization (+40%), not through hero level. So this optimizer focuses on jewels and crew placement.
- A separate view for the hero party itself (gear, soulstones, levels) for campaign damage.
- **Spend optimizer** (verified from in-game screenshots, 2026-10-27):
  - Void crystals: gear enchanting. Hero stats only (no war machine effect). Each enchant doubles that gear piece's effect; cost looks like 60 x 2^(level-1).
  - Ethereal shards: jewel enchanting. **Raises war machine stats** for the machine the hero crews. Our planner jewel math matches the game exactly (Boris: rare damage jewel lv5 = 1,400% -> 1,600%; epic health lv3 = 1,900% -> 2,000%). Cost looks like 60 x 2^(level-1) (lv3 = 240, lv5 = 960). Plan: rank every crew hero's next jewel enchant by squad power gained per shard, using `jewelEffect` in engine.js.
  - Soul shards: soulstone enchanting. Hero stats only; each enchant doubles the effect; level 1 costs 30.
  - Gear tier unlocks use a separate material and need a gear power total (Valerius: tier 2 = 480, needs 1,300 gear power).

## More features
- **Campaign target alerts.** Run campaign missions up to the furthest one the squad covers, and log when a new one opens up.
- **Hero planner.** Gear, jewels and soulstones read from the game, with "upgrade this next".
- **Guardian and Firestone research planners.** Live from the game, with timers.
- **Daily summary page** (`localhost:5577/today`): resources gained, upgrades bought, arena results, campaign progress since you last looked.

## Look and polish
- F2 status screen in the HUD style.
- Settings profiles: save and swap whole F1 setups (Push campaign, Arena week, AFK overnight).
- Check the F1 tabs at other window sizes (small and ultrawide).
- Freeze report: when the game hangs, write the last task and screen to a short file for bug reports.
- First-run tour: F1 settings, F4 planner, F7 start.

## Housekeeping
- Delete the old `tmp-release-asset` branch on GitHub.
- README and docs: squad picker, Arena Squad setting.
