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
CH1_DEDUCE = [1, "rosesalad"]        # the bomber is on the train (rose salad)
CROSS1 = [2, ("P", 2), ("T", 2, "satomitalk")]  # press Sakonji, then his statement 2
CH1 = CH1_DEDUCE + CROSS1
# @choice entries: lobby (0 = eavesdrop, 1 = walk past) then spot (0 = by the pillar, 1 = behind the sofa),
# swamp (0 = chase alone, 1 = wake Kenmochi), Yumi (0 = guard, 1 = press, 2 = let go),
# notebook (0 = leave it, 1 = check it). Branching presentations take evidence ids like any @present.
CH2_DEDUCE = ["bridge", 1, "rope", 1]
CH2 = [0, 1] + CH2_DEDUCE + ["rope", 0]  # overhear from the sofa, clear Nagasaki, chase alone at the swamp
CH3_DEDUCE = [2, "video", 0, "toilet", 1, "bombnote"]
CH3 = CH3_DEDUCE + [2, "rope"]         # let Yumi go, calm Sakuraba with the rope trick
CH3_B = CH3_DEDUCE + [0, "secret", "rope"]  # guard Yumi (Kenmochi convinced by the overheard talk)
CROSS_F = [("P", 2), ("T", 4, "jadebelow")]
FIN_HEAD = [4, "jadebelow", 2] + CROSS_F
FIN_TAIL = ["receipt", "mailvideo", "bagcheck", 1, "smoke"]  # ... motive, then stop the smoke escape
FINAL_TRUE = FIN_HEAD + ["weights"] + FIN_TAIL + ["nails", 0]  # no proof against Sakonji, leave the notebook
FINAL_B = FIN_HEAD + ["yumiwitness", "weights"] + FIN_TAIL     # Yumi alive
FINAL_WAKE = FIN_HEAD + ["weights", "silhouette"] + FIN_TAIL + ["nails", 0]
ACCUSE_BAD = [(0, "sakonji"), (1, "sakuraba"), (2, "satomi"), (3, "nagasaki"), (5, "akechi")]
# four mistakes: two in the train deduction, one in the theater, one in the balloon trick
MISTAKES4 = ([0, 1, "roses", "rosesalad"] + CROSS1 + [0, 1, "bridge", 0, 1, "rope", 1, "rope", 0]
             + [0] + CH3_DEDUCE + [2, "rope"])

def load_ids():
    ids = {}
    with open(GEN_H, encoding="utf-8") as f:
        for m in re.finditer(r"#define EV_(\w+) (\d+)", f.read()):
            ids[m.group(1).lower()] = int(m.group(2))
    return ids


def run(exe, route, ids, title=0, sram=None, shots=None, video="3", traps=False, mash="win", slot=0, save_at=None,
        chsave=0):
    env = dict(os.environ)
    env["SLOT"] = str(slot)
    env["CHSAVE"] = str(chsave)
    if save_at:
        env["SAVE_AT"] = save_at
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
    code, out, err = run(args.exe, [0, 2, 0, 2, 0], ids)
    results.append(check("game over after 5 mistakes", code == 0 and "EVENT bad_end" in out, out[-500:] + err))

    # four mistakes spread over the game still reach the true ending
    route = MISTAKES4 + FIN_HEAD + ["weights", "receipt", "mailvideo", "bagcheck", 1, "smoke", "nails", 0]
    code, out, err = run(args.exe, route, ids)
    results.append(check("true ending with 4 mistakes", code == 0 and "EVENT true_end" in out, out[-500:] + err))

    # a fifth mistake in the final deduction -> game over
    code, out, err = run(args.exe, MISTAKES4 + [4, "roses"], ids)
    results.append(check("game over in final deduction", code == 0 and "EVENT bad_end" in out
                         and "true_end" not in out, out[-500:] + err))

    # cross-examination: a wrong presentation costs a heart, then the contradiction clears it
    route = CH1_DEDUCE + [2, ("T", 0, "joker"), ("T", 2, "satomitalk")] + CH2 + CH3 + FINAL_TRUE
    code, out, err = run(args.exe, route, ids)
    results.append(check("cross-examination: wrong evidence then contradiction",
                         code == 0 and "EVENT true_end" in out and out.count("EVENT contradiction") == 2,
                         out[-500:] + err))

    # picking the wrong witness to press costs a heart and asks again
    route = CH1_DEDUCE + [0, 1, 3] + CROSS1 + CH2 + CH3 + FINAL_TRUE
    code, out, err = run(args.exe, route, ids)
    results.append(check("wrong witnesses before pressing Sakonji", code == 0 and "EVENT true_end" in out,
                         out[-500:] + err))

    # letting the timers run out in the finale costs hearts but the case can still be won
    route = CH1 + CH2 + CH3 + FIN_HEAD + ["TIMEOUT", "weights", "TIMEOUT"] + FIN_TAIL + ["nails", 0]
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
    def branch(name, route, event, extra=True, mash="win", absent=()):
        code, out, err = run(args.exe, route, ids, mash=mash)
        ok = code == 0 and f"EVENT {event}" in out and extra(out) if callable(extra) else code == 0 and f"EVENT {event}" in out
        ok = ok and not any(f"EVENT {a}" in out for a in absent)
        results.append(check(name, ok, out[-800:] + err))

    branch("Yumi guarded and saved -> BEST END", CH1 + CH2 + CH3_B + FINAL_B, "best_end",
           lambda o: "EVENT mash 1" in o)
    branch("Kenmochi not convinced -> Yumi dies -> TRUE END", CH1 + CH2 + CH3_DEDUCE + [0, "joker", "rope"] + FINAL_TRUE,
           "true_end")
    branch("Yumi guarded, grip lost -> TRUE END", CH1 + CH2[:-1] + [1] + CH3_B + FINAL_WAKE,
           "true_end", mash="fail", absent=("best_end",))
    branch("swamp: wake Kenmochi -> no button-mash, silhouette presented", CH1 + CH2[:-1] + [1] + CH3 + FINAL_WAKE,
           "true_end", lambda o: "EVENT mash" not in o)
    branch("Sakuraba's testimony -> GOOD END (witness)",
           CH1 + CH2 + CH3 + FIN_HEAD + ["weights"] + FIN_TAIL + ["sakurabawit"], "good_end")
    branch("notebook checked with Sakonji's plan -> GOOD END (fire)",
           CH1 + CH2 + CH3 + FINAL_TRUE[:-1] + [1, "rockplan"], "good_end")
    branch("notebook checked, wrong evidence -> TRUE END", CH1 + CH2 + CH3 + FINAL_TRUE[:-1] + [1, "joker"],
           "true_end")
    branch("smoke escape not stopped -> NORMAL END",
           CH1 + CH2 + CH3 + FIN_HEAD + ["weights"] + FIN_TAIL[:-1] + ["joker"], "normal_end")
    branch("smoke escape timer runs out -> NORMAL END",
           CH1 + CH2 + CH3 + FIN_HEAD + ["weights"] + FIN_TAIL[:-1] + ["TIMEOUT"], "normal_end")
    branch("Nagasaki taken away -> Yumi rescued but no doctor -> TRUE END",
           CH1 + [0, 1] + CH2_DEDUCE + ["joker", 0] + CH3_B + FINAL_TRUE, "true_end", absent=("best_end",))
    branch("eavesdropping caught by the pillar -> Kenmochi not convinced -> TRUE END",
           CH1 + [0, 0] + CH2_DEDUCE + ["rope", 0] + CH3_DEDUCE + [0, "secret", "rope"] + FINAL_TRUE, "true_end")
    branch("walked past the lobby -> no overheard talk -> TRUE END",
           CH1 + [1] + CH2_DEDUCE + ["rope", 0] + CH3_DEDUCE + [0, "nails", "rope"] + FINAL_TRUE, "true_end")
    branch("Sakuraba runs into the blizzard -> still solvable", CH1 + CH2 + CH3_DEDUCE + [2, "joker"] + FINAL_TRUE,
           "true_end")
    branch("Sakuraba lost, then accused -> BAD END", CH1 + CH2 + CH3_DEDUCE + [2, "joker", 1], "bad_end")

    # chapter save: answer "yes" at each chapter card (slot 3), stop inside chapter 2, then continue
    with tempfile.TemporaryDirectory() as tmp:
        sram = os.path.join(tmp, "save.sav")
        code, out, err = run(args.exe, CH1, ids, sram=sram)
        results.append(check("no automatic save when the player says no", "EVENT saved" not in out, out[-500:] + err))
        code, out, err = run(args.exe, CH1, ids, sram=sram, chsave=3)
        results.append(check("stop in chapter 2 (route exhausted), saved at the card",
                             code == 3 and out.count("EVENT chapter ") == 3 and "EVENT saved 3" in out
                             and out.count("EVENT chapter_save") == 2,  # not asked on the prologue card
                             out[-500:] + err))
        code, out, err = run(args.exe, CH2 + CH3 + FINAL_TRUE, ids, title=1, sram=sram, slot=3)
        results.append(check("continue from chapter 2 save (card not replayed)", code == 0 and "EVENT true_end" in out
                             and out.count("EVENT chapter ") == 2, out[-500:] + err))

    # save anywhere: a manual save in the middle of chapter 2's dialogue resumes on that line
    with tempfile.TemporaryDirectory() as tmp:
        sram = os.path.join(tmp, "save.sav")
        code, out, err = run(args.exe, CH1, ids, sram=sram, save_at="3:4:1")
        results.append(check("manual save mid-dialogue (slot 1)", "EVENT saved 1" in out, out[-500:] + err))
        code, out, err = run(args.exe, CH2 + CH3 + FINAL_TRUE, ids, title=1, sram=sram, slot=1)
        results.append(check("load slot 1 -> resumes mid-chapter, no chapter card replay",
                             code == 0 and "EVENT true_end" in out and out.count("EVENT chapter ") == 2,
                             out[-500:] + err))
        # a save made inside the chapter 1 investigation resumes at the investigation
        code, out, err = run(args.exe, CH1, ids, sram=sram, save_at="2:30:2")
        results.append(check("manual save inside an investigation (slot 2)", "EVENT saved 2" in out, out[-500:] + err))
        code, out, err = run(args.exe, CH1 + CH2 + CH3 + FINAL_TRUE, ids, title=1, sram=sram, slot=2)
        results.append(check("load slot 2 -> back in the investigation",
                             code == 0 and "EVENT true_end" in out and 0 <= out.find("video ->") < out.find("testimony ->")
                             and out.count("EVENT chapter ") == 3, out[-500:] + err))

    failed = results.count(False)
    print(f"\n{len(results) - failed}/{len(results)} passed")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
