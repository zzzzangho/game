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
    __slots__ = ('r', 'mem', 'flags', 'pc', 'depth', 'steps')

    def copy(self):
        s = State(); s.r = dict(self.r); s.mem = dict(self.mem); s.flags = self.flags; s.pc = self.pc
        s.depth = self.depth; s.steps = self.steps; return s


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
        for k in range(4):  # 스택 인자
            s.mem[k * 4] = (('sarg', k), 4)
        self.nodes = 0
        return self.run(s)

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
        if is_sp(addr):
            off = addr[1]
            if off in s.mem:
                v, sz = s.mem[off]
                if sz == size or size == 4 and sz == 4: val = v
                elif size < sz or isinstance(v, int): val = binop('and', v, (1 << (8 * size)) - 1)
                else: val = binop('and', v, (1 << (8 * size)) - 1)
            else:
                # 부분 겹침
                hit = [(o, v, sz) for o, (v, sz) in s.mem.items() if o < off < o + sz]
                if hit:
                    o, v, sz = hit[0]
                    val = binop('and', binop('lsr', v, 8 * (off - o)), (1 << (8 * size)) - 1)
                else:
                    val = ('undef_stack', off)
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
            s.mem[off] = (val, size); return None
        return ('store', size, addr, val)

    def memaddr(self, s, o):
        m = re.match(r'\[(\w+)(?:,\s*(#?-?\w+))?\]', o)
        base = self.reg(s, m.group(1)) if m.group(1) != 'pc' else (s.pc + 4) & ~3
        if m.group(2) is None: return base
        off = self.opval(s, m.group(2))
        return binop('add', base, off)

    def newvar(self, bits=0):
        self.nvar += 1; return ('v', self.nvar, bits)

    def run(self, s):
        self.nodes += 1
        if self.nodes > self.max_nodes: raise Unsupported('too many paths')
        while True:
            s.steps += 1
            if s.steps > self.max_steps: raise Unsupported('too many steps at %x' % s.pc)
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
                if t == 0x0824F434 or t in (0x0824F438, 0x0824F43C):  # bx r4/r5/r6 (간접 호출)
                    fr = {0x0824F434: 'r4', 0x0824F438: 'r5', 0x0824F43C: 'r6'}[t]
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
                        def has_sp(e):
                            return isinstance(e, tuple) and (e[0] == 'sp' or any(has_sp(x) for x in e[1:]))
                        if not any(has_sp(v) for v in vals):
                            return self.native(s, nxt, t, ar)
                if s.depth > 12: raise Unsupported('depth')
                s.r['lr'] = ('retaddr', nxt); s.depth += 1; s.pc = t; continue
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
                s1 = s.copy(); s1.pc = t; s2 = s; s2.pc = nxt
                return ('if', (cc, a, b), self.run(s1), self.run(s2))
            elif mn in ('cmp', 'cmn', 'tst'):
                a = self.opval(s, ops[0]); b = self.opval(s, ops[1])
                if mn == 'cmn': b = binop('sub', 0, b)
                if mn == 'tst': s.flags = (binop('and', a, b), 0)
                else: s.flags = (a, b)
            elif mn in ('movs', 'mov'):
                if ops[0] == 'pc':  # 점프 표
                    t = self.reg(s, ops[1])
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
                self.setreg(s, ops[0], self.load(s, addr, size, mn.startswith('ldrs')))
            elif mn in ('str', 'strh', 'strb'):
                size = {'str': 4, 'strh': 2, 'strb': 1}[mn]
                addr = self.memaddr(s, ops[1])
                eff = self.store(s, addr, self.reg(s, ops[0]), size)
                if eff:
                    s.pc = nxt
                    return eff + (self.run(s),)
            elif mn in ('ldm', 'ldmia', 'stm', 'stmia'):
                return ('fail', mn + ' at %x' % pc)
            elif mn == 'nop' or (mn == 'mov' and ops == ['r8', 'r8']):
                pass
            else:
                raise Unsupported('%s %s at %x' % (mn, raw, pc))
            s.pc = nxt

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
            elif k == 'let':
                for a in n[2][2]: we(a)
                wt(n[3])
            elif k == 'store': we(n[2]); we(n[3]); wt(n[4])
        wt(self.subtrees[t])
        return max(used) + 1 if used else 0

    def ret(self, s, target):
        if target == 'RET':
            return ('ret', s.r.get('r0'))
        if isinstance(target, tuple) and target[0] == 'retaddr':
            s.depth -= 1; s.pc = target[1]
            return self.run(s)
        raise Unsupported('ret to %r' % (target,))

    def native(self, s, nxt, t, nargs):
        args = tuple(self.reg(s, 'r%d' % k) for k in range(min(nargs, 4)))
        sp = self.reg(s, 'sp')
        if nargs > 4:
            args += tuple(self.load(s, ('sp', sp[1] + 4 * k), 4) for k in range(nargs - 4))
        for k in range(4): s.r['r%d' % k] = ('undef_ret',)
        v = self.newvar()
        s.r['r0'] = v; s.pc = nxt; s.flags = None
        return ('let', v, ('call', t, args), self.run(s))

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
    if k == 'let':
        c = t[2]; fn = c[1]
        fname = names.get(fn, '%x' % fn if isinstance(fn, int) else fmt(fn[1]) if isinstance(fn, tuple) else str(fn))
        return p + '%s = %s(%s)\n%s' % (fmt(t[1]), fname, ', '.join(fmt(a) for a in c[2]), show(t[3], ind, names))
    if k == 'store':
        return p + 'm%d[%s] = %s\n%s' % (t[1] * 8, fmt(t[2]), fmt(t[3]), show(t[4], ind, names))
    return p + repr(t)
