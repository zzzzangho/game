#!/usr/bin/env python3
"""검증 전용(로컬): C# 장기 진행에서 기록한 맨 바깥 원작 함수 호출을 원작 ROM 으로 다시 실행해 메모리 결과를 비교한다.

C# 쪽 기록은 tests/Core.Tests 의 장기 진행 시험에서 SK_REC_DIR / SK_REC_FROM / SK_REC_DAYS 로 만든다.
기록 하나 = 함수 주소, 날, 인자, C# 이 돌려준 값, 호출 전 메모리, 호출 뒤 메모리.
호출 전 메모리를 unicorn(romcall.py) 에 넣고 같은 함수를 원작 코드로 실행한 뒤, C# 호출 뒤 메모리와 바이트 단위로 비교한다.
화면·소리 함수(export_orig.UI_NOOP 등, 앱에서 아무 일도 하지 않는 함수)는 원작 쪽에서도 바로 돌아오게 고친 복사본으로 실행한다.
난수(0x02000000)도 기록된 상태 그대로 이어지므로 결과가 같아야 한다. 원작 힙(0x0202BFB0 머리)은 C# 이 따로 잡으므로 차이가 나도 따로 표시한다.

  python3 replay_check.py ROM.gba 기록폴더 [최대개수]
"""
import os, struct, sys, glob
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from romcall import RomCpu  # noqa: E402
from unicorn import UC_HOOK_CODE  # noqa: E402
from unicorn.arm_const import UC_ARM_REG_PC, UC_ARM_REG_R0, UC_ARM_REG_LR  # noqa: E402
import export_orig as E  # noqa: E402

EW, IW, FR, IO = 0x40000, 0x8000, 0x40000, 0x400
HEAP0 = 0x0F001000
NOOP = set(E.UI_NOOP) | {0x08248EE0, 0x082494CC, 0x080955C8, 0x080959DC}


def load(path):
    b = open(path, 'rb').read()
    magic, fn, day, argc = struct.unpack_from('<4I', b, 0)
    assert magic == 0x31434552
    args = list(struct.unpack_from('<%dI' % argc, b, 16)); o = 16 + 4 * argc
    ret = struct.unpack_from('<I', b, o)[0]; o += 4
    n = EW + IW + FR + IO
    return fn, day, args, ret, b[o:o + n], b[o + n:o + 2 * n]


def ranges(addrs):
    out = []
    for a in addrs:
        if out and a <= out[-1][1] + 4: out[-1][1] = a
        else: out.append([a, a])
    return out


_orig_unmapped = RomCpu._unmapped


def _unmapped(self, mu, acc, addr, size, val, ud):
    ok = _orig_unmapped(self, mu, acc, addr, size, val, ud)
    if not ok: print('   매핑 안 된 주소 %08X (pc %08X)' % (addr, mu.reg_read(UC_ARM_REG_PC)))
    return ok


RomCpu._unmapped = _unmapped


def main():
    rom = bytearray(open(sys.argv[1], 'rb').read())
    for f in NOOP: rom[f - 0x08000000:f - 0x08000000 + 4] = b'\x00\x20\x70\x47'   # movs r0,#0; bx lr
    files = sorted(glob.glob(os.path.join(sys.argv[2], 'rec_*.bin')))
    if len(sys.argv) > 3: files = files[:int(sys.argv[3])]
    same = diff = 0
    for p in files:
        fn, day, args, ret, pre, post = load(p)
        cpu = RomCpu(bytes(rom), pre[:EW + IW])
        heap = [HEAP0]

        def alloc(mu, addr, size, ud):
            # 앱과 같은 할당 규칙(OrigVm malloc: 작업 영역에서 차례로, 맞춤 계산과 같은 크기)으로 원작 할당·해제를 대신한다
            if addr == 0x08006A58:
                n = mu.reg_read(UC_ARM_REG_R0); n = (n + 3) & ~3
                mu.reg_write(UC_ARM_REG_R0, heap[0]); heap[0] += n
            mu.reg_write(UC_ARM_REG_PC, mu.reg_read(UC_ARM_REG_LR))
        cpu.mu.hook_add(UC_HOOK_CODE, alloc, begin=0x08006A58, end=0x08006A58)
        cpu.mu.hook_add(UC_HOOK_CODE, alloc, begin=0x0800695C, end=0x0800695C)
        cpu.mu.mem_write(0x0F000000, pre[EW + IW:EW + IW + FR])
        cpu.mu.mem_write(0x04000000, pre[EW + IW + FR:])
        try:
            r = cpu.call(fn | 1, *args, limit=200_000_000)
        except Exception as ex:   # noqa: BLE001
            print('%s 일%d %08X(%s): 원작 실행 실패 %s' % (os.path.basename(p), day, fn, ','.join('%X' % a for a in args), ex)); diff += 1; continue
        got = cpu.ram()
        want = post[:EW + IW]
        # IWRAM 위쪽(0x03007000~)은 원작 실행의 스택이라 빼고 비교한다
        bad = [i for i in range(EW + 0x7000) if got[i] != want[i]]
        heap = [i for i in bad if 0x2BFB0 <= i < 0x2C010]
        rest = [i for i in bad if not (0x2BFB0 <= i < 0x2C010)]
        rok = (r & 0xFFFFFFFF) == ret or (r & 0xFFFFFF00) == 0x03007F00   # 돌려주는 값이 없는 함수: 원작 r0 = 복귀 주소 찌꺼기
        tag = '%s 일%d %08X(%s)' % (os.path.basename(p), day, fn, ','.join('%X' % a for a in args))
        if not rest and rok:
            same += 1
            print(tag + ': 같음' + (' (원작 힙 머리 %d바이트 다름)' % len(heap) if heap else ''))
        else:
            diff += 1
            print(tag + ': 다름  돌려준 값 원작 %X / C# %X' % (r & 0xFFFFFFFF, ret))
            for a, b in ranges(rest)[:12]:
                base = 0x02000000 if a < EW else 0x03000000 - EW
                print('   %08X~%08X 원작 %s / C# %s' % (base + a, base + b, got[a:b + 1][:16].hex(), want[a:b + 1][:16].hex()))
    print('같음 %d / 다름 %d' % (same, diff))


if __name__ == '__main__':
    main()
