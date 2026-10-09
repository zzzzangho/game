#!/usr/bin/env python3
"""검증용 기대값 만들기 (로컬 전용): 원작 ROM 함수를 실제 RAM 덤프 위에서 실행(unicorn)한 결과를 JSON 으로 쓴다.
C# 테스트(tests/Core.Tests)가 같은 RAM 으로 옮긴 규칙을 돌려 결과가 같은지 본다.

  python3 make_vectors.py <천년가족.gba> <orig_rules.json> <출력 json> <RAM 덤프...>
  RAM 덤프 = tools/gba_capture/capture 의 ramdump 결과 (EWRAM 256KB + IWRAM 32KB)
"""
import json, os, struct, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from romcall import RomCpu  # noqa: E402

PERSON = 0x0202C6C4


def main():
    rom = open(sys.argv[1], 'rb').read()
    rules = json.load(open(sys.argv[2], encoding='utf8'))
    out = {"preds": [], "select": [], "events": []}
    preds = sorted({x[5] for x in rules["interests"]})
    for rp in sys.argv[4:]:
        ram = open(rp, 'rb').read(); cpu = RomCpu(rom, ram)
        name = os.path.basename(rp)
        for n in range(8):
            base = PERSON + 976 * n
            pid = struct.unpack_from('<H', ram, base + 0x3C - 0x02000000)[0]
            if pid == 0xFFFF: continue
            # 1) 판정 함수: 그 사람 중심 슬롯표에서 모든 관심사 판정
            cpu.set_ram(ram); cpu.call(0x08110B90, pid, 0); centered = cpu.ram()
            res = []
            for f in preds:
                cpu.set_ram(centered)
                try: res.append(cpu.call(int(f, 16)))
                except Exception: res.append(None)
            out["preds"].append({"ram": name, "person": n, "id": pid, "preds": preds, "expect": res})
            # 1-2) MAX/MIN 사건: 변형 고르기(0x08119CA0) + 효과 함수(결과 기록 +0x1C)
            for ev_off in (0x1C, 0x20):
                cpu.set_ram(ram)
                t, i = struct.unpack_from('<HH', ram, base + 0x80 - 0x02000000)
                u32 = lambda a: struct.unpack_from('<I', rom, a - 0x08000000)[0]
                entry = u32(u32(0x085BD4A0 + 4 * t) + 4 * i); ev = u32(entry + ev_off)
                if not (0x08000000 <= ev < 0x0A000000): continue
                for seed in (5, 0x1234567):
                    cpu.set_ram(ram); cpu.call(0x08110B90, pid, 0); cpu.w32(0x02000000, seed)
                    before = cpu.ram()
                    try:
                        data = cpu.call(0x08119CA0, ev)
                        post = u32(data + 0x1C)
                        cpu.call(post, 0x03007D00, 0, 0, 0)
                    except Exception as ex:
                        out["events"].append({"ram": name, "person": n, "max": ev_off == 0x1C, "seed": seed, "error": str(ex)}); continue
                    after = cpu.ram()
                    out["events"].append({"ram": name, "person": n, "max": ev_off == 0x1C, "seed": seed, "data": data,
                                          "writes": [[0x02000000 + k, after[k]] for k in range(0x40000) if before[k] != after[k]]})
            # 2) 관심사 선택: 여러 seed 로 0x08028524 실행
            for seed in (1, 12345, 0xDEADBEEF, 777, 0x13579BDF, 42, 99991, 0x0BADF00D):
                cpu.set_ram(ram)
                era = cpu.call(0x08111A58, 0x0202C684)
                stage = cpu.call(0x08111888, base + 0x2E)
                cpu.call(0x08110B90, pid, 0)
                cpu.w32(0x02000000, seed)
                before = cpu.ram()
                try:
                    cpu.call(0x08028524, base, era, stage)
                except Exception as ex:
                    out["select"].append({"ram": name, "person": n, "seed": seed, "error": str(ex)}); continue
                after = cpu.ram()
                diffs = [[0x02000000 + i, after[i]] for i in range(0x40000) if before[i] != after[i]]
                out["select"].append({"ram": name, "person": n, "seed": seed, "era": era, "stage": stage, "writes": diffs})
    json.dump(out, open(sys.argv[3], 'w'), separators=(',', ':'))
    print('판정 %d명분, 선택 %d건, 사건 %d건' % (len(out["preds"]), len(out["select"]), len(out["events"])))


if __name__ == '__main__':
    main()
