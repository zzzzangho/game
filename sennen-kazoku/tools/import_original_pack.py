#!/usr/bin/env python3
"""공략 사이트 데이터(bwah-leaf/sennen-kazoku-guide)에서 '예정 상태 → 결과 이벤트' 팩을 생성한다.

  python3 -I import_original_pack.py <guide 저장소 디렉터리> <출력 json> [--no-text] [--rom <천년가족.gba>]

  --rom 을 주면 원작 ROM 의 관심사 표(0x085BD4A0)에서 관심사 유형·MAX/MIN 사건·요일 조건을 읽고,
  사건 변형의 판정 함수 주소를 공략 데이터의 variant_predicates 와 맞춰 MAX/MIN 결과 목록을 정확히 만든다.
  직업 근무 요일표(0x0889D3B8)도 함께 넣는다. (--rom 없이는 원작 구조를 만들 수 없어 오류로 끝난다)

중요
- 출력물에는 원작 번역 대사가 들어간다. 반드시 로컬에서만 쓰고 저장소에 커밋·공개하지 말 것
  (기본 출력 위치 unity/Assets/Resources/LocalPacks/ 는 .gitignore 처리되어 있다).
- 완전히 모델링된 상태만 가져온다: 한 상태의 모든 결과 조건이 앱의 조건 언어로 표현 가능해야 한다.
  표현할 수 없는 조건(관계/상태 코드, 컨디션 단계, 다른 인물 슬롯, 미해명 판정 함수)이 하나라도 있으면
  그 상태 전체를 건너뛴다(그렇지 않으면 폴백 결과가 잘못 자주 발생한다). 건너뛴 이유는 보고서로 출력한다.
- 효과는 표시된 '기본값'만 반영한다. '몰입도·상태 보정'의 계산식은 미해명이므로 보정 없이 기본값을 쓴다.
"""
import json, re, sys, os, collections
from fractions import Fraction

STAT_KEYS = {"person_s0_a0": "self.int", "person_s0_a1": "self.stamina", "person_s0_a2": "self.charm", "person_s0_a3": "self.luck",
             "person_s0_a5": "self.hearts", "person_s0_a7": "self.mastery", "person_s0_a27": "self.age",
             "person_s0_a24": "self.gender", "person_s0_a18": "self.job",
             "global_0": "family.mood", "global_3": "family.assets", "global_1": "family.house"}
LABELS = {"이벤트 발생 본인의 지력": "self.int", "이벤트 발생 본인의 체력": "self.stamina", "이벤트 발생 본인의 매력": "self.charm",
          "이벤트 발생 본인의 운": "self.luck", "이벤트 발생 본인의 하트": "self.hearts", "이벤트 발생 본인의 직업 숙련도": "self.mastery",
          "이벤트 발생 본인의 연령": "self.age", "이벤트 발생 본인의 성별 코드": "self.gender", "이벤트 발생 본인의 직업 코드": "self.job",
          "가족 무드": "family.mood", "현재 연도": "date.year", "가족 자산": "family.assets", "주택 등급": "family.house"}
# 나이 경계는 임시값(원작의 분류 경계는 미해명). '유아: 1~3세'처럼 명시된 것은 그 값을 쓴다.
STAGES = {"유아": (1, 3), "아동": (4, 6), "초등": (7, 12), "중등": (13, 15), "청년": (16, 24), "젊은이": (25, 39), "중년": (40, 59), "노년": (60, 120)}


class Unmodeled(Exception):
    pass


def cmp_(l, op, r):
    return {"op": "cmp", "l": l, "cmp": op, "r": r}


def frac_chance(p):
    f = Fraction(p).limit_denominator(256)
    return {"op": "chance", "num": f.numerator, "den": f.denominator}


def tree_to_cond(t):
    ty = t["type"]
    if ty in ("and", "or"):
        return {"op": ty, "args": [tree_to_cond(i) for i in t["items"]]}
    if ty == "not":
        return {"op": "not", "arg": tree_to_cond(t["item"])}
    if ty == "random":
        return {"op": "chance", "num": t["numerator"], "den": t["denominator"]}
    if ty == "skill_check":
        c = {"op": "has_skill", "id": t["skill_id"]}
        return {"op": "not", "arg": c} if t.get("negative") else c
    if ty == "check":
        if t["key"] not in STAT_KEYS:
            raise Unmodeled("check " + t["key"])
        return cmp_(STAT_KEYS[t["key"]], t["operator"], t["threshold"])
    if ty == "ability_gate":
        if t["key"] not in STAT_KEYS:
            raise Unmodeled("gate " + t["key"])
        v = STAT_KEYS[t["key"]]
        g = t["gates"]
        if len(g) == 1 and g[0]["operator"] == ">=":
            T, P, Q = g[0]["threshold"], g[0]["chance_percent"] / 100, t["otherwise_percent"] / 100
            terms = [{"op": "and", "args": [cmp_(v, ">=", T), frac_chance(P)]}]
            if Q > 0:
                terms.append({"op": "and", "args": [cmp_(v, "<", T), frac_chance(Q)]})
            return {"op": "or", "args": terms}
        if len(g) == 2 and all(x["operator"] == ">=" for x in g) and t["otherwise_percent"] == 0:
            hi, lo = sorted(g, key=lambda x: -x["threshold"])
            q = lo["chance_percent"] / 100
            a = 1 - (1 - hi["chance_percent"] / 100) / (1 - q)    # 두 번의 독립 판정으로부터 개별 확률 복원
            return {"op": "or", "args": [{"op": "and", "args": [cmp_(v, ">=", lo["threshold"]), frac_chance(q)]},
                                         {"op": "and", "args": [cmp_(v, ">=", hi["threshold"]), frac_chance(a)]}]}
        raise Unmodeled("gate shape")
    raise Unmodeled("node " + ty)          # unknown 포함


ATOM_RAND = re.compile(r"^난수 확률 (\d+)/(\d+)$")
ATOM_CMP = re.compile(r"^(.+?) (<=|>=|==|!=|<|>) \(?(\d+)\)?$")
ATOM_CODE = re.compile(r"^이벤트 발생 본인의 관계/상태 코드 (\d+)( 아님)?$")
ATOM_SLOT15 = re.compile(r"^인물 슬롯 15의 유효 상태( 아님)?$")
# 관계/상태 코드 → 관계 (의미 추정, docs/06_관계상태코드_추정.md). 근거가 약한 코드(1~10,20~22,27 등)는 일부러 제외한다.
CODE_REL = {15: "spouse", 28: "lover", 16: "child_spouse", 18: "grandchild"}
USED = {"relation": False}


def split_top(s, word):
    """괄호 깊이 0 에서 ' word ' 로 분리."""
    parts, depth, cur, i = [], 0, "", 0
    tok = " " + word + " "
    while i < len(s):
        ch = s[i]
        if ch == "(": depth += 1
        elif ch == ")": depth -= 1
        if depth == 0 and s.startswith(tok, i):
            parts.append(cur); cur = ""; i += len(tok); continue
        cur += ch; i += 1
    parts.append(cur)
    return parts


def strip_parens(s):
    s = s.strip()
    while s.startswith("(") and s.endswith(")"):
        depth = 0
        for i, ch in enumerate(s):
            depth += ch == "("; depth -= ch == ")"
            if depth == 0 and i < len(s) - 1: return s
        s = s[1:-1].strip()
    return s


def text_to_cond(s):
    """한국어 조건문 → 조건 JSON. 괄호 밖 '또는' 으로 먼저 나누고, 다음 '그리고' 로 나눈다(그리고가 더 강하게 결합)."""
    s = strip_parens(s)
    ors = split_top(s, "또는")
    if len(ors) > 1: return {"op": "or", "args": [text_to_cond(x) for x in ors]}
    ands = split_top(s, "그리고")
    if len(ands) > 1: return {"op": "and", "args": [text_to_cond(x) for x in ands]}
    m = ATOM_RAND.match(s)
    if m: return {"op": "chance", "num": int(m.group(1)), "den": int(m.group(2))}
    m = ATOM_CODE.match(s)
    if m:
        code = int(m.group(1))
        if code not in CODE_REL: raise Unmodeled("code %d" % code)
        USED["relation"] = True
        c = {"op": "relation", "name": CODE_REL[code]}
        return {"op": "not", "arg": c} if m.group(2) else c
    m = ATOM_SLOT15.match(s)
    if m:
        USED["relation"] = True
        c = {"op": "relation", "name": "spouse"}
        return c if not m.group(1) else {"op": "not", "arg": c}
    m = ATOM_CMP.match(s)
    if m and strip_parens(m.group(1)) in LABELS:
        return cmp_(LABELS[strip_parens(m.group(1))], m.group(2), int(m.group(3)))
    raise Unmodeled("atom " + s[:40])


EFF = re.compile(r"^(이벤트 대상의|가족) ?(.*?) (조금 )?(상승|하락)\(기본([+-]\d+)\)")


def parse_effects(effs):
    out, skipped = [], []
    for e in effs:
        m = EFF.match(e)
        if not m:
            skipped.append(e); continue
        who, what, _, _, val = m.groups(); val = int(val)
        what = what.strip()
        if who == "가족" or what.startswith("무드") or what.startswith("자산"):
            if what.startswith("무드") or what == "무드":
                out.append({"op": "family_mood", "value": val}); continue
            if what.startswith("자산") or what == "자산":
                out.append({"op": "family_assets", "value": val}); continue
            skipped.append(e); continue
        if what == "하트":
            out.append({"op": "add_hearts", "target": "self", "value": val})
        elif what in ("지력", "체력", "매력", "운"):
            out.append({"op": "add_stat", "target": "self", "stat": {"지력": "int", "체력": "stamina", "매력": "charm", "운": "luck"}[what], "value": val})
        elif what == "직업 숙련도":
            out.append({"op": "add_job_mastery", "target": "self", "value": val})
        else:
            skipped.append(e)
    return out, skipped


def eligible_of(family_type):
    parts = [p.strip() for p in family_type.split(":")]
    stage = parts[0]
    if stage not in STAGES:
        raise Unmodeled("대상 분류 " + family_type)
    lo, hi = STAGES[stage]
    m = re.match(r"(\d+)~(\d+)세", parts[1]) if len(parts) > 1 else None
    if m:
        lo, hi = int(m.group(1)), int(m.group(2)); parts = parts[:1]
    cond = []
    for p in parts[1:]:
        if p == "미혼": cond.append(cmp_("self.married", "==", 0))
        elif p == "기혼": cond.append(cmp_("self.married", "==", 1))
        elif p == "남": cond.append(cmp_("self.gender", "==", 0))
        elif p == "여": cond.append(cmp_("self.gender", "==", 1))
        else: raise Unmodeled("대상 분류 " + family_type)
    return lo, hi, (cond[0] if len(cond) == 1 else {"op": "and", "args": cond} if cond else None)


HEX = re.compile(r"\{\{HEX:([0-9A-F ]+)\}\}")


def script_to_pages(script):
    """제어 토큰을 최소한으로 해석한다(추정): 1A 02=페이지 끝, 1A 09=줄바꿈, 1A 06 00 04=대상 인물 이름, 그 밖의 참조는 '◯◯', 나머지 제어 토큰은 제거."""
    def rep(m):
        b = m.group(1).split()
        if b[:2] == ["1A", "02"]: return "\u0001"
        if b[:2] == ["1A", "09"]: return "\n"
        if b[:2] == ["1A", "06"]: return "{self}" if b[2:4] == ["00", "04"] else "◯◯"
        return ""
    t = HEX.sub(rep, script)
    pages = [p.strip() for p in t.split("\u0001")]
    return [{"speaker": "", "text": p} for p in pages if p]


# ---------------- 원작 ROM 표 (해독: docs/08_원작규칙해독.md) ----------------
STATE_TABLE, JOB_TABLE = 0x085BD4A0, 0x0889D3B8

class Rom:
    def __init__(self, path): self.b = open(path, "rb").read()
    def u8(self, a): return self.b[a - 0x08000000]
    def u32(self, a): return int.from_bytes(self.b[a - 0x08000000:a - 0x08000000 + 4], "little")
    def ok(self, p): return 0x08000000 <= p < 0x08000000 + len(self.b)
    def state(self, table, index):
        return self.u32(self.u32(STATE_TABLE + 4 * table) + 4 * index)
    def event_info(self, ev):
        """MAX/MIN 사건 항목: +1 요일 조건, +0x24 변형 목록(→ [판정 함수, 대본]) """
        mode = self.u8(ev + 1); lst = self.u32(ev + 0x24); preds = []
        while self.ok(lst) and len(preds) < 32:
            v = self.u32(lst)
            if not self.ok(v): break
            f = self.u32(v)
            if not (self.ok(f) and f & 1 and 0x08100000 <= f < 0x08300000): break   # 변형 항목 = [판정 함수(Thumb), 대본]
            preds.append("0x%08X" % f); lst += 4
        return mode, preds
    def job_masks(self, n):
        out = {}
        for j in range(n):
            p = self.u32(JOB_TABLE + 4 * j)
            if not self.ok(p) or self.u8(p + 7) != j: break      # 항목 +7 = 자기 직업 코드 (확인용)
            out[str(j)] = self.u8(p + 4) & 0x7F
        return out

def main():
    if len(sys.argv) < 3:
        print(__doc__); return 2
    repo, out = sys.argv[1], sys.argv[2]
    with_text = "--no-text" not in sys.argv
    if "--rom" not in sys.argv:
        print("--rom <천년가족.gba> 가 필요합니다 (원작 관심사 표를 읽어야 MAX/MIN 구조를 만들 수 있음)"); return 2
    rom = Rom(sys.argv[sys.argv.index("--rom") + 1])
    data = os.path.join(repo, "assets", "data")
    ps = json.load(open(os.path.join(data, "planned-states.json"), encoding="utf8"))
    scripts = {}
    if with_text:
        for e in json.load(open(os.path.join(data, "event-scripts-ko.json"), encoding="utf8"))["events"]:
            scripts[e["id"]] = e["script_ko"]
    events, states = {}, []
    why = collections.Counter()

    def make_event(o):
        """공략 데이터 결과 하나 → 이벤트 정의 (조건은 변형 조건 + 발생 조건)."""
        occ = [x for x in o["occurrence_conditions"] if x != "상위 추가 조건 없음"]
        occ_cond = [text_to_cond(x) for x in occ]
        sa = o["selection_analysis"]
        if sa.get("unresolved_internal_checks"): raise Unmodeled("미해명 내부 판정")
        if sa.get("probability_tree"): cond = tree_to_cond(sa["probability_tree"])
        else:
            extra = [v for v in o["variant_conditions"] if v != "상위 추가 조건 없음"]
            if not extra: cond = {"op": "always"}
            elif len(extra) == 1: cond = text_to_cond(extra[0])
            else: raise Unmodeled("복수 변형 조건")
        if occ_cond: cond = {"op": "and", "args": occ_cond + ([cond] if cond != {"op": "always"} else [])}
        effects, skipped = parse_effects(o["effects"])
        oid = o["id"]
        ev = {"id": oid, "version": 1, "origin": "original", "certainty": "confirmed" if not skipped else "estimated",
              "textSource": "original-translation" if with_text and oid in scripts else "placeholder", "sourceRef": oid,
              "kind": "outcome", "title": o["title_ko"] if with_text else "[원작 " + oid + "]", "category": "관심사 결과",
              "trigger": {"condition": cond}, "effects": effects,
              "pages": script_to_pages(scripts[oid]) if with_text and oid in scripts else [{"speaker": "", "text": "(문구 없음)"}]}
        if not ev["pages"]: ev["pages"] = [{"speaker": "", "text": "(문구 없음)"}]
        if skipped: ev["unmodeledEffects"] = skipped
        return ev

    # 판정 함수 주소 → 공략 데이터 결과 (MIN 사건은 여러 관심사가 함께 쓰므로 전체에서 찾는다)
    by_pred = {}
    for s in ps["states"]:
        for o in s["outcomes"]:
            for g in o.get("route_groups", []):
                for f in g.get("variant_predicates", []): by_pred.setdefault(f.upper().replace("0X", "0x"), o)
    nomap = collections.Counter()
    for s in ps["states"]:
        try:
            refs = s.get("save_refs") or []
            if not refs: raise Unmodeled("ROM 위치 없음")
            t, i = refs[0]["table"], refs[0]["index"]
            sp = rom.state(t, i)
            if not rom.ok(sp): raise Unmodeled("ROM 위치 없음")
            stype = rom.u8(sp + 0x0F)
            lists, modes = {}, {}
            for key, off in (("max", 0x1C), ("min", 0x20)):
                ep = rom.u32(sp + off)
                if not rom.ok(ep): lists[key] = []; modes[key] = 3; continue
                mode, preds = rom.event_info(ep); modes[key] = mode; ids = []
                for f in preds:
                    o = by_pred.get(f)
                    if o is None: nomap[key] += 1; raise Unmodeled("변형 연결 없음")
                    ev = make_event(o); events.setdefault(ev["id"], ev); ids.append(ev["id"])
                lists[key] = ids
            if not lists["max"] and not lists["min"]: raise Unmodeled("MAX·MIN 결과 없음")
            lo, hi, elig = eligible_of(s["outcomes"][0]["family_type"]) if s["outcomes"] else (0, 120, None)
        except Unmodeled as ex:
            why[str(ex).split(" ")[0] if not str(ex).startswith("대상") else "대상 분류"] += 1; continue
        states.append({"id": s["id"], "title": s["title_ko"] if with_text else s["id"], "romRef": [t, i],
                       "type": stype, "maxDayMode": modes["max"], "minDayMode": modes["min"],
                       "maxOutcomes": lists["max"], "minOutcomes": lists["min"],
                       "minAge": lo, "maxAge": hi, "weight": 10, "certainty": "confirmed", "eligible": elig,
                       "note": "MAX/MIN·요일·유형은 ROM 해독. 대상 조건(eligible)·나이 경계로 관심사를 고르는 부분은 미해독(임시)"})
    if nomap: print("변형 함수가 공략 데이터에 없는 사건:", dict(nomap))
    pack = {"format": 1, "packId": "sk.original.local", "version": 2, "title": "원작 규칙 (로컬 생성, 비공개)", "kind": "base", "origin": "original",
            "minContract": 3, "events": list(events.values()), "plannedStates": states,
            "jobDayMasks": rom.job_masks(len(json.load(open(os.path.join(data, "family-save-layout.json"), encoding="utf8"))["jobs"]))}
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf8", newline="\n") as f:
        json.dump(pack, f, ensure_ascii=False)
    tot = len(ps["states"])
    print("예정 상태 %d / %d 가져옴 (%.1f%%), 결과 이벤트 %d개" % (len(states), tot, 100 * len(states) / tot, len(events)))
    print("건너뛴 이유:", dict(why))
    print("효과 일부 미반영 이벤트:", sum(1 for e in events.values() if "unmodeledEffects" in e))
    return 0


if __name__ == "__main__":
    sys.exit(main())
