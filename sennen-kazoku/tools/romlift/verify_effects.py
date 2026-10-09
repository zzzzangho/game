#!/usr/bin/env python3
"""검증 전용(로컬): 사건 결과의 효과 함수(결과 기록 +0x1C)를 lift.py 트리로 바꾼 것 = 원작 실행인지,
RAM 덤프 위에서 EWRAM 변화(바이트 단위)를 비교한다.

  python3 verify_effects.py <천년가족.gba> <최대 개수> <RAM 덤프...>
"""
import os, struct, sys, signal, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.setrecursionlimit(20000)
from lift import Lifter, Unsupported, B  # noqa: E402
from romcall import RomCpu  # noqa: E402
import irexec  # noqa: E402

NATIVES = {0x08000614: 0, 0x0824F640: 2, 0x0824F5C8: 2, 0x0824F460: 2, 0x0824F4F8: 2,
           0x08115778: 2, 0x08028524: 3, 0x08110B90: 2, 0x08110608: 1}


class TO(Exception): pass


def _al(*a): raise TO()


def effect_functions(rom):
    u32 = lambda a: struct.unpack_from('<I', rom, a - B)[0]
    ok = lambda p: B <= p < B + len(rom)
    out = set()
    for t in range(5):
        tb = u32(0x085BD4A0 + 4 * t); i = 0
        while i < 4096:
            e = u32(tb + 4 * i)
            if not ok(e) or not (ok(u32(e + 8)) and u32(e + 8) & 1): break
            for off in (0x1C, 0x20):
                ev = u32(e + off)
                if not ok(ev): continue
                n = rom[ev + 3 - B]; lst = u32(ev + 0x24)
                for k in range(n + 1):
                    v = u32(lst + 4 * k)
                    if not ok(v): continue
                    d = u32(v + 4)
                    if ok(d) and ok(d + 0x20) and ok(u32(d + 0x1C)) and u32(d + 0x1C) & 1: out.add(u32(d + 0x1C))
            i += 1
    return sorted(out)


def main():
    rom = open(sys.argv[1], 'rb').read(); limit = int(sys.argv[2])
    fns = effect_functions(rom)
    random.Random(1).shuffle(fns); fns = fns[:limit]
    L = Lifter(rom, NATIVES, max_steps=50000, max_nodes=50000); L.auto_subs = True
    signal.signal(signal.SIGALRM, _al)
    trees = {}
    for f in fns:
        signal.alarm(30)
        try: trees[f] = L.lift(f)
        except (Unsupported, TO, RecursionError): pass
        finally: signal.alarm(0)
    print('효과 함수 %d개 중 변환 %d' % (len(fns), len(trees)))
    same = diff = err = 0; icalls = set()
    for rp in sys.argv[3:]:
        ram = open(rp, 'rb').read(); cpu = RomCpu(rom, ram)
        pid = struct.unpack_from('<H', ram, 0x0202C6C4 + 0x3C - 0x02000000)[0]
        cpu.set_ram(ram); cpu.call(0x08110B90, pid, 0); S = cpu.ram()
        for f, tr in trees.items():
            cpu.set_ram(S)
            try:
                cpu.call(f, 0x03007D00, 0, 0, f)
            except Exception:
                err += 1; continue
            want = cpu.ram()[:0x40000]
            cpu.set_ram(S)
            ctx = irexec.Ctx(rom, lambda ad, sz: int.from_bytes(bytes(cpu.mu.mem_read(ad, sz)), 'little'),
                             lambda fn, args: cpu.call(fn, *args))
            ctx.subs = L.subtrees; ctx.icalls = icalls
            ctx.write = lambda ad, sz, v: cpu.mu.mem_write(ad, (v & ((1 << (8 * sz)) - 1)).to_bytes(sz, 'little'))
            try:
                irexec.run(tr, ctx, None, [0x03007D00, 0, 0, f])
            except Exception as ex:
                err += 1; print('실행 오류', hex(f), str(ex)[:80]); continue
            got = cpu.ram()[:0x40000]
            if got == want: same += 1
            else:
                diff += 1
                d = [hex(0x02000000 + i) for i in range(0x40000) if got[i] != want[i]]
                print('다름', hex(f), len(d), d[:6])
    print('같음 %d, 다름 %d, 오류 %d' % (same, diff, err))
    print('함수 포인터 호출 대상', sorted(hex(x) for x in icalls)[:40])
    return 0 if diff == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
