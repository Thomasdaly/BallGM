#!/usr/bin/env python3
"""Tunes a league pack against the real season it was built from.

Phase 1 sweeps build_pack.py's --overall-scale (how far Overall stretches around the median),
simulating each candidate with PackCheck, and keeps the scale whose simulated table best matches the
real one: loss = |sim_sd - real_sd| + rmse(mean sim win% vs real).

Phase 2 (--fit-records) then fits a per-team rating offset: each iteration moves every team's offset
by the gap between its real and simulated win%, and keeps the best iteration. This carries what a box
score cannot see -- defence, scheme, fit -- onto the players who produced the real record. It makes
the opening season look like reality; it stays attached to those players when they move.

All build_pack.py arguments pass straight through; --teams is required. The winning pack is rebuilt
last, so --out holds it on exit, with tuning-report.json (and team-offsets.json) beside it.

  python3 tune.py --players P.csv --teams T.csv --alignment A.json --season 2024 --out ../../local-packs/x [--fit-records]
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CHECK_PROJECT = os.path.join(HERE, "PackCheck")
CHECK_DLL = os.path.join(CHECK_PROJECT, "bin", "Release", "net10.0", "PackCheck.dll")

# Rating points per 1.0 of win% gap, per fitting iteration. Deliberately under-damped (measured
# response is roughly 25-30 points per 1.0), so the fit converges instead of oscillating.
OFFSET_GAIN = 20.0
MAXIMUM_OFFSET = 15
FIT_ITERATIONS = 6


def pop_option(argv, name, has_value=True, default=None):
    if name not in argv:
        return default
    index = argv.index(name)
    value = argv[index + 1] if has_value else True
    del argv[index:index + (2 if has_value else 1)]
    return value


def main():
    argv = sys.argv[1:]
    seasons = int(pop_option(argv, "--check-seasons", default="12"))
    fit_records = pop_option(argv, "--fit-records", has_value=False, default=False)
    pop_option(argv, "--overall-scale")
    pop_option(argv, "--team-offsets")
    out = argv[argv.index("--out") + 1] if "--out" in argv else None
    teams = argv[argv.index("--teams") + 1] if "--teams" in argv else None
    if not out or not teams:
        sys.exit("tune.py needs --out and --teams (real records are what it tunes toward)")

    subprocess.run(["dotnet", "build", CHECK_PROJECT, "-c", "Release", "-v", "q", "-nologo"], check=True, stdout=subprocess.DEVNULL)
    offsets_path = os.path.join(out, "team-offsets.json")

    def run(scale, offsets, check_seasons=seasons):
        extra = ["--overall-scale", str(scale)]
        if offsets:
            os.makedirs(out, exist_ok=True)
            with open(offsets_path, "w", encoding="utf-8") as handle:
                json.dump(offsets, handle, indent=2)
            extra += ["--team-offsets", offsets_path]
        subprocess.run([sys.executable, os.path.join(HERE, "build_pack.py"), *argv, *extra], check=True, stdout=subprocess.DEVNULL)
        output = subprocess.run(["dotnet", CHECK_DLL, os.path.join(out, "pack.json"), os.path.join(out, "records.csv"), str(check_seasons)],
                                check=True, capture_output=True, text=True).stdout
        result = json.loads(output)
        result["loss"] = round(abs(result["sim_sd_win_pct"] - result["real_sd_win_pct"]) + result["rmse_mean_win_pct"], 4)
        return result

    def summary(label, result):
        line = {"step": label, **{key: value for key, value in result.items() if key != "teams"}}
        print(json.dumps(line), flush=True)
        return line

    steps = []
    results = {}
    for scale in [1.0, 1.25, 1.5, 1.75, 2.0]:
        results[scale] = run(scale, None)
        steps.append(summary(f"scale={scale}", results[scale]))
    best_scale = min(results, key=lambda scale: results[scale]["loss"])
    best = {"scale": best_scale, "offsets": None, "result": results[best_scale]}

    if fit_records:
        offsets = {}
        current = results[best_scale]
        for iteration in range(1, FIT_ITERATIONS + 1):
            for key, team in current["teams"].items():
                moved = offsets.get(key, 0.0) + OFFSET_GAIN * (team["real"] - team["sim"])
                offsets[key] = max(-MAXIMUM_OFFSET, min(MAXIMUM_OFFSET, moved))
            rounded = {key: round(value) for key, value in offsets.items()}
            current = run(best_scale, rounded)
            steps.append(summary(f"fit={iteration}", current))
            if current["loss"] < best["result"]["loss"]:
                best = {"scale": best_scale, "offsets": rounded, "result": current}

    final = run(best["scale"], best["offsets"], seasons * 2)
    if best["offsets"] is None and os.path.exists(offsets_path):
        os.remove(offsets_path)
    report = {"chosen_scale": best["scale"], "team_offsets": best["offsets"], "final_check": final, "steps": steps}
    with open(os.path.join(out, "tuning-report.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    print(json.dumps({"chosen_scale": best["scale"], "fitted_offsets": best["offsets"] is not None,
                      **{key: value for key, value in final.items() if key != "teams"}}, indent=2))


if __name__ == "__main__":
    main()
