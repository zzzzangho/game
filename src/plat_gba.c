#include "platform.h"

#define REG16(a) (*(volatile u16 *)(a))
#define REG32(a) (*(volatile u32 *)(a))

#define REG_DISPCNT  REG16(0x04000000)
#define REG_VCOUNT   REG16(0x04000006)
#define REG_BG2CNT   REG16(0x0400000C)
#define REG_BG2PA    REG16(0x04000020)
#define REG_BG2PD    REG16(0x04000026)
#define REG_BG2X     REG32(0x04000028)
#define REG_BG2Y     REG32(0x0400002C)
#define REG_BLDCNT   REG16(0x04000050)
#define REG_BLDY     REG16(0x04000054)
#define REG_KEYINPUT REG16(0x04000130)
#define REG_WAITCNT  REG16(0x04000204)

#define REG_SND1SWEEP REG16(0x04000060)
#define REG_SND1CNT   REG16(0x04000062)
#define REG_SND1FREQ  REG16(0x04000064)
#define REG_SND2CNT   REG16(0x04000068)
#define REG_SND2FREQ  REG16(0x0400006C)
#define REG_SND4CNT   REG16(0x04000078)
#define REG_SND4FREQ  REG16(0x0400007C)
#define REG_SNDDMGCNT REG16(0x04000080)
#define REG_SNDDSCNT  REG16(0x04000082)
#define REG_SNDSTAT   REG16(0x04000084)

#define REG_DMA3SAD  REG32(0x040000D4)
#define REG_DMA3DAD  REG32(0x040000D8)
#define REG_DMA3CNT  REG32(0x040000DC)

#define VRAM ((u16 *)0x06000000)
#define SRAM ((volatile u8 *)0x0E000000)

static u16 backbuf[SCREEN_W * SCREEN_H] EWRAM_BSS;

/* The compiler may emit calls to these for struct copies. */
void *memcpy(void *d, const void *s, unsigned n)
{
    u8 *dp = d;
    const u8 *sp = s;
    while (n--) *dp++ = *sp++;
    return d;
}

void *memset(void *d, int c, unsigned n)
{
    u8 *dp = d;
    while (n--) *dp++ = (u8)c;
    return d;
}

void plat_init(void)
{
    REG_WAITCNT = 0x4317; /* faster ROM access, prefetch on */
    REG_DISPCNT = 3 | (1 << 10); /* mode 3, BG2 */
    REG_BG2PA = 0x100;
    REG_BG2PD = 0x100;
    REG_SNDSTAT = 0x80;
    REG_SNDDMGCNT = 0xFF77;
    REG_SNDDSCNT = 2;
}

u16 *plat_fb(void) { return backbuf; }

void plat_copy32(void *dst, const void *src, int words)
{
    if (words <= 0) return;
    REG_DMA3SAD = (u32)src;
    REG_DMA3DAD = (u32)dst;
    REG_DMA3CNT = (u32)words | (1u << 26) | (1u << 31);
}

void plat_present(int y0, int y1)
{
    if (y1 <= y0) return;
    plat_copy32(VRAM + y0 * SCREEN_W, backbuf + y0 * SCREEN_W, (y1 - y0) * SCREEN_W / 2);
}

void plat_vsync(void)
{
    while (REG_VCOUNT >= 160) {}
    while (REG_VCOUNT < 160) {}
}

u16 plat_keys(void) { return (~REG_KEYINPUT) & 0x3FF; }

void plat_fade(int level)
{
    if (level <= 0) {
        REG_BLDCNT = 0;
        REG_BLDY = 0;
    } else {
        REG_BLDCNT = (1 << 2) | (3 << 6); /* BG2, brightness decrease */
        REG_BLDY = level > 16 ? 16 : level;
    }
}

void plat_hscale(int pa)
{
    /* texture x = BG2X + PA * screen x; keep screen column 120 on texture column 120 */
    if (pa > 32767) pa = 32767;
    if (pa < -32767) pa = -32767;
    REG_BG2PA = (u16)pa;
    REG_BG2X = (u32)(120 * 256 - 120 * pa);
    REG_BG2Y = 0;
}

void plat_offset(int dx, int dy)
{
    REG_BG2X = (u32)(dx << 8);
    REG_BG2Y = (u32)(dy << 8);
}

static u16 rate(int hz) { return (u16)(2048 - 131072 / hz); }

void plat_sfx(int id)
{
    switch (id) {
    case SFX_BLIP:
        REG_SND2CNT = (4 << 12) | (1 << 8) | (2 << 6) | 60;
        REG_SND2FREQ = rate(1400) | 0xC000;
        break;
    case SFX_MOVE:
        REG_SND2CNT = (6 << 12) | (1 << 8) | (2 << 6) | 56;
        REG_SND2FREQ = rate(900) | 0xC000;
        break;
    case SFX_OK:
        REG_SND1SWEEP = (2 << 4) | 3;
        REG_SND1CNT = (10 << 12) | (2 << 8) | (2 << 6) | 40;
        REG_SND1FREQ = rate(700) | 0xC000;
        break;
    case SFX_CANCEL:
        REG_SND1SWEEP = (2 << 4) | (1 << 3) | 3;
        REG_SND1CNT = (9 << 12) | (2 << 8) | (2 << 6) | 40;
        REG_SND1FREQ = rate(600) | 0xC000;
        break;
    case SFX_GET:
        REG_SND1SWEEP = (4 << 4) | 2;
        REG_SND1CNT = (12 << 12) | (4 << 8) | (2 << 6) | 0;
        REG_SND1FREQ = rate(500) | 0xC000;
        break;
    case SFX_WRONG:
        REG_SND1SWEEP = (5 << 4) | (1 << 3) | 2;
        REG_SND1CNT = (15 << 12) | (5 << 8) | (0 << 6) | 0;
        REG_SND1FREQ = rate(180) | 0xC000;
        break;
    case SFX_SHOCK:
        REG_SND4CNT = (15 << 12) | (4 << 8);
        REG_SND4FREQ = 0x8000 | (4 << 4) | 1;
        break;
    case SFX_DUN: /* the eyecatch's heavy "dun": a low falling tone over a noise thump */
        REG_SND1SWEEP = (7 << 4) | (1 << 3) | 3;
        REG_SND1CNT = (15 << 12) | (6 << 8) | (2 << 6) | 0;
        REG_SND1FREQ = rate(160) | 0xC000;
        REG_SND4CNT = (15 << 12) | (5 << 8);
        REG_SND4FREQ = 0x8000 | (6 << 4) | 3;
        break;
    case SFX_OBJECTION:
        REG_SND1SWEEP = (3 << 4) | 1;
        REG_SND1CNT = (15 << 12) | (5 << 8) | (1 << 6) | 0;
        REG_SND1FREQ = rate(300) | 0xC000;
        REG_SND4CNT = (10 << 12) | (2 << 8);
        REG_SND4FREQ = 0x8000 | (2 << 4) | 2;
        break;
    }
}

void plat_sram_read(void *dst, int ofs, int n)
{
    u8 *d = dst;
    for (int i = 0; i < n; i++) d[i] = SRAM[ofs + i];
}

void plat_sram_write(const void *src, int ofs, int n)
{
    const u8 *s = src;
    for (int i = 0; i < n; i++) SRAM[ofs + i] = s[i];
}

int plat_debug_choice(int kind, int n) { (void)kind; (void)n; return -1; }
int plat_debug_menu(int id, int n, u32 traps) { (void)id; (void)n; (void)traps; return -1; }
void plat_debug_event(const char *what, int value) { (void)what; (void)value; }
