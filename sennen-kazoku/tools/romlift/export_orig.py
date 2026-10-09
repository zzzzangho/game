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

# C# 손 이식 함수 (Core/Orig): 난수·나눗셈(libgcc)·관계 판정·관계 슬롯표·가족 명단 검색·다음 관심사 선택
NATIVES = {0x08000614: 0, 0x0824F640: 2, 0x0824F5C8: 2, 0x0824F460: 2, 0x0824F4F8: 2,
           0x08115778: 2, 0x08110B90: 2, 0x08110608: 1, 0x08028524: 3}
NATIVE_NAMES = {0x08000614: "rand", 0x0824F640: "umod", 0x0824F5C8: "udiv", 0x0824F460: "sdiv", 0x0824F4F8: "smod",
                0x08115778: "rel", 0x08110B90: "slots", 0x08110608: "inhouse", 0x08028524: "select"}
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
    if k == 'sp': return ['p', e[1]]
    if k in OPS: return [OPS[k], enc_e(e[1]), enc_e(e[2])]
    if k == 'undef_stack': return ['u', 'stack']
    return ['u', str(k)]        # 정의되지 않은 값 (도달하면 실행기가 오류를 낸다)


def enc_t(t):
    k = t[0]
    if k == 'ret': return ['r', enc_e(t[1]) if t[1] is not None and not isinstance(t[1], str) else ['u', 'none']]
    if k == 'if': return ['i', t[1][0], enc_e(t[1][1]), enc_e(t[1][2]), enc_t(t[2]), enc_t(t[3])]
    if k == 'let':
        c = t[2]; fn = c[1]
        if isinstance(fn, int):
            return ['l', t[1][1], NATIVE_NAMES.get(fn, '0x%08X' % fn), [enc_e(a) for a in c[2]], enc_t(t[3])]
        # 함수 포인터 호출: 첫 인자 자리에 대상 주소 식
        return ['l', t[1][1], '*', [enc_e(fn[1])] + [enc_e(a) for a in c[2]], enc_t(t[3])]
    if k == 'store': return ['s', t[1], enc_e(t[2]), enc_e(t[3]), enc_t(t[4])]
    if k == 'fail': return ['f', t[1]]
    asg = lambda pairs: [[v, enc_e(x)] for v, x in pairs]
    if k == 'if2': return ['j', t[1][0], enc_e(t[1][1]), enc_e(t[1][2]), enc_t(t[2]), enc_t(t[3]), enc_t(t[4])]
    if k == 'end': return ['e', asg(t[1])]
    if k == 'loop': return ['o', t[1], asg(t[2]), enc_t(t[3]), enc_t(t[4])]
    if k == 'cont': return ['c', t[1], asg(t[2])]
    if k == 'brk': return ['b', t[1], asg(t[2])]
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


def trees_raw_consts(trees):
    """인코딩된 트리의 메모리 읽기 주소식 안 ROM 상수 (표 시작 주소 후보)."""
    out = set()
    def we(e, inaddr):
        if isinstance(e, int):
            if inaddr and 0x08400000 <= e < 0x0A000000: out.add(e)
        elif isinstance(e, list) and e:
            if e[0] == 'm': we(e[2], True)
            elif isinstance(e[0], str):
                for x in e[1:]: we(x, inaddr)
            else:
                for x in e: we(x, inaddr)
    def wt(n):
        if not isinstance(n, list): return
        for x in n[1:]:
            if isinstance(x, list) and x and isinstance(x[0], str) and len(x[0]) == 1 and x[0] in 'rijlsfeocb': wt(x)
            else: we(x, False)
    for t in trees.values(): wt(t)
    return out


def rom_spans(rom, consts=(), extra=()):
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
    for c in consts: walk(c, 0x200, 2)
    for a in extra: walk(a, 0x30, 2)
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


def icall_tables(t, rom, out):
    """트리 안의 함수 포인터 호출에서 ROM 함수 표 주소를 찾는다 (m32[표 + k*4])."""
    def we(e):
        if isinstance(e, tuple):
            if e[0] == 'load' and e[1] == 4:
                stack = [e[2]]
                while stack:
                    x = stack.pop()
                    if isinstance(x, int) and rom.ok(x): out.add(x)
                    elif isinstance(x, tuple): stack.extend(x[1:])
            for x in e[1:]: we(x)
    def wt(n):
        k = n[0]
        if k == 'let':
            if isinstance(n[2][1], tuple): we(n[2][1][1])
            wt(n[3])
        elif k in ('if',): wt(n[2]); wt(n[3])
        elif k == 'if2': wt(n[2]); wt(n[3]); wt(n[4])
        elif k == 'loop': wt(n[3]); wt(n[4])
        elif k == 'store': wt(n[4])
    wt(t)


def _work(args):
    path, fns = args
    import signal
    class TO(Exception): pass
    def al(*a): raise TO()
    signal.signal(signal.SIGALRM, al)
    rb = open(path, 'rb').read(); rom = Rom(rb)
    L = Lifter(rb, NATIVES, max_steps=50000, max_nodes=5000); L.auto_subs = True
    trees, fails, tabs = {}, [], set()
    for f in fns:
        signal.alarm(60)
        try:
            t = L.lift(f); trees[f] = t; icall_tables(t, rom, tabs)
        except (Unsupported, TO, RecursionError) as ex:
            fails.append(('0x%08X' % f, str(ex)[:60] or type(ex).__name__))
        finally:
            signal.alarm(0)
    for a, t in L.subtrees.items():
        trees.setdefault(a, t); icall_tables(t, rom, tabs)
    return {('0x%08X' % a): enc_t(t) for a, t in trees.items()}, fails, tabs


def lift_all(path, fns, procs=4):
    from multiprocessing import Pool
    fns = sorted(fns); chunks = [fns[i::procs * 4] for i in range(procs * 4)]
    trees, fails, tabs = {}, [], set()
    with Pool(procs) as pool:
        for k, (t, f, tb) in enumerate(pool.imap_unordered(_work, [(path, c) for c in chunks if c])):
            trees.update(t); fails += f; tabs |= tb
            print('  변환 묶음 %d/%d' % (k + 1, len(chunks)), flush=True)
    return trees, fails, tabs


def thumb_fn(rom, p):
    return rom.ok(p) and p & 1 and 0x08000000 <= p < 0x08300000


def main():
    if len(sys.argv) < 3:
        print(__doc__); return 2
    rb = open(sys.argv[1], 'rb').read(); rom = Rom(rb)
    interests, preds = [], {}
    events, vdata = {}, {}
    for t in range(5):
        tb = rom.u32(STATE_TABLE + 4 * t)
        for i in range(table_len(rom, t)):
            e = rom.u32(tb + 4 * i)
            f = rom.u32(e + 8)
            mx, mn = rom.u32(e + 0x1C), rom.u32(e + 0x20)
            interests.append([t, i, rom.u8(e + 0x0F), rom.u8(e + 0x10), rom.u8(e + 0x12), '0x%08X' % f,
                              '0x%08X' % mx if rom.ok(mx) else None, '0x%08X' % mn if rom.ok(mn) else None])
            preds[f] = None
            # MAX/MIN 사건: 변형 목록 (0x08119CA0: 참인 첫 변형, 없으면 n 번째)
            for ev in (mx, mn):
                if not rom.ok(ev) or ev in events: continue
                n = rom.u8(ev + 3); lst = rom.u32(ev + 0x24); vs = []
                for k in range(n + 1):
                    v = rom.u32(lst + 4 * k) if rom.ok(lst) else 0
                    if not rom.ok(v): vs.append(None); continue
                    pf, d = rom.u32(v), rom.u32(v + 4)
                    vs.append(['0x%08X' % pf if k < n and thumb_fn(rom, pf) else None, '0x%08X' % d if rom.ok(d) else None])
                    if k < n and thumb_fn(rom, pf): preds[pf] = None
                    if rom.ok(d) and d not in vdata:
                        pre, post = rom.u32(d + 0x18), rom.u32(d + 0x1C)
                        vdata[d] = {"pre": '0x%08X' % pre if thumb_fn(rom, pre) else None,
                                    "post": '0x%08X' % post if thumb_fn(rom, post) else None,
                                    "raw": rb[d - B:d - B + 0x30].hex()}
                        if thumb_fn(rom, post): preds[post] = None
                events[ev] = {"dayMode": rom.u8(ev + 1), "count": n, "variants": vs, "list": lst}
    print('관심사 %d, 사건 %d, 결과 기록 %d, 변환할 함수 %d' % (len(interests), len(events), len(vdata), len(preds)), flush=True)
    trees, fails, tabs = lift_all(sys.argv[1], preds)
    # 함수 포인터 표의 대상도 변환 (새 표가 안 나올 때까지)
    done_tabs = set()
    while tabs - done_tabs:
        new = set()
        for tb in tabs - done_tabs:
            k = 0
            while k < 256 and thumb_fn(rom, rom.u32(tb + 4 * k)):
                f = rom.u32(tb + 4 * k) & ~1
                if '0x%08X' % f not in trees and '0x%08X' % (f | 1) not in trees: new.add(f)
                k += 1
        done_tabs |= tabs
        if not new: break
        print('함수 포인터 대상 %d개 변환' % len(new), flush=True)
        t2, f2, tb2 = lift_all(sys.argv[1], new)
        trees.update(t2); fails += f2; tabs |= tb2
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
    for j in range(1024):   # 직업 표 (0x0889D3B8): 항목 +5 공통 후보 수, +7 직업 분류, +0x1C 공통 후보, +0x20 직급별 후보
        p = rom.u32(0x0889D3B8 + 4 * j)
        if not rom.ok(p): break
        n1 = rom.u8(p + 5); l1 = rom.u32(p + 0x1C)
        ranks = []; rp = rom.u32(p + 0x20)
        for r in range(16):
            q = rom.u32(rp + 4 * r) if rom.ok(rp) else 0
            if not rom.ok(q): break
            n2 = rom.u8(q + 9); l2 = rom.u32(q + 0x0C)
            ranks.append([rom.u16(l2 + 2 * k) for k in range(n2)] if rom.ok(l2) else [])
        # 0x081191A4: 분류(+7)와 b15 로 다음 직업 후보표를 고른다 (0x0889D63C + 분류*32 + b15*4 + 0x0C)
        cat = rom.u8(p + 7)
        jmap = [rom.u16(0x0889D63C + 32 * cat + 4 * b + 0x0C) for b in range(3)]
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
           "events": {('0x%08X' % a): v for a, v in events.items()}, "variantData": {('0x%08X' % a): v for a, v in vdata.items()},
           "modeTable": mode_table, "b15Table": b15_table, "jobs": jobs, "specials": specials,
           "rom": rom_spans(rom, trees_raw_consts(trees), list(events) + list(vdata)), "liftFailures": fails}
    with open(sys.argv[2], 'w', encoding='utf8') as fp:
        json.dump(out, fp, separators=(',', ':'))
    print('관심사 %d, 트리 %d (실패 %d), 직업 %d, 특별 후보표 %d, ROM 조각 %d, 크기 %.1f KB' % (
        len(interests), len(trees), len(fails), len(jobs), len(specials), len(out['rom']), os.path.getsize(sys.argv[2]) / 1024))
    return 0


if __name__ == '__main__':
    sys.exit(main())
