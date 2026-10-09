#!/usr/bin/env python3
"""원작 결과 기록 → 사건 제목·대사 대응표 만들기 (로컬 전용 — 결과 파일은 번역 대사를 담으므로 저장소에 넣지 않는다).

원작 결과 기록(사건 변형의 대본, 0x30 바이트)은 +0x10 에 제목 글, +0x14 에 대사 글 포인터를 둔다.
참고 자료 events-ko.json 의 title_uid 는 각 사건 글 묶음의 시작 오프셋이고, 그 묶음 바로 다음에 오는 제목 포인터가
그 사건의 결과 기록이다(효과 함수 주소로 맞춰 본 5,312건 중 5,311건 일치).

  python3 export_text.py ROM.gba orig_rules.json events-ko.json event-scripts-ko.json 출력.json [planned-states.json]
출력: {"format": "sk-orig-text/1", "records": {"0x089416A0": ["sk-event-4403", 제목, 대사], ...},
       "interests": {"표,번호": 관심사 제목}}   — 관심사 제목은 관심사 항목의 판정 함수(+8)와 참고 자료 handler_functions 로 맞춘다.
대사의 제어 토큰은 참고 자료 그대로({{HEX:1A ..}}) 둔다. 앱(OrigText.cs)이 해석한다.
"""
import bisect, json, struct, sys


def main():
    if len(sys.argv) < 6:
        print(__doc__); return 2
    rom = open(sys.argv[1], 'rb').read()
    u32 = lambda a: struct.unpack_from('<I', rom, a - 0x08000000)[0]
    rules = json.load(open(sys.argv[2], encoding='utf8'))
    items = json.load(open(sys.argv[3], encoding='utf8'))['events']
    scripts = {x['id']: x.get('script_ko', '') for x in json.load(open(sys.argv[4], encoding='utf8'))['events']}
    recs = set()
    for e in rules['events'].values():
        for v in e['variants']:
            if v and v[1]: recs.add(int(v[1], 16))
    for d in rules.get('variantData', {}): recs.add(int(d, 16))
    ptr = {}
    for r in sorted(recs):
        p = u32(r + 0x10)
        if 0x08000000 <= p < 0x0A000000: ptr.setdefault(p & 0xFFFFFF, r)
    ps = sorted(ptr)
    out, miss = {}, 0
    for it in items:
        uid = it.get('title_uid')
        if not uid: miss += 1; continue
        u = int(uid.split(':')[1], 16)
        i = bisect.bisect_right(ps, u)
        if i >= len(ps) or ps[i] - u > 0x400: miss += 1; continue
        out['0x%08X' % ptr[ps[i]]] = [it['id'], it.get('title_ko', ''), scripts.get(it['id'], '')]
    titles = {}
    if len(sys.argv) > 6:
        ps_ = json.load(open(sys.argv[6], encoding='utf8'))
        sts = ps_.get('states') or []
        byfn = {}
        for st in sts:
            for f in st.get('handler_functions', []): byfn.setdefault(int(f, 16), st.get('title_ko', ''))
        for it in rules.get('interests', []):
            f = int(it[5], 16) if isinstance(it[5], str) and it[5] else None
            if f in byfn: titles['%d,%d' % (it[0], it[1])] = byfn[f]
        print('관심사 제목 %d / %d' % (len(titles), len(rules.get('interests', []))))
    json.dump({"format": "sk-orig-text/1", "records": out, "interests": titles}, open(sys.argv[5], 'w', encoding='utf8'), ensure_ascii=False, separators=(',', ':'))
    print('결과 기록 %d개에 글 대응, 대응 못 한 참고 항목 %d개' % (len(out), miss))


if __name__ == '__main__':
    sys.exit(main())
