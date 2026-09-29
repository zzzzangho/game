/* Hardware abstraction: implemented by plat_gba.c (real ROM) and test/plat_pc.c (host test harness). */
#ifndef PLATFORM_H
#define PLATFORM_H

typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long long u64;
typedef signed short s16;
typedef signed int s32;

#ifdef PC_BUILD
#define EWRAM_BSS
#else
#define EWRAM_BSS __attribute__((section(".ewram"), aligned(4)))
#endif

#define SCREEN_W 240
#define SCREEN_H 160

#define KEY_A      0x0001
#define KEY_B      0x0002
#define KEY_SELECT 0x0004
#define KEY_START  0x0008
#define KEY_RIGHT  0x0010
#define KEY_LEFT   0x0020
#define KEY_UP     0x0040
#define KEY_DOWN   0x0080
#define KEY_R      0x0100
#define KEY_L      0x0200

enum { SFX_BLIP, SFX_MOVE, SFX_OK, SFX_CANCEL, SFX_GET, SFX_WRONG, SFX_SHOCK, SFX_OBJECTION, SFX_DUN };

/* Debug hooks used only by the host harness. */
enum { DBG_MENU, DBG_ACCUSE, DBG_PRESENT, DBG_INVEST, DBG_TITLE, DBG_VIDEO, DBG_TESTIMONY, DBG_MASH, DBG_SLOT, DBG_SAVE };

void plat_init(void);
u16 *plat_fb(void);                     /* 240x160 BGR555 back buffer */
void plat_present(int y0, int y1);      /* copy rows [y0,y1) of back buffer to the screen */
void plat_vsync(void);
u16 plat_keys(void);                    /* currently held keys, active high */
void plat_fade(int level);              /* 0 = normal, 16 = black */
void plat_offset(int dx, int dy);       /* screen shake */
void plat_hscale(int pa);               /* horizontal scale around the centre (8.8 inverse; 256 = off) */
void plat_sfx(int id);
void plat_sram_read(void *dst, int ofs, int n);
void plat_sram_write(const void *src, int ofs, int n);
void plat_copy32(void *dst, const void *src, int words);

int plat_debug_choice(int kind, int n); /* -1 on hardware */
int plat_debug_menu(int id, int n, u32 traps); /* explore-menu choice for the test harness; -1 on hardware */
void plat_debug_event(const char *what, int value);

#endif
