#!/usr/bin/env python3
"""ROM 에셋 형식 조사 (통계만 출력, 추출물은 저장하지 않음).
사용: python3 -I rom_survey.py <rom.gba>
GBA BIOS LZ77(0x10) 블록 후보, 팔레트 후보, Sappy(m4a) 사운드 엔진 흔적을 센다."""
import sys, struct, collections

def lz77_ok(d, p, limit=1 << 20):
    """0x10 헤더 LZ77이 끝까지 유효하게 풀리는지(참조 거리 검사 포함). 성공 시 (출력크기, 입력소비) 반환."""
    if p + 4 > len(d) or d[p] != 0x10: return None
    size = d[p+1] | d[p+2] << 8 | d[p+3] << 16
    if size < 64 or size > limit: return None
    i, out = p + 4, 0
    while out < size:
        if i >= len(d): return None
        flags = d[i]; i += 1
        for b in range(8):
            if out >= size: break
            if flags & (0x80 >> b):
                if i + 1 >= len(d): return None
                n = (d[i] >> 4) + 3; disp = ((d[i] & 15) << 8 | d[i+1]) + 1; i += 2
                if disp > out: return None
                out += n
            else:
                if i >= len(d): return None
                i += 1; out += 1
    return size, i - p

def main(path):
    d = open(path, 'rb').read()
    print('size', len(d), 'title', d[0xA0:0xAC], 'game code', d[0xAC:0xB0])
    found = []; p = 0
    while True:
        p = d.find(b'\x10', p)
        if p < 0: break
        if p % 4 == 0:
            r = lz77_ok(d, p)
            if r: found.append((p, r[0], r[1]))
        p += 1
    big = [f for f in found if f[1] >= 512]
    print('LZ77 후보(4바이트 정렬, 유효 디코드):', len(found), ' 512B 이상:', len(big))
    hist = collections.Counter(f[1] for f in big)
    print('  흔한 출력 크기:', hist.most_common(8))
    print('  주소 분포(MB 단위):', sorted(collections.Counter(f[0] >> 20 for f in big).items()))
    # 팔레트 후보: 16/256색 BGR555 (bit15 == 0) 연속
    pal = 0
    for off in range(0, len(d) - 32, 32):
        w = struct.unpack_from('<16H', d, off)
        if all(x < 0x8000 for x in w) and len(set(w)) > 3 and w[0] == 0: pal += 1
    print('16색 팔레트 형태 후보(32B 정렬):', pal)
    # Sappy / m4a 흔적: "Smsh"/ voicegroup 구조 대신 알려진 sound engine 문자열 탐색
    for sig in (b'Sappy', b'M4A', b'MPlay', b'AGB Sound', b'Smsh', b'SoundDriver', b'MusicPlayer'):
        print('시그니처', sig, d.count(sig))
    # ARM/Thumb 함수 풀 내에서 SoundMain 의 전형: 0x68736D53 ('Smsh') 값 참조
    print('0x68736D53("Smsh") 리터럴:', d.count(struct.pack('<I', 0x68736D53)))
if __name__ == '__main__':
    main(sys.argv[1])
