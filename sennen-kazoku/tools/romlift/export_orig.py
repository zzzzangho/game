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
           0x08115778: 2, 0x08110B90: 2, 0x08110608: 1, 0x08028524: 3,
           0x0824F700: 3, 0x0824E2BC: 3, 0x08248EE0: 1, 0x082494CC: 1, 0x080955C8: 0, 0x080959DC: 4}
# 새 가족 장면의 화면 정리 함수 (0x0802D7A0 끝부분), 장면 객체 128개 정리(0x08001014, 가족 레코드 만들기 시작) —
# 다음 장면으로 바꾸기(0x0809AF60, 가족 레코드 만들기 끝) — 규칙 상태를 바꾸지 않음.
# 0x08001014 를 건너뛰어도 원작 가족 만들기 결과 EWRAM 전체가 같고, 0x0809AF60 을 건너뛰면 저장 범위 안에서는
# 0x0203BE04(장면 작업 핸들, 0x080A6038 이 0x08003C00 결과를 넣는 곳)만 다르다.
UI_NOOP = (0x08047024, 0x08048390, 0x08047864, 0x08046F80, 0x08046804, 0x08003FE8, 0x08001014, 0x0809AF60,
           # 사건 뒤 메인 장면 복귀(0x080111BA)의 화면·시스템 부분: 힙 초기화, 소리, 리셋 확인, 화면 DMA, 작업(task) 만들기
           0x08006A0C, 0x0802A630, 0x080087C4, 0x08003A2C, 0x08003C00)
NATIVES.update({a: 1 for a in UI_NOOP})
# 크게 변환해야 하는 새 가족 함수: 추천 가족 틀, 구성원 마무리(채우기 0x0804B1F0 포함), 가족 레코드 만들기
BIG = (0x0804BC2C, 0x0802D7A0, 0x080417E0)
NATIVE_NAMES = {0x08000614: "rand", 0x0824F640: "umod", 0x0824F5C8: "udiv", 0x0824F460: "sdiv", 0x0824F4F8: "smod",
                0x08115778: "rel", 0x08110B90: "slots", 0x08110608: "inhouse", 0x08028524: "select",
                0x0824F700: "memcpy", 0x0824E2BC: "cpuset", 0x08248EE0: "noop", 0x082494CC: "noop", 0x080955C8: "noop", 0x080959DC: "noop"}
NATIVE_NAMES.update({a: "noop" for a in UI_NOOP})
# 원작 힙(머리 0x0202BFB0): 할당 0x08006A58, 해제 0x0800695C. 앱은 원작 힙 상태를 두지 않고 따로 잡은 작업 영역에서 할당한다.
NATIVES.update({0x08006A58: 1, 0x0800695C: 1})
NATIVE_NAMES.update({0x08006A58: "malloc", 0x0800695C: "free"})
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
    if k == 'bind': return ['d', t[1], enc_e(t[2]), enc_t(t[3])]
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
            if isinstance(x, list) and x and isinstance(x[0], str) and len(x[0]) == 1 and x[0] in 'rijlsfeocbd': wt(x)
            else: we(x, False)
    for t in trees.values(): wt(t)
    return out


def rom_spans(rom, consts=(), extra=(), more=()):
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
    # 특별 사건 표 (0x08119B8C: m32[m32[0x085BD4C8 + 종류*4] + 번호*4]) — 종류 9개, 각 사건 기록 포인터 배열
    for t in range(16):
        tb = rom.u32(0x085BD4C8 + 4 * t)
        if not isptr(tb): break
        walk(0x085BD4C8 + 4 * t, 4, 0); walk(tb, 4 * n_ptrs(tb), 1)
    # 새 가족: 성(姓) 256개 표, 추천 가족 틀 8개(20바이트씩)
    walk(0x088A3F84, 4 * 256, 1)
    walk(0x08587DBC, 8 * 20, 0)
    for c in consts: walk(c, 0x200, 2)
    for a in extra: walk(a, 0x30, 2)
    for t in range(5):
        tb = rom.u32(STATE_TABLE + 4 * t)
        walk(STATE_TABLE + 4 * t, 4, 0)
        n = table_len(rom, t); walk(tb, 4 * n, 0)
        for i in range(n): walk(rom.u32(tb + 4 * i), 0x28, 0)
    for a, size in more: spans.add((a, size))   # 실행해서 모은 범위 (newgame_spans.py)
    # 겹치는 구간 합치기
    iv = sorted((a, a + s) for a, s in spans)
    out = []
    for a, b in iv:
        if out and a <= out[-1][1]: out[-1][1] = max(out[-1][1], b)
        else: out.append([a, b])
    return [[a, rom.b[a - B:b - B].hex()] for a, b in out]


def more_spans(out_path):
    """출력 파일 옆 newgame_spans.json (newgame_spans.py 결과) 이 있으면 그 범위도 넣는다."""
    p = os.path.join(os.path.dirname(os.path.abspath(out_path)), 'newgame_spans.json')
    return [tuple(x) for x in json.load(open(p))] if os.path.exists(p) else []


def respan(rom_path, out_path):
    """변환은 다시 하지 않고 ROM 조각·네이티브 표만 다시 계산한다 (--respan)."""
    rb = open(rom_path, 'rb').read(); rom = Rom(rb)
    out = json.load(open(out_path, encoding='utf8'))
    trees = {}
    for line in open(out_path + '.trees', encoding='utf8'):
        a, _, t = line.rstrip('\n').partition('\t'); trees[a] = json.loads(t)
    out['natives'] = {('0x%08X' % a): n for a, n in NATIVE_NAMES.items()}
    out['rom'] = rom_spans(rom, trees_raw_consts(trees), [int(a, 16) for a in out['events']] + [int(a, 16) for a in out['variantData']],
                           more_spans(out_path))
    with open(out_path, 'w', encoding='utf8') as fp: json.dump(out, fp, separators=(',', ':'))
    print('ROM 조각 %d, 크기 %.1f KB' % (len(out['rom']), os.path.getsize(out_path) / 1024))
    return 0


def add_funcs(rom_path, out_path, addrs):
    """이미 만든 트리 파일에 함수를 더 변환해 넣는다 (--add 주소...). 함수 포인터로 부르는 대상 등."""
    trees = {}
    for line in open(out_path + '.trees', encoding='utf8'):
        a, _, t = line.rstrip('\n').partition('\t'); trees[a] = t
    new = [a for a in addrs if '0x%08X' % a not in trees]
    big = '--big' in sys.argv
    t2, f2, _ = lift_all(rom_path, new, procs=4, big=big) if new else ({}, [], set())
    n = 0
    for a, t in t2.items():
        if a not in trees: trees[a] = json.dumps(t, separators=(',', ':')); n += 1
    with open(out_path + '.trees', 'w', encoding='utf8') as fp:
        for a in sorted(trees): fp.write(a + '\t' + trees[a] + '\n')
    out = json.load(open(out_path, encoding='utf8'))
    out['liftFailures'] = out.get('liftFailures', []) + f2
    with open(out_path, 'w', encoding='utf8') as fp: json.dump(out, fp, separators=(',', ':'))
    print('트리 %d개 추가, 실패 %s' % (n, f2))
    return respan(rom_path, out_path)


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
        elif k == 'bind': we(n[2]); wt(n[3])
    wt(t)


def _work(args):
    if len(args) > 2:
        # 큰 함수는 트리가 깊어 재귀 한도·스택을 늘린 스레드에서 변환
        import threading
        sys.setrecursionlimit(1000000); threading.stack_size(1024 * 1024 * 1024)
        box = []
        th = threading.Thread(target=lambda: box.append(_work1(args))); th.start(); th.join()
        return box[0]
    return _work1(args)


def _work1(args):
    path, fns = args[:2]; big = len(args) > 2
    import signal
    class TO(Exception): pass
    def al(*a): raise TO()
    import threading
    main = threading.current_thread() is threading.main_thread()
    if main: signal.signal(signal.SIGALRM, al)
    rb = open(path, 'rb').read(); rom = Rom(rb)
    L = Lifter(rb, NATIVES, max_steps=200000 if big else 50000, max_nodes=500000 if big else 50000); L.auto_subs = True
    trees, fails, tabs = {}, [], set()
    for f in fns:
        if main: signal.alarm(60)
        try:
            t = L.lift(f); trees[f] = t; icall_tables(t, rom, tabs)
        except (Unsupported, TO, RecursionError) as ex:
            fails.append(('0x%08X' % f, str(ex)[:60] or type(ex).__name__))
        finally:
            if main: signal.alarm(0)
    for a, t in L.subtrees.items():
        trees.setdefault(a, t); icall_tables(t, rom, tabs)
    return {('0x%08X' % a): enc_t(t) for a, t in trees.items()}, fails, tabs


def lift_all(path, fns, procs=4, big=False):
    from multiprocessing import Pool
    fns = sorted(fns); chunks = [[f] for f in fns] if big else [fns[i::procs * 4] for i in range(procs * 4)]
    trees, fails, tabs = {}, [], set()
    with Pool(procs) as pool:
        for k, (t, f, tb) in enumerate(pool.imap_unordered(_work, [(path, c, 1) if big else (path, c) for c in chunks if c])):
            trees.update(t); fails += f; tabs |= tb
            print('  변환 묶음 %d/%d' % (k + 1, len(chunks)), flush=True)
    return trees, fails, tabs


def thumb_fn(rom, p):
    return rom.ok(p) and p & 1 and 0x08000000 <= p < 0x08300000


def scan_events(rom):
    """ROM 데이터 영역에서 사건 기록(0x28 바이트) 찾기."""
    fn = lambda p: rom.ok(p) and p & 1 and 0x08100000 <= p < 0x08300000
    out = []
    for a in range(0x08400000, B + len(rom.b) - 0x28, 4):
        if not (fn(rom.u32(a + 0x18)) and fn(rom.u32(a + 0x1C))): continue
        lst = rom.u32(a + 0x24)
        if not rom.ok(lst): continue
        n = rom.u8(a + 3)
        if n > 64: continue
        good = True
        for k in range(n):
            v = rom.u32(lst + 4 * k)
            if not rom.ok(v) or not fn(rom.u32(v)) or not rom.ok(rom.u32(v + 4)): good = False; break
        if good: out.append(a)
    return out


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
    # 관심사 밖의 사건 기록(독립·가족·예약 사건 등, 같은 0x28 바이트 배치): ROM 데이터 영역을 훑어 모두 넣는다
    #   +3 변형 수, +0x18/+0x1C 함수(조건·시작), +0x24 변형 목록 [판정 함수, 결과 기록]
    for ev in scan_events(rom):
        if ev in events: continue
        n = rom.u8(ev + 3); lst = rom.u32(ev + 0x24); vs = []
        for k in range(n + 1):
            v = rom.u32(lst + 4 * k) if rom.ok(lst) else 0
            if not rom.ok(v): vs.append(None); continue
            pf, d = rom.u32(v), rom.u32(v + 4)
            vs.append(['0x%08X' % pf if k < n and thumb_fn(rom, pf) else None, '0x%08X' % d if rom.ok(d) else None])
            if k < n and thumb_fn(rom, pf): preds[pf] = None
            if rom.ok(d) and rom.ok(d + 0x20) and d not in vdata:
                pre, post = rom.u32(d + 0x18), rom.u32(d + 0x1C)
                vdata[d] = {"pre": '0x%08X' % pre if thumb_fn(rom, pre) else None,
                            "post": '0x%08X' % post if thumb_fn(rom, post) else None,
                            "raw": rb[d - B:d - B + 0x30].hex()}
                if thumb_fn(rom, post): preds[post] = None
        for o in (0x18, 0x1C):
            f = rom.u32(ev + o)
            if thumb_fn(rom, f): preds[f] = None
        events[ev] = {"dayMode": rom.u8(ev + 1), "count": n, "variants": vs, "list": lst,
                      "f18": '0x%08X' % rom.u32(ev + 0x18), "f1c": '0x%08X' % rom.u32(ev + 0x1C), "raw": rb[ev - B:ev - B + 0x28].hex()}
    # 앱 진행에 쓰는 원작 함수: 관심사 하루 처리, 시대, 나이 단계, 새 가족 구성원 채우기·가족 레코드 만들기
    for f in (0x08027E78, 0x08111A58, 0x08111888, 0x08119C5C, 0x08119B8C, 0x0804B1F0): preds[f] = None
    print('관심사 %d, 사건 %d, 결과 기록 %d, 변환할 함수 %d' % (len(interests), len(events), len(vdata), len(preds)), flush=True)
    trees, fails, tabs = lift_all(sys.argv[1], preds)
    print('새 가족 함수 변환 (오래 걸림)', flush=True)
    t2, f2, tb2 = lift_all(sys.argv[1], BIG, procs=len(BIG), big=True)
    for a, t in t2.items(): trees.setdefault(a, t)
    fails += f2; tabs |= tb2
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
           "interests": interests, "treesFile": os.path.basename(sys.argv[2]) + ".trees", "candidates": cands,
           "events": {('0x%08X' % a): v for a, v in events.items()}, "variantData": {('0x%08X' % a): v for a, v in vdata.items()},
           "modeTable": mode_table, "b15Table": b15_table, "jobs": jobs, "specials": specials,
           "rom": rom_spans(rom, trees_raw_consts(trees), list(events) + list(vdata), more_spans(sys.argv[2])), "liftFailures": fails}
    with open(sys.argv[2], 'w', encoding='utf8') as fp:
        json.dump(out, fp, separators=(',', ':'))
    # 트리는 한 줄에 함수 하나 ("0x주소\t트리 JSON") — 앱은 부르는 함수만 그때 읽는다
    with open(sys.argv[2] + '.trees', 'w', encoding='utf8') as fp:
        for a in sorted(trees): fp.write(a + '\t' + json.dumps(trees[a], separators=(',', ':')) + '\n')
    print('관심사 %d, 트리 %d (실패 %d), 직업 %d, 특별 후보표 %d, ROM 조각 %d, 크기 %.1f KB + 트리 %.1f KB' % (
        len(interests), len(trees), len(fails), len(jobs), len(specials), len(out['rom']), os.path.getsize(sys.argv[2]) / 1024,
        os.path.getsize(sys.argv[2] + '.trees') / 1024))
    return 0


if __name__ == '__main__':
    # 새 가족 함수 트리는 깊어서 재귀 한도·스택을 늘린 스레드에서 실행
    import threading
    sys.setrecursionlimit(1000000); threading.stack_size(1024 * 1024 * 1024)
    rc = []
    if '--add' in sys.argv:
        go = lambda: add_funcs(sys.argv[1], sys.argv[2], [int(x, 16) for x in sys.argv[sys.argv.index('--add') + 1:] if x != '--big'])
    elif '--respan' in sys.argv:
        go = lambda: respan(sys.argv[1], sys.argv[2])
    else:
        go = main
    th = threading.Thread(target=lambda: rc.append(go())); th.start(); th.join()
    sys.exit(rc[0] if rc else 1)
