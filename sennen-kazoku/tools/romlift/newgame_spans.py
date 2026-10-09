"""새 가족 만들기 코드가 읽는 ROM 표 범위 모으기 (로컬 전용, 검증·팩 만들기 보조).

원작 ROM 을 unicorn 으로 실행해(romcall.py) 새 가족 만들기 순서를 여러 난수 seed 로 반복하고,
ROM 데이터 읽기 주소를 모아 0x400 바이트 단위로 묶어 낸다. 어떤 항목을 읽는지는 난수(추천 가족 틀·성·구성원 값)에 따라 달라서
한 번 실행으로는 표 전체가 잡히지 않는다.
결과 파일은 export_orig.py 가 읽어 팩의 ROM 조각에 더한다.

사용: python3 newgame_spans.py ROM.gba 덤프.ram 출력.json [반복수]
  덤프.ram = 원작 실행 중 RAM 덤프(EWRAM 256KB + IWRAM 32KB). 새 가족 장면 근처면 된다.
"""
import sys, json, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from romcall import RomCpu  # noqa: E402
from unicorn import UC_HOOK_MEM_READ  # noqa: E402

SETUP, MEMBERS, MSIZE = 0x02001AF0, 0x02001AF0 + 0x44, 0x9F0
SCENE = 0x0203F000
UI = (0x08047024, 0x08048390, 0x08047864, 0x08046F80, 0x08046804, 0x08003FE8, 0x0800695C, 0x08001014, 0x0809AF60)


def init(ram):
    """0x0804134C 데이터 부분 (OrigNewGame.Init 와 같음)."""
    o = lambda a: a - 0x02000000
    ram[o(0x0202C6A0)] = 0
    ram[o(SETUP):o(SETUP) + 0x44] = b'\xff' * 0x44
    for k in range(8):
        p = o(MEMBERS) + k * MSIZE
        ram[p:p + MSIZE] = b'\xff' * MSIZE
        ram[p + 0xE] = 0; ram[p + 0x4C] = 0
    ram[o(SETUP):o(SETUP) + 4] = bytes([0xD5, 0x07, 1, 1]); ram[o(SETUP) + 0x28] = 0


def confirm(ram):
    """0x0802C9A0 데이터 부분 (OrigNewGame.Confirm 와 같음)."""
    s = SETUP - 0x02000000; m = MEMBERS - 0x02000000
    mask = ram[s + 0x28]; ram[s + 0x29] = bin(mask).count('1'); k = 0
    for i in range(8):
        if not (mask >> i) & 1: continue
        rel = [0, 1, 2, 4][i] if i < 4 else (6 if ram[s + 0x1C + i - 4] == 0 else 7)
        ram[m + k * MSIZE + 0x4D] = rel; k += 1


def main():
    rom = bytearray(open(sys.argv[1], 'rb').read())
    for f in UI: rom[f - 0x08000000:f - 0x08000000 + 2] = b'\x70\x47'   # bx lr (화면 정리 생략)
    base = open(sys.argv[2], 'rb').read()
    n = int(sys.argv[4]) if len(sys.argv) > 4 else 64
    reads = set()

    def hook(mu, acc, addr, size, val, ud):
        if 0x08000000 <= addr < 0x0A000000:
            for a in range(addr, addr + size): reads.add(a)
    for seed in range(n):
        ram = bytearray(base)
        ram[0:4] = (0x12345 + seed * 0x9E3779B1 & 0xFFFFFFFF).to_bytes(4, 'little')
        init(ram)
        cpu = RomCpu(bytes(rom), bytes(ram)); cpu.mu.hook_add(UC_HOOK_MEM_READ, hook)
        cpu.call(0x0804BC2C, SETUP, MEMBERS, limit=5_000_000)
        ram = bytearray(cpu.ram()); confirm(ram)
        o = SCENE - 0x02000000; ram[o:o + 4] = SETUP.to_bytes(4, 'little'); ram[o + 0x11] = 0
        s = SETUP - 0x02000000 + 0xD340; ram[s:s + 4] = b'\x01\0\0\0'   # 추천 가족 경로
        cpu.set_ram(bytes(ram))
        cpu.call(0x0802D7A0, SCENE, limit=50_000_000)
        cpu.mu.mem_write(SETUP + 0xD34A, b'\0')   # 확인 화면에서 결정 (0 이 아니면 가족을 만들지 않음)
        cpu.call(0x080417E0, SETUP, limit=50_000_000)
        print('seed %d: 읽은 ROM 바이트 %d, 1KB 묶음 %d' % (seed, len(reads), len({a & ~0x3FF for a in reads if a >= 0x08300000})), flush=True)
    # 코드 영역(0x08300000 미만)의 리터럴 읽기는 트리에 상수로 들어가 있으므로 뺀다
    blocks = sorted({a & ~0x3FF for a in reads if a >= 0x08300000})
    spans = []
    for b in blocks:
        if spans and spans[-1][1] == b: spans[-1][1] = b + 0x400
        else: spans.append([b, b + 0x400])
    json.dump([[a, e - a] for a, e in spans], open(sys.argv[3], 'w'))
    print('범위 %d개, %d KB' % (len(spans), sum(e - a for a, e in spans) // 1024))


if __name__ == '__main__':
    main()
