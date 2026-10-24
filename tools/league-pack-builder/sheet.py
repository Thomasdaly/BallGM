"""The full player sheet (league pack schema 3) from a season of box-score stats.

Content-neutral, like the rest of the builder: it reads whatever per-player totals the CSV has and,
when a pipeline supplies them, optional extras (usage, assist/rebound percentages, shot-diet splits,
hustle counts). Every attribute is the player's Overall plus a spread times a stat composite's z-score
across the league, then re-centred so the sheet's weighted Current Ability -- which is how BallGM
derives Overall -- is exactly the target. A pack written this way loads its ratings as stated: no
generator runs at load, and a shooter's sheet says he shoots because his numbers did.

Weights, keys and bounds mirror the engine (BallGM.Domain.Players.PlayerAttributes and
PlayerSheetCodec). If those change, change them here; the loader validates every value regardless.
"""
from __future__ import annotations

import math
import statistics

# Sheet order and Current Ability weights (out of 100), as PlayerAttributes states them.
VISIBLE = [
    ("finishing", 4), ("dunking", 1), ("postScoring", 2), ("closeRange", 2), ("midRange", 3), ("threePoint", 5),
    ("freeThrow", 2), ("shotCreation", 5),
    ("ballHandling", 4), ("passing", 4), ("vision", 3), ("offBallMovement", 2), ("screenSetting", 1),
    ("perimeterDefence", 4), ("interiorDefence", 4), ("helpDefence", 3), ("stealing", 2), ("blocking", 2),
    ("defensiveDiscipline", 1),
    ("offensiveRebounding", 2), ("defensiveRebounding", 3), ("boxingOut", 1),
    ("basketballIq", 5), ("anticipation", 3), ("composure", 3), ("concentration", 2), ("workRate", 2), ("hustle", 1),
    ("aggression", 1), ("leadership", 1), ("teamwork", 2),
    ("speed", 3), ("firstStep", 3), ("lateralQuickness", 3), ("strength", 3), ("vertical", 3), ("stamina", 3),
    ("naturalFitness", 2),
]
HIDDEN = ["consistency", "bigGames", "injuryProneness", "versatility", "professionalism", "ambition", "temperament",
          "loyalty", "adaptability"]
TENDENCIES = ["touches", "shot", "drive", "postUp", "shotClose", "shotMid", "shotThree", "pullUp", "pass", "crashGlass",
              "gamble", "blockShot", "foul"]
POSITIONS = ["PointGuard", "ShootingGuard", "SmallForward", "PowerForward", "Center"]
FAMILIARITY_BY_DISTANCE = ["Natural", "Accomplished", "Competent", "Unconvincing", "Awkward"]
ATTRIBUTE_FLOOR, ATTRIBUTE_CEILING = 5, 99
# The league's middle Overall, where an average player's sheet is centred before it is shaped.
LEAGUE_MIDDLE = 64
# How far a star's strengths may be amplified, before a uniform shift, to reach his Overall.
SHAPE_AMPLIFICATION = 2.5
TENDENCY_FLOOR, TENDENCY_CEILING = 2, 98

# How far each attribute strays from a player's Overall (standard deviation of attribute - Overall),
# and how widely each tendency spreads, in the generated calibration league the match engine's
# couplings were tuned on (five loads of data/rulesets/calibration-league.json). A pack built to the
# same spreads gives the engine the shapes it knows: finishing that tracks a player's level closely,
# rebounding and shot-blocking that separate bigs from guards, a post-up game that is a specialist's.
# Re-measure if the generator changes.
ATTRIBUTE_SPREADS = {
    "finishing": 4.9,
    "dunking": 11.6,
    "postScoring": 13.2,
    "closeRange": 4.2,
    "midRange": 6.7,
    "threePoint": 9.2,
    "freeThrow": 7.4,
    "shotCreation": 8.4,
    "ballHandling": 11.7,
    "passing": 9.1,
    "vision": 8.7,
    "offBallMovement": 4.6,
    "screenSetting": 14.1,
    "perimeterDefence": 8.5,
    "interiorDefence": 14.5,
    "helpDefence": 6.9,
    "stealing": 6.2,
    "blocking": 15.3,
    "defensiveDiscipline": 4.4,
    "offensiveRebounding": 14.5,
    "defensiveRebounding": 14.1,
    "boxingOut": 13.5,
    "basketballIq": 5.7,
    "anticipation": 5.7,
    "composure": 5.6,
    "concentration": 5.7,
    "workRate": 4.6,
    "hustle": 4.8,
    "aggression": 3.8,
    "leadership": 5.8,
    "teamwork": 5.7,
    "speed": 10.6,
    "firstStep": 10.6,
    "lateralQuickness": 9,
    "strength": 12,
    "vertical": 6.2,
    "stamina": 4.8,
    "naturalFitness": 3.8,
}
# Where a box score is direct evidence the generator does not have, its attribute spreads wider:
# stamina reads minutes per game, and the depth chart moves minutes by stamina only up to
# MinutesAllocationBounds.MaximumStaminaTenths (25 points from a player's level), which the
# generator's blind draw almost never reaches.
ATTRIBUTE_SPREADS["stamina"] = 12.0

TENDENCY_MEANS = {
    "touches": 49,
    "shot": 50,
    "drive": 52,
    "postUp": 37,
    "shotClose": 53,
    "shotMid": 51,
    "shotThree": 49,
    "pullUp": 50,
    "pass": 52,
    "crashGlass": 50,
    "gamble": 51,
    "blockShot": 50,
    "foul": 50,
}
TENDENCY_SPREADS = {
    "touches": 18.2,
    "shot": 8.5,
    "drive": 16,
    "postUp": 28.9,
    "shotClose": 16.9,
    "shotMid": 10.4,
    "shotThree": 20,
    "pullUp": 12.8,
    "pass": 17.7,
    "crashGlass": 18.7,
    "gamble": 7.8,
    "blockShot": 22.9,
    "foul": 6.6,
}
WINGSPAN_OVER_HEIGHT_CM = 8

# League-typical rates a small sample is shrunk toward, and how many attempts of evidence the prior is worth.
PRIOR_3P, PRIOR_3P_ATTEMPTS = 0.355, 80
PRIOR_FT, PRIOR_FT_ATTEMPTS = 0.770, 50
PRIOR_2P, PRIOR_2P_ATTEMPTS = 0.530, 100

# Potential: rating points of headroom by age, the rest of a career's growth.
HEADROOM_BY_AGE = {19: 16, 20: 14, 21: 11, 22: 8, 23: 6, 24: 4, 25: 2}


def _z(values):
    mean = statistics.fmean(values)
    sd = statistics.pstdev(values) or 1.0
    return [(value - mean) / sd for value in values]


def _features(row):
    """Per-player rates from totals, plus any extras the pipeline attached (row.extras)."""
    s = row.stats
    minutes = max(s["min"], 1.0)
    per36 = lambda key: s[key] * 36.0 / minutes
    fg2m, fg2a = s["fgm"] - s["fg3m"], s["fga"] - s["fg3a"]
    fga = max(s["fga"], 1.0)
    extras = getattr(row, "extras", None) or {}
    features = {
        "three": (s["fg3m"] + PRIOR_3P * PRIOR_3P_ATTEMPTS) / (s["fg3a"] + PRIOR_3P_ATTEMPTS),
        "free": (s["ftm"] + PRIOR_FT * PRIOR_FT_ATTEMPTS) / (s["fta"] + PRIOR_FT_ATTEMPTS),
        "two": (fg2m + PRIOR_2P * PRIOR_2P_ATTEMPTS) / (fg2a + PRIOR_2P_ATTEMPTS),
        "three_rate": s["fg3a"] / fga,
        "ft_rate": s["fta"] / fga,
        "pts36": per36("pts"), "fga36": per36("fga"), "ast36": per36("ast"), "tov36": per36("tov"),
        "stl36": per36("stl"), "blk36": per36("blk"), "oreb36": per36("oreb"), "dreb36": per36("dreb"),
        "pf36": per36("pf"), "fta36": per36("fta"), "mpg": s["min"] / max(s["gp"], 1.0),
        "ast_to": (s["ast"] + 5) / (s["tov"] + 5),
        "games_share": s.get("games_share", s["gp"] / 82.0),
    }
    for key in ("usg", "ast_pct", "oreb_pct", "dreb_pct", "ts", "pie", "three_share", "paint_share", "mid_share",
                "unassisted_share", "fast_break_share", "deflections36", "contested2_36", "contested3_36", "charges36",
                "screen_assists36", "loose_balls36", "box_outs36"):
        if key in extras:
            features[key] = extras[key]
    return features


def sheets(rows, overall, spread=1.0, skill_weight=0.3, adjust=None, ceiling=None):
    """Full sheets for rows (Row objects with stats, height, weight, age, derived['position']).

    overall: the production Overall per row, in row order. spread scales every attribute's spread
    (ATTRIBUTE_SPREADS) together; 1.0 is the engine's calibration league. Returns one dict per row with ratings,
    hidden, potential, body, positions, traits and tendencies, ready for a schema 3+ pack, and the
    Overall each sheet was built to (row.derived['sheet_overall']).

    skill_weight blends production with skill: a sheet averages to its Overall, so a production
    Overall alone makes an efficient rim-running big (Game Score loves rebounds and dunks) a 90
    whose sheet must then say he shoots threes. Each player's skill level is his sheet's own
    Current Ability on an absolute scale (every attribute from its composite alone, centred on the
    league), mapped onto the production Overalls' spread; the Overall used is the blend of the two.

    adjust: optional rating points added to each row's blended Overall afterwards (a pipeline's
    fitted team-strength offset, which must survive the blend whole), and ceiling a soft upper bound
    (a function of the Overall) applied last.
    """
    feats = [_features(row) for row in rows]
    keys = set().union(*(f.keys() for f in feats))
    z = {}
    for key in keys:
        present = [f[key] for f in feats if key in f]
        mean = statistics.fmean(present)
        z[key] = _z([f.get(key, mean) for f in feats])
    heights = [row.height for row in rows if row.height]
    height_mean = statistics.fmean(heights) if heights else 78.0
    size = _z([row.height or height_mean for row in rows])
    weights = [row.weight for row in rows if row.weight]
    weight_mean = statistics.fmean(weights) if weights else 215.0
    mass = _z([row.weight or weight_mean for row in rows])
    age = _z([row.age for row in rows])
    per_row = [{key: z[key][i] for key in keys} for i in range(len(rows))]
    raw = [_composites(per_row[i], size[i], mass[i], age[i]) for i in range(len(rows))]
    # Composites are weighted sums of z-scores with different spreads; standardise each across the
    # league so every attribute moves the same distance for the same standing.
    standard = {name: _z([r[name] for r in raw]) for name, _ in VISIBLE}
    weight = dict(VISIBLE)
    skill = _z([sum(weight[name] * standard[name][i] for name, _ in VISIBLE) for i in range(len(rows))])
    level_mean, level_sd = statistics.fmean(overall), statistics.pstdev(overall) or 1.0
    top = max(overall)
    bound = ceiling or (lambda value: min(top, value))
    adjust = adjust or [0] * len(rows)
    targets = [max(ATTRIBUTE_FLOOR, int(round(bound((1 - skill_weight) * overall[i] + skill_weight * (level_mean + level_sd * skill[i]) + adjust[i]))))
               for i in range(len(rows))]
    for row, target in zip(rows, targets):
        row.derived["sheet_overall"] = target
    raw_tendencies = [_tendency_composites(per_row[i], size[i]) for i in range(len(rows))]
    keys = list(raw_tendencies[0])
    tendency_z = {key: _z([t[key] for t in raw_tendencies]) for key in keys}
    usages = [getattr(row, "extras", {}).get("usg") for row in rows]
    known = [usage for usage in usages if usage]
    usage_mean = statistics.fmean(known) if known else None
    return [_sheet(rows[i], targets[i], per_row[i], {name: standard[name][i] for name, _ in VISIBLE}, size[i], spread,
                   _tendency_values({key: tendency_z[key][i] for key in keys}, usages[i] / usage_mean if usages[i] and usage_mean else None))
            for i in range(len(rows))]


def _composites(f, size, mass, age):
    """Each attribute's stat composite (in z units). Extras weigh in when present; otherwise the box
    score carries it alone."""
    g = lambda key, default=0.0: f.get(key, default)
    paint = g("paint_share", 0.6 * size - 0.6 * g("three_rate"))
    mid = g("mid_share", -0.3 * g("three_rate"))
    creation = g("unassisted_share", 0.6 * g("usg", g("fga36")))
    deflect = g("deflections36", g("stl36"))
    hustle = g("loose_balls36", 0.5 * g("oreb36")) + 0.5 * g("charges36") + 0.3 * deflect
    return {
        "finishing": 0.6 * g("two") + 0.4 * paint + 0.3 * g("ft_rate"),
        "dunking": 0.5 * size + 0.4 * paint + 0.3 * g("blk36") + 0.2 * g("oreb36") - 0.2 * mass,
        "postScoring": 0.6 * size + 0.5 * paint - 0.4 * g("three_rate") + 0.2 * g("pts36"),
        "closeRange": 0.6 * g("two") + 0.4 * paint,
        "midRange": 0.5 * g("free") + 0.4 * mid + 0.2 * g("two"),
        "threePoint": 0.7 * g("three") + 0.5 * g("three_rate"),
        "freeThrow": g("free"),
        "shotCreation": 0.5 * creation + 0.4 * g("usg", g("fga36")) + 0.3 * g("pts36"),
        "ballHandling": 0.5 * g("ast36") + 0.4 * g("usg", g("fga36")) - 0.3 * size - 0.2 * g("tov36"),
        "passing": 0.7 * g("ast_pct", g("ast36")) + 0.3 * g("ast36"),
        "vision": 0.6 * g("ast_pct", g("ast36")) + 0.4 * g("ast_to"),
        "offBallMovement": 0.4 * g("ts", g("two")) + 0.3 * g("three_rate") - 0.4 * creation + 0.2 * g("pts36"),
        "screenSetting": 0.6 * g("screen_assists36", size) + 0.4 * mass,
        "perimeterDefence": 0.4 * deflect + 0.4 * g("stl36") + 0.3 * g("contested3_36") - 0.3 * size,
        "interiorDefence": 0.5 * g("blk36") + 0.4 * size + 0.3 * g("contested2_36") + 0.2 * mass,
        "helpDefence": 0.4 * g("blk36") + 0.3 * g("stl36") + 0.3 * g("charges36") + 0.2 * deflect,
        "stealing": 0.7 * g("stl36") + 0.3 * deflect,
        "blocking": g("blk36"),
        "defensiveDiscipline": -g("pf36"),
        "offensiveRebounding": g("oreb_pct", g("oreb36")),
        "defensiveRebounding": g("dreb_pct", g("dreb36")),
        "boxingOut": 0.6 * g("box_outs36", g("dreb36")) + 0.4 * mass,
        "basketballIq": 0.4 * g("ast_to") + 0.3 * g("ts", g("two")) + 0.3 * g("pie", g("pts36")),
        "anticipation": 0.5 * g("stl36") + 0.3 * deflect + 0.2 * g("ast36"),
        "composure": 0.5 * g("free") + 0.3 * g("ast_to") + 0.2 * g("ts", g("two")),
        "concentration": -0.5 * g("pf36") - 0.5 * g("tov36") + 0.3 * g("mpg"),
        "workRate": 0.6 * g("mpg") + 0.4 * hustle,
        "hustle": hustle,
        "aggression": 0.5 * g("ft_rate") + 0.3 * g("pf36") + 0.3 * paint,
        "leadership": 0.6 * age + 0.4 * g("mpg"),
        "teamwork": 0.5 * g("ast36") + 0.3 * g("screen_assists36", 0.0) - 0.3 * creation,
        "speed": -0.6 * size + 0.3 * g("stl36") + 0.2 * g("fast_break_share", 0.0),
        "firstStep": 0.4 * g("ft_rate") + 0.3 * creation - 0.4 * size + 0.2 * paint,
        "lateralQuickness": -0.5 * size + 0.3 * g("stl36") + 0.2 * deflect,
        "strength": 0.6 * mass + 0.3 * g("oreb36") + 0.2 * paint,
        "vertical": 0.4 * g("blk36") + 0.3 * g("oreb36") + 0.2 * paint - 0.3 * mass,
        "stamina": g("mpg"),
        "naturalFitness": 0.6 * g("games_share") - 0.4 * age,
    }


def _tendency_composites(f, size):
    g = lambda key, default=0.0: f.get(key, default)
    paint = g("paint_share", 0.6 * size - 0.6 * g("three_rate"))
    raw = {
        "touches": g("usg", g("fga36")),
        "shot": g("fga36"),  # the engine takes his passing off it itself
        "drive": 0.5 * g("ft_rate") + 0.4 * paint - 0.4 * size,
        "postUp": 0.6 * size + 0.5 * paint - 0.5 * g("three_rate"),
        "shotClose": paint,
        "shotMid": g("mid_share", -0.3 * g("three_rate")),
        "shotThree": g("three_share", g("three_rate")),
        "pullUp": g("unassisted_share", 0.6 * g("usg", g("fga36"))),
        "pass": g("ast_pct", g("ast36")),
        "crashGlass": g("oreb_pct", g("oreb36")),
        "gamble": 0.6 * g("stl36") + 0.4 * g("deflections36", g("stl36")),
        "blockShot": g("blk36"),
        "foul": g("pf36"),
        # Not a tendency of its own: how often he gets to the line, which the engine reads as the larger
        # of drive and post-up (see _tendency_values).
        # Beyond what his shot diet explains: the engine already draws more fouls on shots at the rim,
        # so a rim-runner's free throws are not counted twice; a guard who gets to the line from the
        # perimeter keeps his.
        "attack": 0.6 * g("ft_rate") + 0.4 * g("fta36", g("ft_rate")) - 0.6 * paint,
    }
    return raw


# MatchModelBounds.UsageTouchesDoublingPoints: touch-tendency points that double a player's share of
# the ball against his team-mates'. Re-check if the engine's constant changes.
TOUCHES_DOUBLING = 10

# The engine draws a shooting foul by the larger of a player's drive and post-up tendencies; in the
# calibration league that larger value averages 64 with a spread of 13. A box score says how often a
# player actually got to the line, so the larger of the two is set from that, and the smaller kept
# below it: a big who posts up but rarely draws a whistle is not a foul-line regular.
ATTACK_MEAN, ATTACK_SPREAD = 64, 8


def _tendency_values(standard, usage_ratio=None):
    values = {key: max(TENDENCY_FLOOR, min(TENDENCY_CEILING, int(round(TENDENCY_MEANS[key] + TENDENCY_SPREADS[key] * value))))
              for key, value in standard.items() if key != "attack"}
    if usage_ratio:
        # The engine weighs touches exponentially (doubling every TOUCHES_DOUBLING points against his
        # team-mates'), and a usage rate is a ratio: 32% against 16% is twice the ball, not five times.
        touches = TENDENCY_MEANS["touches"] + TOUCHES_DOUBLING * math.log2(usage_ratio)
        values["touches"] = max(TENDENCY_FLOOR, min(TENDENCY_CEILING, int(round(touches))))
    if "attack" in standard:
        attack = max(TENDENCY_FLOOR, min(TENDENCY_CEILING, int(round(ATTACK_MEAN + ATTACK_SPREAD * standard["attack"]))))
        lead, other = ("postUp", "drive") if values["postUp"] > values["drive"] else ("drive", "postUp")
        values[lead] = attack
        values[other] = min(values[other], attack)
    return values


def _logistic_sheet(target, composites, scale):
    """Attributes on a logistic curve: the attribute's own spread per z in the middle of the scale,
    bending smoothly toward the floor and ceiling instead of clipping at them, so a star's sheet keeps
    its order rather than flattening at 99.

    Reaching the target Overall, a player's shape is amplified before it is shifted: starting from
    the league's middle, a player whose strengths outweigh his weaknesses gets them more pronounced
    (up to SHAPE_AMPLIFICATION times), and only what that cannot reach is a shift of every attribute.
    A rebounding, rim-protecting centre rated a star stays a non-shooter; a shift alone would have made
    him a good shooter to average out to his Overall, and the ball would find him.
    """
    names = [name for name, _ in VISIBLE]
    weight = dict(VISIBLE)
    span = ATTRIBUTE_CEILING - ATTRIBUTE_FLOOR + 2
    low = ATTRIBUTE_FLOOR - 1
    slope = {name: scale * ATTRIBUTE_SPREADS[name] / (span / 4.0) for name in names}
    centre = math.log((LEAGUE_MIDDLE - low) / (span - (LEAGUE_MIDDLE - low)))

    def value(name, shift, amplify):
        return low + span / (1.0 + math.exp(-(shift + amplify * slope[name] * composites[name])))

    def mean(shift, amplify):
        return sum(weight[name] * value(name, shift, amplify) for name in names) / 100.0

    def solve(lo, hi, function):
        for _ in range(60):
            mid = (lo + hi) / 2
            if function(mid) < target + 0.5:
                lo = mid
            else:
                hi = mid
        return (lo + hi) / 2

    amplify = 1.0
    if mean(centre, 1.0) < target and mean(centre, SHAPE_AMPLIFICATION) > mean(centre, 1.0):
        amplify = min(SHAPE_AMPLIFICATION, solve(1.0, SHAPE_AMPLIFICATION, lambda k: mean(centre, k)))
    shift = solve(-12.0, 12.0, lambda x: mean(x, amplify))
    return {name: value(name, shift, amplify) for name in names}


def _recentre(target, values):
    """Integer attributes whose weighted mean (sum(w*a) // 100) is exactly target."""
    names = [name for name, _ in VISIBLE]
    weight = dict(VISIBLE)
    ints = {name: max(ATTRIBUTE_FLOOR, min(ATTRIBUTE_CEILING, int(round(values[name])))) for name in names}
    goal = target * 100 + 50
    for _ in range(4000):
        total = sum(weight[name] * ints[name] for name in names)
        if target * 100 <= total < target * 100 + 100:
            break
        step = 1 if total < goal else -1
        movable = [name for name in names if ATTRIBUTE_FLOOR <= ints[name] + step <= ATTRIBUTE_CEILING]
        if not movable:
            break
        # Move the heaviest attribute furthest from the target first: the sheet keeps its shape.
        pick = max(movable, key=lambda name: (weight[name], (target - ints[name]) * step))
        ints[pick] += step
    return ints


def _sheet(row, target, f, composites, size, spread, tendencies):
    ratings = _recentre(target, _logistic_sheet(target, composites, spread))

    position = row.derived["position"]
    natural = POSITIONS.index(position)
    positions = {name: FAMILIARITY_BY_DISTANCE[min(abs(index - natural), 4)] for index, name in enumerate(POSITIONS)}

    potential = min(99, target + HEADROOM_BY_AGE.get(row.age, 0 if row.age > 25 else 16))
    hidden = {name: 50 for name in HIDDEN}
    hidden["injuryProneness"] = max(5, min(95, int(round(50 - 14 * f.get("games_share", 0.0)))))

    height_cm = int(round((row.height or 78.0) * 2.54))
    weight_kg = int(round((row.weight or 215.0) * 0.4536))
    reach = WINGSPAN_OVER_HEIGHT_CM + int(round(3 * (0.6 * f.get("blk36", 0.0) + 0.4 * f.get("stl36", 0.0))))
    body = {"heightCm": max(150, min(240, height_cm)), "weightKg": max(55, min(170, weight_kg)),
            "wingspanCm": max(150, min(250, height_cm + max(-30, min(30, reach))))}

    traits = []
    if tendencies["drive"] >= 72:
        traits.append("DrivesToTheBasket")
    if tendencies["shotThree"] >= 70 and ratings["threePoint"] >= target:
        traits.append("StretchesTheFloor")
    if tendencies["postUp"] >= 75:
        traits.append("PostsUp")
    if tendencies["pass"] >= 75 and tendencies["shot"] <= 55:
        traits.append("PassFirst")
    if tendencies["pullUp"] >= 75:
        traits.append("ShootsOffTheDribble")
    if tendencies["gamble"] >= 78:
        traits.append("GamblesForSteals")
    if tendencies["crashGlass"] >= 78:
        traits.append("CrashesTheOffensiveGlass")
    if f.get("charges36", 0.0) >= 2.0:
        traits.append("TakesCharges")
    if f.get("fast_break_share", 0.0) >= 2.0:
        traits.append("RunsTheFloor")

    # Only what differs from the middle: the loader reads an absent hidden attribute as neutral.
    hidden = {name: value for name, value in hidden.items() if value != 50}
    return {"ratings": ratings, "hidden": hidden, "potential": max(potential, target), "body": body,
            "positions": positions, "traits": traits, "tendencies": tendencies}


# How each position's game leans, in z units, for a player with no stat line (a rookie): a point
# guard handles and passes, a centre rebounds and protects the rim. Applied with his real size.
ARCHETYPES = {
    "PointGuard": {"ast36": 1.6, "ast_pct": 1.6, "usg": 0.4, "stl36": 0.6, "three_rate": 0.5, "ft_rate": 0.2, "oreb36": -0.9, "dreb36": -0.8, "blk36": -0.8, "pf36": -0.4},
    "ShootingGuard": {"three_rate": 0.9, "three": 0.4, "usg": 0.3, "ast36": 0.3, "stl36": 0.3, "oreb36": -0.6, "dreb36": -0.6, "blk36": -0.6},
    "SmallForward": {"three_rate": 0.4, "stl36": 0.2},
    "PowerForward": {"oreb36": 0.6, "dreb36": 0.7, "blk36": 0.4, "three_rate": -0.3, "ast36": -0.4, "pf36": 0.3},
    "Center": {"oreb36": 1.4, "dreb36": 1.4, "blk36": 1.5, "two": 0.8, "three_rate": -1.3, "ast36": -0.8, "ft_rate": 0.5, "pf36": 0.7, "free": -0.6},
}


def rookie_sheet(row, target, size, mass, spread=1.0):
    """A sheet for a player with no stat line, from his position's archetype and his size. The
    archetype's composites are already in z units, so they are scaled to unit spread by a fixed
    factor rather than standardised against a league of one."""
    f = dict(ARCHETYPES.get(row.derived["position"], {}))
    f.setdefault("mpg", -0.8)
    composites = _composites(f, size, mass, -1.5)
    tendencies = _tendency_values({key: 0.8 * value for key, value in _tendency_composites(f, size).items()})
    return _sheet(row, target, f, {name: 0.8 * value for name, value in composites.items()}, size, spread, tendencies)
