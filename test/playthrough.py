#!/usr/bin/env python3
"""Automated playthroughs of the host build: every ending, game over, and chapter save/continue.

Usage: python3 test/playthrough.py build/host_game [--shots DIR]
Route entries are menu indices (ints) or evidence ids (strings) for @present prompts.
"""
import argparse
import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
GEN_H = os.path.join(HERE, "..", "build", "gen_data.h")

CH1 = [1, "chain", 1, "schedule"]
CH2 = [1, "ice", 1, "patrol", 1, "glove"]
FINAL_TRUE = [3, "tape", "ice", "alibi", 1, "puppet"]


def load_ids():
    ids = {}
    with open(GEN_H, encoding="utf-8") as f:
        for m in re.finditer(r"#define EV_(\w+) (\d+)", f.read()):
            ids[m.group(1).lower()] = int(m.group(2))
    return ids


def run(exe, route, ids, title=0, sram=None, shots=None):
    env = dict(os.environ)
    env["ROUTE"] = ",".join(str(ids[x]) if isinstance(x, str) else str(x) for x in route)
    env["TITLE"] = str(title)
    if sram:
        env["SRAM_FILE"] = sram
    if shots:
        os.makedirs(shots, exist_ok=True)
        env["SHOTS"] = shots
    p = subprocess.run([exe], env=env, capture_output=True, text=True, timeout=600)
    return p.returncode, p.stdout, p.stderr


def check(name, ok, detail=""):
    print(("PASS " if ok else "FAIL ") + name + ("" if ok else "\n" + detail))
    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("--shots", help="save screenshots of the true-ending run here")
    args = ap.parse_args()
    ids = load_ids()
    results = []

    code, out, err = run(args.exe, CH1 + CH2 + FINAL_TRUE, ids, shots=args.shots)
    results.append(check("true ending", code == 0 and "EVENT true_end" in out, out[-800:] + err))

    for idx, who in [(0, "reika"), (1, "okada"), (2, "izumi"), (4, "kenmochi")]:
        code, out, err = run(args.exe, CH1 + CH2 + [idx], ids)
        results.append(check(f"bad ending: accuse {who}", code == 0 and "EVENT bad_end" in out
                             and "true_end" not in out, out[-500:] + err))

    # five wrong answers in the first deduction -> game over
    code, out, err = run(args.exe, [0, 0, 2, 3, 0], ids)
    results.append(check("game over after 5 mistakes", code == 0 and "EVENT bad_end" in out, out[-500:] + err))

    # four mistakes spread over the game still reach the true ending
    route = [0, 1, "window", "chain", 1, "schedule", 0, 1, "ice", 1, "patrol", 1, "glove",
             3, "tape", "ice", "alibi", 0, 1, "puppet"]
    code, out, err = run(args.exe, route, ids)
    results.append(check("true ending with 4 mistakes", code == 0 and "EVENT true_end" in out, out[-500:] + err))

    # a fifth mistake in the final deduction -> game over
    route = [0, 1, "window", "chain", 1, "schedule", 0, 1, "ice", 1, "patrol", 1, "glove",
             3, "letter", "letter"]
    code, out, err = run(args.exe, route, ids)
    results.append(check("game over in final deduction", code == 0 and "EVENT bad_end" in out
                         and "true_end" not in out, out[-500:] + err))

    # chapter save: stop inside chapter 2, then continue from the title screen
    with tempfile.TemporaryDirectory() as tmp:
        sram = os.path.join(tmp, "save.sav")
        code, out, err = run(args.exe, CH1, ids, sram=sram)
        results.append(check("stop in chapter 2 (route exhausted)", code == 3 and out.count("EVENT chapter") == 3,
                             out[-500:] + err))
        code, out, err = run(args.exe, CH2 + FINAL_TRUE, ids, title=1, sram=sram)
        results.append(check("continue from chapter 2 save", code == 0 and "EVENT true_end" in out
                             and out.count("EVENT chapter") == 2, out[-500:] + err))

    failed = results.count(False)
    print(f"\n{len(results) - failed}/{len(results)} passed")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
