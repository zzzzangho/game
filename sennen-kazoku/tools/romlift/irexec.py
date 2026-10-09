"""결정 트리 IR 실행기 (C# 이식 전 기준 구현)."""
import struct
from lift import s32, M32, binop, eval_cc, B


class Ctx:
    def __init__(self, rom, read, natives):
        self.rom = rom; self.read = read; self.natives = natives

    def rd(self, size, a):
        a &= M32
        if B <= a < B + len(self.rom):
            return int.from_bytes(self.rom[a - B:a - B + size], 'little')
        return self.read(a, size)


def ev(e, env, ctx):
    if isinstance(e, int): return e & M32
    k = e[0]
    if k == 'v':
        x = env[e[1]]
        if x is None: raise ValueError('정의되지 않은 합류 값')
        return x
    if k == 'ram': return ctx.rd(e[1], e[2])
    if k == 'load': return ctx.rd(e[1], ev(e[2], env, ctx))
    if k == 'sext':
        v = ev(e[1], env, ctx) & ((1 << e[2]) - 1)
        if v & (1 << (e[2] - 1)): v -= 1 << e[2]
        return v & M32
    if k == 'arg': return env['args'][e[1]] if e[1] < len(env['args']) else 0
    if k == 'sarg': return env['args'][4 + e[1]] if 4 + e[1] < len(env['args']) else 0
    if k == 'sp': return (env['frame'] + e[1]) & M32
    if k in ('undef', 'undef_stack', 'undef_ret', 'undef_switch'):
        raise ValueError('unbound %r' % (e,))
    return binop(k, ev(e[1], env, ctx), ev(e[2], env, ctx))


FRAME_TOP, FRAME_SIZE = 0x03006000, 0x400


def run(t, ctx, env=None, args=()):
    depth = getattr(ctx, 'depth', 0); ctx.depth = depth + 1
    try:
        return _run(t, ctx, env, args, FRAME_TOP - FRAME_SIZE * depth)
    finally:
        ctx.depth = depth


def _assign(pairs, env, ctx):
    vals = []
    for v, e in pairs:
        try: vals.append((v, ev(e, env, ctx)))
        except (ValueError, IndexError, KeyError): vals.append((v, None))   # 이후에 쓰지 않는 값
    for v, x in vals: env[v] = x


def _run(t, ctx, env, args, frame):
    env = {'args': list(args)} if env is None else env
    env['frame'] = frame
    conts = []
    steps = 0
    while True:
        steps += 1
        if steps > 5_000_000: raise ValueError('too many steps')
        k = t[0]
        if k == 'end':
            _assign(t[1], env, ctx)
            while conts and conts[-1][0] != 'k': conts.pop()
            t = conts.pop()[1]; continue
        if k == 'if2':
            cc, a, b = t[1]
            conts.append(('k', t[4]))
            t = t[2] if eval_cc(cc, ev(a, env, ctx), ev(b, env, ctx)) else t[3]
            continue
        if k == 'loop':
            _assign(t[2], env, ctx)
            conts.append(('L', t[1], t[3], t[4]))
            t = t[3]; continue
        if k in ('cont', 'brk'):
            _assign(t[2], env, ctx)
            while not (conts[-1][0] == 'L' and conts[-1][1] == t[1]): conts.pop()
            if k == 'cont': t = conts[-1][2]
            else: t = conts.pop()[3]
            continue
        if k == 'ret': return ev(t[1], env, ctx)
        if k == 'fail': raise ValueError('lifted path not supported: ' + t[1])
        if k == 'if':
            cc, a, b = t[1]
            t = t[2] if eval_cc(cc, ev(a, env, ctx), ev(b, env, ctx)) else t[3]
        elif k == 'let':
            c = t[2]
            args = []
            for a in c[2]:
                try: args.append(ev(a, env, ctx))
                except ValueError: args.append(0)   # 재귀 호출의 쓰지 않는 인자
            sub = getattr(ctx, 'subs', {}).get(c[1])
            env[t[1][1]] = (run(sub, ctx, None, args) if sub is not None else ctx.natives(c[1], args)) & M32
            t = t[3]
        elif k == 'store':
            ctx.write(ev(t[2], env, ctx), t[1], ev(t[3], env, ctx))
            t = t[4]
        else:
            raise ValueError(k)
