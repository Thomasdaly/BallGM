# Product scope

## Vertical-slice target

A fictional league can be created, played for multiple seasons, saved, loaded, and modded.

The player controls one team and can:

- view roster, contracts, cap sheet, picks, injuries, and organisation information;
- propose and complete legal trades;
- receive understandable reasons for illegal trades;
- negotiate contracts;
- sign and release players;
- participate in a draft;
- simulate games and dates;
- review standings, results, statistics, transactions, and league history.

AI-controlled teams can:

- classify their competitive direction;
- manage rosters and cap space;
- value players, contracts, and draft assets;
- propose and evaluate trades;
- negotiate with free agents;
- draft players;
- make explainable decisions.

## MVP systems

1. Fictional league creation
2. Team and player domain models
3. Contracts and cap sheets
4. Draft-pick ownership
5. Pick protections and swaps
6. Trade validation and execution
7. Injuries and transaction eligibility
8. Contract negotiation
9. Free agency
10. Draft
11. Schedule and game simulation
12. Standings and postseason
13. AI general managers
14. Save/load
15. JSON data-pack/mod loading
16. Avalonia management UI

## Deferred systems

- detailed coaching/staff market;
- owner personalities and board objectives, and the directives they set;
- media and narrative generation;
- advanced scouting uncertainty;
- international leagues, and retained rights over players in them;
- historical rulesets, and rule changes adopted mid-save;
- player relationships, personality traits, and locker-room chemistry;
- general-manager trust, and promises made during negotiation;
- expansion, relocation, and rebranding during a save;
- in-season secondary competitions;
- cash as a tradeable asset;
- post-buyout free-agent market and short-term contracts;
- pre-authored draft-class playlists;
- workshop publishing;
- Steam achievements and cloud saves.

Each of the new entries above is dated in `docs/competitive-feature-review.md`; most land in Milestone 13. "Deferred" there means a recorded decision with a milestone, not an omission.

## Declined systems

Recorded so they are not re-proposed — reasoning in `docs/competitive-feature-review.md`:

- a player-controlled career mode with experience tiers, unlockable perks, and purchased attributes;
- discrete named on-court bonuses for star pairings (a continuous chemistry term does the same job without the cliff);
- hidden difficulty multipliers that are not expressible as ruleset overrides.
