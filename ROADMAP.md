# Roadmap

Ideas for the next updates. Nothing here is promised; it is the list we work from.

## Next up
- **Auto-apply on a schedule.** Re-run Auto-build every few hours and apply it when the gain is worth it (for example 3% or more), so the squad stays optimal as heroes and machines grow.
- **Upgrade advisor.** In the planner, rank every possible upgrade (machine level, blueprint, jewel) by war machine power gained per resource spent. The War Machine Upgrade task can then follow that list.
- **Arena smart picks.** Read each opponent's squad from the battle preview and only fight the ones the math says you beat.

## Hero optimizer (new)
- "What's my next big war machine boost from heroes?" For each hero on a crew: what the next jewel upgrade, enchant or rarity step adds to squad power, and what it costs.
- Note from the planner math: crew heroes boost war machines through their jewels and through matching the machine's specialization (+40%), not through hero level. So this optimizer focuses on jewels and crew placement.
- A separate view for the hero party itself (gear, soulstones, levels) for campaign damage.
- Open question: which hero upgrade currency is "the little blue diamond" one; check in game and wire it in.

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
