#!/usr/bin/env python3
"""원작 ROM 에서 '관심사 선택 규칙' 데이터를 뽑아 로컬 팩용 JSON 으로 쓴다 (로컬 전용, 커밋 금지).

  python3 export_orig.py <천년가족.gba> <출력 json>

담는 것
- trees: 관심사 판정 함수(관심사 항목 +0x08)를 lift.py 로 옮긴 결정 트리와 그 보조 함수 트리
- interests: [표, 번호, 유형(+0x0F), 성별 조건(+0x10), 시작 게이지(+0x12), 판정 함수 주소]
- candidates: 0x085BD4B4[유형][나이 단계][시대] 후보 번호 목록 (유형 0·2·3)
- modeTable(0x08584CA4)·b15Table(0x08584D24), 직업 후보표(0x0889D3B8·0x0889D63C), 특별 후보표(0x088915C8)
- rom: 트리가 실행 중 읽는 ROM 표 조각 (직업·스킬·특별 후보 표 등)

ROM 자체나 이 출력물을 저장소에 올리지 말 것. 해독 근거: docs/08_원작규칙해독.md
"""
import json, os, struct, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.setrecursionlimit(20000)
from lift import Lifter, Unsupported, B, M32  # noqa: E402

NATIVES = {0x08000614: 0, 0x0824F640: 2, 0x08115778: 2}   # rand, umod, rel (C# 손 이식)
NATIVE_NAMES = {0x08000614: "rand", 0x0824F640: "umod", 0x08115778: "rel"}
STATE_TABLE, CAND_TABLE = 0x085BD4A0, 0x085BD4B4
OPS = {'add': '+', 'sub': '-', 'mul': '*', 'and': '&', 'or': '|', 'xor': '^', 'shl': '<<', 'lsr': '>>', 'asr': '>>>',
       'udiv': '/', 'umod': '%', 'sdiv': '/s', 'smod': '%s'}


class Rom:
    def __init__(self, b): self.b = b
    def ok(self, p): return B <= p < B + len(self.b)
    def u8(self, a): return self.b[a - B]
    def u16(self, a): return struct.unpack_from('<H', self.b, a - B)[0]
    def u32(self, a): return struct.unpack_from('<I', self.b, a - B)[0]


def enc_e(e):
    if isinstance(e, int): return e & M32
    k = e[0]
    if k == 'v': return ['v', e[1]]
    if k == 'arg': return ['a', e[1]]
    if k == 'sarg': return ['a', 4 + e[1]]
    if k == 'ram': return ['m', e[1], e[2]]
    if k == 'load': return ['m', e[1], enc_e(e[2])]
    if k == 'sext': return ['x', e[2], enc_e(e[1])]
    if k in OPS: return [OPS[k], enc_e(e[1]), enc_e(e[2])]
    return ['u', str(k)]        # 정의되지 않은 값 (도달하면 실행기가 오류를 낸다)


def enc_t(t):
    k = t[0]
    if k == 'ret': return ['r', enc_e(t[1]) if t[1] is not None else ['u', 'none']]
    if k == 'if': return ['i', t[1][0], enc_e(t[1][1]), enc_e(t[1][2]), enc_t(t[2]), enc_t(t[3])]
    if k == 'let':
        c = t[2]; fn = c[1]
        name = NATIVE_NAMES.get(fn, '0x%08X' % fn) if isinstance(fn, int) else 'icall'
        return ['l', t[1][1], name, [enc_e(a) for a in c[2]], enc_t(t[3])]
    if k == 'store': return ['s', t[1], enc_e(t[2]), enc_e(t[3]), enc_t(t[4])]
    if k == 'fail': return ['f', t[1]]
    raise ValueError(k)


def table_len(rom, t):
    tb = rom.u32(STATE_TABLE + 4 * t); n = 0
    while n < 4096:
        e = rom.u32(tb + 4 * n)
        if not rom.ok(e): break
        f = rom.u32(e + 8)
        if not (rom.ok(f) and f & 1): break
        n += 1
    return n


def rom_spans(rom):
    """트리가 읽는 ROM 표: 포인터 표와 그 대상(0x40 바이트씩, 3단계까지)."""
    spans = set()
    isptr = lambda p: B + 0x400000 <= p < B + len(rom.b) and (p & 3) == 0

    def walk(a, size, depth):
        if (a, size) in spans: return
        spans.add((a, size))
        if depth == 0: return
        for o in range(0, size, 4):
            p = rom.u32(a + o)
            if isptr(p): walk(p, 0x40, depth - 1)

    def n_ptrs(a):
        n = 0
        while n < 4096 and isptr(rom.u32(a + 4 * n)): n += 1
        return n
    for base in (0x088915C8, 0x0889D3B8, 0x088A309C):
        walk(base, 4 * n_ptrs(base), 3)
    walk(0x08891D9C, 28 * 512, 0)
    for t in range(5):
        tb = rom.u32(STATE_TABLE + 4 * t)
        walk(STATE_TABLE + 4 * t, 4, 0)
        n = table_len(rom, t); walk(tb, 4 * n, 0)
        for i in range(n): walk(rom.u32(tb + 4 * i), 0x28, 0)
    # 겹치는 구간 합치기
    iv = sorted((a, a + s) for a, s in spans)
    out = []
    for a, b in iv:
        if out and a <= out[-1][1]: out[-1][1] = max(out[-1][1], b)
        else: out.append([a, b])
    return [[a, rom.b[a - B:b - B].hex()] for a, b in out]


def main():
    if len(sys.argv) < 3:
        print(__doc__); return 2
    rb = open(sys.argv[1], 'rb').read(); rom = Rom(rb)
    L = Lifter(rb, NATIVES, max_steps=50000, max_nodes=5000); L.auto_subs = True
    interests, preds = [], {}
    for t in range(5):
        tb = rom.u32(STATE_TABLE + 4 * t)
        for i in range(table_len(rom, t)):
            e = rom.u32(tb + 4 * i)
            f = rom.u32(e + 8)
            interests.append([t, i, rom.u8(e + 0x0F), rom.u8(e + 0x10), rom.u8(e + 0x12), '0x%08X' % f])
            preds[f] = None
    trees, fails = {}, []
    for f in sorted(preds):
        try:
            trees['0x%08X' % f] = enc_t(L.lift(f))
        except Unsupported as ex:
            fails.append(('0x%08X' % f, str(ex)))
    for a, t in L.subtrees.items():
        trees['0x%08X' % a] = enc_t(t)
    cands = {}
    for ty in (0, 2, 3):
        base = rom.u32(CAND_TABLE + 4 * ty); per = []
        for st in range(8):
            row = []
            for era in range(4):
                p = rom.u32(base + 16 * st + 4 * era)
                row.append([rom.u16(p + 2 + 2 * k) for k in range(rom.u16(p))] if rom.ok(p) else [])
            per.append(row)
        cands[str(ty)] = per
    mode_table = [[rom.u8(0x08584CA4 + 16 * st + k) for k in range(16)] for st in range(8)]
    b15_table = [rom.u8(0x08584D24 + 10 * k) for k in range(3)]
    jobs = []
    for j in range(256):
        p = rom.u32(0x0889D3B8 + 4 * j)
        if not rom.ok(p) or rom.u8(p + 7) != j: break
        n1 = rom.u8(p + 5); l1 = rom.u32(p + 0x1C)
        ranks = []; rp = rom.u32(p + 0x20)
        for r in range(16):
            q = rom.u32(rp + 4 * r) if rom.ok(rp) else 0
            if not rom.ok(q): break
            n2 = rom.u8(q + 9); l2 = rom.u32(q + 0x0C)
            ranks.append([rom.u16(l2 + 2 * k) for k in range(n2)] if rom.ok(l2) else [])
        # 0x081191A4: 직업 코드 j 와 b15 로 다음 직업 후보표를 고른다 (0x0889D63C + j*32 + b15*4 + 0x0C)
        jmap = [rom.u16(0x0889D63C + 32 * j + 4 * b + 0x0C) for b in range(5)]
        jobs.append({"list": [rom.u16(l1 + 2 * k) for k in range(n1)] if rom.ok(l1) else [], "ranks": ranks, "map": jmap})
    specials = []
    for s in range(1024):
        p = rom.u32(0x088915C8 + 4 * s)
        if not rom.ok(p): break
        n = rom.u8(p + 8); ents = rom.u32(p + 0x10)
        specials.append([[rom.u16(ents + 8 * k), rom.u16(ents + 8 * k + 4), rom.u16(ents + 8 * k + 6)] for k in range(n)] if rom.ok(ents) else [])
    out = {"format": 1, "source": "ROM 해독 (tools/romlift). 로컬 전용",
           "natives": {('0x%08X' % a): n for a, n in NATIVE_NAMES.items()},
           "interests": interests, "trees": trees, "candidates": cands,
           "modeTable": mode_table, "b15Table": b15_table, "jobs": jobs, "specials": specials,
           "rom": rom_spans(rom), "liftFailures": fails}
    with open(sys.argv[2], 'w', encoding='utf8') as fp:
        json.dump(out, fp, separators=(',', ':'))
    print('관심사 %d, 트리 %d (실패 %d), 직업 %d, 특별 후보표 %d, ROM 조각 %d, 크기 %.1f KB' % (
        len(interests), len(trees), len(fails), len(jobs), len(specials), len(out['rom']), os.path.getsize(sys.argv[2]) / 1024))
    return 0


if __name__ == '__main__':
    sys.exit(main())
