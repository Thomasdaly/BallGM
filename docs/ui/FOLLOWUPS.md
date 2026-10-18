# Follow-ups

> Everything deferred, blocked or out of scope, with the reason. Spec 00 §4–8.

**Types:** `NEEDS-GAME-LOGIC` · `BLOCKED` · `P2-DEFERRED` · `OUT-OF-SCOPE` · `SPEC-ISSUE` · `S3-POLISH`

| ID | Item | Type | Why | Where (files, screens) | Suggested next step |
|---|---|---|---|---|---|
| F01 | S06 Inbox and news | NEEDS-GAME-LOGIC | The game produces no inbox items, news or decisions that block time; an inbox needs a message model and generators. | shell nav, S05 inbox tile, Continue "Blocked" state | Add an inbox/news domain model and generators, then build S06 on the T4 template. |
| F02 | S09 Tactics / playbook | NEEDS-GAME-LOGIC | No tactics, schemes or sliders exist in the rules or match engine. | — | Model schemes in Rules and the engine first. |
| F03 | S16 Live game | NEEDS-GAME-LOGIC | Games are simulated whole; there is no play-by-play stream or live clock to show. | S15 "Go to game" | Expose a possession event stream from `PossessionMatchEngine`. |
| F04 | S21 Draft (big board, prospect profiles, lottery, draft night) | NEEDS-GAME-LOGIC | `DraftDay` runs automatically inside `ConcludeSeason`; no draft class, lottery result or pick-by-pick state is exposed to the client. The pick board (S35) shows draft assets. | — | Expose the draft class and lottery through `LeagueSession`, then build S21. |
| F05 | S22 Scouting | NEEDS-GAME-LOGIC | Scouting ranges exist in Rules but there are no assignments or reports in the session. | — | Wire `ScoutingModel` into the session. |
| F06 | S24 Staff | NEEDS-GAME-LOGIC | No staff model. | — | — |
| F07 | S25 Training and development | NEEDS-GAME-LOGIC | Development runs in Rules but is not threaded through the season boundary or exposed. | — | Wire development into the season and expose deltas. |
| F08 | S27 Awards, history and records | NEEDS-GAME-LOGIC | Award and season-stat calculators exist in Rules but are not wired to the session; only per-player career lines exist (shown in S10). | — | Wire `SeasonAwardCalculator` and a records book. |
| F09 | Stable entity IDs for packs | NEEDS-GAME-LOGIC | IDs are minted per load, so FM-style graphics files named by entity ID cannot work and screenshots differ between runs. "Show entity IDs" can only show per-load IDs plus the pack's own key and image path. | S30, S10, hover cards | Derive IDs deterministically from pack keys (an entity-ID change, out of scope here). |
| F10 | Separate graphics packs (faces/logos folders) | NEEDS-GAME-LOGIC | The only mod kind is the league pack, whose teams/players name their own image files; there is no user mods folder or graphics-pack format. The UI presents the loaded league pack as the one pack. | S30, S03 | Define a graphics-pack format keyed by stable IDs (needs F09). |
| F11 | Fixture draft prospects and forced 24–11 record | NEEDS-GAME-LOGIC | Spec 06 §1.2 asks for 60 prospects and HCM at 24–11; prospects are only generated at season end and records come from the real engine (Decision 4). | fixture league | — |
| F12 | Depth chart editing (drag and drop, Alt+Up/Down) | NEEDS-GAME-LOGIC | The depth chart is built by `DepthChartBuilder`; a user-set rotation is planned as attributes A3b. S08 is read-only. | S08 | Build the A3b rotation editor, then add reordering. |
| F13 | Injury status taxonomy (Out, Doubtful, Questionable, Probable, Day-to-day) | NEEDS-GAME-LOGIC | Injuries are free text plus an injured flag; the UI shows the text in a single INJURED status chip rather than inventing a status. | StatusChip, S07, S10 | Model injury status and expected return. |
