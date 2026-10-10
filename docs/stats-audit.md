# Stats audit — towards Football-Manager-depth statistics

Phase 1 of the deep-stats goal: what the engine simulates, what the domain keeps, what the save
persists, what the Application exposes, and what each screen shows — and where each stat is dropped
on the way. This document is diagnosis only. Phase 2 (design) is appended after review.

Code audited: `main` @ `7d5fba0`. Every claim below is a `file:line` in that tree.

---

## 0. Baselines (measured before any change)

| Gate | Result | Source |
|---|---|---|
| `/sim-audit all` | **ship-blocking.** Halted after Battery 1 (the spec stops on any nonzero Battery-1 rate). Batteries 2–7 not run. | `reports/sim-audit-4d6d61ff-36be-4a11-a137-223b73a58104.md` |
| `/sim-regress` (50 seasons, seeds 1–50) | **FAIL 1/12**: row `4-reb`. Rows 1, 2, 3, 5 re-measured and pass. Rows 6–12 were not re-run, because the engine, `MatchModelBounds` and fixture strengths are unchanged since their last measurement (`git diff 2b59e97..HEAD` touches only schedule, bracket and copy). | this run |
| Season sim time | **13.5 ms/season** median (M1, shipped 6-team fixture league, `StartSeason`+`AdvanceToEndOfSeason`, Load excluded; three runs of 5×200 seasons gave 13.55 / 13.09 / 13.58). **Budget for the whole goal: ≤ 16.9 ms (+25 %).** | `scratch/bench/` (gitignored) |

### What "no new failures vs baseline" means here

The baseline audit already fails, so the goal's "/sim-audit: no new failures vs baseline" bar is low.
The goal's own engine work (§6 cluster A) is what fixes the baseline failures. Once Battery 1 holds,
Batteries 2–7 run for the first time on this engine, and their results become the real baseline.
**Decision for review (Q1):** treat the first full 1–7 run after the Battery-1 fix as the
baseline, rather than today's halted run.

### `/sim-regress` row 4 was never actually green

`suite.md` marks `4-reb` as "done". The strict form of the identity,
`ORB_team + DRB_opp == (FGA−FGM) + trip-ending FT misses + team rebounds`, fails in **100 % of
team-games** at both 50 and 1,000 seasons. There are two separate causes:

- **Leaked boards: 1.81 per team-game.** `FinishSide` (`PossessionMatchEngine.cs:336-357`) floors
  `0.74·M_opp` and `0.24·M_own`. Those two shares sum to 0.98, and the floor drops about 0.9 more per
  side. The missing boards are recorded nowhere, not even as team rebounds.
- **Phantom misses: 7.28 per team-game, 8.1 % of all FGA.** The FGA is charged at
  `PossessionMatchEngine.cs:204` before `ResolveMiss` (`:290`) decides that the miss was a shooting
  foul. A fouled missed shot is not a field-goal attempt.

Row 4 passed only because the old check compared boards with *the engine's own miss counter*. That
counter is the same quantity the shares were floored from, so the check could not catch the leak.

---

## 1. Ground truth vs the goal statement

| Goal statement | Verified? | Evidence |
|---|---|---|
| `PossessionMatchEngine` + `MatchModelBounds` read only `Overall` | **True.** Position is also read, for the rebound and assist weights. | `PossessionMatchEngine.cs:450` (`OverallOf`), `:466-471` (strength), `:473-484` (usage), `:486-492` (position weights); `Height`/`Speed`/`Strength`/`Passing`/`LateralQuickness` appear nowhere in `src/BallGM.Simulation` or `src/BallGM.Rules/Seasons` |
| TOV/STL/BLK/PF aren't modelled | **True**, with one nuance: 8 % of possessions never reach a shot (`FieldGoalAttemptRate = 9_200`, `MatchModelBounds.cs:112`). That is an implicit turnover with no stat and no player credited. | `PossessionMatchEngine.cs:196-199` |
| `PlayerStatLine` has OREB/DREB/FGA/3PA/FTA/USG | **True.** It also has FGM/3PM/FTM and `Started`. | `BoxScore.cs:25-147` |
| `BoxScoreLine`/`PlayerSeasonTotals` carry only MIN/PTS/REB/AST | **True.** `BoxScoreLine` also carries `Started`; `PlayerSeasonTotals` also carries GP. | `SeasonModels.cs:121-128`, `:239-245` |
| *(not in goal)* Profile already has full shooting | `SeasonDetailLine` carries FGM/FGA/3PM/3PA/FTM/FTA/OREB/DREB/USG plus team and opponent context, and `StatFormulas` already computes eFG/TS/3PAr/FTr/AST%/REB%. The profile is the one surface that does not drop shooting. | `PlayerProfileModels.cs:33-60`, `LeagueSession.Players.cs:144-182`, `StatFormulas.cs:16-72` |
| *(not in goal)* STL/BLK/TOV/PF/+/- already render | **Only for pack-stated seasons.** They come from the open `metrics` dictionary on a career line (`PlayerStatsSections.cs:80-157`), so a simulated season can never fill them. | `LeaguePackDataSource.cs:530-544` |

---

## 2. Scoring trace — possession decision → box score → screen

One made basket, followed from the first rule that shapes it to the pixel that shows it.
`PME` = `src/BallGM.Simulation/Seasons/PossessionMatchEngine.cs`,
`MMB` = `src/BallGM.Simulation/Seasons/MatchModelBounds.cs`. All probabilities are basis points
(bp, /10,000) and all efficiencies are points per 10,000 possessions.

| # | Step | file:line | Term (value) | Draw |
|---|---|---|---|---|
| 1 | Rotation and minutes: Overall-weighted share of 240, clamped | `Rules/Seasons/DepthChartBuilder.cs:140-215` | `MinutesAllocationBounds.TeamMinutesPerGame` 240, `MaximumMinutesPerPlayer` 42, `MinimumRotationMinutes` 6, `MaximumRotationSize` 10 | — |
| 2 | Team strength = minutes-weighted mean Overall | PME:466-471 | clamp `PlayerRating.Min/MaxOverall` | — |
| 3 | Offensive efficiency = base + strength + home − fatigue | PME:151-167 | `BaseOffensiveEfficiency` 11,500; strength `(S−S_opp)×EfficiencyPerRatingPoint(60)/2` clamped ±`MaximumStrengthEfficiencySwing` 1,200; `HomeCourtEfficiencyBonus` 200; fatigue `min(missingRest×90, 250)` | — |
| 4 | Possessions, shared by both sides | PME:87-88 | `BasePossessionsPerGame` 98 ± `PossessionSpread` 9 | `NextInt32(-9,10)` |
| 5 | Efficiency → FG% | PME:229-246 | swing `(eff−11,500)×FieldGoalPercentSwingAtMaximumStrength(800)/1,200`; `BaseTwoPointPercentage` 5,300, `BaseThreePointPercentage` 3,700, clamp [2,000, 7,500] | — |
| 6 | Hot/cold night, one draw per period, applied to both shot types | PME:182-192 | `GameShootingVarianceRange` ±850 | `NextInt32(-850,851)` |
| 7 | Does the possession reach a shot? If not, it is a dead possession and **no stat is recorded** | PME:196-199 | `FieldGoalAttemptRate` 9,200 | `NextInt32(0,10000)` |
| 8 | Two or three | PME:201 | `ThreePointAttemptShare` 4,000 | `NextInt32(0,10000)` |
| 9 | Shooter by usage weight: `MIN × clamp(Ovr²·100/meanOvr², 55, 210)` | PME:202, weights :473-484, `PickBy` :543-568 | `MinimumUsageFactor` 55, `MaximumUsageFactor` 210 | `NextInt32(0,ΣW)` |
| 10 | **FGA charged here, before the foul is resolved** | PME:204-208 | — | — |
| 11 | Make? | PME:210 | fg2/fg3 % from steps 5–6 | `NextInt32(0,10000)` |
| 12 | Make: +2/+3 to `Points`, `PointsBy`, FGM, 3PM | PME:262-274 | — | — |
| 13 | And-one | PME:278-282 | `AndOneChanceOnMake` 500 | `NextInt32(0,10000)` |
| 13′ | Miss: shooting foul (2 or 3 FTs) or a clean miss going to the `Misses` pool | PME:290-303 | `ShootingFoulChanceOnMiss` 1,500 | `NextInt32(0,10000)` |
| 14 | Free throws: a flat percentage, not adjusted for the player or the matchup | PME:306-324 | `FreeThrowPercentage` 7,700 | one draw per shot |
| 15 | OT loop while tied; after 6 OT a synthetic basket decides it | PME:96-112 | `OvertimePossessions` 11, `MaximumOvertimePeriods` 6 | as above |
| 16 | **After the game:** boards and assists dealt out by fixed share, floored | PME:336-357 | `DefensiveReboundShare` 7,400, `OffensiveReboundShare` 2,400, `AssistShareOfMadeFieldGoals` 5,800; position weights PME:636-652 | `PickBy` per board and per assist |
| 17 | `PlayerStatLine` built; the points identity is checked | `Domain/Seasons/BoxScore.cs:27-90` (identity :84-90) | `Points == 2(FGM−3PM)+3·3PM+FTM` | — |
| 18 | `BoxScore.Create`: Σ points equals the result, Σ USG = 100 | `BoxScore.cs:200-239` | error codes :165-167 | — |
| 19 | `GameResult.Create`, then `SeasonRun.RecordResult` | `Domain/Seasons/GameResult.cs:48-90` | — | — |
| 20 | Saved: every `PlayerStatLine` field round-trips | `Infrastructure/Seasons/SeasonEnvelope.cs:14-29` (schema v3 :79) | — | — |
| 21a | Box score read model: **drops to MIN/PTS/REB/AST** | `Application/Leagues/LeagueSession.Seasons.cs:734-749` → `SeasonModels.cs:121` | — | — |
| 21b | Season totals: **drops to GP/MIN/PTS/REB/AST**, and postseason games are mixed in | `Rules/Seasons/PlayerSeasonStatsCalculator.cs:18-35` → `LeagueSession.Seasons.cs:499` | — | — |
| 21c | Profile season detail: keeps everything | `LeagueSession.Players.cs:144-182` | — | — |
| 22a | Game sheet final: the PTS column | `Client/ViewModels/GameSheet.cs:212` → `Views/SeasonView.axaml:15-29` | — | — |
| 22b | Profile stats tab: PTS, FG, 3P, FT, eFG, TS | `ViewModels/PlayerStatsSections.cs:21-73` → `Views/PlayerProfileView.axaml:130-164` | — | — |
| 22c | Roster: PPG | `ViewModels/RosterViewModel.cs:241-245` → `Views/RosterView.axaml:52-101` | — | — |

---

## 3. Matrix: stat × layer × surface

Key: ✓ present · ◐ partial or wrong (see note) · ✗ **gap** · n/a means the screen does not exist yet,
so every cell in that column is a gap.

| Stat | Modelled | Stored (domain + save) | Exposed (Application) | Box score (final) | Game sheet (preview) | Profile | Roster | Season stats | Leaders | Front office |
|---|---|---|---|---|---|---|---|---|---|---|
| MIN | ◐ allocated pre-game, no substitutions | ✓ | ✓ | ✓ | ✓ projected | ✓ | ✓ MPG | n/a | n/a | ✗ |
| PTS | ✓ | ✓ | ✓ | ✓ | ✓ PPG, L5 | ✓ | ✓ PPG | n/a | n/a | ✗ |
| FGM / FGA | ◐ FGA includes fouled misses (+8.1 %) | ✓ | ◐ profile and recent form only | ✗ | ✗ | ✓ | ✗ | n/a | n/a | ✗ |
| 3PM / 3PA | ✓ | ✓ | ◐ profile only | ✗ | ✗ | ◐ season tab yes, recent form no | ✗ | n/a | n/a | ✗ |
| 2PM / 2PA | ✓ derived | ✓ derived | ✗ | ✗ | ✗ | ✗ | ✗ | n/a | n/a | ✗ |
| FG by zone (rim / paint / mid / corner 3 / above-break 3) | ✗ two-vs-three only | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | n/a | n/a | ✗ |
| FTM / FTA | ◐ flat 77 % for every player | ✓ | ◐ profile only | ✗ | ✗ | ◐ season tab only | ✗ | n/a | n/a | ✗ |
| OREB / DREB | ◐ dealt out post-game; 1.81 leak per team-game; OREB never extends a possession | ✓ | ◐ profile only | ✗ merged into REB | ✗ | ✓ | ✗ | n/a | n/a | ✗ |
| REB | ◐ as above | ✓ | ✓ | ✓ | ✓ RPG | ✓ | ✓ RPG | n/a | n/a | ✗ |
| Team rebounds | ✗ leaked, not recorded | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | n/a | n/a | ✗ |
| AST | ◐ `floor(0.58·FGM)` post-game; no scorer link, so a player can "assist" his own basket; game SD 0.008 | ✓ | ✓ | ✓ | ✓ APG | ✓ | ✓ APG | n/a | n/a | ✗ |
| STL | ✗ | ✗ | ✗ | ✗ | ✗ | ◐ pack seasons only | ✗ | n/a | n/a | ✗ |
| BLK | ✗ | ✗ | ✗ | ✗ | ✗ | ◐ pack seasons only | ✗ | n/a | n/a | ✗ |
| TOV | ✗ the 8 % dead possessions record nothing | ✗ | ✗ | ✗ | ✗ | ◐ pack seasons only | ✗ | n/a | n/a | ✗ |
| PF / foul-outs | ✗ only shooting fouls, not credited to any defender | ✗ | ✗ | ✗ | ✗ | ◐ pack seasons only | ✗ | n/a | n/a | ✗ |
| +/- | ✗ no lineup or stint tracking (`BoxScore.cs:130`) | ✗ | ✗ | ✗ | ✗ | ◐ pack seasons only; simulated seasons show a **team** margin labelled "TEAM +/-" | ✗ | n/a | n/a | ✗ |
| USG% | ◐ **an input weight, not observed usage**: 2.58 pp mean absolute error vs `FGA+0.44·FTA` share | ✓ | ◐ profile only (minutes-weighted double) | ✗ | ✗ | ✓ | ✗ | n/a | n/a | ✗ |
| GS / Started | ✓ | ✓ | ◐ | ✓ starters/bench split | ✓ | ◐ in the subtitle text only | ✗ | n/a | n/a | ✗ |
| Match rating /10 | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | n/a | n/a | ✗ |
| DD2 / TD3 | ✓ derived | — | ✓ profile | ✗ | ✗ | ✓ | ✗ | n/a | n/a | ✗ |

**Where each stat is dropped, at the first layer it is lost:**

| # | Drop point | file:line | Lost |
|---|---|---|---|
| D1 | `PlayerSeasonStatsCalculator.Calculate` | `Rules/Seasons/PlayerSeasonStatsCalculator.cs:26-32` | OREB/DREB split, all shooting, USG, GS. This drop **crosses the `ISeasonEngine` port** (`ISeasonEngine.cs:72`, `RulesSeasonEngine.cs:89-93`). |
| D2 | `LeagueSession.ToLines` (box score) | `LeagueSession.Seasons.cs:741-748` | OREB, DREB, USG, FGA, FGM, 3PA, 3PM, FTA, FTM |
| D3 | `PlayerSeasonTotals()` | `LeagueSession.Seasons.cs:499-510` | same as D1, which feeds the roster and game sheet |
| D4 | `ToRecentGame` | `LeagueSession.Players.cs:267-292` | OREB/DREB, 3P, FT, USG, GS |
| D5 | `MetricsFrom` at season conclusion | `LeagueSession.Players.cs:110-141` | GS, team and opponent context, OREB%/DREB%, 3PAr, FTr. **Once the season is concluded these cannot be recomputed.** |
| D6 | **Save/load drops `CareerHistory` entirely** | `Infrastructure/Saves/SaveGameSerializer.cs:171-180` (no `CareerHistory` initialiser); `LeagueSession.Saves.cs:43` | every concluded simulated season and all pack-stated career rows. **A real bug, independent of this goal.** |

---

## 4. Other findings (ranked)

| # | Sev | Finding | Evidence |
|---|---|---|---|
| F1 | P0 | Rebound conservation fails in 100 % of team-games (§0) | audit 1.4; `PME:204`, `:336-349` |
| F2 | P0 | Phantom FGA bias low-balls every shooting rate: league FG% 0.461 against 0.502 with fouled misses excluded, so eFG% and TS% are biased too | audit cluster 2 |
| F3 | P0 (Battery-1 rule) / P2 (magnitude) | AST/FGM is 0.568, just under the 0.57 band floor (floor truncation). Assists are deterministic per game (SD 0.008 vs about 0.076 binomial), with no scorer link | audit 1.3; `PME:351-356` |
| F4 | P1 | **Career history lost on save/load** (D6). No test catches it: `SaveGameSerializerTests.cs:23` never concludes a season | `SaveGameSerializer.cs:171-180` |
| F5 | P1 | Postseason box scores are summed into season totals, so PPG/RPG/APG on the roster and game sheet include playoff games (no phase filter) | `PlayerSeasonStatsCalculator.cs:22-24` |
| F6 | P2 | `PlayerStatLine` never checks `3PM ≤ FGM`, so a line with negative two-point makes is constructible. The doc comment at `BoxScore.cs:22` says it is checked | `BoxScore.cs:64-82` |
| F7 | P2 | `BoxScore.Create` doesn't check Σ MIN = 240 + 25·OT, duplicate player IDs, or that the box score's home/away teams match the fixture. These hold today by construction only | `BoxScore.cs:200-239`, `GameResult.cs:48-90` |
| F8 | P2 | USG% is the pre-game shot-selection weight, not what happened | audit 1.7 |
| F9 | P1 (structural) | An offensive rebound never extends a possession, so there are no second-chance points. ORB% has no effect on the score | `PME:336-349` |
| F10 | P2 | No turnover stat, so the box-score possession estimate (`FGA+0.44FTA−ORB+TOV`) is off by −9.0 per team-game, and ORtg/DRtg/pace can't be computed from the box score | audit 1.5 |
| F11 | P2 | `SeasonAwardCalculator` has no production caller. `AwardStatBasis` is totals-only, there are no qualifier minimums, and the shipped ruleset states no awards | `SeasonAwardCalculator.cs:26-61`, `AwardRules.cs:6-12` |
| F12 | P2 | AI valuation never reads production: `AssetValuationModel` "Production" is `Rating.Overall` | `Rules/AI/AssetValuationModel.cs:58-79` |
| F13 | P2 | Rate arithmetic in view models bypasses `StatFormulas`: raw `PerGame` in `GameSheet.cs:277`, `PlayerProfileViewModel.cs:128`, `RosterViewModel.cs:244`, `SeasonOutlook.cs:413`, `PlayerStatsSections.cs:85,90`; career FG%/3P% at `PlayerProfileViewModel.cs:173-174,186-187`; win% at `SeasonOutlook.cs:309,416` | ui explorer table 4 |
| F14 | P2 | Numerals missing tabular figures (`num`/`+tnum` absent): game-sheet MIN/REB/AST `SeasonView.axaml:23-26`, recent form `PlayerProfileView.axaml:114-118`, career `:283-293`, standings `SeasonView.axaml:254-257`. Profile stat values are **italic Black 19 pt** (`PlayerProfileView.axaml:154,186`), against the "upright, never italic" rule (`Controls.axaml:30`). There are no spacing tokens | ui explorer table 3 |
| F15 | P3 | LiveCharts2 is referenced (`BallGM.Client.Avalonia.csproj:14`) but has **zero instances**. Every existing chart is hand-drawn (ribbon, `ProgressBar.attr`) | — |
| F16 | P3 | There are no property-based tests (seed loops only) and no perf test of any kind | domain/engine explorer tables |
| F17 | P3 | `RecentGameCount = 5` is hard-coded, so the profile can't show a last-N game log | `LeagueSession.Players.cs:14` |

---

## 5. Metrics ranked by the GM decision each informs

A GM in this game makes eight recurring decisions. A metric earns its place by moving one of them.
Tier 1 is what a GM must see at a glance, on every surface. Tier 2 is one click away on the profile
and leaders. Tier 3 is depth for the curious.

| GM decision | Where it's made today |
|---|---|
| **R** Rotation: who starts, who plays how long | depth chart (automatic) |
| **C** Contract: re-sign, extend, how much | contracts screen, FA board |
| **T** Trade: who to target or sell | trade screen, front office |
| **F** Free agency: who to bid on | FA board |
| **S** Scheme fit: does he fit how we play | — (no scheme yet) |
| **D** Development: is he improving | profile |
| **P** Playoff readiness: contender or seller | front office, standings |
| **A** Awards and records | — |

### Box (counting) stats

| Rank | Stat | Tier | Decisions | Why it moves the decision |
|---|---|---|---|---|
| 1 | PTS with FGA (volume and the cost of it) | 1 | R C T F | The first number any GM reads. Shown alone, without FGA, it rewards chuckers |
| 2 | MIN | 1 | R | The rotation itself. Per-36 and every rate depends on it |
| 3 | TOV | 1 | R C T S | The cost side of a ball-handler. Without it AST is free, and a point guard can't be priced |
| 4 | REB, split OREB / DREB | 1 | R T S | OREB buys extra possessions, which only matters once F9 is fixed. DREB ends opponent possessions. These are separate skills with separate attribute couplings |
| 5 | AST | 1 | R C T S | Creation for others. Only meaningful once it is linked to a real teammate's make (F3) |
| 6 | 3PM / 3PA | 1 | S T F | Spacing. The 40 % attempt share makes it the biggest single scheme lever |
| 7 | STL | 1 | R T | Disruption. Has to pair with PF to show gambling |
| 8 | BLK | 1 | R T | Rim protection. Has to pair with PF |
| 9 | +/- | 1 | R P | The only on/off signal. Noisy per game, valuable per season; needs on-floor lineups |
| 10 | FTM / FTA | 2 | C S | Foul-drawing is a real skill. FT% is flat today, so it carries no player signal until it couples to an attribute |
| 11 | PF and foul-outs | 2 | R | Availability: a player who fouls out every third night has a minutes ceiling |
| 12 | FGM/FGA by zone (rim, paint, mid, corner 3, above-break 3) | 2 | S D T | Shot-diet fit. Needs a zone model; there is none today |
| 13 | GS | 3 | C | Role history in contract talks |

### Rate stats

| Rank | Rate | Formula (as it will live in `StatFormulas`) | Tier | Decisions | Note |
|---|---|---|---|---|---|
| 1 | TS% | `PTS / (2·(FGA + 0.44·FTA))` | 1 | C T F | The single best scoring-efficiency number. **Biased low today (F2)** |
| 2 | USG% | `100·((FGA + 0.44·FTA + TOV)·(TmMIN/5)) / (MIN·(TmFGA + 0.44·TmFTA + TmTOV))` | 1 | R C S | Must become **observed**, not the input weight (F8). Read it next to TS% |
| 3 | Match rating /10 | §5.1 | 1 | R D C | The at-a-glance verdict on a game; averaged, it is the season form line |
| 4 | ORtg / DRtg (team) and on/off | `100·PTS/POSS`, `POSS = FGA + 0.44·FTA − ORB + TOV` | 1 | P T | Needs TOV (F10). Player ORtg/DRtg use the on-floor estimate |
| 5 | eFG% | `(FGM + 0.5·3PM)/FGA` | 2 | S | Already in `StatFormulas` |
| 6 | TOV% | `TOV / (FGA + 0.44·FTA + TOV)` | 2 | R S | Ball security per play used |
| 7 | AST% | already in `StatFormulas.AssistPercent` | 2 | S T | Will become exact once on-floor teammate FGM is tracked |
| 8 | REB% / OREB% / DREB% | already in `StatFormulas.ReboundPercent` | 2 | S | The denominator must count **available** boards, team rebounds included (F1) |
| 9 | STL% | `100·STL·(TmMIN/5) / (MIN·OppPOSS)` | 2 | R | |
| 10 | BLK% | `100·BLK·(TmMIN/5) / (MIN·(OppFGA − Opp3PA))` | 2 | R | Twos only: threes are rarely blocked. Leave Phase 2 free to model 3PA blocks |
| 11 | Team four factors | eFG%, TOV%, ORB%, FT/FGA, for and against | 2 | P S | The team-level explanation of a record: "why are we 20-30?" |
| 12 | 3PAr, FTr | already in `StatFormulas` | 3 | S | |
| 13 | Per-36 of every counting stat | `StatFormulas.Per36` | — | R | A toggle, not a separate stat |

### 5.1 Match rating /10 — formula and breakdown

Football Manager's rating is a 1–10 number that most of the time lands between 6.0 and 8.0, and a
player reads it in one glance. Basketball's closest established equivalent is Hollinger's **Game
Score**. The proposal below uses it as the backbone, normalises it for minutes so a bench player
isn't marked down for playing 12 minutes, and adds an on-court impact term.

**Step 1 — production, in integer tenths of a Game Score point.** Each family is one named term, and
each family is one row of the breakdown:

```
Scoring      = 10·PTS + 4·FGM − 7·FGA − 4·(FTA − FTM)
Rebounding   =  7·OREB + 3·DREB
Playmaking   =  7·AST − 10·TOV
Defence      = 10·STL + 7·BLK
Discipline   = −4·PF  (− FoulOutPenalty if fouled out)
Production   = Scoring + Rebounding + Playmaking + Defence + Discipline     (= 10 × Game Score)
```

**Step 2 — production over expectation.**
`Expected = BaselineProductionPerMinute × MIN`. The baseline is the league's mean Game Score per
minute, a named Rules constant measured from the engine and overridable by the ruleset.
`Surplus = Production − Expected`, still in tenths.

**Step 3 — impact.** `Impact = clamp(PlusMinus × ImpactPerPoint, ±MaxImpact)`. This is bounded so that a
player who was on the floor for a blowout can't be rated well purely for being there.

**Step 4 — shrink and map to the 1–10 scale, in hundredths.**

```
Raw     = Surplus × RatingPerProductionTenth + Impact
Shrunk  = Raw × MIN / (MIN + ShrinkMinutes)          (few minutes ⇒ pulled toward the anchor)
Rating  = clamp(AnchorRating + Shrunk, 100, 1000)    (AnchorRating ≈ 650, i.e. 6.5)
```

`Rating` is shown to one decimal. A player with 0 minutes has no rating (`—`), not a 6.5.
`RatingPerProductionTenth`, `ShrinkMinutes`, `ImpactPerPoint`, `MaxImpact` and `AnchorRating` are
named and bounded, and they are tuned so the league's per-game rating has mean ≈ 6.6 and SD ≈ 0.8
with a long right tail. A 40-point night is a 9-something; a 0-for-9 is a 4-something.

**Explainable breakdown.** The pill opens a breakdown listing every family's contribution in rating
points, e.g. `Scoring +1.2 · Rebounding +0.3 · Playmaking −0.2 · Defence +0.4 · Discipline −0.1 ·
Impact +0.3 · minutes 34 → 7.9`. The terms sum exactly to the shown value: the shrink is applied per
term and the rounding remainder goes to the largest term. This is the same "named terms, never a
mystery number" principle the match engine and `PreferenceModel` already follow.

**Pill colour bands.** These use status colours, never team paint, and always show the number
itself, so the colour is never the only signal.

| Band | Range | Token |
|---|---|---|
| Outstanding | ≥ 8.0 | `GoodColor` |
| Good | 7.0–7.9 | `GoodSoft` |
| Average | 6.0–6.9 | neutral (`Bg3`) |
| Poor | < 6.0 | `BadColor` |

**Where it lives (proposal; Phase 2 to confirm).** `MatchRatingCalculator` in `BallGM.Rules.Seasons`
is a pure function of a `PlayerStatLine` plus the ruleset constants. The rating is **derived, never
stored**, just as `Overall` is never stored on `PlayerRating`, so tuning the constants never needs a
save migration. `StatFormulas` exposes it to the client.

---

## 6. Root causes, clustered for Phase 2

- **A. Events recorded after the fact.** Boards, assists and FGA are booked before or after the event
  that produces them, not at it (F1, F2, F3, F9). Fixing this is the precondition for every other
  stat. STL must equal opponent TOV and BLK must be a subset of missed FGA, and neither identity can
  hold while misses are a floored aggregate.
- **B. One scalar drives everything.** Only `Overall` (plus position) reaches the engine. Every new
  event needs its attribute coupling (`targets.json` → `expected_rating_couplings`: height→block,
  speed→steal, strength→ORB, passing→turnover, …), or each new stat becomes Overall wearing a
  different hat.
- **C. No lineups.** +/-, true on-floor AST%/REB%, observed USG% per stint, and foul-outs all need to
  know who was on the floor for each possession.
- **D. Read models narrower than the domain.** D1–D5 are four separate DTO shapes that each pick a
  different subset. One shared stat-line read model, carried by box score, season totals, game log
  and leaders, removes the drift structurally.
- **E. Persistence gaps.** D6 (career history) and F5 (postseason mixing) must be fixed before
  season-level stats become trustworthy history.
- **F. UI system.** There are no shared stat components, tabular numerals are missing in places, and
  some rate maths lives in view models (F13, F14). Every new surface would otherwise repeat these.

---

## 7. Questions for review (answers shape Phase 2)

1. **Baseline.** Should the post-fix first full `/sim-audit` (Batteries 1–7) be the "no new
   failures" baseline, given today's halts at Battery 1?
2. **Suite row 4-reb.** Restate it as the strict identity, with team rebounds and trip-ending FT
   misses recorded on the box score. It has been passing vacuously.
3. **Career-history save loss (D6).** Fix it inside this goal (as part of the persistence slice) or
   as a separate `fix:` commit first? Recommended: a separate commit first, because it is a live bug.
4. **Postseason (F5).** Should season totals be regular-season only, with playoff totals kept and
   shown separately? Recommended: yes, as every real stats site does.
5. **Front office column.** "Every stat surface" includes the front office, which shows no stats
   today. Recommended: show a compact production line (rating, PTS/REB/AST/TS%) on trade and FA
   targets, read-only. Whether AI valuation should *use* stats (F12) is a Milestone 9 change, kept
   out of scope here.
6. **Shot zones.** Should five zones (rim, short mid, long mid, corner 3, above-break 3) be modelled
   in the engine? That is needed for a real shot chart. The alternative is a chart over the existing
   2-vs-3 split, which would be thin. Recommended: model the zones.

---

# Phase 2 — design

Draft for approval. The §7 questions are answered here with the recommended option. Each one is marked
**(assumed)** and can be overridden at plan approval.

- Q1 **(assumed):** the first full `/sim-audit 1–7` after the event-loop fix is the baseline.
- Q2 **(assumed):** row 4-reb is restated as the strict identity.
- Q3 **(assumed):** career history ships as a separate `fix:` commit first.
- Q4 **(assumed):** season totals are regular-season only, and postseason totals are kept separately.
- Q5 **(assumed):** the front office shows a read-only production line on targets. AI valuation stays on Overall (Milestone 9).
- Q6 **(assumed):** five shot zones are modelled.

## 8. Engine design — one event per thing that happens

The possession loop is rewritten once (patch P-7). Every later stat hangs off that same loop, so each
one is recorded at the moment it happens, by construction. The order of patches and the frozen rows
come from `/sim-patch` (§8.3).

### 8.1 The possession, in order

Home and away possessions now **alternate**; today each side plays all of its possessions in a block.
The possession count is still one shared draw (`BasePossessionsPerGame` ± `PossessionSpread`).
`StintPlanner` (P-8) supplies the five players on the floor for each possession.

```
for each possession (offence O, defence D, on-floor fives o5/d5, quarter q):
  1 turnover?    P = BaseTurnoverRate ± passing term ± (d5 − o5) term   → TOV to handler
                   steal?  StealShareOfTurnovers → STL to a d5 player (speed-weighted)
                   offensive foul?  OffensiveFoulShareOfTurnovers → also PF on the handler
  2 non-shooting foul?  NonShootingFoulRate → PF on a d5 player; in the bonus → 2 FTs
  3 shot: shooter by usage; zone by the shooter's zone mix; make% = zone base + team swing
          + shooter offset + night variance
  4 fouled?  miss → FT trip with NO FGA charged; make → and-one
  5 missed, unfouled → block?  BLK to a d5 player (height-weighted); maybe out of bounds
  6 every live miss → exactly one of: OREB (o5, possession continues at step 3) /
          DREB (d5) / team rebound
  7 make → assist?  AssistChance(zone) → AST to an o5 teammate ≠ shooter (passing-weighted)
  8 points scored → +p to each of o5, −p to each of d5 (plus/minus)
```

### 8.2 The events: coupling, term, target, identity

"Composite" always means one side's on-floor mean of an attribute minus the other side's. Strength
therefore enters only as a difference between the sides. `Overall` is never touched. Each
player-level weight is the attribute minus the team mean, so it changes who is credited, not how
much the team totals.

| Event | Attribute coupling (sign) | Named term(s) in `MatchModelBounds` (bp unless noted) | Calibration target (`targets.json`) | Identity (enforced in `BoxScore.Create`) |
|---|---|---|---|---|
| Turnover | handler **Passing (−)**; defence Speed+LateralQuickness composite (+) | `BaseTurnoverRate` ≈1,280; `TurnoverPassingSwing` ±300; `MaximumTurnoverStrengthSwing` ±250; clamp `Min/MaxTurnoverRate` | tov_pct 0.128 [0.120, 0.140] | box-score possessions `FGA + 0.44·FTA − OREB + TOV` within ±1.5 of true |
| Steal | stealer weight **Speed + LateralQuickness (+)** | `StealShareOfTurnovers` ≈5,500 | stl/g 7.4 [6.6, 8.2] | **Σ STL_A ≤ Σ TOV_B** (= opponent live-ball TOV) |
| Block | blocker weight **Height (+)**; zone matters (rim ≫ three) | `BlockShareOfMissedRimAttempts` ≈1,300, `…Paint` ≈700, `…Mid` ≈350, `…Three` ≈150; `BlockedOutOfBoundsShare` ≈3,000 | blk/g 4.9 [4.2, 5.6] | **Σ BLK_A ≤ Σ (FGA − FGM)_B**; a blocked shot is always an unfouled missed FGA |
| Off. rebound | offence-minus-defence composite of **Height + Strength (+)**; rebounder weight Height+Strength (+) and position | `OffensiveReboundRate` ≈2,650; `ReboundStrengthSwing` ±300 | orb_pct 0.265 [0.25, 0.29] | **Σ live misses_A = OREB_A + TeamOREB_A + DREB_B + TeamDREB_B** (live misses = (FGA−FGM) + FT trips ending in a miss) |
| Def. rebound | the complement of the above | `TeamReboundShare` ≈500 | drb share = 1 − orb_pct | same identity; REB = live misses − team rebounds |
| Assist | assister weight **Passing (+)** × position; never the shooter | `AssistChanceRim` ≈5,000, `…Paint` ≈5,500, `…Mid` ≈6,000, `…Three` ≈8,000 | ast/fgm 0.61 [0.57, 0.65] | **AST_A ≤ FGM_A − (unassisted makes)**; per make, assister ≠ shooter |
| Personal foul | fouler weight **(100 − LateralQuickness) (+)** × minutes | `NonShootingFoulRate` ≈900; `OffensiveFoulShareOfTurnovers` ≈1,200; existing `ShootingFoulChanceOnMiss` retuned down | pf/g 19.5 [17.5, 21.5]; ft_per_fga held at 0.22 | **PF ≤ `personalFoulLimit` (ruleset, default 6); FouledOut ⇔ PF = limit; no minutes after a foul-out** |
| Shot zone | zone mix by **Height**: rim/paint (+), three (−) | `ZoneShareRim/Paint/Mid/Corner3/AboveBreak3` (sum 10,000; threes total `ThreePointAttemptShare` 4,000) | fg3a_per_fga 0.40; efg_pct 0.545 | **Σ zone FGA = FGA; Corner3A + AboveBreak3A = 3PA** |
| Zone make% | shooter offset `ZoneHeightSwing`: + at the rim, − from three (zero-mean) | `ZoneBasePercentage*`; existing `FieldGoalPercentSwingAtMaximumStrength` and `GameShootingVarianceRange` unchanged in role | efg 0.545, ts 0.575 | the existing points identity, unchanged |
| Plus/minus | — (lineups) | — (deterministic `StintPlanner`, `SubstitutionCheckpointPossessions` ≈6) | — | **Σ PlusMinus_A = 5·(PTS_A − PTS_B)**; **Σ MIN_A = 240 + 25·OT** |
| Usage | — | — (now **observed**) | yoy usg r (battery 5) | Σ USG% per team = 100 (largest remainder of observed `FGA + 0.44·FTA + TOV`) |

**Deferred, on purpose.** There is no shooting attribute, so FT% stays flat and three-point skill can
only come from (negative) Height. Coupling FT% to an unrelated attribute would invent a fact. A
`Shooting` attribute changes the rating schema and PCA row 11, so it is a follow-up, not part of this
goal.

### 8.3 Patch order and frozen rows

The full plan (per-patch classification, predictions, falsification and risk) is in the `/sim-patch`
output for `reports/sim-audit-4d6d61ff…`. In summary:

- **Order:** S0 (non-engine fixes) → P-7 event loop → recalibrate → P-8 stints/+/- → P-9 TOV/STL →
  joint recalibrate → P-10 BLK → P-11 PF → joint recalibrate → P-12 zones → full recalibrate.
- After the full recalibration, the first full `/sim-audit 1–7` run becomes the new baseline.

Row 4-reb is restated as strict. Rows 13–22 are added:

| ID | Check | Band |
|---|---|---|
| 13-tov | tov_pct | [0.120, 0.140] |
| 14-stl | stl/g | [6.6, 8.2] |
| 15-blk | blk/g | [4.2, 5.6] |
| 16-pf | pf/g | [17.5, 21.5] |
| 17-ast | ast/fgm | [0.57, 0.65] |
| 18-orb | orb_pct | [0.25, 0.29] |
| 19-ids | STL ≤ opp TOV, BLK ≤ opp missed FGA, Σ±=5·margin, PF ≤ limit | exact |
| 20-fg3a | fg3a_per_fga | [0.33, 0.44] |
| 21-replay | same seed ⇒ identical box score, every field | exact |
| 22-time | season sim time (bench, M1) | ≤ 16.9 ms |

**What is deliberately not fixed:**

- **The shared possession draw.** It keeps pace variance from leaking into the margin.
- **The night-variance term.** It is the margin tail.
- **The 6-OT terminal rule.**
- **The usage clamp [55, 210].**

### 8.4 Determinism and speed

- **One RNG per game, as today.** The new draws are made in a fixed order inside the possession, so
  `SeedMixer.Mix(seasonSeed, gameId)` still replays a game exactly.
- **`StintPlanner` draws nothing.** Lineups are deterministic.
- **Replay test.** A new same-seed test compares *every* `PlayerStatLine` and `TeamStatLine` field,
  plus injuries.
- **Speed.** The hot loop allocates nothing:
  - arrays are pre-sized on `Side`;
  - the stint table is computed once per game;
  - there is no LINQ inside the possession.
- **Measuring speed.** The bench runs after every engine slice, and the budget is 16.9 ms per season.
  Engine time is today a fraction of the 13.5 ms; the rest is box-score construction, standings and
  recording. If a slice goes over budget, the first lever is to shrink `BoxScore.Create`'s LINQ
  validation, not to drop events.

## 9. Domain, save and Application

**Domain.**

- `PlayerStatLine` gains:
  - `Steals`, `Blocks`, `Turnovers`, `PersonalFouls`, `PlusMinus` (signed) and `FouledOut`;
  - a `ShotZoneLine` of 5 zones × (FGA, FGM).
- Construction checks gain `3PM ≤ FGM` (F6) and the zone sums.
- New `TeamStatLine` per side: `TeamOffensiveRebounds`, `TeamDefensiveRebounds` and
  `LiveFreeThrowMisses`. `BoxScore` gains it, plus `OvertimePeriods`.
- `BoxScore.Create` enforces every identity in §8.2, each with its own `box_score.*` code.
- `PlayerSeasonStatLine` becomes the full counting line, split regular season / postseason (F5), so
  the port no longer drops anything (D1).
- **Match rating:** `StatFormulas.MatchRating(line)` returns a `MatchRating` (value plus per-family
  breakdown). It is derived and never stored. Its constants live in a named `MatchRatingScale`, and
  the bands come from the same place (§5.1).

**Save.**

| Envelope | Change |
|---|---|
| `SeasonEnvelope` | 3 → 4: the new fields, the team line, the zones and the OT count |
| `SaveGameEnvelope` | 2 → 3: `CareerHistory` persisted, typed for the new counting stats (D6) |
| Ruleset | 10 → 11 (S5): `personalFoulLimit` and the bonus thresholds (an optional `gameRules` section). 11 → 12 (S8): `statLeaders` qualifiers (`minimumGamesShareBasisPoints` 7,000, `minimumMinutesPerGame` 15 for the per-36 boards, plus make minimums per game of the season for FG%/3P%/FT%: 3.66 / 1.00 / 1.52, stored as hundredths). As built, the games are counted against the season's length so far, not the current team's, so a traded player is not penalised |

All three follow the current policy: an older file is refused, not migrated.

**Application.** One shared read model, `StatLine`, replaces the four ad-hoc shapes.

- `StatLine` holds every counting stat plus GP/GS.
- `BoxScoreLine`, `PlayerSeasonTotals`, `RecentGameLine` and the leaders and stats rows are built on
  it, which removes drop points D2–D5 structurally.
- New `LeagueSession` queries:
  - `GameLog(playerId, count)`;
  - `LeagueLeaders(category, basis)`;
  - `LeagueStatsTable(basis, filter)`;
  - `ShotChart(playerId)`, which returns each zone with the league's FG% from that zone;
  - `StatPercentiles(playerId)`.
- `StatFormulas` gains:
  - STL%, BLK%, TOV%, observed USG%, ORtg/DRtg (box-score possessions), team four factors and
    `MatchRating`;
  - `PerGame`/`Per36`/`Percent` overloads that every view model calls. This removes the raw arithmetic
    listed in F13.

## 10. UI design — one stat component set

**Direction.** The client already has a strong identity: arena-navy surfaces, condensed display
numerals, team paint as the only accent, and tier colours for Overall. These surfaces extend that
look rather than add a new one. Most of the new UI stays quiet: tables, tabular figures and the
existing type scale. The one expressive piece is the **shot chart**, drawn on the existing
`Court.HalfCourt` geometry with the maple court lines that are already the brand's hardwood motif.

### 10.1 Tokens (added to `Tokens.axaml`)

- **Spacing:** `Space1`–`Space6` (4 / 8 / 12 / 16 / 24 / 32), replacing the literal margins on stat
  surfaces (F14).
- **Rating bands:** `RatingOutstandingBrush` = Good, `RatingGoodBrush` = GoodSoft with Good text,
  `RatingAverageBrush` = Bg3, `RatingPoorBrush` = BadSoft with Bad text. These are status colours and
  never team paint.
- **Chart, diverging (shot chart):**
  - Cold arm `ChartCold3/2/1` = `#33508F` / `#4566BC` / `#5878DA`.
  - Neutral `ChartNeutral` = `#3E4A60`.
  - Hot arm `ChartHot1/2/3` = `#8E3D2C` / `#C5543A` / `#F08A68`.
  - **Validated** with the dataviz validator on Bg0 `#121C2C`: each arm passes as a one-hue ordinal
    ramp (monotone lightness, ΔL ≥ 0.06, light end ≥ 2:1).
  - The cold light end was re-stepped from `#7AA0EA` to `#5878DA` so it clears Info `#6DB6F2`
    (normal-vision ΔE 16.9 ≥ 15).
  - The hot arm clears Bad and Warn.
- **Chart, sequential (percentile bars, sparkline):** `ChartSequential` = `#5878DA` (the cold arm's
  top step) on a `Bg3` track. It is one hue, so it doesn't collide with status colours or tiers.

### 10.2 Controls (added to `Controls.axaml` and `Controls/`)

| Component | What it is | Used on |
|---|---|---|
| **`StatColumns` catalogue** (client, formatting only) | One entry per stat: key, header, tooltip, family, decimals, and which `StatFormulas` call formats it. Every surface takes its columns from here, so "REB" has the same header, tooltip and precision everywhere. | all tables |
| **`StatTable`** (`ItemsControl.stattable` + `Grid.thead`) | Frozen player column; tabular `num` cells; sortable headers (existing `Button.th`); zebra; a team-totals footer row with team rebounds | box score, roster, game log, leaders/stats, career |
| **`RatingPill`** (`TemplatedControl`) | The value is always printed (one decimal, tabular) and the band is a background class. A flyout breakdown lists each family's contribution in rating points; the rows sum to the value. `—` for 0 minutes. Sizes `small` (tables) and `large` (profile header). | box score, game sheet, profile, game log, front office |
| **`BasisToggle`** (three `RadioButton.chip`) | Per game / Per 36 / Totals. The choice is the session's `StatBasis`, which all tables share. | profile, roster, leaders/stats |
| **`StatPanel`** (`Border.panel`) | One panel per family: **Scoring, Rebounding, Playmaking, Defence, Discipline**. Each holds a grid of `StatCell`s (label, value, `PercentileBar`). | profile |
| **`PercentileBar`** (custom `Control`) | Fill in `ChartSequential` on a `Bg3` track, a tick at the 50th, and the label "88th" in text ink. For stats where lower is better (TOV, PF) the bar is flipped, so further right always means better, and the caption says so. | profile panels, leaders |
| **`Sparkline`** (custom `Control.Render`) | Last-N match ratings on a fixed 1–10 y-scale (comparable across players), with a faint baseline at 6.5. The last point is a ≥ 8 px dot labelled with its value. Hovering a point shows the date, opponent and rating. | profile header, game log |
| **`ShotChart`** (custom `Control.Render`) | A half-court with five zone regions. Fill is diverging: player FG% − league FG% from the same zone, three steps per arm, with ±2 pts treated as neutral. Each zone prints "48% · 12/25" and "+5" in text ink, so colour is never the only signal. Zones with < 5 attempts show "—". Below it sit a legend and a **Show as table** toggle. | profile (Shooting) |
| **`LeaderBoard`** | Per category: top 5 with rank, player, team badge and value, plus "See all" into the full `StatTable`. Rate categories show the qualifier note. | Leaders screen |
| **`FoulOutMarker`** (`Border.pill.warn` "FO") | Next to the player's name, with an icon and a tooltip; never colour alone | box score, game log |

**Charts are hand-drawn in Avalonia, not with LiveCharts2.** Three small marks don't justify the
dependency's surface area, and drawing them keeps them on the tokens. The existing LiveCharts2
reference is left untouched.

### 10.3 Surfaces

| Surface | Change |
|---|---|
| **Box score** (game sheet, final) | Pos · Player · RTG pill · MIN · PTS · FG · 3P · FT · OREB · DREB · REB · AST · STL · BLK · TOV · PF · +/-. Starters and bench as now; FO marker; a team totals row including team rebounds. Scrolls horizontally with the player column frozen. |
| **Game sheet** (preview) | Projected rotation with season per-game PTS/REB/AST/STL/BLK/TOV, +/- per game, and an average-rating pill. L5 becomes a last-5 rating sparkline. |
| **Profile** | Header: the season's average-rating pill plus a last-10 sparkline. **Stats tab:** `BasisToggle`, the five family panels with percentile bars, then the shot chart. **Game log tab** (new): last N, chosen from 5/10/20/All, with the rating pill per game. **Career tab** gains STL BLK TOV PF +/- and the average rating; pack-stated seasons fill the same columns from `metrics`. |
| **Roster** | Adds RTG (season average), STL, BLK, TOV, +/- and `BasisToggle`; every column sortable |
| **Leaders and season stats** (new, under the League nav group) | Tab 1 **Leaders**: a `LeaderBoard` grid by family, with totals / per game / rates; qualifiers come from the ruleset. Tab 2 **Player stats**: the full league `StatTable`, filterable by team and position, sortable, with `BasisToggle`. |
| **Front office** | Each trade and free-agent target row gets a read-only production line: RTG pill · PTS/REB/AST per game · TS% |

### 10.4 Copy (design:ux-copy)

The copy uses sentence case and the same word for the same thing on every screen. Player pronouns
are avoided wherever the sentence allows.

- **Panel titles:** Scoring · Rebounding · Playmaking · Defence · Discipline.
- **Rating pill tooltip:** "Match rating 7.9. What this player added in 34 minutes, compared with an
  average player in the same minutes."
- **Breakdown flyout:**
  - Title: "How this rating was built".
  - Rows: "Scoring +1.2", "Rebounding +0.3", "Playmaking −0.2", "Defence +0.4", "Discipline −0.1",
    "On-court impact +0.3".
  - Footnote: "Everyone starts at 6.5. Short stints count for less."
- **Basis toggle:** Per game · Per 36 · Totals.
- **Percentile caption:** "Percentile among qualified players this season. 50th is league average."
  For lower-is-better stats it adds: "Fewer is better, so the bar is flipped."
- **Game log, empty:** "No games yet. The log fills in after this player's first simulated game this
  season."
- **Shot chart:**
  - Caption: "Field-goal percentage by zone, compared with the league from the same spot."
  - Legend: "Colder than league", "League average (±2 pts)", "Hotter than league".
  - Thin zone: "Fewer than 5 attempts."
- **Leaders, qualifier note:** "To qualify, a player must appear in 70% of the team's games.
  Percentage leaders also need 3.7 field goals, 1.0 three or 1.5 free throws made per team game."
- **Leaders, empty:** "No qualified players yet. Leaders appear once teams have played a few games."
- **Foul-out marker:** "FO". Tooltip: "Fouled out: 6 personal fouls, 3rd quarter."

## 11. Change-control ask (one request)

| Item | Change | Compatibility |
|---|---|---|
| Save schema | `SeasonEnvelope` 3 → 4 (and +1 per stat slice that adds fields: 4 → 8 across S1–S7) | older saves refused, not migrated (current policy) |
| Save schema | `SaveGameEnvelope` 2 → 3 (career history) | older saves refused |
| Ruleset schema | 10 → 11 (`gameRules`, S5), then 11 → 12 (`statLeaders`, S8); both sections optional, with documented defaults | older files refused with the existing "bump the number" hint — including the ruleset a save carries, so a save from before a bump does not load |
| Application API | `ISeasonEngine.PlayerSeasonStats` returns the full split line; `BoxScoreLine`/`PlayerSeasonTotals`/`RecentGameLine` rebuilt on the shared `StatLine`; new `LeagueSession` queries (§9); `LeagueConfiguration` gains game rules and leader qualifiers; `StatFormulas` additions | consumed by the client and integration tests only |
| Simulation contract | `MatchSetup` gains the foul limit and bonus threshold; `MatchModelBounds` gains the §8.2 terms | internal |
| Packages | **none.** Property tests are hand-rolled seeded loops, as today; charts are hand-drawn. | — |

## 12. Vertical slices (each one is its own commit)

Every slice runs `/sim-regress`, `verify-dotnet.sh`, Zafiro screenshots of every stat surface,
the accessibility review and the design critique, `/code-review high` and `/simplify`, in that order,
then commits.

| Slice | Engine | Domain / save | Application | UI |
|---|---|---|---|---|
| S0a `fix:` | — | `SaveGameEnvelope` v3, career history persisted | — | — |
| S0b `fix:` | — | season line split by phase | regular-season totals | — |
| S1 Rebounding + scoring truth | P-7 event loop, FGA after the foul, rebounds and assists as events, team rebounds | `TeamStatLine`, strict REB identity, 3PM ≤ FGM, Season v4 | shared `StatLine`; D1–D5 removed | `StatColumns`, `StatTable`, tokens; box score, roster, game sheet and profile show FG/3P/FT/OREB/DREB everywhere |
| S2 Impact | P-8 `StintPlanner`, minutes from stints, +/- | `PlusMinus`, Σ identities, OT count, v5 | +/- in `StatLine` | +/- on every surface |
| S3 Playmaking + defence I | P-9 TOV, STL, observed USG | v6 | TOV%, STL%, USG%, ORtg/DRtg, four factors | `StatPanel` families, `BasisToggle` |
| S4 Defence II | P-10 BLK | v7 | BLK% | blocks on every surface |
| S5 Discipline | P-11 PF, bonus, foul-outs; ruleset v11 `gameRules` | v8 | PF in `StatLine` | `FoulOutMarker`, Discipline panel |
| S6 Match rating | — | — | `MatchRating` + breakdown, `GameLog` | `RatingPill`, `Sparkline`, game log tab, pill in box score / game sheet / profile |
| S7 Shot zones | P-12 zones, couplings | `ShotZoneLine`, v9 | `ShotChart` query | `ShotChart` control |
| S8 Leaders + percentiles + front office | — | ruleset `statLeaders` | `LeagueLeaders`, `LeagueStatsTable`, `StatPercentiles` | Leaders screen, `PercentileBar`, front-office production line |
| S9 Close-out | full recalibrate; first `/sim-audit 1–7` | — | — | — |

S9 also covers `docs/architecture.md` and CLAUDE.md, which are updated in every slice and finalised
here.

## 13. Corrections adopted at approval (supersede §8–12 where they differ)

A design stress-test, folded into the approved plan, found the following. Where this section and
§8–12 disagree, this section wins.

- **The engine can't see attributes.** `AvailablePlayer(PlayerId, Position, int Overall)`
  (`DepthChartBuilder.cs:8`) is all it receives. It will carry `PlayerRating`, with an int overload
  that builds a flat rating for fixtures. This is a cross-project API change, approved with the plan.
- **`BaseOffensiveEfficiency` cancels out.** `EfficiencyFor` adds it (`PME:166`) and
  `FieldGoalPercentagesFor` subtracts it (`PME:231`), so it isn't a tuning knob. The scoring level
  comes from base FG%, the dead-possession/turnover rate, fouls and FT%.
- **Retune order.**
  - Removing the phantom FGA needs base FG% about 9 % lower (Two ≈ 4,850, Three ≈ 3,380) before any
    ORB.
  - S1 sets the dead-possession rate to the turnover target, so S3 only *relabels* those possessions
    as turnovers.
  - `BaseTurnoverRate` ≈ 1,380 per possession start. tov_pct is per play.
- **Identities, refined.**
  - The box-score possession estimate becomes a calibration band, not a construction check, because
    `0.44·FTA` is only an estimate.
  - The assist identity is `ΣAST ≤ ΣFGM` and `AST_p ≤ teamFGM − FGM_p`. Assister ≠ shooter is checked
    in an engine test.
  - A blocked ball that goes out of bounds is a **team offensive rebound**.
  - A missed and-one free throw is a live miss.
  - The OT terminal basket credits +/- to both on-floor fives and has no assist.
  - The foul limit is **stored on the box score**.
- **Strength as difference.**
  - Composites use each player's *shape* (`attribute − Overall`). Level enters only through the master
    term, so nothing is double-counted, and the 45-vs-90 scale test stays exact.
  - Player offsets are centred on the usage-weighted on-floor mean.
  - Weights are `clamp(100 + k·Δ, lo, hi)`, never a ratio.
- **StintPlanner.**
  1. Each player's possession target is `Hamilton(DC_i·N/48)`.
  2. A player whose remaining debt is ≥ the possessions left is forced on.
  3. Segment length is `min(6, min on-floor debt, R − max off-floor debt)`.
  4. This ends exact, within ±1 minute of the depth chart.
  5. If fewer than five eligible players remain, a fouled-out player stays on.
- **Naming.** `UsagePercent` (share of team plays) is renamed `TeamPlayShare`. USG% (normalised for
  minutes) lives in `StatFormulas`.
- **Schema numbers.**
  - Season envelope: v4 (S1), v5 (S2), v6 (S3), v7 (S4), v8 (S5), v9 (S7).
  - Save game: v3 (S0a, today's shape), then v4 (S3).
  - Ruleset: v11 (S5), v12 (S8).
- **Performance.**
  - Measure the engine's share of season time first.
  - Add `NextBasisPoint()` (bit-identical output).
  - Pick from per-stint prefix sums.
  - Validate `BoxScore.Create` in a single pass.

## 14. Finding during S0/S1: row 10 was measured on the absolute margin

The calibration probe (`scratch/calib`, 6,000 controlled 72-vs-72 games, `main` @ `7d8d303`)
measured the following:

| Measure | Value | Target (`targets.json`) |
|---|---|---|
| SD of the signed home − away margin | **21.70** | 13.5 [12.5, 14.5] |
| SD of the absolute margin | 13.03 | — |
| SD of team score | **16.80** | 12.0 [11.0, 13.0] |
| Mean absolute margin | 17.57 | about 11–12 in the real sport |
| League games, signed margin SD | 24.20 | 13.5 |

P-5's recorded "sd_game_margin ≈ 13.45" matches the SD of the *absolute* margin, which for a normal
distribution is about 0.60 × the signed SD. So row 10 has been passing on the wrong statistic, and
the hot/cold-night term (`GameShootingVarianceRange` = ±850 bp) was tuned to it.

- **Effect:** games are about 60 % noisier than the target, and team scores about 40 % noisier.
- **What changes:** S1's retune targets the signed definition. `suite.md` row 10 is restated as
  "signed home − away margin, league games".
- **Locked bands:** the `MatchModelCalibrationTests` bands that P-5 widened (mean |margin| ceiling
  17 → 19) are revisited with written reasoning.
