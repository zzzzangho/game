#!/usr/bin/env python3
"""검증 전용(로컬): 관계 슬롯표 작성(0x08110B90)·관계 판정(0x08115778) 손 이식 = 원작 실행 결과인지 RAM 덤프로 확인.
  python3 verify_slots.py <천년가족.gba> <RAM 덤프...>
"""
import os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from romcall import RomCpu  # noqa: E402
import slots_ref as S  # noqa: E402


def mem_of(buf):
    mir = lambda a: 0x02000000 | (a & 0x3FFFF)
    r16 = lambda a: struct.unpack_from('<H', buf, mir(a) - 0x02000000)[0]
    r8 = lambda a: buf[mir(a) - 0x02000000] if 0x02000000 <= a < 0x03000000 else 0
    def w16(a, v): struct.pack_into('<H', buf, mir(a) - 0x02000000, v & 0xFFFF)
    return S.Mem(r16, r8, w16)


def main():
    rom = open(sys.argv[1], 'rb').read(); ok = bad = 0
    for rp in sys.argv[2:]:
        ram = open(rp, 'rb').read(); cpu = RomCpu(rom, ram)
        for pid in list(range(40)) + [0xFFFF]:
            cpu.set_ram(ram); cpu.call(0x08110B90, pid, 0)
            want = [cpu.r16(S.SLOT + 2 * k) for k in range(28)]
            buf = bytearray(ram); m = mem_of(buf); S.build(m, pid)
            got = [m.r16(S.SLOT + 2 * k) for k in range(28)]
            ok, bad = (ok + 1, bad) if got == want else (ok, bad + 1)
        for pid in range(4):
            cpu.set_ram(ram); cpu.call(0x08110B90, pid, 0); base = cpu.ram()
            for slot in range(23):
                for k in list(range(23)) + [0x1B, 0x1C]:
                    if k == 0x1C and slot not in (0, 15): continue
                    cpu.set_ram(base)
                    try: want = cpu.call(0x08115778, slot, k)
                    except Exception: continue
                    got = S.rel(mem_of(bytearray(base)), slot, k)
                    ok, bad = (ok + 1, bad) if got == want else (ok, bad + 1)
    print('같음', ok, '다름', bad)
    return 0 if bad == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
