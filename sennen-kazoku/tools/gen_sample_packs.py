#!/usr/bin/env python3
"""저장소에 포함되는 샘플 팩 생성기.
- sk.sample: 원작 '예정 상태 → 결과' 구조 3종. 규칙(조건·확률·효과 수치)은 공략 사이트의 ROM 정적 분석 값,
  제목·대사는 원작 번역문이 아닌 임시 문구(textSource=placeholder). 원작 이벤트 id 는 sourceRef 로만 남긴다.
- nova.pack001: 임시 신규 이벤트 1건(확장 구조 증명용).
python3 -I gen_sample_packs.py <출력 디렉터리>"""
import json, sys, os

def cmp_(l, c, r): return {"op": "cmp", "l": l, "cmp": c, "r": r}
def page(speaker, text): return {"speaker": speaker, "text": text}

def outcome(eid, src, title, cond, effects, pages, certainty="confirmed"):
    return {"id": eid, "version": 1, "origin": "original", "certainty": certainty, "textSource": "placeholder",
            "sourceRef": src, "kind": "outcome", "title": title, "category": "예정 상태 결과",
            "trigger": {"condition": cond}, "pages": pages, "effects": effects}

hearts = lambda v: {"op": "add_hearts", "target": "self", "value": v}
mood = lambda v: {"op": "family_mood", "value": v}
mast = lambda v: {"op": "add_job_mastery", "target": "self", "value": v}

# 원작 가중 게이트: (stat < T and 1/4) or (stat >= T and 3/4)   (T = floor(base*9/10), 공략 사이트 probability_tree 값)
def gate(stat, base):
    t = base * 9 // 10
    return {"op": "or", "args": [
        {"op": "and", "args": [cmp_(stat, "<", t), {"op": "chance", "num": 1, "den": 4}]},
        {"op": "and", "args": [cmp_(stat, ">=", t), {"op": "chance", "num": 3, "den": 4}]}]}

events = [
    # planned-0072FD80  젊은이: 미혼: 여   (하트 >= 96 이면 호전, 아니면 폴백)
    outcome("sk.s1.good", "sk-event-2301", "[원작 구조] 조금 누그러짐", cmp_("self.hearts", ">=", 96),
            [hearts(24), mood(20)], [page("self", "{self}의 표정이 조금 풀렸다. (임시 문구)")]),
    outcome("sk.s1.fallback", "sk-event-2300", "[원작 구조] 가족이 싫어짐", {"op": "always"},
            [hearts(-48), mood(-28)], [page("self", "{self:은는} 방문을 닫고 들어가 버렸다. (임시 문구)")]),
    # planned-006E0BAC  청년: 미혼: 여   (가족 무드 >= 160)
    outcome("sk.s2.good", "sk-event-1437", "[원작 구조] 마음을 고쳐먹음", cmp_("family.mood", ">=", 160),
            [hearts(24)], [page("self", "{self:은는} 다시 해 보기로 마음먹었다. (임시 문구)")]),
    outcome("sk.s2.fallback", "sk-event-1436", "[원작 구조] 의욕 없음", {"op": "always"},
            [hearts(-24)], [page("self", "{self:은는} 의욕이 나지 않는 모양이다. (임시 문구)")]),
    # planned-006E311C  청년: 미혼: 남   (체력 게이트 base 2200 → 1980)
    outcome("sk.s3.good", "sk-event-1585", "[원작 구조] 솜씨가 늘었다", gate("self.stamina", 2200),
            [mast(6)], [page("self", "{self:은는} 연습한 보람이 있었다. (임시 문구)")]),
    outcome("sk.s3.fallback", "sk-event-1586", "[원작 구조] 무리한 연습", {"op": "always"},
            [mast(3), hearts(-48)], [page("self", "{self:은는} 지쳐 버렸다. (임시 문구)")]),
]

def state(sid, src, rom_ref, max_mode, title, minage, maxage, elig, outs):
    # 원작 구조: 열중 게이지 255 → MAX 사건(이 관심사의 변형들), 0 → MIN 사건(여러 관심사가 함께 쓰는 공용 사건 — 샘플에는 없음)
    # type·maxDayMode 는 ROM 관심사 표(0x085BD4A0) 값, MAX 변형 = 공략 데이터 결과 2개 (판정 함수 주소로 대조)
    return {"id": sid, "title": title, "minAge": minage, "maxAge": maxage, "weight": 10, "romRef": rom_ref,
            "type": 0, "maxDayMode": max_mode, "minDayMode": 3, "maxOutcomes": outs, "minOutcomes": [],
            "eligible": elig, "certainty": "estimated",
            "note": "MAX 변형·조건은 원작 분석(" + src + "), 유형·요일 조건은 ROM. 대상 분류·연령 경계로 관심사를 고르는 부분은 미해독(임시)."}

single = cmp_("self.married", "==", 0)
g = lambda n: cmp_("self.gender", "==", n)
states = [
    state("sk.ps.family-nag", "planned-0072FD80", [2, 39], 3, "[원작 구조] 가족의 간섭이 신경 쓰임", 18, 29, {"op": "and", "args": [single, g(1)]}, ["sk.s1.good", "sk.s1.fallback"]),
    state("sk.ps.job-hunt", "planned-006E0BAC", [1, 343], 3, "[원작 구조] 가족의 시선이 부담됨", 18, 29, {"op": "and", "args": [single, g(1)]}, ["sk.s2.good", "sk.s2.fallback"]),
    state("sk.ps.training", "planned-006E311C", [1, 396], 1, "[원작 구조] 기술 연마 중", 18, 29, {"op": "and", "args": [single, g(0)]}, ["sk.s3.good", "sk.s3.fallback"]),
]

sk = {"format": 1, "packId": "sk.sample", "version": 1, "title": "원작 규칙 구조 샘플 (임시 문구)", "kind": "base", "origin": "original",
      "minContract": 3, "events": events, "plannedStates": states}

nova = {"format": 1, "packId": "nova.pack001", "version": 1, "title": "신규 이벤트 팩 1 (임시)", "kind": "expansion", "origin": "new",
        "minContract": 1, "requires": [{"packId": "sk.sample", "minVersion": 1}],
        "events": [{
            "id": "nova.picnic.001", "version": 1, "origin": "new", "certainty": "placeholder", "textSource": "new",
            "kind": "standalone", "title": "[신규] 갑작스러운 소풍", "category": "가족",
            "roles": {"spouse": "spouse"},
            "trigger": {"condition": {"op": "and", "args": [cmp_("self.age", ">=", 20), cmp_("family.mood", ">=", 96)]},
                        "weight": 10, "priority": 1, "cooldownDays": 720},
            "pages": [page("self", "{self:이가} 말했다. \"날씨도 좋은데 어디 나가 볼까?\""),
                      page("spouse", "{spouse:은는} 기다렸다는 듯 도시락을 꺼냈다.")],
            "choices": [
                {"id": "go", "text": "다 같이 소풍을 간다", "effects": [mood(12), hearts(24)],
                 "result": [page("", "온 가족이 공원에서 즐거운 하루를 보냈다.")]},
                {"id": "stay", "text": "집에서 쉰다", "effects": [hearts(6)],
                 "result": [page("", "느긋한 하루가 지나갔다.")]}],
            "effects": [{"op": "set_flag", "scope": "family", "name": "nova.picnic.done"}]}]}

out = sys.argv[1]; os.makedirs(out, exist_ok=True)
# sk.sample → 게임 번들, nova.pack001 → 테스트 픽스처(원작에 없는 선택지 이벤트라 게임에 넣지 않는다)
fixtures = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tests", "Core.Tests", "Fixtures")
for name, obj, d in (("sk.sample", sk, out), ("nova.pack001", nova, fixtures)):
    with open(os.path.join(d, name + ".json"), "w", encoding="utf8", newline="\n") as f:
        json.dump(obj, f, ensure_ascii=False, indent=1); f.write("\n")
print("ok", out)
