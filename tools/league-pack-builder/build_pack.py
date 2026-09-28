#!/usr/bin/env python3
"""Build a BallGM league pack (schema v1) from a season of per-player box-score stats.

Content-neutral: this tool knows no league. You supply
  --players   CSV of per-player season stats (totals or per-game, see --per-mode)
  --alignment JSON naming conferences, divisions, and teams (key, name, franchiseName)
  --teams     optional CSV of team records (key, W, L) used only by --report / tuning
and it writes pack.json + ruleset.json into --out.

Output from real-world data is for private, local use only. Keep it under local-packs/
(gitignored); never commit or distribute it. See tools/league-pack-builder/README.md.

Ratings pipeline (the "tuning"):
  1. production rate: Game Score per 36 minutes, shrunk toward replacement level by minutes played,
     blended with minutes-per-game (a coach's revealed evaluation of the player);
  2. that composite is percentile-ranked and mapped through a target Overall quantile table
     (--overall-scale stretches it around the median; tune it with tune.sh against real records);
  3. five attributes are spread around the Overall from stat-derived z-scores, then re-centred so
     the attributes' integer mean -- which is how PlayerRating derives Overall -- equals the target.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import os
import statistics
import sys
from dataclasses import dataclass, field

# Column aliases: the canonical name first, then common exports' spellings.
ALIASES = {
    "name": ["PLAYER_NAME", "Player", "player", "name"],
    "team": ["TEAM_ABBREVIATION", "Tm", "Team", "team"],
    "age": ["AGE", "Age", "age"],
    "gp": ["GP", "G", "gp"],
    "min": ["MIN", "MP", "min"],
    "fgm": ["FGM", "FG", "fgm"],
    "fga": ["FGA", "fga"],
    "fg3m": ["FG3M", "3P", "fg3m"],
    "fg3a": ["FG3A", "3PA", "fg3a"],
    "ftm": ["FTM", "FT", "ftm"],
    "fta": ["FTA", "fta"],
    "oreb": ["OREB", "ORB", "oreb"],
    "dreb": ["DREB", "DRB", "dreb"],
    "ast": ["AST", "ast"],
    "stl": ["STL", "stl"],
    "blk": ["BLK", "blk"],
    "tov": ["TOV", "TO", "tov"],
    "pf": ["PF", "pf"],
    "pts": ["PTS", "pts"],
    "pos": ["POSITION", "Pos", "pos", "position"],
    "height": ["PLAYER_HEIGHT_INCHES", "height_inches", "HEIGHT_IN"],
    "weight": ["PLAYER_WEIGHT", "weight", "WEIGHT"],
    "exp": ["SEASON_EXP", "EXP", "exp", "seasons_of_service"],
    "salary": ["SALARY", "salary"],
}
STAT_KEYS = ["gp", "min", "fgm", "fga", "fg3m", "fg3a", "ftm", "fta", "oreb", "dreb", "ast", "stl", "blk", "tov", "pf", "pts"]
AGGREGATE_TEAMS = {"TOT", "2TM", "3TM", "4TM", "5TM"}

# Percentile -> Overall. Anchored to the shipped fixture's scale (star high-80s/low-90s, deep bench
# mid-50s) so the match engine, calibrated on that scale, sees a familiar spread.
OVERALL_QUANTILES = [(0.00, 42), (0.10, 52), (0.30, 59), (0.50, 64), (0.70, 70), (0.85, 76), (0.95, 83), (0.99, 90), (1.00, 95)]

POSITIONS = ["PointGuard", "ShootingGuard", "SmallForward", "PowerForward", "Center"]


@dataclass
class Row:
    name: str
    team: str
    age: int
    stats: dict
    pos: str | None = None
    height: float | None = None
    weight: float | None = None
    exp: int | None = None
    salary: int | None = None
    derived: dict = field(default_factory=dict)


def resolve_columns(header):
    found = {}
    for key, options in ALIASES.items():
        for option in options:
            if option in header:
                found[key] = option
                break
    missing = [key for key in ["name", "team", "age", "gp", "min", "fgm", "fga", "ftm", "fta", "ast", "stl", "blk", "tov", "pts"] if key not in found]
    if missing:
        sys.exit(f"players CSV is missing required columns: {missing} (see ALIASES in build_pack.py)")
    return found


def number(value, default=0.0):
    try:
        return float(value) if value not in (None, "") else default
    except ValueError:
        return default


def read_players(path, per_mode):
    with open(path, newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        columns = resolve_columns(reader.fieldnames or [])
        rows_by_name: dict[str, list[Row]] = {}
        for raw in reader:
            name = raw[columns["name"]].strip()
            if not name or name.lower() == "player":
                continue
            gp = number(raw[columns["gp"]])
            stats = {}
            for key in STAT_KEYS:
                value = number(raw.get(columns.get(key, ""), 0))
                stats[key] = value * gp if per_mode == "per-game" and key != "gp" else value
            row = Row(name=name, team=raw[columns["team"]].strip(), age=int(number(raw[columns["age"]], 25)), stats=stats)
            if "pos" in columns:
                row.pos = raw[columns["pos"]].strip() or None
            if "height" in columns:
                row.height = number(raw[columns["height"]], None)
            if "weight" in columns:
                row.weight = number(raw[columns["weight"]], None)
            if "exp" in columns:
                exp = raw[columns["exp"]].strip()
                row.exp = 0 if exp in ("", "R") else int(number(exp, 0))
            if "salary" in columns:
                row.salary = int(number(raw[columns["salary"]].replace("$", "").replace(",", ""), 0)) or None
            rows_by_name.setdefault(name, []).append(row)
    return [combine(rows) for rows in rows_by_name.values()]


def combine(rows: list[Row]) -> Row:
    """One player traded mid-season: stats from an aggregate row (TOT/2TM) if the export has one,
    otherwise summed across team rows; the team is the last team row, where the season ended."""
    aggregate = next((row for row in rows if row.team in AGGREGATE_TEAMS), None)
    team_rows = [row for row in rows if row.team not in AGGREGATE_TEAMS]
    result = aggregate or team_rows[0]
    if aggregate is None and len(team_rows) > 1:
        result.stats = {key: sum(row.stats[key] for row in team_rows) for key in STAT_KEYS}
    if team_rows:
        result.team = team_rows[-1].team
    return result


def game_score(s):
    return (s["pts"] + 0.4 * s["fgm"] - 0.7 * s["fga"] - 0.4 * (s["fta"] - s["ftm"]) + 0.7 * s["oreb"]
            + 0.3 * s["dreb"] + s["stl"] + 0.7 * s["ast"] + 0.7 * s["blk"] - 0.4 * s["pf"] - s["tov"])


def zscores(values):
    mean = statistics.fmean(values)
    sd = statistics.pstdev(values) or 1.0
    return [(value - mean) / sd for value in values]


def quantile_overall(percentile, scale):
    for (p0, o0), (p1, o1) in zip(OVERALL_QUANTILES, OVERALL_QUANTILES[1:]):
        if percentile <= p1:
            base = o0 + (o1 - o0) * ((percentile - p0) / (p1 - p0) if p1 > p0 else 0)
            break
    median = 64
    return median + (base - median) * scale


def derive(rows, args, team_offsets):
    for row in rows:
        s = row.stats
        minutes = max(s["min"], 1.0)
        rate = game_score(s) * 36.0 / minutes
        shrink = args.shrink_minutes
        replacement = args.replacement_gmsc36
        row.derived["rate"] = (rate * s["min"] + replacement * shrink) / (s["min"] + shrink)
        row.derived["mpg"] = s["min"] / max(s["gp"], 1.0)
        per36 = lambda key: s[key] * 36.0 / minutes
        row.derived.update(
            ast36=per36("ast"), stl36=per36("stl"), blk36=per36("blk"), reb36=per36("oreb") + per36("dreb"),
            oreb36=per36("oreb"), fta36=per36("fta"), fg3a_share=s["fg3a"] / max(s["fga"], 1.0), tov36=per36("tov"))

    rate_z = zscores([row.derived["rate"] for row in rows])
    mpg_z = zscores([row.derived["mpg"] for row in rows])
    composite = [args.rate_weight * r + (1 - args.rate_weight) * m for r, m in zip(rate_z, mpg_z)]
    order = sorted(range(len(rows)), key=lambda index: composite[index])
    for rank, index in enumerate(order):
        percentile = rank / max(len(rows) - 1, 1)
        # A fitted per-team offset (tune.py --fit-records) carries what a box score cannot see --
        # defence, scheme, fit -- onto the players who produced the team's real record.
        offset = team_offsets.get(rows[index].team, 0)
        rows[index].derived["overall"] = int(round(min(99, max(25, quantile_overall(percentile, args.overall_scale) + offset))))

    # Size proxy: real height if the CSV has it, otherwise rebounding/blocks against assists/threes.
    if all(row.height for row in rows):
        size_z = zscores([row.height for row in rows])
    else:
        size_raw = [row.derived["reb36"] + 1.5 * row.derived["blk36"] - 0.6 * row.derived["ast36"] - 4 * row.derived["fg3a_share"] for row in rows]
        size_z = zscores(size_raw)
    weight_z = zscores([row.weight for row in rows]) if all(row.weight for row in rows) else size_z
    ast_z = zscores([row.derived["ast36"] - 0.3 * row.derived["tov36"] for row in rows])
    stl_z = zscores([row.derived["stl36"] for row in rows])
    phys_z = zscores([row.derived["oreb36"] + 0.4 * row.derived["fta36"] for row in rows])

    for index, row in enumerate(rows):
        row.derived["size_z"] = size_z[index]
        raw = {
            "height": size_z[index],
            "strength": 0.5 * weight_z[index] + 0.5 * phys_z[index],
            "speed": -0.6 * size_z[index] + 0.4 * stl_z[index],
            "passing": ast_z[index],
            "lateralQuickness": -0.5 * size_z[index] + 0.5 * stl_z[index],
        }
        row.derived["ratings"] = spread_attributes(row.derived["overall"], raw, args.attribute_spread)
        row.derived["position"] = position_for(row, size_z[index])


def spread_attributes(overall, raw, spread):
    """Attributes = overall + spread*z, re-centred so their integer mean is exactly the overall."""
    keys = list(raw)
    mean_raw = statistics.fmean(raw.values())
    values = {key: overall + spread * (raw[key] - mean_raw) for key in keys}
    ints = {key: max(0, min(100, int(round(value)))) for key, value in values.items()}
    target_sum = overall * 5 + 2  # PlayerRating.Overall is sum // 5; aim mid-bucket
    for _ in range(200):
        diff = target_sum - sum(ints.values())
        if -2 <= diff <= 2:
            break
        step = 1 if diff > 0 else -1
        movable = [key for key in keys if 0 < ints[key] + step <= 100]
        if not movable:
            break
        ints[min(movable, key=lambda key: ints[key]) if step > 0 else max(movable, key=lambda key: ints[key])] += step
    return ints


def position_for(row, size_z):
    if row.pos:
        primary = row.pos.split("-")[0].strip().upper()
        mapping = {"PG": "PointGuard", "SG": "ShootingGuard", "SF": "SmallForward", "PF": "PowerForward", "C": "Center",
                   "G": None, "F": None}
        if mapping.get(primary):
            return mapping[primary]
        if primary == "G":
            return "PointGuard" if row.derived["ast36"] >= 5.5 else "ShootingGuard"
        if primary == "F":
            return "PowerForward" if size_z > 0.3 else "SmallForward"
    if size_z > 1.0:
        return "Center"
    if size_z > 0.3:
        return "PowerForward"
    if row.derived["ast36"] >= 6.0:
        return "PointGuard"
    if size_z < -0.4:
        return "ShootingGuard"
    return "SmallForward"


def salary_for(row, ruleset):
    if row.salary:
        return row.salary
    cap = ruleset["softCap"]
    service = row.exp if row.exp is not None and row.exp >= 0 else max(0, row.age - 20)
    floor = max(band["amount"] for band in ruleset["compensationFloorScale"] if band["minimumSeasonsOfService"] <= service)
    ceiling_pct = max(t["percentOfSoftCap"] for t in ruleset["compensationCeilingTiers"] if t["minimumSeasonsOfService"] <= service)
    ceiling = cap * ceiling_pct // 100
    quality = max(0.0, min(1.0, (row.derived["overall"] - 58) / 34))
    return int(floor + (ceiling - floor) * quality ** 2.2) // 1000 * 1000


def contract_for(row, ruleset):
    first = salary_for(row, ruleset)
    years = 1 if row.age >= 34 else 2 + (hash_int(row.name) % 3)
    raise_pct = 5
    salaries = [first + first * raise_pct * offset // 100 for offset in range(years)]
    contract = {"salaries": salaries}
    option_roll = hash_int(row.name + "opt") % 6
    if years >= 2 and option_roll == 0:
        contract["finalSeasonOption"] = "Player"
    elif years >= 2 and option_roll == 1:
        contract["finalSeasonOption"] = "Team"
    return contract


def hash_int(text):
    # Stable across runs (unlike hash()), so the same CSV builds the same pack.
    value = 2166136261
    for char in text.encode("utf-8"):
        value = ((value ^ char) * 16777619) & 0xFFFFFFFF
    return value


def build(args):
    with open(args.alignment, encoding="utf-8") as handle:
        alignment = json.load(handle)
    with open(args.ruleset_template, encoding="utf-8") as handle:
        ruleset = json.load(handle)
    # An override of null removes the rule, which is how a pack says "this league has no such rule".
    for key, value in alignment.get("rulesetOverrides", {}).items():
        if value is None:
            ruleset.pop(key, None)
        else:
            ruleset[key] = value

    rows = [row for row in read_players(args.players, args.per_mode) if row.stats["min"] >= args.minimum_minutes]
    team_offsets = {}
    if args.team_offsets:
        with open(args.team_offsets, encoding="utf-8") as handle:
            team_offsets = json.load(handle)

    # A team may list "aliases": other abbreviations the same team goes by in some exports. They
    # are resolved here and stripped, because the pack format does not carry them.
    alias_to_key = {}
    for team in alignment["teams"]:
        for alias in team.pop("aliases", []):
            alias_to_key[alias] = team["key"]
    for row in rows:
        row.team = alias_to_key.get(row.team, row.team)

    derive(rows, args, team_offsets)

    team_keys = [team["key"] for team in alignment["teams"]]
    max_roster = ruleset["maximumRosterPlayers"]
    by_team = {key: [] for key in team_keys}
    unknown_teams = set()
    for row in rows:
        if row.team in by_team:
            by_team[row.team].append(row)
        else:
            unknown_teams.add(row.team)
    if unknown_teams:
        print(f"warning: players on teams the alignment does not name, treated as free agents: {sorted(unknown_teams)}", file=sys.stderr)

    rostered, free_agents = [], []
    for key, members in by_team.items():
        members.sort(key=lambda row: row.stats["min"], reverse=True)
        rostered += [(row, key) for row in members[:max_roster]]
        free_agents += members[max_roster:]
        if len(members) < ruleset["minimumRosterPlayers"]:
            print(f"warning: team {key} has only {len(members)} players above the minutes threshold", file=sys.stderr)
    free_agents += [row for row in rows if row.team not in by_team]
    free_agents.sort(key=lambda row: row.derived["overall"], reverse=True)
    free_agents = free_agents[: args.free_agent_count]

    season_start = args.season
    players = []
    for row, key in rostered + [(row, None) for row in free_agents]:
        service = row.exp if row.exp is not None and row.exp >= 0 else max(0, row.age - 20)
        player = {
            "name": row.name,
            "position": row.derived["position"],
            "birthDate": f"{season_start - row.age:04d}-01-01",
            "seasonsOfService": service,
            "ratings": row.derived["ratings"],
        }
        if key is not None:
            player["team"] = key
            player["contract"] = contract_for(row, ruleset)
        players.append(player)

    pack = {
        "schemaVersion": 1,
        "name": alignment.get("name", "Local League Pack"),
        "season": season_start,
        "rulesetFile": "ruleset.json",
        "conferences": alignment["conferences"],
        "teams": alignment["teams"],
        "players": players,
    }

    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "pack.json"), "w", encoding="utf-8") as handle:
        json.dump(pack, handle, indent=2, ensure_ascii=False)
    with open(os.path.join(args.out, "ruleset.json"), "w", encoding="utf-8") as handle:
        json.dump(ruleset, handle, indent=2, ensure_ascii=False)

    records = {}
    if args.teams:
        names = {team["name"]: team["key"] for team in alignment["teams"]}
        keys = set(team_keys)
        records = read_team_records(args.teams, lambda text: text if text in keys else alias_to_key.get(text) or names.get(text))
        # Normalised copy, keyed like the pack, for PackCheck and tune.py to read.
        with open(os.path.join(args.out, "records.csv"), "w", encoding="utf-8") as handle:
            handle.write("key,W,L\n")
            for key, pct in records.items():
                handle.write(f"{key},{round(pct * 1000)},{1000 - round(pct * 1000)}\n")

    report(rows, by_team, rostered, free_agents, records, args.out)


def report(rows, by_team, rostered, free_agents, records, out):
    overalls = [row.derived["overall"] for row, _ in rostered]
    team_strength = {}
    for key, members in by_team.items():
        top = sorted((row.derived["overall"] for row in members), reverse=True)[:8]
        team_strength[key] = statistics.fmean(top) if top else 0
    summary = {
        "players_read": len(rows),
        "rostered": len(rostered),
        "free_agents": len(free_agents),
        "overall_mean": round(statistics.fmean(overalls), 2),
        "overall_sd": round(statistics.pstdev(overalls), 2),
        "overall_max": max(overalls),
        "top_eight_mean_by_team": {key: round(value, 1) for key, value in sorted(team_strength.items(), key=lambda item: -item[1])},
    }
    if records:
        pairs = [(team_strength[key], records[key]) for key in team_strength if key in records]
        if len(pairs) >= 3:
            xs, ys = zip(*pairs)
            summary["real_win_pct_sd"] = round(statistics.pstdev(ys), 4)
            summary["strength_vs_real_win_pct_r"] = round(statistics.correlation(xs, ys), 3)
    with open(os.path.join(out, "build-report.json"), "w", encoding="utf-8") as handle:
        json.dump(summary, handle, indent=2)
    print(json.dumps(summary, indent=2))


def read_team_records(path, resolve):
    """Real records as {teamKey: win%}. The key column may hold an abbreviation, an alias, or the
    team's full name, whichever the export uses; `resolve` maps any of them to the pack's key."""
    with open(path, newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        header = reader.fieldnames or []
        key_col = next(col for col in ["TEAM_ABBREVIATION", "key", "Tm", "Team", "TEAM_NAME"] if col in header)
        records = {}
        for raw in reader:
            wins, losses = number(raw.get("W")), number(raw.get("L"))
            key = resolve(raw[key_col].strip().rstrip("*"))
            if key and wins + losses > 0:
                records[key] = wins / (wins + losses)
        return records


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--players", required=True)
    parser.add_argument("--alignment", required=True)
    parser.add_argument("--teams")
    parser.add_argument("--season", type=int, required=True, help="the year the season starts in, e.g. 2024 for 2024-25")
    parser.add_argument("--out", required=True)
    parser.add_argument("--per-mode", choices=["totals", "per-game"], default="totals")
    parser.add_argument("--ruleset-template", default=os.path.join(repo, "data", "rulesets", "default-league.json"))
    parser.add_argument("--minimum-minutes", type=float, default=100)
    parser.add_argument("--free-agent-count", type=int, default=40)
    parser.add_argument("--overall-scale", type=float, default=1.0)
    parser.add_argument("--rate-weight", type=float, default=0.6)
    parser.add_argument("--shrink-minutes", type=float, default=400)
    parser.add_argument("--replacement-gmsc36", type=float, default=8.0)
    parser.add_argument("--attribute-spread", type=float, default=9.0)
    parser.add_argument("--team-offsets", help="JSON {teamKey: rating points} added to each rostered player's Overall")
    args = parser.parse_args()
    build(args)


if __name__ == "__main__":
    main()
