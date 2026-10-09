"""Thumb(-O0) 함수 기호 실행 → 결정 트리 IR.
IR:  ('ret', e) | ('if', cond, then, else) | ('let', var, ('call', fn, args), body) | ('store', size, addr, val, body)
expr: int | ('v', n) | ('sp', off) | (op, a, b) | ('load', size, addr) | ('ram', size, addr)
cond: (cc, a, b)  cc in eq ne ltu leu gtu geu lt le gt ge
"""
import struct, capstone, re, sys
sys.setrecursionlimit(20000)
B = 0x08000000
M32 = 0xFFFFFFFF


class Unsupported(Exception):
    pass


class NeedMem(Unsupported):
    """지역 변수를 변수 첨자·포인터로 읽고 쓰는 함수: 스택을 실제 프레임 메모리에 함께 쓰는 방식으로 다시 변환."""
    pass


def has_sp(e):
    """식 안에 스택 주소가 있는가 (공유된 부분식이 많아 방문한 것은 다시 보지 않는다)."""
    if not isinstance(e, tuple): return False
    seen, st = set(), [e]
    while st:
        x = st.pop()
        if not isinstance(x, tuple) or id(x) in seen: continue
        seen.add(id(x))
        if x and x[0] == 'sp': return True
        st.extend(x[1:])
    return False


def s32(x):
    x &= M32
    return x - (1 << 32) if x & 0x80000000 else x


def binop(op, a, b):
    if isinstance(a, int) and isinstance(b, int):
        if op == 'add': return (a + b) & M32
        if op == 'sub': return (a - b) & M32
        if op == 'mul': return (a * b) & M32
        if op == 'and': return a & b
        if op == 'or': return a | b
        if op == 'xor': return a ^ b
        if op == 'shl': return (a << b) & M32 if b < 32 else 0
        if op == 'lsr': return a >> b if b < 32 else 0
        if op == 'asr': return (s32(a) >> min(b, 31)) & M32
        if op == 'udiv': return a // b if b else 0
        if op == 'umod': return a % b if b else a
        if op == 'sdiv':
            if not b: return 0
            q = abs(s32(a)) // abs(s32(b)); return (q if (s32(a) < 0) == (s32(b) < 0) else -q) & M32
        if op == 'smod':
            if not b: return a
            r = abs(s32(a)) % abs(s32(b)); return (-r if s32(a) < 0 else r) & M32
    # 간단화
    if op == 'add':
        if b == 0: return a
        if a == 0: return b
        if isinstance(a, tuple) and a[0] == 'sp' and isinstance(b, int): return ('sp', a[1] + s32(b))
        if isinstance(b, tuple) and b[0] == 'sp' and isinstance(a, int): return ('sp', b[1] + s32(a))
        if isinstance(b, int) and isinstance(a, tuple) and a[0] == 'add' and isinstance(a[2], int):
            return binop('add', a[1], (a[2] + b) & M32)
    if op == 'sub' and isinstance(b, int):
        if isinstance(a, tuple) and a[0] == 'sp': return ('sp', a[1] - s32(b))
        return binop('add', a, (-b) & M32)
    if op in ('or', 'xor') and b == 0: return a
    if op in ('or', 'xor') and a == 0: return b
    if op == 'and' and (a == 0 or b == 0): return 0
    if op == 'and' and b == M32: return a
    if op in ('shl', 'lsr', 'asr') and b == 0: return a
    if op == 'lsr' and isinstance(b, int) and isinstance(a, tuple) and a[0] == 'shl' and a[2] == b:
        return binop('and', a[1], M32 >> b)
    if op == 'asr' and isinstance(b, int) and isinstance(a, tuple) and a[0] == 'shl' and a[2] == b:
        return ('sext', a[1], 32 - b)
    if op == 'and' and isinstance(b, int) and isinstance(a, tuple) and a[0] == 'and' and isinstance(a[2], int):
        return binop('and', a[1], a[2] & b)
    if op == 'and' and isinstance(b, int) and isinstance(a, tuple) and a[0] == 'load' and b >= (1 << (8 * a[1])) - 1:
        return a
    if op == 'and' and isinstance(b, int) and isinstance(a, tuple) and a[0] == 'v' and len(a) > 2 and a[2] and b >= (1 << a[2]) - 1:
        return a
    return (op, a, b)


def is_sp(x): return isinstance(x, tuple) and x[0] == 'sp'


class State:
    __slots__ = ('r', 'mem', 'flags', 'pc', 'depth', 'steps', 'fns', 'stops', 'skip', 'membacked')

    def copy(self):
        s = State(); s.r = dict(self.r); s.mem = dict(self.mem); s.flags = self.flags; s.pc = self.pc
        s.depth = self.depth; s.steps = self.steps; s.fns = list(self.fns); s.stops = self.stops; s.skip = False; s.membacked = self.membacked; return s


CC = {'beq': 'eq', 'bne': 'ne', 'bhs': 'geu', 'bcs': 'geu', 'blo': 'ltu', 'bcc': 'ltu', 'bhi': 'gtu', 'bls': 'leu',
      'bge': 'ge', 'blt': 'lt', 'bgt': 'gt', 'ble': 'le', 'bmi': 'mi', 'bpl': 'pl'}
NEG = {'eq': 'ne', 'ne': 'eq', 'geu': 'ltu', 'ltu': 'geu', 'gtu': 'leu', 'leu': 'gtu', 'ge': 'lt', 'lt': 'ge', 'gt': 'le', 'le': 'gt', 'mi': 'pl', 'pl': 'mi'}


def eval_cc(cc, a, b):
    ua, ub = a & M32, b & M32; sa, sb = s32(a), s32(b)
    return {'eq': ua == ub, 'ne': ua != ub, 'geu': ua >= ub, 'ltu': ua < ub, 'gtu': ua > ub, 'leu': ua <= ub,
            'ge': sa >= sb, 'lt': sa < sb, 'gt': sa > sb, 'le': sa <= sb, 'mi': s32(a - b) < 0, 'pl': s32(a - b) >= 0}[cc]


class Lifter:
    def __init__(self, rom, natives, max_steps=4000, max_nodes=20000):
        self.rom = rom; self.md = capstone.Cs(capstone.CS_ARCH_ARM, capstone.CS_MODE_THUMB)
        self.natives = natives  # addr -> name (호출을 그대로 남김)
        self.cache = {}; self.nvar = 0; self.max_steps = max_steps; self.max_nodes = max_nodes
        self.unknown_calls = set()
        self.subs = set(); self.subtrees = {}; self.auto_subs = False; self.nosub = set(); self.inprog = set()
        self.join_cache = {}; self.head_cache = {}; self.exit_cache = {}; self.use_joins = True; self.use_loops = True; self.ntag = 0; self.mem_mode = False; self.allow_loops = True
        self.pre = []   # store() 가 앞에 넣을 스택 쓰기 (지역 변수 주소가 메모리로 나갈 때)
        # 경로 폭발 대응: 후지배자가 아니라 "두 갈래가 처음 다시 만나는 명령"에서 합친다. 합치는 곳이 겹쳐질 수 있으므로
        # if2·end 에 번호(tag)를 붙여 실행기가 맞는 합류로 간다. 기존 방식이 실패한 함수에만 쓴다.
        self.agg = False; self.agg_cache = {}

    def u(self, a, n):
        return int.from_bytes(self.rom[a - B:a - B + n], 'little')

    def ins(self, a):
        if a not in self.cache:
            i = next(self.md.disasm(self.rom[a - B:a - B + 4], a), None)
            if i is None: raise Unsupported('decode %x' % a)
            ops = [o.strip() for o in re.split(r',(?![^\[]*\])', i.op_str)] if i.op_str else []
            self.cache[a] = (i.mnemonic, ops, i.size, i.op_str)
        return self.cache[a]

    def lift(self, fn, nargs=4):
        s = State(); s.r = {('r%d' % k): ('arg', k) for k in range(nargs)}
        s.r['sp'] = ('sp', 0); s.r['lr'] = 'RET'; s.mem = {}; s.flags = None; s.pc = fn & ~1; s.depth = 0; s.steps = 0
        s.fns = [fn & ~1]; s.stops = (); s.skip = False; s.membacked = False
        for k in range(4):  # 스택 인자
            s.mem[k * 4] = (('sarg', k), 4)
        self.nodes = 0
        # 먼저 반복문을 펼쳐 보고(값이 정해지는 반복은 펼친 결과가 더 단순하다), 안 되면 반복문 노드로 바꾼다.
        # 지역 변수를 포인터로 다루면 스택을 프레임 메모리에 함께 쓰는 방식으로 다시 한다.
        saved, saved_mem = self.use_loops, self.mem_mode
        try:
            last = None
            saved_agg = self.agg
            for agg in ((False, True) if not saved_agg else (True,)):
                self.agg = agg
                for mem in (False, True):
                    for loops in ((False, True) if self.allow_loops else (False,)):
                        self.use_loops, self.mem_mode = loops, mem; self.nodes = 0
                        try:
                            return self.run(s.copy())
                        except NeedMem as ex:
                            last = ex; break
                        except Unsupported as ex:
                            last = ex
                if not (isinstance(last, Unsupported) and 'too many' in str(last)): break
            raise last
        finally:
            self.use_loops, self.mem_mode = saved, saved_mem
            self.agg = saved_agg

    # ---- 값 읽기
    def reg(self, s, name):
        name = {'sb': 'r9', 'sl': 'r10', 'fp': 'r11', 'ip': 'r12'}.get(name, name)
        if name == 'pc': return s.pc + 4
        return s.r.get(name, ('undef', name))

    def setreg(self, s, name, v):
        name = {'sb': 'r9', 'sl': 'r10', 'fp': 'r11', 'ip': 'r12'}.get(name, name)
        s.r[name] = v

    def opval(self, s, o):
        if o.startswith('#'): return int(o[1:], 0) & M32
        return self.reg(s, o)

    def load(self, s, addr, size, signed=False):
        if not is_sp(addr) and has_sp(addr):
            if not self.mem_mode: raise NeedMem('stack pointer load')
            v = ('load', size, addr)
            return ('sext', v, 8 * size) if signed and size < 4 else v
        if is_sp(addr):
            off = addr[1]
            if off in s.mem and s.mem[off][1] == size:
                val = s.mem[off][0]
            elif self.mem_mode or s.membacked:
                val = ('load', size, addr)   # 호출한 함수가 포인터로 썼을 수 있다 → 프레임 메모리에서
            else:
                # 크기가 다른 칸들에서 바이트별로 모은다
                val = 0
                for b in range(size):
                    byte = None
                    for o, (v, sz) in s.mem.items():
                        if o <= off + b < o + sz:
                            sh = 8 * (off + b - o)
                            byte = binop('and', binop('lsr', v, sh), 0xFF); break
                    if byte is None: byte = ('undef_stack', off + b)
                    val = binop('or', val, binop('shl', byte, 8 * b)) if b else byte
            if signed and size < 4: val = ('sext', val, 8 * size)
            return val
        if isinstance(addr, int):
            if B <= addr < B + len(self.rom):
                v = self.u(addr, size)
                if signed and size < 4 and v & (1 << (8 * size - 1)): v -= 1 << (8 * size)
                return v & M32
            v = ('ram', size, addr)
            return ('sext', v, 8 * size) if signed and size < 4 else v
        v = ('load', size, addr)
        return ('sext', v, 8 * size) if signed and size < 4 else v

    def store(self, s, addr, val, size):
        if is_sp(addr):
            off = addr[1]
            for o in [o for o, (v, sz) in s.mem.items() if o < off + size and off < o + sz and o != off]:
                del s.mem[o]
            if size < 4 and not isinstance(val, int) or size < 4:
                val = binop('and', val, (1 << (8 * size)) - 1)
            s.mem[off] = (val, size)
            return ('store', size, addr, val) if (self.mem_mode or s.membacked) else None   # 프레임 메모리에도 쓴다
        if has_sp(val) and not has_sp(addr):
            # 지역 변수 주소를 메모리에 넣는다(예: DMA 원본 주소 레지스터 0x040000D4) → 그 주소로 스택을 읽을 수 있으니 먼저 프레임에 쓴다
            self.pre += self.spill_stack(s)
        if has_sp(addr):
            if not self.mem_mode: raise NeedMem('stack pointer store')
            # 어느 칸을 덮었는지 모르므로 이후 스택 읽기는 메모리에서
            isret = lambda v: v == 'RET' or (isinstance(v, tuple) and v[0] == 'retaddr')
            s.mem = {o: (v, sz) if isret(v) else (('load', sz, ('sp', o)), sz) for o, (v, sz) in s.mem.items()}
        return ('store', size, addr, val)

    def memaddr(self, s, o):
        m = re.match(r'\[(\w+)(?:,\s*(#?-?\w+))?\]', o)
        base = self.reg(s, m.group(1)) if m.group(1) != 'pc' else (s.pc + 4) & ~3
        if m.group(2) is None: return base
        off = self.opval(s, m.group(2))
        return binop('add', base, off)

    def eager(self, val, pending):
        """메모리 읽기는 읽는 순간의 값이어야 한다(뒤에 같은 곳에 쓰기가 올 수 있음) → 변수에 묶는다."""
        x = val[1] if isinstance(val, tuple) and val[0] == 'sext' else val
        if isinstance(x, tuple) and x[0] == 'load' and self.rom_addr(x[2]):
            return val          # ROM 은 바뀌지 않는다 (점프 표 등은 식 그대로 둔다)
        if isinstance(x, tuple) and x[0] in ('ram', 'load'):
            v = self.newvar(); pending.append((v[1], val)); return v
        return val

    @staticmethod
    def rom_addr(a):
        """주소식이 ROM 상수 + 첨자 꼴인가."""
        if isinstance(a, tuple) and a[0] == 'add':
            for c in (a[1], a[2]):
                if isinstance(c, int) and 0x08000000 <= c < 0x0A000000: return True
        return False

    def newvar(self, bits=0):
        self.nvar += 1; return ('v', self.nvar, bits)

    # ---- 분기 합류점: 함수 안 명령 단위 흐름도의 직접 후지배자 (if/else 가 다시 만나는 곳)
    def succs(self, a):
        mn, ops, size, raw = self.ins(a)
        if mn == 'b': return [int(ops[0][1:], 16)]
        if mn in CC: return [int(ops[0][1:], 16), a + size]
        if mn == 'bx' or (mn == 'pop' and 'pc' in raw) or (mn in ('mov', 'movs') and ops and ops[0] == 'pc'): return []
        return [a + size]

    def joins(self, fn):
        if fn in self.join_cache: return self.join_cache[fn]
        nodes, work = set(), [fn]
        while work:
            a = work.pop()
            if a in nodes: continue
            try: nx = self.succs(a)
            except Unsupported: nx = []
            nodes.add(a)
            if len(nodes) > 6000: self.join_cache[fn] = {}; return {}
            work.extend(x for x in nx if x not in nodes)
        succ = {}
        for a in nodes:
            try: succ[a] = [x for x in self.succs(a) if x in nodes]
            except Unsupported: succ[a] = []
        EXIT = -1
        allset = frozenset(nodes) | {EXIT}
        pd = {a: allset for a in nodes}
        order = sorted(nodes, reverse=True)
        changed = True
        while changed:
            changed = False
            for a in order:
                ss = succ[a]
                if not ss: new = frozenset((a, EXIT))
                else:
                    inter = None
                    for x in ss: inter = pd[x] if inter is None else inter & pd[x]
                    new = inter | {a}
                if new != pd[a]: pd[a] = new; changed = True
        res = {}
        for a in nodes:
            cand = [x for x in pd[a] if x != a and x != EXIT]
            if not cand: continue
            res[a] = max(cand, key=lambda x: len(pd[x]))   # 가장 가까운 후지배자
        # 반복문 머리: 깊이 우선 탐색에서 조상으로 돌아가는 간선의 대상
        heads, state = set(), {}
        stack = [(fn, iter(succ.get(fn, [])))]; state[fn] = 1
        while stack:
            a, it = stack[-1]
            nxt = next(it, None)
            if nxt is None: state[a] = 2; stack.pop(); continue
            if state.get(nxt) == 1: heads.add(nxt)
            elif nxt not in state: state[nxt] = 1; stack.append((nxt, iter(succ.get(nxt, []))))
        # 반복문 출구: 머리의 후지배자 중 반복문 몸체(머리와 서로 닿는 명령들) 밖에서 가장 가까운 것
        pred = {}
        for a, ss in succ.items():
            for b in ss: pred.setdefault(b, []).append(a)
        exits = {}
        for h in heads:
            fw, st = set(), [h]
            while st:
                a = st.pop()
                if a in fw: continue
                fw.add(a); st.extend(succ.get(a, []))
            bw, st = set(), [h]
            while st:
                a = st.pop()
                if a in bw: continue
                bw.add(a); st.extend(pred.get(a, []))
            body = fw & bw
            cand = [x for x in pd[h] if x not in body and x != EXIT]
            if cand: exits[h] = max(cand, key=lambda x: len(pd[x]))
        self.join_cache[fn] = res; self.head_cache[fn] = heads; self.exit_cache[fn] = exits
        return res

    def agg_join(self, fn, pc, a, b):
        """두 갈래(a, b)에서 모두 닿는 명령 중 가장 가까운 것 (두 거리 중 큰 값이 최소). 반복문 머리는 넘지 않는다."""
        key = (fn, pc)
        if key in self.agg_cache: return self.agg_cache[key]
        heads = self.loop_heads(fn)
        def dist(start):
            d = {start: 0}; q = [start]; i = 0
            while i < len(q):
                x = q[i]; i += 1
                if len(d) > 4000: break
                if x in heads and x != start: continue
                try: nx = self.succs(x)
                except Unsupported: nx = []
                for y in nx:
                    if y not in d: d[y] = d[x] + 1; q.append(y)
            return d
        da, db = dist(a), dist(b)
        common = [x for x in da if x in db and x != pc]
        J = min(common, key=lambda x: (max(da[x], db[x]), da[x] + db[x], x)) if common else None
        if J is None: J = self.joins(fn).get(pc)
        self.agg_cache[key] = J
        return J

    def loop_heads(self, fn):
        self.joins(fn)
        return self.head_cache.get(fn, set())

    def merge(self, ends):
        sts = [st for _, st in ends]
        base = sts[0].copy(); assigns = {i: [] for i, _ in ends}
        keys = set().union(*[set(st.r) for st in sts])
        isret = lambda v: v == 'RET' or (isinstance(v, tuple) and v[0] == 'retaddr')
        for k in keys:
            vals = [st.r.get(k, ('undef', k)) for st in sts]
            if all(v == vals[0] for v in vals): base.r[k] = vals[0]; continue
            if any(isret(v) for v in vals): base.r[k] = ('undef', k); continue
            v = self.newvar(); base.r[k] = v
            for (i, _), val in zip(ends, vals): assigns[i].append((v[1], val))
        offs = set().union(*[set(st.mem) for st in sts]); base.mem = {}
        for o in offs:
            vals = [st.mem.get(o) for st in sts]
            have = [x for x in vals if x is not None]
            if any(x[1] != have[0][1] for x in have): continue
            if len(have) < len(vals):
                # 한쪽 가지에서만 쓴 칸: 쓰지 않은 가지는 그 자리의 이전 메모리(초기화되지 않은 스택) 그대로다.
                # (예: 0x08027090 의 "가장 이른 칸 번호" 바이트 — 버리면 합류 뒤 읽기가 쓰레기가 된다)
                # 그 가지에 이 자리와 겹치는 다른 모양의 칸이 있으면 값을 알 수 없으므로 전처럼 버린다.
                sz = have[0][1]
                if any(x is None and any(p < o + sz and o < p + q for p, (_, q) in st.mem.items()) for x, st in zip(vals, sts)): continue
                und = ('undef_stack', o)
                for b in range(1, sz): und = binop('or', und, binop('shl', ('undef_stack', o + b), 8 * b))
                vals = [x if x is not None else (und, sz) for x in vals]
            if all(x == vals[0] for x in vals): base.mem[o] = vals[0]; continue
            v = self.newvar(); base.mem[o] = (v, vals[0][1])
            for (i, _), val in zip(ends, vals): assigns[i].append((v[1], val[0]))
        base.flags = None; base.steps = max(st.steps for st in sts)
        base.membacked = any(st.membacked for st in sts)
        return base, assigns

    def run(self, s):
        self.nodes += 1
        if self.nodes > self.max_nodes: raise Unsupported('too many paths')
        while True:
            if s.skip: s.skip = False
            else:
                for (spc, sd, kind, col, tag) in reversed(s.stops):
                    if s.pc == spc and len(s.fns) == sd:
                        i = len(col); col.append((i, s))
                        return (kind, tag, i)
                if self.use_loops and s.pc in self.loop_heads(s.fns[-1]):
                    return self.make_loop(s)
            s.steps += 1
            if s.steps > self.max_steps: raise Unsupported('too many steps at %x' % s.pc)
            pending = []
            pc = s.pc
            mn, ops, size, raw = self.ins(pc)
            nxt = pc + size
            if mn in ('push',):
                regs = re.findall(r'\w+', raw)
                sp = self.reg(s, 'sp'); sp = ('sp', sp[1] - 4 * len(regs))
                for k, rg in enumerate(regs): s.mem[sp[1] + 4 * k] = (self.reg(s, rg), 4)
                s.r['sp'] = sp
            elif mn == 'pop':
                regs = re.findall(r'\w+', raw); sp = self.reg(s, 'sp')
                for k, rg in enumerate(regs):
                    v = s.mem.get(sp[1] + 4 * k, (('undef',), 4))[0]
                    if rg == 'pc':
                        s.r['sp'] = ('sp', sp[1] + 4 * len(regs))
                        return self.ret(s, v)
                    self.setreg(s, rg, v)
                s.r['sp'] = ('sp', sp[1] + 4 * len(regs))
            elif mn == 'bx':
                t = self.reg(s, ops[0])
                return self.ret(s, t)
            elif mn == 'bl':
                t = int(ops[0][1:], 16)
                TRAMP = {0x0824F424: 'r0', 0x0824F428: 'r1', 0x0824F42C: 'r2', 0x0824F430: 'r3',
                         0x0824F434: 'r4', 0x0824F438: 'r5', 0x0824F43C: 'r6'}
                if t in TRAMP:  # bx rN (간접 호출)
                    fr = TRAMP[t]
                    f = self.reg(s, fr)
                    if not isinstance(f, int): return self.native(s, nxt, ('icall', f), 4)
                    t = f & ~1
                if t in self.natives:
                    return self.native(s, nxt, t, self.natives[t])
                if t in self.subs or (self.auto_subs and t not in self.nosub):
                    try:
                        ar = self.sub_arity(t)
                    except Unsupported:
                        self.nosub.add(t); ar = None
                    if ar is not None:
                        sp = self.reg(s, 'sp')
                        vals = [self.reg(s, 'r%d' % k) for k in range(min(ar, 4))] + \
                               [self.load(s, ('sp', sp[1] + 4 * k), 4) for k in range(max(0, ar - 4))]
                        # 지역 변수 주소를 넘기면 native() 가 스택 값을 프레임 메모리에 쓰고 호출 뒤 다시 읽는다
                        return self.native(s, nxt, t, ar)
                if s.depth > 12: raise Unsupported('depth')
                s.r['lr'] = ('retaddr', nxt); s.depth += 1; s.fns.append(t); s.pc = t; continue
            elif mn == 'b':
                s.pc = int(ops[0][1:], 16); continue
            elif mn in CC:
                cc = CC[mn]; t = int(ops[0][1:], 16)
                if s.flags is None: raise Unsupported('flags %x' % pc)
                a, b = s.flags
                if isinstance(a, int) and isinstance(b, int):
                    s.pc = t if eval_cc(cc, a, b) else nxt; continue
                if a == b and cc in ('eq', 'ne', 'geu', 'leu', 'ge', 'le', 'ltu', 'gtu', 'lt', 'gt'):
                    s.pc = t if eval_cc(cc, 0, 0) else nxt; continue
                J = (self.agg_join(s.fns[-1], pc, t, nxt) if self.agg else self.joins(s.fns[-1]).get(pc)) if self.use_joins else None
                if J is None:
                    s1 = s.copy(); s1.pc = t; s2 = s; s2.pc = nxt
                    return ('if', (cc, a, b), self.run(s1), self.run(s2))
                outer = s.stops; ends = []; self.ntag += 1; tag = self.ntag
                stops = outer + ((J, len(s.fns), 'end', ends, tag),)
                s1 = s.copy(); s1.pc = t; s1.stops = stops
                s2 = s.copy(); s2.pc = nxt; s2.stops = stops
                t1 = self.run(s1); t2 = self.run(s2)
                if not ends: return ('if', (cc, a, b), t1, t2)
                merged, assigns = self.merge(ends)
                merged.stops = outer; merged.pc = J
                fix = lambda tr: self.patch(tr, 'end', tag, assigns)
                if self.agg: return ('if2', (cc, a, b), fix(t1), fix(t2), self.run(merged), tag)
                return ('if2', (cc, a, b), fix(t1), fix(t2), self.run(merged))
            elif mn in ('cmp', 'cmn', 'tst'):
                a = self.opval(s, ops[0]); b = self.opval(s, ops[1])
                if mn == 'cmn': b = binop('sub', 0, b)
                if mn == 'tst': s.flags = (binop('and', a, b), 0)
                else: s.flags = (a, b)
            elif mn in ('movs', 'mov'):
                if ops[0] == 'pc':  # 점프 표 또는 복귀
                    t = self.reg(s, ops[1])
                    if t == 'RET' or (isinstance(t, tuple) and t[0] == 'retaddr'): return self.ret(s, t)
                    if not isinstance(t, int): return self.switch(s, t)
                    s.pc = t & ~1; continue
                v = self.opval(s, ops[1]); self.setreg(s, ops[0], v)
                if mn == 'movs': s.flags = (v, 0)
            elif mn in ('adds', 'subs', 'add', 'sub'):
                if len(ops) == 2: a = self.reg(s, ops[0]); b = self.opval(s, ops[1])
                else: a = self.opval(s, ops[1]); b = self.opval(s, ops[2])
                op = 'add' if mn.startswith('add') else 'sub'
                if ops[0] == 'pc': raise Unsupported('add pc')
                v = binop(op, a, b); self.setreg(s, ops[0], v)
                if mn.endswith('s'): s.flags = (v, 0) if op == 'add' else (a, b)
            elif mn == 'rsbs':
                v = binop('sub', 0, self.opval(s, ops[1])); self.setreg(s, ops[0], v); s.flags = (v, 0)
            elif mn in ('lsls', 'lsrs', 'asrs', 'ands', 'orrs', 'eors', 'muls', 'bics', 'mvns', 'negs', 'rors'):
                if mn == 'mvns':
                    v = binop('xor', self.opval(s, ops[1]), M32)
                elif mn == 'negs':
                    v = binop('sub', 0, self.opval(s, ops[1]))
                else:
                    if len(ops) == 3: a = self.opval(s, ops[1]); b = self.opval(s, ops[2])
                    else: a = self.reg(s, ops[0]); b = self.opval(s, ops[1])
                    if mn in ('lsls', 'lsrs', 'asrs') and not isinstance(b, int): b = binop('and', b, 0xFF)
                    if mn == 'bics': b = binop('xor', b, M32)
                    op = {'lsls': 'shl', 'lsrs': 'lsr', 'asrs': 'asr', 'ands': 'and', 'orrs': 'or', 'eors': 'xor',
                          'muls': 'mul', 'bics': 'and'}.get(mn)
                    if op is None: raise Unsupported(mn)
                    if mn == 'lsrs' and isinstance(b, int) and b == 0 and len(ops) == 3 and ops[2].startswith('#'): b = 32
                    v = binop(op, a, b)
                self.setreg(s, ops[0], v); s.flags = (v, 0)
            elif mn in ('ldr', 'ldrh', 'ldrb', 'ldrsh', 'ldrsb'):
                size = {'ldr': 4, 'ldrh': 2, 'ldrb': 1, 'ldrsh': 2, 'ldrsb': 1}[mn]
                addr = self.memaddr(s, ops[1])
                self.setreg(s, ops[0], self.eager(self.load(s, addr, size, mn.startswith('ldrs')), pending))
            elif mn in ('str', 'strh', 'strb'):
                size = {'str': 4, 'strh': 2, 'strb': 1}[mn]
                addr = self.memaddr(s, ops[1])
                self.pre = []
                eff = self.store(s, addr, self.reg(s, ops[0]), size)
                pre, self.pre = self.pre, []
                if eff:
                    s.pc = nxt
                    node = eff + (self.run(s),)
                    for st in reversed(pre): node = st + (node,)
                    return node
            elif mn in ('ldm', 'ldmia', 'stm', 'stmia'):
                base_r = re.match(r'(\w+)', raw).group(1)
                regs = re.findall(r'\br\d+\b', raw.split('{', 1)[1])
                addr = self.reg(s, base_r)
                if mn.startswith('ldm'):
                    for k, rg in enumerate(regs):
                        self.setreg(s, rg, self.eager(self.load(s, binop('add', addr, 4 * k), 4), pending))
                    if base_r not in regs: self.setreg(s, base_r, binop('add', addr, 4 * len(regs)))
                else:
                    vals = [self.reg(s, rg) for rg in regs]
                    self.setreg(s, base_r, binop('add', addr, 4 * len(regs)))
                    effs = []
                    for k, v in enumerate(vals):
                        self.pre = []
                        eff = self.store(s, binop('add', addr, 4 * k), v, 4)
                        effs += self.pre; self.pre = []
                        if eff: effs.append(eff)
                    if effs:
                        s.pc = nxt
                        node = self.run(s)
                        for eff in reversed(effs): node = eff + (node,)
                        return node
            elif mn == 'nop' or (mn == 'mov' and ops == ['r8', 'r8']):
                pass
            else:
                raise Unsupported('%s %s at %x' % (mn, raw, pc))
            s.pc = nxt
            if pending:
                node = self.run(s)
                for v, e in reversed(pending): node = ('bind', v, e, node)
                return node

    def quick_arity(self, fn):
        """함수 앞부분에서 쓰기 전에 읽는 r0~r3 개수 (재귀 호출용 근사)."""
        a = fn & ~1; written = set(); read = set()
        for _ in range(24):
            mn, ops, size, raw = self.ins(a)
            if mn in ('bl', 'b', 'bx') or mn in CC: break
            regs = re.findall(r'\br[0-3]\b', raw)
            if mn.startswith('str') or mn in ('cmp', 'tst', 'push'):
                srcs, dsts = regs, []
            else:
                srcs, dsts = regs[1:], regs[:1]
                if len(ops) == 2 and not ops[1].startswith('#') and not ops[1].startswith('['): srcs = regs
            for r in srcs:
                if r not in written: read.add(int(r[1]))
            for r in dsts: written.add(r)
            a += size
        return max(read) + 1 if read else 0

    def sub_arity(self, t):
        if t in self.inprog: return self.quick_arity(t)       # 재귀 호출
        if t not in self.subtrees:
            self.inprog.add(t)
            saved = self.nodes
            try:
                self.subtrees[t] = self.lift(t)
            finally:
                self.inprog.discard(t)
                self.nodes = saved
                if self.subtrees.get(t) is None: self.subtrees.pop(t, None)
        used = set()
        def we(e):
            if isinstance(e, tuple):
                if e[0] == 'arg': used.add(e[1])
                elif e[0] == 'sarg': used.add(4 + e[1])
                else:
                    for x in e[1:]: we(x)
        def wt(n):
            if n is None: return
            k = n[0]
            if k == 'ret': we(n[1])
            elif k == 'if': we(n[1][1]); we(n[1][2]); wt(n[2]); wt(n[3])
            elif k == 'if2': we(n[1][1]); we(n[1][2]); wt(n[2]); wt(n[3]); wt(n[4])
            elif k == 'end':
                for _, e in n[1]: we(e)
            elif k in ('cont', 'brk'):
                for _, e in n[2]: we(e)
            elif k == 'loop':
                for _, e in n[2]: we(e)
                wt(n[3]); wt(n[4])
            elif k == 'let':
                for a in n[2][2]: we(a)
                wt(n[3])
            elif k == 'store': we(n[2]); we(n[3]); wt(n[4])
            elif k == 'bind': we(n[2]); wt(n[3])
        wt(self.subtrees[t])
        return max(used) + 1 if used else 0

    def patch(self, t, kind, tag, assigns):
        """정지점 잎 (kind, tag, i) 를 대입 목록이 붙은 잎으로 바꾼다. end → ('end', 대입), cont/brk → (kind, tag, 대입)."""
        k = t[0]
        P = lambda x: self.patch(x, kind, tag, assigns)
        if k == kind and len(t) == 3 and t[1] == tag and isinstance(t[2], int):
            a = tuple(assigns[t[2]])
            if kind == 'end': return ('end', a, tag) if self.agg else ('end', a)
            return (kind, tag, a)
        if k == 'if': return ('if', t[1], P(t[2]), P(t[3]))
        if k == 'if2': return ('if2', t[1], P(t[2]), P(t[3]), P(t[4])) + tuple(t[5:])
        if k == 'let': return ('let', t[1], t[2], P(t[3]))
        if k == 'store': return ('store', t[1], t[2], t[3], P(t[4]))
        if k == 'bind': return ('bind', t[1], t[2], P(t[3]))
        if k == 'loop': return ('loop', t[1], t[2], P(t[3]), P(t[4]))
        return t

    def make_loop(self, s):
        """반복문: 머리 H 에서 모든 레지스터·스택 칸을 반복 변수로 바꾸고, H 로 돌아오면 cont, 출구 E 에 닿으면 brk."""
        H = s.pc; d = len(s.fns)
        self.joins(s.fns[-1]); E = self.exit_cache.get(s.fns[-1], {}).get(H)
        isret = lambda v: v == 'RET' or (isinstance(v, tuple) and v[0] == 'retaddr')
        # 지역 변수 주소는 반복 중에도 그대로인 경우가 대부분 → 바뀌는 것이 확인될 때만 반복 변수로
        regs = [k for k, v in s.r.items() if k not in ('sp', 'pc', 'lr') and not isret(v) and not has_sp(v)]
        offs = [o for o, (v, sz) in s.mem.items() if not isret(v) and not has_sp(v)]
        for attempt in range(3):
            self.ntag += 1; tag = self.ntag
            lv = {}; init = []; bs = s.copy(); bs.flags = None
            for k in regs:
                v = self.newvar(); lv[('r', k)] = v; init.append((v[1], s.r.get(k, ('undef', k)))); bs.r[k] = v
            for o in offs:
                sz = s.mem[o][1] if o in s.mem else 4
                v = self.newvar(); lv[('m', o)] = v; init.append((v[1], s.mem[o][0] if o in s.mem else ('undef_stack', o))); bs.mem[o] = (v, sz)
            conts, brks = [], []
            bs.stops = s.stops + ((H, d, 'cont', conts, tag),) + (((E, d, 'brk', brks, tag),) if E is not None else ())
            bs.skip = True
            body = self.run(bs)
            # 본문에서 새로 생긴 칸이 다음 반복으로 넘어가면 다시 (반복 변수로 포함)
            new_regs = {k for _, st in conts for k, v in st.r.items() if k not in regs and k not in ('sp', 'pc', 'lr') and not isret(v)
                        and v != s.r.get(k)}
            new_offs = {o for _, st in conts for o, (v, sz) in st.mem.items() if o not in offs and not isret(v) and o >= min(offs + [0])
                        and (o not in s.mem or v != s.mem[o][0])}
            if not new_regs and not new_offs: break
            regs += sorted(new_regs); offs += sorted(new_offs)
        cassign = {}
        for i, st in conts:
            a = []
            for (kind, key), v in lv.items():
                val = st.r.get(key, ('undef', key)) if kind == 'r' else (st.mem[key][0] if key in st.mem else ('undef_stack', key))
                if val != v: a.append((v[1], val))
            cassign[i] = a
        body = self.patch(body, 'cont', tag, cassign)
        if brks:
            ex, bassign = self.merge(brks)
            ex.stops = s.stops; ex.pc = E
            body = self.patch(body, 'brk', tag, bassign)
            after = self.run(ex)
        else:
            after = ('ret', ('undef', 'noexit'))
        return ('loop', tag, tuple(init), body, after)

    def ret(self, s, target):
        if target == 'RET':
            return ('ret', s.r.get('r0'))
        if isinstance(target, tuple) and target[0] == 'retaddr':
            s.depth -= 1; s.fns.pop(); s.pc = target[1]
            return self.run(s)
        raise Unsupported('ret to %r' % (target,))

    def spill_stack(self, s):
        """기호로만 들고 있던 스택 칸을 프레임 메모리에 쓰는 저장 목록을 만들고, 이후 스택 읽기는 메모리에서 하게 한다."""
        isret = lambda v: v == 'RET' or (isinstance(v, tuple) and v[0] == 'retaddr')
        def undef_in(e):
            if not isinstance(e, tuple): return e is None or isinstance(e, str)
            seen, st = set(), [e]
            while st:
                x = st.pop()
                if not isinstance(x, tuple) or id(x) in seen: continue
                seen.add(id(x))
                if x and x[0] in ('undef', 'undef_ret', 'undef_switch'): return True
                st.extend(x[1:])
            return False
        spill = []
        for off, (val, size) in sorted(s.mem.items()):
            if isret(val): continue          # 복귀 주소는 호출한 함수가 바꾸지 않는다
            if undef_in(val): continue       # 들어올 때 값을 모르는 저장 레지스터 (쓰이지 않음)
            if isinstance(val, tuple) and val[0] == 'load' and val[2] == ('sp', off): continue   # 이미 메모리에 있음
            spill.append(('store', size, ('sp', off), val))
        for off, (val, size) in list(s.mem.items()):
            if isret(val): continue
            s.mem[off] = (('load', size, ('sp', off)), size)
        s.membacked = True
        return spill

    def native(self, s, nxt, t, nargs):
        args = tuple(self.reg(s, 'r%d' % k) for k in range(min(nargs, 4)))
        sp = self.reg(s, 'sp')
        if nargs > 4:
            args += tuple(self.load(s, ('sp', sp[1] + 4 * k), 4) for k in range(nargs - 4))
        spill = []
        if any(has_sp(a) for a in args):
            # 지역 변수 주소를 넘기는 호출: 스택 값을 실제 프레임 메모리에 쓰고, 호출 뒤 다시 읽는다
            spill = self.spill_stack(s)
        for k in range(4): s.r['r%d' % k] = ('undef_ret',)
        v = self.newvar()
        s.r['r0'] = v; s.pc = nxt; s.flags = None
        node = ('let', v, ('call', t, args), self.run(s))
        for st in reversed(spill): node = st + (node,)
        return node

    def switch(self, s, t):
        # t = load(4, table + idx*4) : 표 범위를 모르면 지원 안 함
        # t = m32[table + (idx << 2)] : 표 항목이 코드 주소인 동안 열거해 idx == k 분기로 바꾼다
        if not (isinstance(t, tuple) and t[0] == 'load' and t[1] == 4 and t[2][0] == 'add'): raise Unsupported('switch %r' % (t,))
        a, b = t[2][1], t[2][2]
        base, sh = (b, a) if isinstance(b, int) else (a, b)
        if not (isinstance(base, int) and isinstance(sh, tuple) and sh[0] == 'shl' and sh[2] == 2): raise Unsupported('switch form')
        idx = sh[1]; targets = []
        for k in range(256):
            v = self.u(base + 4 * k, 4)
            if not (abs((v & ~1) - s.pc) < 0x4000): break
            targets.append(v & ~1)
        def chain(k):
            if k == len(targets): return ('ret', ('undef_switch',))
            s1 = s.copy(); s1.pc = targets[k]
            return ('if', ('eq', idx, k), self.run(s1), chain(k + 1))
        return chain(0)


def fmt(e):
    if isinstance(e, int): return hex(e) if e > 9 else str(e)
    if e is None: return 'None'
    k = e[0]
    if k == 'v': return 'v%d' % e[1]
    if k == 'arg': return 'a%d' % e[1]
    if k == 'sarg': return 's%d' % e[1]
    if k == 'load': return 'm%d[%s]' % (e[1] * 8, fmt(e[2]))
    if k == 'ram': return 'RAM%d[%x]' % (e[1] * 8, e[2])
    if k == 'sext': return 'sx%d(%s)' % (e[2], fmt(e[1]))
    if k == 'sp': return 'SP%+d' % e[1]
    if len(e) == 3 and isinstance(e[0], str):
        sym = {'add': '+', 'sub': '-', 'mul': '*', 'and': '&', 'or': '|', 'xor': '^', 'shl': '<<', 'lsr': '>>', 'asr': '>>>',
               'udiv': '/', 'umod': '%', 'sdiv': '/s', 'smod': '%s'}.get(k)
        if sym: return '(%s %s %s)' % (fmt(e[1]), sym, fmt(e[2]))
    return repr(e)


def show(t, ind=0, names={}):
    p = '  ' * ind
    k = t[0]
    if k == 'ret': return p + 'return ' + fmt(t[1])
    if k == 'if':
        cc, a, b = t[1]
        return p + 'if %s %s %s:\n%s\n%selse:\n%s' % (fmt(a), cc, fmt(b), show(t[2], ind + 1, names), p, show(t[3], ind + 1, names))
    if k == 'if2':
        cc, a, b = t[1]
        return p + 'if %s %s %s {\n%s\n%s} else {\n%s\n%s}\n%s' % (fmt(a), cc, fmt(b), show(t[2], ind + 1, names), p, show(t[3], ind + 1, names), p, show(t[4], ind, names))
    if k == 'end':
        return p + '; '.join('v%d = %s' % (v, fmt(e)) for v, e in t[1]) if t[1] else p + 'pass'
    if k in ('cont', 'brk'):
        return p + '; '.join('v%d = %s' % (v, fmt(e)) for v, e in t[2]) + ('; ' if t[2] else '') + ('continue' if k == 'cont' else 'break')
    if k == 'loop':
        return p + '; '.join('v%d = %s' % (v, fmt(e)) for v, e in t[2]) + '\n' + p + 'loop {\n' + show(t[3], ind + 1, names) + '\n' + p + '}\n' + show(t[4], ind, names)
    if k == 'let':
        c = t[2]; fn = c[1]
        fname = names.get(fn, '%x' % fn if isinstance(fn, int) else fmt(fn[1]) if isinstance(fn, tuple) else str(fn))
        return p + '%s = %s(%s)\n%s' % (fmt(t[1]), fname, ', '.join(fmt(a) for a in c[2]), show(t[3], ind, names))
    if k == 'store':
        return p + 'm%d[%s] = %s\n%s' % (t[1] * 8, fmt(t[2]), fmt(t[3]), show(t[4], ind, names))
    if k == 'bind':
        return p + 'v%d := %s\n%s' % (t[1], fmt(t[2]), show(t[3], ind, names))
    return p + repr(t)
