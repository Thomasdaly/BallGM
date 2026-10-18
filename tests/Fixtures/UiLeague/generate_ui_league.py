#!/usr/bin/env python3
"""Writes ui-league.json: the fictional 30-team league the UI tests and screenshots load through the
real league-pack loader (docs/ui/spec/06_QUALITY_GATES.md section 1).

Deterministic (seed 42): the same file every run. Every team, player and colour is invented. Re-run
after editing, and commit both this script and its output:

    python3 tests/Fixtures/UiLeague/generate_ui_league.py
"""
import json
import os
import random

here = os.path.dirname(os.path.abspath(__file__))
rng = random.Random(42)

# Abbreviation, team, conference, division, primary, secondary — spec 06 section 1.1, verbatim.
TEAMS = [
    ("HCM", "Harbor City Mariners", "East", "North", "#0B1D3A", "#19A7A0"),
    ("NGG", "Northgate Glaciers", "East", "North", "#FFFFFF", "#7FC8F8"),
    ("OPA", "Old Port Anchors", "East", "North", "#13294B", "#E04545"),
    ("CAP", "Capital Senators", "East", "North", "#5A1022", "#D4AF37"),
    ("GFP", "Granite Falls Peaks", "East", "North", "#4A5A6A", "#FFFFFF"),
    ("IRV", "Ironvale Forge", "East", "Central", "#000000", "#FF6A13"),
    ("MDM", "Midland Monarchs", "East", "Central", "#4B2991", "#F2C14E"),
    ("RVB", "Riverbend Otters", "East", "Central", "#1F4FD8", "#F68B1F"),
    ("CDH", "Cedar Hollow Owls", "East", "Central", "#4E3524", "#E0A526"),
    ("PRS", "Prairie Storm", "East", "Central", "#777777", "#1D2A44"),
    ("BAY", "Bayline Gulls", "East", "South", "#F4F4F4", "#D7263D"),
    ("TWT", "Tidewater Typhoons", "East", "South", "#007C80", "#0A0A0A"),
    ("SLS", "Silverlake Sirens", "East", "South", "#C2185B", "#111111"),
    ("PKF", "Palm Key Flamingos", "East", "South", "#FF5FA2", "#00A5A8"),
    ("CVE", "Cinder Valley Embers", "East", "South", "#E25822", "#2B2B2B"),
    ("FRM", "Frontier Mustangs", "West", "North", "#C8102E", "#0B2341"),
    ("PSA", "Port Saint Augustine Navigators", "West", "North", "#CB1131", "#FFFFFF"),
    ("RWR", "Redwood Rangers", "West", "North", "#0E3B22", "#EFE6D2"),
    ("PHP", "Pinehurst Pilots", "West", "North", "#3FA9F5", "#0A2342"),
    ("GWH", "Glasswater Herons", "West", "North", "#2E8B57", "#F5F5DC"),
    ("CPR", "Copper Ridge Miners", "West", "Central", "#B87333", "#111111"),
    ("LSL", "Lakeshore Lynx", "West", "Central", "#5B2A86", "#C0C5CE"),
    ("DSC", "Desert Springs Coyotes", "West", "Central", "#D9B382", "#8B2E16"),
    ("SMS", "Sun Mesa Scorch", "West", "Central", "#FFD100", "#2B2B2B"),
    ("SBR", "Stonebridge Rooks", "West", "Central", "#1C1C1C", "#9EA2A2"),
    ("NCV", "Neon Coast Voltage", "West", "South", "#B6FF00", "#000000"),
    ("BWB", "Bluewater Barracudas", "West", "South", "#0057B8", "#FFC72C"),
    ("HLS", "Highland Stags", "West", "South", "#2F4F2F", "#C9A227"),
    ("MVC", "Mesa Verde Condors", "West", "South", "#A0522D", "#F4E1C1"),
    ("SFC", "Starfall Comets", "West", "South", "#0B0B45", "#9B5DE5"),
]

# Name stress (spec 06 section 1.2): long, hyphenated, apostrophes, diacritics, a single name.
STRESS = {
    "HCM": ["Maximilian Okonkwo-Vanderbilt", "D'Shawn O'Malley-Fitzgerald", "Jean-Baptiste Kouyaté-Lindqvist"],
    "IRV": ["Tyrese-Montgomery Abernathy-Clarke"],
    "NCV": ["Đorđe Vukčević-Ilunga"],
    "SMS": ["Nguyễn Thành Long"],
    "NGG": ["Kaleo"],
}

GIVEN = [
    "Amari", "Bastien", "Caius", "Dario", "Emeka", "Felipe", "Gideon", "Hakeem", "Ilias", "Jonah", "Kofi",
    "Lior", "Mateo", "Nnamdi", "Oskar", "Pavel", "Quentin", "Rafael", "Soren", "Tariq", "Ulises", "Viktor",
    "Wendell", "Xavier", "Yannick", "Zoran", "Anders", "Bram", "Cyrus", "Desmond", "Elio", "Florian", "Goran",
    "Hollis", "Idris", "Jalen", "Kenji", "Leandro", "Marek", "Nikolai", "Odell", "Paolo", "Rune", "Sekou",
    "Teodor", "Uriah", "Vidar", "Wiley", "Yusuf", "Zeke",
]
SURNAMES = [
    "Achebe", "Bergstrom", "Castellano", "Delacroix", "Eriksen", "Fontaine", "Gallagher", "Halloran", "Ivanov",
    "Jovanovic", "Kirwan", "Lindgren", "Marchetti", "Nakashima", "Okafor", "Petrossian", "Quintero", "Rasmussen",
    "Salazar", "Thorsen", "Underwood", "Varga", "Whitcombe", "Yilmaz", "Zielinski", "Abara", "Bittencourt",
    "Cardenas", "Danilov", "Esposito", "Ferreira", "Grummond", "Haddad", "Ibekwe", "Kowalczyk", "Lachance",
    "Mbeki", "Novak", "Osei", "Pellegrino", "Radcliffe", "Sorensen", "Takahashi", "Uduak", "Vasquez", "Wakefield",
    "Adeyemi", "Brennan", "Coelho", "Duarte",
]

POSITIONS = ["PointGuard", "ShootingGuard", "SmallForward", "PowerForward", "Center"]
# One roster: a star, a second option, a core, a thinning bench. Shifted per team by its strength.
SLOT_CURVE = [86, 81, 78, 75, 73, 71, 69, 67, 65, 62, 59, 56, 52, 48]
STRENGTH = {abbr: offset for abbr, offset in zip(
    [team[0] for team in TEAMS],
    [10, 9, -3, 3, -6, 5, 2, 4, -4, -8, -1, 0, -5, 1, -7, 6, -2, -9, 3, 7, -3, 1, -6, 4, 0, 2, -10, -4, 5, -1])}

INJURIES = [
    "Sprained left ankle — out 2-3 weeks",
    "Right knee soreness — day to day",
    "Fractured shooting hand — out 6 weeks",
    "Lower back strain — out indefinitely",
    "Concussion protocol — questionable",
    "Left hamstring tightness — probable",
    "Torn ligament, right thumb — out for the season",
]

used_names = set(name for names in STRESS.values() for name in names)


def fresh_name():
    while True:
        name = f"{rng.choice(GIVEN)} {rng.choice(SURNAMES)}"
        if name not in used_names:
            used_names.add(name)
            return name


def legacy_ratings(overall, position):
    """Five legacy attributes whose integer mean is the overall, shaped by position."""
    index = POSITIONS.index(position)
    size = (index - 2) * 4  # guards small, centres big
    raw = {
        "height": overall + size + rng.randint(-3, 3),
        "speed": overall - size + rng.randint(-3, 3),
        "strength": overall + size // 2 + rng.randint(-3, 3),
        "passing": overall - size + rng.randint(-4, 4),
        "lateralQuickness": overall - size // 2 + rng.randint(-3, 3),
    }
    ratings = {key: max(1, min(99, value)) for key, value in raw.items()}
    target = overall * 5 + 2
    keys = list(ratings)
    for _ in range(400):
        diff = target - sum(ratings.values())
        if -2 <= diff <= 2:
            break
        step = 1 if diff > 0 else -1
        key = keys[rng.randrange(len(keys))]
        if 1 <= ratings[key] + step <= 99:
            ratings[key] += step
    return ratings


def salary_for(overall, service):
    floor = 3_300_000 if service >= 10 else 2_100_000 if service >= 3 else 1_150_000
    ceiling_pct = 35 if service >= 10 else 30 if service >= 7 else 25
    ceiling = 141_000_000 * ceiling_pct // 100
    scale = max(0.0, (overall - 45) / 45.0)
    amount = floor + int((ceiling - floor) * scale ** 3.0)
    return max(floor, min(ceiling, amount // 50_000 * 50_000))


def contract_for(overall, age, service, kind):
    first = salary_for(overall, service)
    years = 1 if age >= 34 else rng.randint(1, 4)
    salaries = [first + first * 5 * offset // 100 for offset in range(years)]
    contract = {"salaries": salaries}
    if years >= 2 and kind == "player":
        contract["finalSeasonOption"] = "Player"
    elif years >= 2 and kind == "team":
        contract["finalSeasonOption"] = "Team"
    return contract


def age_and_service():
    age = rng.choices(range(19, 41), weights=[6, 8, 9, 9, 9, 9, 8, 8, 7, 6, 5, 4, 3, 3, 2, 2, 1, 1, 1, 1, 1, 1])[0]
    service = max(0, min(age - 19, 15) - rng.randint(0, 1))
    return age, service


SEASON = 2040
players = []
injury_cursor = 0
for team_index, (abbr, name, conference, division, primary, secondary) in enumerate(TEAMS):
    stress = list(STRESS.get(abbr, []))
    for slot, base in enumerate(SLOT_CURVE):
        overall = max(30, min(97, base + STRENGTH[abbr] + rng.randint(-2, 2)))
        position = POSITIONS[(slot + team_index) % 5]
        player_name = stress.pop(0) if stress else fresh_name()
        age, service = age_and_service()
        if slot == 0 and abbr in ("HCM", "NGG"):
            age, service = 27, 6
        option = ["none", "none", "player", "team"][rng.randrange(4)]
        injury = None
        if (team_index * 7 + slot) % 23 == 5:
            injury = INJURIES[injury_cursor % len(INJURIES)]
            injury_cursor += 1
        player = {
            "name": player_name,
            "position": position,
            "birthDate": f"{SEASON - age:04d}-{rng.randint(1, 12):02d}-{rng.randint(1, 28):02d}",
            "seasonsOfService": service,
            "team": abbr,
            "ratings": legacy_ratings(overall, position),
            "contract": contract_for(overall, age, service, option),
        }
        if injury:
            player["injury"] = injury
        players.append(player)

# Payrolls like a real capped league: each team's total lands between about $100M and $180M (the
# strongest teams above the tax line, the weakest near the floor) whatever its rating curve says.
for abbr, *_ in TEAMS:
    roster = [player for player in players if player.get("team") == abbr]
    total = sum(player["contract"]["salaries"][0] for player in roster)
    target = 140_000_000 + STRENGTH[abbr] * 4_000_000
    for player in roster:
        salaries = player["contract"]["salaries"]
        player["contract"]["salaries"] = [max(1_150_000, salary * target // total // 50_000 * 50_000) for salary in salaries]

# 150 free agents across every tier.
for index in range(150):
    overall = rng.choice([44, 47, 50, 53, 56, 58, 61, 64, 67, 70, 73, 76, 80, 84, 89])
    position = POSITIONS[index % 5]
    age, service = age_and_service()
    player = {
        "name": fresh_name(),
        "position": position,
        "birthDate": f"{SEASON - age:04d}-{rng.randint(1, 12):02d}-{rng.randint(1, 28):02d}",
        "seasonsOfService": service,
        "ratings": legacy_ratings(overall, position),
    }
    if index % 37 == 3:
        player["injury"] = INJURIES[index % len(INJURIES)]
    players.append(player)

conferences = []
for conference in ("East", "West"):
    divisions = []
    for division in ("North", "Central", "South"):
        divisions.append({
            "name": f"{conference} {division}",
            "teams": [t[0] for t in TEAMS if t[2] == conference and t[3] == division],
        })
    conferences.append({"name": f"{conference}ern Conference", "divisions": divisions})

pack = {
    "schemaVersion": 2,
    "name": "Coastline Basketball Association",
    "season": SEASON,
    "rulesetFile": "../../../data/rulesets/calibration-league.json",
    "conferences": conferences,
    "teams": [
        {
            "key": abbr,
            "franchiseName": f"{name} Basketball Club",
            "name": name,
            "colours": {"primary": primary, "secondary": secondary},
        }
        for abbr, name, _, _, primary, secondary in TEAMS
    ],
    "players": players,
}

with open(os.path.join(here, "ui-league.json"), "w", encoding="utf-8") as handle:
    json.dump(pack, handle, indent=1, ensure_ascii=False)
    handle.write("\n")

print(f"wrote {len(TEAMS)} teams, {len(players)} players")
