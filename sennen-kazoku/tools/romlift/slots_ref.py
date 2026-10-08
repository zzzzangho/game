"""관계 슬롯표(0x0203C3E0) 작성 루틴 0x08110B90 의 손 이식 (C# 이식 기준). mem: 읽기/쓰기 객체."""
FAM, GENE, SLOT = 0x0202C010, 0x0202EB9C, 0x0203C3E0
NONE = 0xFFFF


class Mem:
    def __init__(self, r16, r8, w16):
        self.r16, self.r8, self.w16 = r16, r8, w16


def gene(i): return (GENE + 60 * i) & 0xFFFFFFFF


def in_house(m, x):                       # 0x08110608
    if x == NONE: return NONE
    n = m.r8(FAM + 0x6A3)
    for i in range(n, 0, -1):
        if m.r16(FAM + 0x2538 + 28 * (i - 1)) == x: return i
    return NONE


def anc_m(m, x, n):                       # 0x08110690
    if n == 1: return m.r16(gene(x) + 0x10)
    f, mo = m.r16(gene(x) + 0x10), m.r16(gene(x) + 0x12)
    if m.r16(gene(f) + 0x10) != NONE: return anc_m(m, f, n - 1)
    if m.r16(gene(mo) + 0x10) != NONE: return anc_m(m, mo, n - 1)
    return NONE


def anc_f(m, x, n):                       # 0x081107A8
    if n == 1: return m.r16(gene(x) + 0x12)
    f, mo = m.r16(gene(x) + 0x10), m.r16(gene(x) + 0x12)
    if m.r16(gene(mo) + 0x12) != NONE: return anc_f(m, mo, n - 1)
    if m.r16(gene(f) + 0x12) != NONE: return anc_f(m, f, n - 1)
    return NONE


def desc(m, x, n, i):                     # 0x081108C0
    if n == 1: return m.r16(gene(x) + 0x16 + 2 * i)
    for k in range(4):
        c = m.r16(gene(x) + 0x16 + 2 * k)
        if in_house(m, c) != NONE: return desc(m, c, n - 1, i)
    return NONE


def spouse_of(m, x, n, g):                # 0x081109C0
    if n == 0:
        sp = m.r16(gene(x) + 0x14)
        if g == 2: return sp
        return sp if m.r8(gene(sp) + 0x31) == g else NONE
    for k in range(4):
        c = m.r16(gene(x) + 0x16 + 2 * k)
        if in_house(m, c) != NONE: return spouse_of(m, c, n - 1, g)
    return NONE


def first_child_in_house(m, x):           # 0x08111384
    for k in range(4):
        if in_house(m, m.r16(gene(x) + 0x16 + 2 * k)) != NONE: return k
    return 4


def build(m, x):                          # 0x08110B90 (두 번째 인자 0 인 경우)
    for k in range(23): m.w16(SLOT + 2 * k, NONE)
    s = lambda k, v: m.w16(SLOT + 2 * k, v)
    s(0, x)
    s(1, anc_m(m, x, 3)); s(2, anc_f(m, x, 3))
    s(3, anc_m(m, x, 2)); s(4, anc_f(m, x, 2))
    s(5, anc_m(m, x, 1)); s(6, anc_f(m, x, 1))
    s(15, spouse_of(m, x, 0, 2))
    for i in range(4): s(7 + i, desc(m, x, 1, i))
    for i in range(4): s(11 + i, desc(m, x, 2, i))
    s(16, spouse_of(m, x, 1, 2)); s(17, spouse_of(m, x, 2, 2))
    k = first_child_in_house(m, x)
    if k != 4:
        c = m.r16(gene(x) + 0x16 + 2 * k)
        if m.r8(gene(c) + 0x31) == 0:
            s(18, m.r16(SLOT + 2 * 16)); s(19, c)
        else:
            s(18, c); s(19, m.r16(SLOT + 2 * 16))
    f, mo = m.r16(gene(x) + 0x10), m.r16(gene(x) + 0x12)
    p = gene(f) if f != NONE else gene(mo) if mo != NONE else None
    if p is not None:
        j = 0
        for i in range(4):
            c = m.r16(p + 0x16 + 2 * i)
            if c == NONE: break
            if c != x:
                s(20 + j, c); j += 1
    s(27, m.r16(FAM + 0x66C))


PERSON = 0x0202C6C4


def rec(m, x):                            # 0x08110B2C: 가족 레코드 주소 (없으면 0)
    if x == NONE: return 0
    for n in range(8):                    # 0x0800E524
        if m.r16(PERSON + 976 * n + 0x3C) == x: return PERSON + 976 * n
    return 0


def rel(m, slot, k):                      # 0x08115778
    target = m.r16(SLOT + 2 * slot)
    if target == NONE: return 0xFFFFFFFF
    saved = m.r16(SLOT)
    build(m, target)
    res = 0
    if k == 0x1C:
        res = 1 if m.r8(rec(m, m.r16(SLOT)) + 0x74) != 0xFF else 0
    elif k == 0x0F:
        r = rec(m, m.r16(SLOT))
        if r == 0: r = gene(m.r16(SLOT))
        res = 1 if rec(m, m.r16(r + 0x14)) != 0 else 0
    else:
        res = 1 if rec(m, m.r16(SLOT + 2 * k)) != 0 else 0
    build(m, saved)
    return res
