# Product vision

## One-sentence vision

A deeply moddable, cross-platform basketball management simulation with accurate roster-building rules, believable negotiations, explainable front-office AI, and rich multi-season history.

## Motivation

Existing basketball franchise modes often simplify or mishandle details such as:

- future draft-pick ownership;
- pick protections and swaps;
- salary-cap thresholds and apron restrictions;
- trade salary matching;
- multi-team transactions;
- injured-player transaction eligibility;
- exceptions and hard-cap triggers;
- contract negotiation behaviour;
- long-term roster planning;
- organisational identities and decision-making;
- what players think of each other, and how that changes who signs where;
- whether a general manager's word means anything;
- leagues that change shape over a career — rules, membership, and identities.

Football management games demonstrate how a sports simulation can support deep databases, long careers, community-created content, and high-quality management decisions without requiring a 3D-first experience.

## Core pillars

### Rules depth

The game should model detailed basketball roster-management rules through configurable rulesets rather than hardcoding one real-world league.

### Simulation quality

Long-term outcomes should be plausible, reproducible where required, and rich enough to create stories.

### Moddability

Users should be able to create fictional or custom leagues, teams, players, draft classes, rulesets, schedules, and presentation assets through validated data packs.

The engine is **content-neutral**: it describes leagues, it does not prefer any. The precedent is football management, where the developer ships its own content and the community authors packs for the leagues it cares about. That sets the bar for the data-pack format — a league we never anticipated (uncapped, no draft, a different postseason format, a different award set) must be expressible in a pack rather than requiring a code change. It does not change what *we* ship: every asset in this repository is fictional, and we do not bundle, host, mirror, or endorse third-party packs.

### Explainability

When a trade fails, a player rejects a contract, or an AI general manager changes direction, the game should explain why.

This extends to the softer systems as they arrive. "The player didn't believe you" is only a satisfying outcome if it is backed by a number the player can see moving — which is why a general-manager trust rating (`docs/competitive-feature-review.md` §2) is treated as an explainability feature rather than a flavour one.

### Cross-platform desktop release

The intended commercial destination is Steam on Windows, macOS, and Linux.

## Explicit non-goals for the first release

- A 3D basketball match engine
- A player-controlled career mode, and the character-progression vocabulary that goes with it (experience tiers, unlockable perks, purchased attribute increases) — see `docs/competitive-feature-review.md` §6 for the reasoning
- Licensed teams or players, or any real-world branding in shipped content
- Online multiplayer
- Mobile release
- Arbitrary executable-code mods
- Photorealistic presentation
- Every historical collective bargaining agreement at launch
