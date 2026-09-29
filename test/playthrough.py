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

# route tokens: int = menu index, "id" = evidence to present, ("T", s, "id") = present on testimony
# statement s, ("P", s) = press statement s, "TIMEOUT" = let the timer run out
CROSS1 = [("P", 1), ("T", 1, "satomitalk")]
CH1 = CROSS1 + [2, "video", 0, "toilet", 0, "bombnote"]
# @choice entries: swamp (0 = chase alone, 1 = wake Kenmochi), Yumi (0 = guard, 1 = press, 2 = let go),
# notebook (0 = leave it, 1 = check it; then 0/1/2 = the flaw, 1 = phosphorus)
CH2 = [1, "yurama", 1, "rope", 1, "register", 0]
CH3 = [2]
CROSS_F = [("P", 1), ("T", 3, "jadebelow")]
FINAL_B = [4, 2] + CROSS_F + ["weights", "bagcheck", 1]  # Yumi alive: no notebook choice
FINAL_TRUE = FINAL_B + [0]
ACCUSE_BAD = [(0, "sakonji"), (1, "sakuraba"), (2, "satomi"), (3, "nagasaki"), (5, "akechi")]


def load_ids():
    ids = {}
    with open(GEN_H, encoding="utf-8") as f:
        for m in re.finditer(r"#define EV_(\w+) (\d+)", f.read()):
            ids[m.group(1).lower()] = int(m.group(2))
    return ids


def run(exe, route, ids, title=0, sram=None, shots=None, video="3", traps=False, mash="win"):
    env = dict(os.environ)
    env["VIDEO"] = video
    if traps:
        env["TRAPS"] = "1"
    def code(x):
        if isinstance(x, tuple):
            return x[1] if x[0] == "P" else 1000 + x[1] * 100 + ids[x[2]]
        if x == "TIMEOUT":
            return 9999
        return ids[x] if isinstance(x, str) else x
    env["ROUTE"] = ",".join(str(code(x)) for x in route)
    env["MASH"] = mash
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

    code, out, err = run(args.exe, CH1 + CH2 + CH3 + FINAL_TRUE, ids, shots=args.shots)
    results.append(check("true ending", code == 0 and "EVENT true_end" in out, out[-800:] + err))

    for idx, who in ACCUSE_BAD:
        code, out, err = run(args.exe, CH1 + CH2 + CH3 + [idx], ids)
        results.append(check(f"bad ending: accuse {who}", code == 0 and "EVENT bad_end" in out
                             and "true_end" not in out, out[-500:] + err))

    # five wrong answers in the first deduction -> game over
    code, out, err = run(args.exe, CROSS1 + [0, 1, 3, 0, 1], ids)
    results.append(check("game over after 5 mistakes", code == 0 and "EVENT bad_end" in out, out[-500:] + err))

    # four mistakes spread over the game still reach the true ending
    route = CROSS1 + [0, 2, "roses", "video", 0, "toilet", 0, "bombnote", 0, 1, "yurama", 1, "rope", 1,
                      "register", 0, 2, 4, 2] + CROSS_F + ["weights", "bagcheck", 0, 1, 0]
    code, out, err = run(args.exe, route, ids)
    results.append(check("true ending with 4 mistakes", code == 0 and "EVENT true_end" in out, out[-500:] + err))

    # a fifth mistake in the final deduction -> game over
    route = CROSS1 + [0, 2, "roses", "video", 0, "toilet", 0, "bombnote", 0, 1, "yurama", 1, "rope", 1,
                      "register", 0, 2, 4, 0, 0]
    code, out, err = run(args.exe, route, ids)
    results.append(check("game over in final deduction", code == 0 and "EVENT bad_end" in out
                         and "true_end" not in out, out[-500:] + err))

    # cross-examination: a wrong presentation costs a heart, then the contradiction clears it
    route = [("T", 0, "joker"), ("T", 1, "satomitalk")] + CH1[2:] + CH2 + CH3 + FINAL_TRUE
    code, out, err = run(args.exe, route, ids)
    results.append(check("cross-examination: wrong evidence then contradiction",
                         code == 0 and "EVENT true_end" in out and out.count("EVENT contradiction") == 2,
                         out[-500:] + err))

    # letting the timers run out in the finale costs hearts but the case can still be won
    route = CH1 + CH2 + CH3 + [4, 2] + CROSS_F + ["TIMEOUT", "weights", "TIMEOUT", "bagcheck", 1, 0]
    code, out, err = run(args.exe, route, ids)
    results.append(check("timeouts in the finale", code == 0 and "EVENT true_end" in out
                         and out.count("EVENT timeout") == 2, out[-500:] + err))

    # failing the swamp escape costs a heart, the story carries on
    code, out, err = run(args.exe, CH1 + CH2 + CH3 + FINAL_TRUE, ids, mash="fail")
    results.append(check("failed button-mash", code == 0 and "EVENT true_end" in out and "EVENT mash 0" in out,
                         out[-500:] + err))

    # stopping on the wrong video frames first costs nothing
    code, out, err = run(args.exe, CH1 + CH2 + CH3 + FINAL_TRUE, ids, video="0,5,3")
    results.append(check("wrong video frames, then the right one", code == 0 and "EVENT true_end" in out,
                         out[-500:] + err))

    # walking into every trap drains all five hearts
    code, out, err = run(args.exe, CH1 + CH2 + CH3 + FINAL_TRUE, ids, traps=True)
    results.append(check("every trap taken -> game over", code == 0 and "EVENT bad_end" in out
                         and "true_end" not in out, out[-500:] + err))

    # branches -------------------------------------------------------------
    # guarding Yumi and holding on to her -> she survives and testifies -> BEST END
    code, out, err = run(args.exe, CH1 + CH2 + [0] + FINAL_B, ids)
    results.append(check("route B: Yumi saved -> best ending", code == 0 and "EVENT best_end" in out
                         and "EVENT mash 1" in out, out[-800:] + err))

    # guarding Yumi but losing the grip -> canon murder, the true ending still reachable
    route = CH1 + CH2[:-1] + [1, 0] + FINAL_TRUE
    code, out, err = run(args.exe, route, ids, mash="fail")
    results.append(check("route B: grip lost -> true ending", code == 0 and "EVENT true_end" in out
                         and "best_end" not in out, out[-800:] + err))

    # waking Kenmochi at the swamp skips the drowning crisis entirely
    route = CH1 + CH2[:-1] + [1] + CH3 + FINAL_TRUE
    code, out, err = run(args.exe, route, ids)
    results.append(check("swamp: wake Kenmochi -> no button-mash", code == 0 and "EVENT true_end" in out
                         and "EVENT mash" not in out, out[-800:] + err))

    # checking the notebook and spotting the phosphorus -> GOOD END (needs Sakonji's plan from chapter 3)
    code, out, err = run(args.exe, CH1 + CH2 + CH3 + FINAL_TRUE[:-1] + [1, 1], ids)
    results.append(check("notebook checked, flaw found -> good ending", code == 0 and "EVENT good_end" in out,
                         out[-800:] + err))

    # checking the notebook but naming the wrong flaw -> canon true ending
    code, out, err = run(args.exe, CH1 + CH2 + CH3 + FINAL_TRUE[:-1] + [1, 0], ids)
    results.append(check("notebook checked, wrong flaw -> true ending", code == 0 and "EVENT true_end" in out,
                         out[-800:] + err))

    # chapter save: stop inside chapter 2, then continue from the title screen
    with tempfile.TemporaryDirectory() as tmp:
        sram = os.path.join(tmp, "save.sav")
        code, out, err = run(args.exe, CH1, ids, sram=sram)
        results.append(check("stop in chapter 2 (route exhausted)", code == 3 and out.count("EVENT chapter") == 3,
                             out[-500:] + err))
        code, out, err = run(args.exe, CH2 + CH3 + FINAL_TRUE, ids, title=1, sram=sram)
        results.append(check("continue from chapter 2 save", code == 0 and "EVENT true_end" in out
                             and out.count("EVENT chapter") == 3, out[-500:] + err))

    failed = results.count(False)
    print(f"\n{len(results) - failed}/{len(results)} passed")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
