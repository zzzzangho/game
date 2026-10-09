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
    if e is None or isinstance(e, str): raise ValueError('정의되지 않은 값')
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
    if k == 'undef_stack': return 0          # 원작이 초기화하지 않은 스택 바이트 (보통 상위 바이트, 결과에 안 쓰임)
    if k in ('undef', 'undef_ret', 'undef_switch'):
        raise ValueError('unbound %r' % (e,))
    if len(e) != 3 or k not in ('add', 'sub', 'mul', 'and', 'or', 'xor', 'shl', 'lsr', 'asr', 'udiv', 'umod', 'sdiv', 'smod'):
        raise ValueError('정의되지 않은 값 %r' % (k,))
    return binop(k, ev(e[1], env, ctx), ev(e[2], env, ctx))


FRAME_TOP, FRAME_SIZE = 0x0F100000, 0x1000   # 변환한 함수의 지역 변수 프레임 (GBA 에 없는 빈 주소 영역)


def run(t, ctx, env=None, args=()):
    depth = getattr(ctx, 'depth', 0); ctx.depth = depth + 1
    try:
        return _run(t, ctx, env, args, FRAME_TOP - 0x100 - FRAME_SIZE * depth)   # 위쪽 0x100 은 받은 스택 인자 자리
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
            tag = t[2] if len(t) > 2 else None
            while conts and not (conts[-1][0] == 'k' and (tag is None or conts[-1][2] == tag)): conts.pop()
            t = conts.pop()[1]; continue
        if k == 'if2':
            cc, a, b = t[1]
            conts.append(('k', t[4], t[5] if len(t) > 5 else None))
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
        if k == 'ret':
            try: return ev(t[1], env, ctx)
            except (ValueError, IndexError, KeyError): return 0   # 돌려주는 값이 없는 함수
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
            fn = c[1]
            if isinstance(fn, tuple) and fn[0] == 'icall':      # 함수 포인터 호출
                fn = ev(fn[1], env, ctx) & ~1
                getattr(ctx, 'icalls', set()).add(fn)
                lift_sub = getattr(ctx, 'lift_sub', None)
                if lift_sub and fn not in ctx.subs: lift_sub(fn)
            sub = getattr(ctx, 'subs', {}).get(fn)
            env[t[1][1]] = (run(sub, ctx, None, args) if sub is not None else ctx.natives(fn, args)) & M32
            t = t[3]
        elif k == 'store':
            try: val = ev(t[3], env, ctx)
            except ValueError: val = 0      # 호출한 쪽이 남긴 레지스터 값 (원작에서도 의미 없는 값)
            ctx.write(ev(t[2], env, ctx), t[1], val)
            t = t[4]
        elif k == 'bind':
            env[t[1]] = ev(t[2], env, ctx)
            t = t[3]
        else:
            raise ValueError(k)
