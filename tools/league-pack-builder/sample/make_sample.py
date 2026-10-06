#!/usr/bin/env python3
"""Writes a fictional 30-team sample (players.csv, teams.csv, alignment.json) for exercising
build_pack.py without any real-world data. Deterministic: same files every run."""
import csv
import json
import os
import random

here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "generated")
os.makedirs(out, exist_ok=True)
rng = random.Random(2040)

places = ["Ashford", "Brightwater", "Coldharbour", "Dunmere", "Eastvale", "Fernhollow", "Greystone", "Highmoor",
          "Ironbridge", "Juniper Bay", "Kingsreach", "Larkfield", "Marrowby", "Northgate", "Oakhaven", "Pinecrest",
          "Queensfold", "Redcliff", "Silverlake", "Thornbury", "Upton Vale", "Valemount", "Westmarch", "Yarrowdale",
          "Zephyr Point", "Amberlight", "Blackfen", "Copperfield", "Driftwood", "Elmstead"]
nicknames = ["Herons", "Comets", "Foxes", "Anvils", "Mariners", "Owls", "Stags", "Pilots", "Rooks", "Tides",
             "Lynx", "Wardens", "Bison", "Sparks", "Kites", "Pines", "Crowns", "Rams", "Otters", "Thorns",
             "Voyagers", "Summit", "Gales", "Yeomen", "Zephyrs", "Lanterns", "Ravens", "Miners", "Drifters", "Elks"]
given = ["Arlo", "Bex", "Cato", "Dario", "Eamon", "Felix", "Gideon", "Hollis", "Ivo", "Jonas", "Kian", "Lior",
         "Milo", "Nils", "Otto", "Pax", "Quill", "Rafe", "Soren", "Tove", "Ulric", "Vance", "Wren", "Xan", "Yves", "Zane"]
surnames = ["Ashdown", "Brackett", "Coldwell", "Dunleavy", "Everly", "Fairbairn", "Greaves", "Holloway", "Ingram",
            "Jessop", "Kettering", "Loxley", "Merriman", "Northcott", "Ormsby", "Pennick", "Quarles", "Rudge",
            "Stanhope", "Thackeray", "Underhill", "Varley", "Whitlock", "Yardley"]

keys = [f"T{index:02d}" for index in range(30)]
# Fictional colours, one evenly spaced hue per team, so the sample exercises pack colours too.
def hue_hex(hue, saturation, lightness):
    import colorsys
    red, green, blue = colorsys.hls_to_rgb(hue, lightness, saturation)
    return "#{:02X}{:02X}{:02X}".format(round(red * 255), round(green * 255), round(blue * 255))


teams = [
    {
        "key": key,
        "name": f"{places[i]} {nicknames[i]}",
        "franchiseName": f"{places[i]} Basketball Club",
        "colours": {"primary": hue_hex(i / 30, 0.65, 0.45), "secondary": hue_hex((i / 30 + 0.5) % 1, 0.55, 0.6)},
    }
    for i, key in enumerate(keys)
]
conferences = []
for c, conference in enumerate(["Northern", "Southern"]):
    divisions = []
    for d in range(3):
        start = c * 15 + d * 5
        divisions.append({"name": f"{conference} Division {d + 1}", "teams": keys[start:start + 5]})
    conferences.append({"name": f"{conference} Conference", "divisions": divisions})

alignment = {
    "name": "Sample Thirty-Team League",
    "conferences": conferences,
    "teams": teams,
    "rulesetOverrides": {
        "name": "Sample Thirty-Team League Rules",
        "regularSeasonGameCount": 82,
        "gamesVersusDivisionOpponent": None,
        "gamesVersusConferenceOpponent": None,
        "gamesVersusOtherConferenceOpponent": None,
        "postseasonQualifyingTeamsPerConference": 8,
        "postseasonSeriesLengths": [7, 7, 7, 7],
        "postseasonDays": 60,
        "draftClassSize": 60,
        "draftLotteryWeights": [140, 140, 140, 125, 105, 90, 75, 60, 45, 30, 20, 15, 10, 5],
    },
}
with open(os.path.join(out, "alignment.json"), "w") as handle:
    json.dump(alignment, handle, indent=2)

team_quality = {key: rng.gauss(0, 1) for key in keys}
rows, records = [], []
used = set()
for key in keys:
    for slot in range(16):
        while True:
            name = f"{rng.choice(given)} {rng.choice(surnames)}"
            if name not in used:
                used.add(name)
                break
        talent = rng.gauss(0, 1) + 0.35 * team_quality[key] - slot * 0.12
        mpg = max(3.0, min(37.0, 34 - slot * 2.4 + rng.gauss(0, 3)))
        gp = rng.randint(20, 82)
        big = rng.random() < 0.35
        m = mpg * gp
        per36 = lambda base: max(0.0, base * (1 + 0.25 * talent)) * m / 36
        fga = per36(15)
        fg3a = fga * (0.15 if big else 0.42)
        fgm = fga * (0.46 + 0.03 * talent + (0.06 if big else 0))
        fg3m = fg3a * 0.36
        fta = per36(4.5)
        rows.append({
            "PLAYER_NAME": name, "TEAM_ABBREVIATION": key, "AGE": rng.randint(20, 36), "GP": gp, "MIN": round(m),
            "FGM": round(fgm), "FGA": round(fga), "FG3M": round(fg3m), "FG3A": round(fg3a), "FTM": round(fta * 0.77),
            "FTA": round(fta), "OREB": round(per36(3.2 if big else 1.0)), "DREB": round(per36(8 if big else 4)),
            "AST": round(per36(1.8 if big else 4.5)), "STL": round(per36(1.1)), "BLK": round(per36(1.6 if big else 0.4)),
            "TOV": round(per36(2.0)), "PF": round(per36(2.8)), "PTS": round(2 * (fgm - fg3m) + 3 * fg3m + fta * 0.77),
        })
    win_pct = max(0.2, min(0.8, 0.5 + 0.12 * team_quality[key]))
    wins = round(82 * win_pct)
    records.append({"TEAM_ABBREVIATION": key, "W": wins, "L": 82 - wins})

with open(os.path.join(out, "players.csv"), "w", newline="") as handle:
    writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
    writer.writeheader()
    writer.writerows(rows)
with open(os.path.join(out, "teams.csv"), "w", newline="") as handle:
    writer = csv.DictWriter(handle, fieldnames=list(records[0]))
    writer.writeheader()
    writer.writerows(records)
print(out)
