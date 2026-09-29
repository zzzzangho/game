/* Host (PC) platform used by the automated playthrough tests.
 *
 * Environment:
 *   ROUTE      comma separated answers for @ask / @accuse / @present (evidence ids)
 *   VIDEO      comma separated frame choices for the video player (n = stop watching)
 *   MASH=fail  let every button-mash crisis fail
 *   ROUTE value 9999 = give no answer and release no keys until the timer runs out
 *   ROUTE values for @testimony: s = press statement s, 1000 + s*100 + e = present evidence e on statement s
 *   Exploration menus (@investigate / @menu) are walked round-robin, skipping trap options.
 *   TITLE      title menu choice (0 = new game, 1 = continue)
 *   SRAM_FILE  file used as battery save
 *   SHOTS      directory for PPM screenshots (optional), MAX_SHOTS limits the count
 * Exit codes: 0 = returned to title after an ending, 2 = frame limit, 3 = ROUTE exhausted.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "platform.h"

static u16 backbuf[SCREEN_W * SCREEN_H];
static u16 screen[SCREEN_W * SCREEN_H];
static u8 sram[32768];
static unsigned long frames;
static int route[512], route_len, route_pos;
static int invest_counter;
static int muted; /* waiting for a timer to run out */
static int video_route[64], video_len, video_pos;
static unsigned short menu_next[65536];
static int shots, max_shots = 400;
static const char *shot_dir, *sram_file;

void plat_init(void)
{
    const char *r = getenv("ROUTE");
    while (r && *r) {
        route[route_len++] = atoi(r);
        r = strchr(r, ',');
        if (r) r++;
    }
    r = getenv("VIDEO");
    while (r && *r) {
        video_route[video_len++] = atoi(r);
        r = strchr(r, ',');
        if (r) r++;
    }
    shot_dir = getenv("SHOTS");
    if (getenv("MAX_SHOTS")) max_shots = atoi(getenv("MAX_SHOTS"));
    sram_file = getenv("SRAM_FILE");
    memset(sram, 0xFF, sizeof sram);
    if (sram_file) {
        FILE *f = fopen(sram_file, "rb");
        if (f) {
            if (fread(sram, 1, sizeof sram, f) == 0) memset(sram, 0xFF, sizeof sram);
            fclose(f);
        }
    }
}

u16 *plat_fb(void) { return backbuf; }

void plat_present(int y0, int y1)
{
    memcpy(screen + y0 * SCREEN_W, backbuf + y0 * SCREEN_W, (y1 - y0) * SCREEN_W * 2);
}

void plat_vsync(void)
{
    if (++frames > 3000000) {
        fprintf(stderr, "frame limit reached\n");
        exit(2);
    }
}

/* Tap A every other frame. */
u16 plat_keys(void) { return (!muted && (frames & 1)) ? KEY_A : 0; }

void plat_fade(int level) { (void)level; }
void plat_offset(int dx, int dy) { (void)dx; (void)dy; }
void plat_sfx(int id) { (void)id; }

void plat_sram_read(void *dst, int ofs, int n) { memcpy(dst, sram + ofs, n); }

void plat_sram_write(const void *src, int ofs, int n)
{
    memcpy(sram + ofs, src, n);
    if (sram_file) {
        FILE *f = fopen(sram_file, "wb");
        if (f) {
            fwrite(sram, 1, sizeof sram, f);
            fclose(f);
        }
    }
}

void plat_copy32(void *dst, const void *src, int words) { memmove(dst, src, (size_t)words * 4); }

static int next_route(const char *what)
{
    if (route_pos >= route_len) {
        fprintf(stderr, "ROUTE exhausted at %s (answer #%d)\n", what, route_pos);
        exit(3);
    }
    return route[route_pos++];
}

int plat_debug_choice(int kind, int n)
{
    int c;
    if (muted) return -1;
    switch (kind) {
    case DBG_MASH:
        return getenv("MASH") && !strcmp(getenv("MASH"), "fail") ? 1 : 0;
    case DBG_TESTIMONY:
        c = next_route("testimony");
        printf("testimony -> %d\n", c);
        return c;
    case DBG_TITLE:
        return getenv("TITLE") ? atoi(getenv("TITLE")) : 0;
    case DBG_INVEST:
        c = invest_counter < n ? invest_counter : n - 1;
        invest_counter++;
        printf("invest -> %d\n", c);
        return c;
    case DBG_VIDEO:
        c = video_pos < video_len ? video_route[video_pos++] : n;
        printf("video -> %d\n", c);
        return c;
    case DBG_PRESENT:
        c = next_route("present");
        if (c == 9999) {
            muted = 1;
            printf("waiting for the timer\n");
            return -1;
        }
        printf("present -> evidence %d\n", c);
        return c;
    default:
        c = next_route(kind == DBG_ACCUSE ? "accuse" : "menu");
        if (c == 9999) {
            muted = 1;
            printf("waiting for the timer\n");
            return -1;
        }
        printf("%s -> %d\n", kind == DBG_ACCUSE ? "accuse" : "menu", c);
        return c;
    }
}

/* Round-robin over the options of each exploration menu (the last one is the exit), skipping traps. */
int plat_debug_menu(int id, int n, u32 traps)
{
    for (int k = 0; k < n; k++) {
        int c = (menu_next[id] + k) % n;
        if (!getenv("TRAPS") && c < 32 && (traps >> c) & 1) continue;
        menu_next[id] = (unsigned short)(c + 1);
        return c;
    }
    return n - 1;
}

static void shot(const char *what)
{
    char path[512];
    if (!shot_dir || shots >= max_shots) return;
    snprintf(path, sizeof path, "%s/%04d_%s.ppm", shot_dir, shots++, what);
    FILE *f = fopen(path, "wb");
    if (!f) return;
    fprintf(f, "P6\n%d %d\n255\n", SCREEN_W, SCREEN_H);
    for (int i = 0; i < SCREEN_W * SCREEN_H; i++) {
        u16 c = backbuf[i];
        u8 rgb[3] = {(u8)((c & 31) << 3), (u8)(((c >> 5) & 31) << 3), (u8)(((c >> 10) & 31) << 3)};
        fwrite(rgb, 1, 3, f);
    }
    fclose(f);
}

void plat_debug_event(const char *what, int value)
{
    static const char *last;
    static int last_value = -1;
    if (!strcmp(what, "invest")) invest_counter = 0;
    if (!strcmp(what, "timeout")) {
        muted = 0;
        printf("EVENT timeout\n");
    }
    if (!strcmp(what, "mash") || !strcmp(what, "contradiction")) printf("EVENT %s %d\n", what, value);
    if (!strcmp(what, "true_end") || !strcmp(what, "bad_end") || !strcmp(what, "good_end") ||
        !strcmp(what, "best_end") || !strcmp(what, "chapter") ||
        !strcmp(what, "invest"))
        printf("EVENT %s %d\n", what, value);
    /* menus/records report every frame; only snapshot when something changed */
    if (last && !strcmp(last, what) && last_value == value && (!strcmp(what, "menu") || !strcmp(what, "record") ||
                                                              !strcmp(what, "title")))
        return;
    last = what;
    last_value = value;
    shot(what);
    if (!strcmp(what, "to_title")) {
        printf("EVENT to_title frames=%lu\n", frames);
        exit(0);
    }
}
