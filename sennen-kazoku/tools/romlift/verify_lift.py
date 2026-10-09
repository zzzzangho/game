#!/usr/bin/env python3
"""검증 전용(로컬): 관심사 판정 함수를 lift.py 로 바꾼 트리 = 원작 실행 결과인지 RAM 덤프로 확인.
  python3 verify_lift.py <천년가족.gba> <RAM 덤프...>
"""
import os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.setrecursionlimit(20000)
from lift import Lifter, Unsupported, B  # noqa: E402
from romcall import RomCpu  # noqa: E402
import irexec  # noqa: E402
from export_orig import NATIVES, STATE_TABLE, table_len, Rom  # noqa: E402


def main():
    rb = open(sys.argv[1], 'rb').read(); rom = Rom(rb)
    L = Lifter(rb, NATIVES, max_steps=50000, max_nodes=50000); L.auto_subs = True
    preds = set()
    for t in range(5):
        tb = rom.u32(STATE_TABLE + 4 * t)
        for i in range(table_len(rom, t)): preds.add(rom.u32(rom.u32(tb + 4 * i) + 8))
    trees = {}
    for f in sorted(preds):
        try: trees[f] = L.lift(f)
        except Unsupported as e: print('변환 실패', hex(f), e)
    same = diff = 0
    for rp in sys.argv[2:]:
        ram = open(rp, 'rb').read(); cpu = RomCpu(rb, ram)
        for n in range(8):
            pid = struct.unpack_from('<H', ram, 0x0202C6C4 + 976 * n + 0x3C - 0x02000000)[0]
            if pid == 0xFFFF: continue
            cpu.set_ram(ram); cpu.call(0x08110B90, pid, 0); S = cpu.ram()
            for f, tr in trees.items():
                cpu.set_ram(S); a = cpu.call(f)
                cpu.set_ram(S)
                ctx = irexec.Ctx(rb, lambda ad, sz: int.from_bytes(bytes(cpu.mu.mem_read(ad, sz)), 'little'),
                                 lambda fn, args: cpu.call(fn, *args))
                ctx.subs = L.subtrees
                ctx.write = lambda ad, sz, v: cpu.mu.mem_write(ad, (v & ((1 << (8 * sz)) - 1)).to_bytes(sz, 'little'))
                b = irexec.run(tr, ctx)
                same, diff = (same + 1, diff) if a == b else (same, diff + 1)
                if a != b and diff <= 8: print('다름', hex(f), '인물', n, '원작', a, '변환', b)
    print('트리 %d개, 같음 %d, 다름 %d' % (len(trees), same, diff))
    return 0 if diff == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
