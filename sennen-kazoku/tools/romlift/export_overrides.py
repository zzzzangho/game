#!/usr/bin/env python3
"""번역 패치가 옮기지 않은 원문 글의 한국어 대체 글을 로컬 팩에 넣는다 (로컬 전용 — 번역은 저장소에 넣지 않는다).

  python3 export_overrides.py orig_text.json 대체글.json
대체글.json = {"0x08889BB4": "…|…", ...} — 글 주소(결과 스크립트가 글 프레임으로 여는 ROM 글) → 한국어.
"|" 다음 장, "/" 줄바꿈, {플레이어}·{가문} 이름 자리, {이가}·{은는}·{을를}·{과와} 받침 조사 (OrigText.Overrides).

지금 쓰는 곳: 신님 랭크 보상 설명. 랭크 표 0x0888D0A8 (12바이트씩, +0 설명 글 · +4 보상 목록) 의 설명 글 51개 중
랭크 3·7~50 의 45개를 패치가 옮기지 않았다(가나가 남아 있음). 글 끝의 "1A 12 05 26 …" 은 랭크 확인 명령이라 원작 바이트대로 돌고,
앱은 화면 글만 바꿔 끼운다.
"""
import json, re, sys


def main():
    if len(sys.argv) < 3:
        print(__doc__); return 2
    pack = json.load(open(sys.argv[1], encoding='utf8'))
    ov = json.load(open(sys.argv[2], encoding='utf8'))
    for k, v in ov.items():
        int(k, 16)
        if re.search(r'[぀-ヿ]', v): print('가나가 남은 글:', k); return 1
    pack.setdefault('overrides', {}).update({'0x%08X' % int(k, 16): v for k, v in ov.items()})
    json.dump(pack, open(sys.argv[1], 'w', encoding='utf8'), ensure_ascii=False, separators=(',', ':'))
    print('대체 글 %d개 (팩 전체 %d개)' % (len(ov), len(pack['overrides'])))


if __name__ == '__main__':
    sys.exit(main())
