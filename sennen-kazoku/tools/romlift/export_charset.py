#!/usr/bin/env python3
"""번역 패치 글자표 만들기 (로컬 전용 — 결과는 ROM 에서 나온 표이므로 저장소에 넣지 않는다).

번역 패치 ROM 의 글은 Shift-JIS 2바이트 자리에 한글을 넣은 것이다. 한글 음절은 0x889F 부터 차례대로
(둘째 바이트 0x40~0x7E, 0x80~0xFC) 놓여 있고, 0x8740~0x8743 은 받침에 따라 고르는 조사(은/는·이/가·을/를·과/와)다.
0x889F 아래 기호·영숫자는 Shift-JIS(cp932), 1바이트 0x20~0x7E 는 ASCII 그대로다.
번역 패치가 옮기지 않은 일본어 글(랭크 보상 설명 일부)은 가나 코드(0x82xx·0x83xx)가 그대로 남아 있어 앱이 알아본다(OrigText.Untranslated).

글자 자리는 export_text.py 가 만든 orig_text.json 의 대사(참고 번역문)와 ROM 의 같은 대사(결과 기록 +0x14)를
제어 토큰을 기준점으로 맞춰 세어 정한다(같은 길이로 맞은 조각의 글자끼리 투표). 대사에 안 나오는 글자는
실기 화면 글자 그림을 보고 읽은 값을 따로 주는 파일(한 줄에 "코드 글자")로 채운다.

  python3 export_charset.py ROM.gba orig_text.json [손으로_읽은_글자.txt]
  python3 export_charset.py ROM.gba orig_text.json --jobs-only   — 직업 이름만 더하기
결과: orig_text.json 에 "charset": {"889F": "가", ..., "8740": "은/는"} 를 더해 다시 쓴다.
"""
import collections, json, re, struct, sys

TOK = re.compile(r'\{\{HEX:([0-9A-F ]+)\}\}')
JOSA = ('은/는', '이/가', '을/를', '과/와', '와/과', '(으)로', '으로/로', '이야/야', '이랑/랑', '아/야', '이여/여')


def tlen(b, o):
    """글 토큰 1A xx 의 길이 (OrigResultScript.TokenLen 과 같은 표)."""
    if o + 1 >= len(b): return None
    op = b[o + 1]
    if op == 0x0E and o + 2 >= len(b): return None
    if op in (1, 2, 9, 0xD, 0xFF): return 2
    if op in (3, 0xA): return 3
    if op in (5, 6, 8, 0xB, 0xF): return 4
    if op == 0x10: return 5
    if op == 0x0E: return {0: 4, 1: 7, 2: 5}.get(b[o + 2], 2)
    return None


def rom_units(rom, o, limit=6000):
    units, end = [], o + limit
    while o < end:
        if rom[o] == 0x1A:
            if rom[o + 1] in (0xFF, 0x12): units.append(('T', rom[o:o + 2])); break
            L = tlen(rom, o)
            if L is None: break
            units.append(('T', rom[o:o + L])); o += L
        elif rom[o] >= 0x80: units.append(('C', rom[o] << 8 | rom[o + 1])); o += 2
        else: units.append(('C', rom[o])); o += 1
    return units


def ref_units(s):
    units, i = [], 0
    while i < len(s):
        m = TOK.match(s, i)
        if m:
            b = bytes(int(x, 16) for x in m.group(1).split()); j = 0
            while j < len(b):   # 붙어 있는 토큰 나누기
                if b[j] == 0x1A and b[j + 1:j + 2] == b'\x12':
                    units.append(('T', b'\x1a\x12')); break
                if b[j] == 0x1A:
                    L = tlen(b, j) or (len(b) - j)
                    units.append(('T', b[j:j + L])); j += L
                else: units.append(('B', b[j])); j += 1
            i = m.end()
            continue
        for jo in JOSA:
            if s.startswith(jo, i): units.append(('C', jo)); i += len(jo); break
        else: units.append(('C', s[i])); i += 1
    return units


def runs(units):
    out, cur = [], []
    for u in units:
        if u[0] == 'C': cur.append(u[1])
        elif u[0] == 'T': out.append((cur, u[1])); cur = []
    out.append((cur, None))
    return out


SKILLS = 0x088A309C


UI = 0x085C081C
JOBS = 0x0889D3B8


def decode(rom, table, p, limit=64):
    """ROM 글 하나를 글자표로 푼다 (0 이나 1A FF 에서 끝, 1A 01 줄바꿈, 1A 06 aa 이름 자리 "{이름aa}", 그 밖 토큰은 뺀다)."""
    o, out = p - 0x08000000, []
    lens = {1: 2, 2: 2, 9: 2, 0xD: 2, 3: 3, 0xA: 3, 5: 4, 6: 4, 8: 4, 0xB: 4, 0xF: 4, 0x10: 5}
    while len(out) < limit and rom[o] != 0:
        if rom[o] == 0x1A:
            op = rom[o + 1]
            if op == 0xFF or op == 0x12 or (op not in lens and op != 0xE): break
            if op == 1: out.append('\n')
            if op == 6: out.append('{이름%d}' % rom[o + 2])
            o += {0: 4, 1: 7, 2: 5}.get(rom[o + 2], 2) if op == 0xE else lens[op]
        elif rom[o] >= 0x80: out.append(table.get('%04X' % (rom[o] << 8 | rom[o + 1]), '□')); o += 2
        else: out.append(table.get('%04X' % rom[o], '□')); o += 1
    return ''.join(out)


def job_names(rom, table):
    """직업 이름: 직업 표 0x0889D3B8 (직업 번호 → 직업 기록, 기록 +0 = 이름 글). 시대가 바뀌면 같은 자리의 다른 번호(예: 26 → 65)가 된다."""
    jobs = []
    for k in range(1024):
        p = struct.unpack_from('<I', rom, JOBS - 0x08000000 + 4 * k)[0]
        if not 0x08000000 <= p < 0x0A000000: break
        n = struct.unpack_from('<I', rom, p - 0x08000000)[0]
        jobs.append(decode(rom, table, n, 40) if 0x08000000 <= n < 0x0A000000 else '')
    return jobs


def main():
    if len(sys.argv) < 3:
        print(__doc__); return 2
    rom = open(sys.argv[1], 'rb').read()
    pack = json.load(open(sys.argv[2], encoding='utf8'))
    if '--jobs-only' in sys.argv:   # 이미 만든 글자표로 직업 이름만 더한다 (번역해 넣은 스킬 등은 그대로)
        pack['jobs'] = job_names(rom, pack['charset'])
        json.dump(pack, open(sys.argv[2], 'w', encoding='utf8'), ensure_ascii=False, separators=(',', ':'))
        print('직업 이름 %d개' % len(pack['jobs'])); return 0
    votes = collections.defaultdict(collections.Counter)
    for k, v in pack['records'].items():
        p = struct.unpack_from('<I', rom, int(k, 16) - 0x08000000 + 0x14)[0]
        if not 0x08000000 <= p < 0x0A000000: continue
        ru = rom_units(rom, p - 0x08000000)
        if not ru: continue
        a, b = runs(ru), runs(ref_units(v[2]))
        ia = ib = 0
        while ia < len(a) and ib < len(b):
            ca, ta = a[ia]; cb, tb = b[ib]
            if len(ca) == len(cb) and ta == tb:
                for x, y in zip(ca, cb): votes[x][y] += 1
            if ta == tb: ia += 1; ib += 1; continue
            for da, db in sorted(((da, db) for da in range(4) for db in range(4) if da or db), key=sum):
                if ia + da < len(a) and ib + db < len(b) and a[ia + da][1] == b[ib + db][1]:
                    ia += da + 1; ib += db + 1; break
            else: break
    table = {'%04X' % c: v.most_common(1)[0][0] for c, v in votes.items()}
    voted = len(table)
    for hi in range(0x81, 0x89):   # 한글 아래 기호·영숫자 = Shift-JIS
        for lo in list(range(0x40, 0x7F)) + list(range(0x80, 0xFD)):
            c = hi << 8 | lo
            if c >= 0x889F or '%04X' % c in table: continue
            try: table['%04X' % c] = bytes([hi, lo]).decode('cp932')
            except UnicodeDecodeError: pass
    for c in range(0x20, 0x7F):   # 1바이트 글자 = ASCII (결과 문구의 띄어쓰기 0x20 등)
        table.setdefault('%04X' % c, chr(c))
    manual = 0
    if len(sys.argv) > 3:
        for line in open(sys.argv[3], encoding='utf8'):
            p = line.split()
            if len(p) >= 2 and not line.startswith('#'): table[p[0].upper()] = p[1]; manual += 1
    pack['charset'] = dict(sorted(table.items()))
    # 스킬 이름: 스킬 표 0x088A309C (스킬 번호 → 항목 포인터, 항목 +0 이름 글 · +4 설명 글). 이름은 번역돼 있고 설명은 일본어 그대로다.
    skills = []
    for k in range(256):
        e = struct.unpack_from('<I', rom, SKILLS - 0x08000000 + 4 * k)[0]
        if not 0x08000000 <= e < 0x0A000000: break
        skills.append(decode(rom, table, struct.unpack_from('<I', rom, e - 0x08000000)[0]))
    pack['skills'] = skills
    # 화면 글 표 0x085C081C (메뉴·설정·가족 유형·종합 진단 등, 원작 코드가 0x085C081C + 4·번호 로 고른다). 이름 자리 1A 06 aa → "{이름aa}".
    ui = []
    for k in range(2048):
        p = struct.unpack_from('<I', rom, UI - 0x08000000 + 4 * k)[0]
        if not 0x08000000 <= p < 0x0A000000: break
        ui.append(decode(rom, table, p, 200))
    pack['ui'] = ui
    jobs = pack['jobs'] = job_names(rom, table)
    json.dump(pack, open(sys.argv[2], 'w', encoding='utf8'), ensure_ascii=False, separators=(',', ':'))
    print('글자 %d개 (대사 맞추기 %d, 손으로 읽음 %d, 나머지 Shift-JIS), 스킬 이름 %d개, 화면 글 %d개, 직업 이름 %d개' % (len(table), voted, manual, len(skills), len(ui), len(jobs)))


if __name__ == '__main__':
    sys.exit(main())
