# Changelog

## 0.7.58 (2026-11-04)
- **Void Crystal advisor fixed.** It could list no gear at all even when you had crystals, because of an unverified enchant-cap check and how the game numbers your hero's gear tier. Tier 1 gear is always included now, and each rarity's cap is read the way the guides describe it (+2 enchants per rarity step). When nothing qualifies, F4 and the log (`[Crystals] Gear scan`) say why: how many pieces are capped, in a locked tier or empty.
- **Guide-backed priority orders for everyone.** Talents, Oracle, Personal Tree, Forbidden Knowledge, Meteorite Research, Firestone Research, Exotic Merchant and Event Exchange now ship in the order the community guides recommend (Aurora's Steam Complete Guide, the Steam War Machine guide, the Firestone Fandom wiki and firestone-idle-rpg.de). Talents lead with **Battle cry** and **Fate**; the Oracle leads with Fate, then Guardian Power. Locked upgrades are skipped until you unlock them, so nothing stalls.
- On this update each of those lists is switched to the guide order **once**. After that your own edits are kept.
- Every ordered list in F1 has two new buttons: **Clear** empties the list and **Recommended** puts the guide order back.

## 0.7.57 (2026-11-03)
- Meteorite Research now protects **Meteorites To Keep** using the game's actual balance and cost, not rounded screen numbers. It buys at x1 and checks the latest reserve again at the purchase click, so changes to the setting take effect before the next purchase.
- Research waits for both the level increase and the exact meteorite debit before buying again. Changed selections, prices or unreadable values stop spending; an unaffordable higher priority still blocks cheaper lower priorities.
- Meteorite Research reads names and unlocks directly from the live tree instead of relying on remembered previews. Your existing reserve and priority settings are kept. A purchase already sent to the game can still finish if you raise the reserve afterward.

## 0.7.56 (2026-11-03)
- New **Void Crystal advisor** in F4: choose hero damage, health, armor or gold, then see the cheapest matching gear enchants across your whole roster. Global gear counts even on benched heroes. Pick one hero to include their personal weapon, chest and boots. These upgrades help heroes, not war machines.
- Prices, current effects and next effects are read from the game. Locked tiers, missing gear and maxed enchants are excluded. Equal-price ties favor relics because they help all three hero stats.
- **Spend for me** starts only after you enter a crystal budget and confirm. It re-reads prices and re-ranks after every enchant, verifies both the level and exact crystal debit, and stops on changed data or an unconfirmed result. There is a Stop spending button and a 40-enchant limit per request. No crystal purchases, tier unlocks or other currencies are used.
- Crystal spending waits for the bot's current job, pauses AutoUpgrade, blocks overlapping planner actions and cancels on game scene changes. New accounts can load the hero advisor before their war machines are available.

## 0.7.55 (2026-11-03)
- New accounts: the bot now waits while the game's guided tutorial has a step on screen, instead of clicking over the highlighted button. AutoUpgrade and AutoSkill pause too, and the upgrades menu stays open for the tutorial's own upgrade step. Tasks show **Tutorial** on F2 while this happens.
- Tasks for features the game hasn't given you yet (Tavern, Guild, Oracle, Arena, World Map and the rest) are held back until the game unlocks them. F2 shows what each one is waiting on, like **Locked (Guild)**, and the log says when the game unlocks it. Accounts at level 200 and up work exactly as before.
- Story pop-ups are clicked through between tasks: story dialogue, Aurelia's tips and "New hero!" screens. Finished storyline chapter rewards are claimed. Nothing is clicked while the tutorial is forcing you to press something.
- AutoUpgrade no longer clicks the upgrade-mode button over and over on accounts that don't have it yet.

## 0.7.54 (2026-11-03)
- War Machine Planner: **Mission needs** now comes straight from the game. Firestone patch 9.1.4 raised the recommended power on war machine campaign missions, and the planner's old estimate ran low, so it could say you were ready when you weren't. The ring, the verdict, "Power covers up to" and the power curve all use the game's numbers once the planner is linked. Until then it shows the old estimate with a note saying so.
- The mission box and the curve follow however many campaign missions the game has, instead of assuming 90.
- Checked the bot against patch 9.1.4: it builds against the updated game and loads cleanly. Campaign fighting doesn't use recommended power, so it's unaffected.

## 0.7.53 (2026-11-03)
- War Machine Planner: dragging a hero from the bench into a machine works again with live game data. Hero IDs from the game are numbers, and the drop compared them as text, so heroes could be moved to the bench but not back. Saved crews with mixed IDs are repaired on load, and the live refresh waits while you're dragging.
- New **War machine tier list** panel ranks every machine S to C for long-term investment, grouped by damage, tank and healer, with a goal squad (Cloudfist, Talos, Thunderclap, Goliath, Hunter) to work toward. Each role says the best machine you have and what to hope for. Tiers come from a Japanese player wiki (Sept 2025), with 2024 arena simulations from a Steam guide as a second opinion in each tooltip.
- Tier badges now appear on every machine in the bays, the garage, the advisor picks and the level and blueprint tables. Hover one for why it's ranked there and what to spend on it. The advisor adds a caution when its pick is tier C. Auto-build still picks by today's numbers.
- Planner text now says **jewel chests** (Wooden, Iron, Golden and up) give war machine parts and conduits, not gear chests.
- New [War Machine Guide](WAR-MACHINES.md): jewel vs gear chests, how unlocks work, the tier list, where to spend, expedition token tips and the chest timing trick, including which bot chest settings to untick for it.

## 0.7.52 (2026-11-03)
- War Machine Planner: the upgrade advisor was rebuilt. It used to charge a whole machine level as a single workshop upgrade, so levels looked up to about 20 times cheaper than they are, and it listed each level four times and compared machines by parts they cannot share.
- The advisor now opens with two picks, the best machine to level and the best blueprint to buy, followed by a level table and a blueprint table. Levels count the real number of workshop upgrades from the machine's current XP (each upgrade costs 500 expedition tokens, 20 screws, 12 cogs and 1 metal and gives 100 XP). Blueprints show the stat, the cost and the level cap.
- The advisor checks each machine's own screws, cogs and metal and your expedition tokens and blueprints. When the best-value machine is short on parts, it suggests the best machine you can level now.
- Hover tooltips (or tap the **?**) explain the planner's numbers: parts needed for a machine and where to get more, level and blueprint costs, overdrive, Game check, power, crew gains, shards and the advisor's columns.
- The planner now reads each machine's XP and parts and your expedition tokens, blueprints and ethereal shards from the game.

## 0.7.51 (2026-11-02)
- War Machine Planner: Auto-build now picks machines and crews together. A machine that is weak on its own but gains a lot from the heroes available to it can now make the squad. Arena mode places crews by arena power.

## 0.7.50 (2026-11-02)
- Notification quiet periods and the separate Chaos Rift and Mystery Box retries now survive game restarts. Scheduled task checks are not blocked by a quiet notification.
- Saved task schedules and retry delays are shared across accounts on the same installation, so switching accounts can carry over a wait.

## 0.7.49 (2026-11-02)
- Free Pickaxes now waits at least ten minutes before retrying an unreadable quantity or an unconfirmed claim while the game stays open, even if its notification remains lit.
- After a confirmed prestige, the bot waits briefly for the completion screen and its Start now button instead of missing a screen that appears after the stage resets.

## 0.7.48 (2026-11-02)
- Arena falls back to the current squad only when the chosen squad is confirmed empty. Unreadable squad data stops the fight instead of being mistaken for an empty squad.
- Free Pickaxes confirms claims only from a readable quantity decrease or a newly visible claimed marker; uncertain claims are retried.
- Mystery Box claims require a claimed marker or rewards screen, and an unconfirmed claim is retried without postponing other daily rewards. Opening boxes requires a readable amount and a free control with no visible price marker.
- Chaos Rift's six-hour retry no longer mutes Guardian Holy Upgrade notifications.
- Temple's delayed previews preserve the next hourly growth sample, including when a boss or stall trigger checks a zero boost.
- These changes passed simulated regression checks, but the Arena empty-squad representation and the Mystery Box store tab have not yet been verified in the live game.

## 0.7.47 (2026-11-02)
- Balanced AutoUpgrade uses idle gaps even when a scheduled task is less than 30 seconds away. Frequent Temple and boss checks no longer keep it paused between tasks; ready and running tasks still take priority, and Focus keeps its early pause.
- Free Pickaxes no longer reports a successful claim as failed. The game counts down unclaimed pickaxes after a claim, and the bot now checks for that.
- Arena: if the chosen Arena squad has no war machines, the bot warns once and fights with your current squad, instead of skipping Arena forever.
- Chaos Rift: when the game shows a message instead of opening the Rift, the bot logs the message text, closes it, and waits 6 hours before trying again.
- Open Chests also opens Mystery Boxes from the Bag (F1 setting **Open Mystery Boxes**, on by default).
- The free Mystery Box claim switches to the store's value-bundle tab when the store opens on another tab. An already-claimed box is logged as information, not as a warning.
- Temple: when a trigger fires but the minimum boost or run age is not reached yet, the bot waits a few minutes before the next check instead of reopening the preview every minute. All reset safeguards are unchanged.

## 0.7.46 (2026-10-31)
- Temple Prestige now recognizes a displayed `+0%` as a real zero boost instead of an unreadable value, while still refusing to empower until the preview shows a positive boost.
- A zero-boost growth sample is remembered, so ordinary one-minute checks no longer reopen the Temple for that same first sample. Hourly growth reads and qualifying boss/stall checks remain active.

## 0.7.45 (2026-10-30)
- Temple Prestige now reads the game's minutes-and-seconds run clock (for example, 35:47) as a fraction of an hour, instead of treating minutes as hours. Invalid seconds still fail closed.
- The F2 task list now has an ON/OFF switch beside each task, connected to the same saved enabled setting used in F1.

## 0.7.44 (2026-10-30)
- AutoUpgrade now finishes its short balanced pass across selected slots before handing the game back to the main bot tasks. A task no longer interrupts the pass after only the first few upgrades.

## 0.7.43 (2026-10-30)
- Fixed Temple prestige cancelling itself when opening the Temple hides the battle HUD. Stage checks and boss retry tracking now read live game data, including the final safety check and reset confirmation; missing or changed data still prevents a reset.
- Prestige cancellation logs now include the before/after stage, boost and run-age readings.
- Free prestige handles the extra confirmation only after opening it from the verified free control. Existing, unrelated, priced, or changed confirmations are left untouched.
- Remembered upgrade names no longer change when menus move in the UI hierarchy. Existing Oracle name mappings are reused instead of opening every preview again.
- Scarab uses the game's Auto mode, with currency checks guarding each native spin. Pharaoh's tokens still require the separate opt-in, off by default; gems and token purchases remain excluded.
- Awakening uses Auto with an x1, x2, x5, x10 or x20 amount selector. It spends every affordable selected batch instead of stopping after ten clicks, without falling back to another amount or buying crystals.
- Both Auto activities confirm currency use and turn Auto off when finished, stopped, or interrupted. A task time limit can split a large balance across visits.
- Talents read names, levels and unlock state from the live tree and open only eligible priority previews, rather than discovering every name through a popup.
- Compatible town and guild tasks reuse their hub instead of returning to battle between each task. Popup cleanup remains; battle tasks and idle upgrade windows still return to the battle screen.

## 0.7.42 (2026-10-30)
- Temple prestige prioritizes the growth-efficiency trigger, then repeated boss attempts at the same stage, with a selectable 30-minute or 1-hour no-progress fallback. The free preview is used only when a trigger and the minimum run age and boost are verified.
- Liberation Missions can target Easy (1 star), Normal (2 stars) or both, and verifies difficulty changes before starting fights.
- Battle Pass skips scrolling the reward track when its rewards marker is present but hidden; it retains the conservative scan if the marker is missing.

## 0.7.41 (2026-10-30)
- Added opt-in local Arena matchup recording for confirmed bot-started wins and losses. It saves preview formations, available game-reported stats, displayed powers and outcomes as append-only JSON lines without player names, account IDs or changes to fight decisions. Missing values remain null.
- Recording is off by default, including with Enable All On Launch. Write failures are logged. The per-machine preview stats have not been validated against combat and are not a win predictor. No live Arena fights were performed for this release.

## 0.7.40 (2026-10-30)
- Arena rejects missing, malformed, nonpositive or unsupported power readings instead of treating them as easy opponents or disabling the power limit. It selects the lowest readable opponent within the configured percentage; displayed power is not a win prediction.
- Before starting a fight, Arena rechecks the selected opponent's name, rank and power, the preview squad power, the configured squad and the fight control. Existing or mismatched previews, changed values and unconfirmed starts or results stop the batch. Arena reads the actual win/loss indicator instead of guessing from the result popup's generic name.
- The optional once-daily exception still permits the weakest opponent above the limit after 20:00 when all three opponents are readable and refresh is unavailable. It records an attempted exception before the click so an uncertain result cannot trigger another that day. Refreshes must produce a stable changed list.
- Squad switches are verified, and Arena reserves task runtime for restoration and logs when it cannot verify the original squad. Stopping the bot, changing accounts or closing the game mid-task can still interrupt restoration; check the active squad afterward. Regression tests cover selection, preview and result failures, daily attempts, refreshes and squad switching. Live Arena fights were not performed for this release.

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
