/* 소년탐정 김전일 - 마술열차 살인사건 (unofficial fan game)
 * Script-driven detective adventure engine for GBA mode 3.
 * Story bytecode, font and art come from build/gen_data.c (tools/build_assets.py).
 */
#include "platform.h"
#include "gen_data.h"

#define RGB(r, g, b) ((u16)((r) | ((g) << 5) | ((b) << 10)))
#define C_WHITE  RGB(31, 31, 31)
#define C_SHADOW RGB(1, 1, 4)
#define C_GREY   RGB(14, 14, 17)
#define C_GOLD   RGB(31, 25, 8)
#define C_RED    RGB(31, 6, 6)
#define C_BOX    RGB(2, 3, 9)
#define C_PANEL  RGB(4, 5, 12)
#define C_HILITE RGB(9, 12, 24)
#define C_BORDER RGB(24, 19, 8)
#define C_EMPH   RGB(31, 13, 7) /* {강조} words in the script */

#define LINE_H 13
#define BOX_Y 114 /* dialogue box: 3 lines of text */
#define TEXT_X 8
#define TEXT_Y 118
#define PORTRAIT_X ((SCREEN_W - PORTRAIT_W) / 2)
#define PORTRAIT_Y 0 /* bust hangs from the top; the translucent text box covers only the chest */

#define REC_ROWS 5
#define REC_LIST_Y 21
#define REC_DESC_Y 100

enum { RET_RETURN, RET_TITLE };

static u16 *fb;
static u16 snapshot[SCREEN_W * SCREEN_H] EWRAM_BSS;
static u16 scratch[SCREEN_W * SCREEN_H] EWRAM_BSS; /* the animated background, or the court record's saved screen */
/* scratch holds the animated background (no characters) at the current step, so a new line
 * redraws the scene with one copy instead of replaying every animation step */
static int anim_bg_ok;
static int dirty0 = SCREEN_H, dirty1 = 0;
static u16 keys_held, keys_new;
static u32 frame_count;

static int cur_scene, cur_portrait = NONE;
static int cut_mode; /* an anime capture is on screen: no portraits over it */
static int lives, max_lives, in_game;
static u64 ev_flags; /* evidence owned (up to 64 items) */
static u32 prof_flags;
#define EV_BIT(i) ((u64)1 << (i))
static u16 prof_text[32]; /* current profile text per character (@profile_update changes it) */
static u32 flags[32];     /* 1024 script flags: named @set flags + "already chosen" marks for menu options */

static int flag_get(int f) { return (flags[f >> 5] >> (f & 31)) & 1; }
static void flag_set(int f) { flags[f >> 5] |= 1u << (f & 31); }

/* Option condition word: NONE = always; bit 15 negates; bit 14 = evidence (else flag); low 12 bits = index. */
static int cond_true(u16 c)
{
    if (c == NONE) return 1;
    int idx = c & 0x0FFF;
    int v = (c & 0x4000) ? (int)((ev_flags >> idx) & 1) : flag_get(idx);
    return (c & 0x8000) ? !v : v;
}
static u16 gameover_pc;

/* ------------------------------------------------------------------ frame / input */

static void mark(int y0, int y1)
{
    if (y0 < 0) y0 = 0;
    if (y1 > SCREEN_H) y1 = SCREEN_H;
    if (y0 < dirty0) dirty0 = y0;
    if (y1 > dirty1) dirty1 = y1;
}

static void frame(void)
{
    u16 prev = keys_held;
    plat_vsync();
    if (dirty1 > dirty0) {
        plat_present(dirty0, dirty1);
        dirty0 = SCREEN_H;
        dirty1 = 0;
    }
    keys_held = plat_keys();
    keys_new = keys_held & ~prev;
    frame_count++;
}

static void wait_frames(int n)
{
    while (n-- > 0) frame();
}

static void fade_out(void)
{
    for (int l = 1; l <= 16; l++) {
        plat_fade(l);
        frame();
    }
}

static void fade_in(void)
{
    frame(); /* make sure the new picture is on screen first */
    for (int l = 15; l >= 0; l--) {
        plat_fade(l);
        frame();
    }
}

/* ------------------------------------------------------------------ drawing */

/* Hot pixel loops run as ARM code from IWRAM on the GBA (ROM thumb code is ~4x slower). */
#if defined(__arm__)
#define FAST __attribute__((section(".iwram.fast"), target("arm"), long_call, noinline))
#else
#define FAST
#endif

static inline void px(int x, int y, u16 c)
{
    if ((unsigned)x < SCREEN_W && (unsigned)y < SCREEN_H) fb[y * SCREEN_W + x] = c;
}

static void fill(int x, int y, int w, int h, u16 c)
{
    if (x < 0) w += x, x = 0;
    if (y < 0) h += y, y = 0;
    if (x + w > SCREEN_W) w = SCREEN_W - x;
    if (y + h > SCREEN_H) h = SCREEN_H - y;
    if (w <= 0 || h <= 0) return;
    for (int j = y; j < y + h; j++) {
        u16 *p = fb + j * SCREEN_W + x;
        for (int i = 0; i < w; i++) p[i] = c;
    }
    mark(y, y + h);
}

/* 50% blend toward colour c (translucent panels). */
FAST static void shade(int x, int y, int w, int h, u16 c)
{
    u16 half = (c >> 1) & 0x3DEF;
    for (int j = y; j < y + h; j++) {
        if ((unsigned)j >= SCREEN_H) continue;
        for (int i = x; i < x + w; i++) {
            if ((unsigned)i >= SCREEN_W) continue;
            u16 *p = &fb[j * SCREEN_W + i];
            *p = ((*p >> 1) & 0x3DEF) + half;
        }
    }
    mark(y, y + h);
}

static void frame_rect(int x, int y, int w, int h, u16 c)
{
    fill(x, y, w, 1, c);
    fill(x, y + h - 1, w, 1, c);
    fill(x, y, 1, h, c);
    fill(x + w - 1, y, 1, h, c);
}

static void blit_keyed(int x, int y, int w, int h, const u16 *src)
{
    for (int j = 0; j < h; j++)
        for (int i = 0; i < w; i++) {
            u16 c = src[j * w + i];
            if (c != TRANSPARENT) px(x + i, y + j, c);
        }
    mark(y, y + h);
}

static void draw_text_ex(int x, int y, int id, u16 c, int scale, int centred);

/* Display-font image of a title/banner text (tools/display_font.py), centred on (cx, cy).
 * vis limits how many columns are shown (for a wipe-in); -1 = all. */
/* clipped keyed copy of an image's columns i0..i1 (fast: banners slide in every frame) */
FAST static void blit_keyed_rows(int x, int y, int w, int h, int i0, int i1, const u16 *src)
{
    for (int j = 0; j < h; j++) {
        int yy = y + j;
        if ((unsigned)yy >= SCREEN_H) continue;
        const u16 *row = src + j * w;
        u16 *dst = fb + yy * SCREEN_W + x;
        for (int i = i0; i < i1; i++) {
            u16 c = row[i];
            if (c != TRANSPARENT) dst[i] = c;
        }
    }
}

static void draw_disp_part(int t, int cx, int cy, int vis)
{
    int d = disp_of_text[t];
    if (d == NONE) {
        draw_text_ex(cx, cy - 6, t, C_GOLD, 1, 1);
        return;
    }
    int w = disp_w[d], h = disp_h[d], x = cx - w / 2, y = cy - h / 2;
    if (vis < 0 || vis > w) vis = w;
    int i0 = x < 0 ? -x : 0, i1 = x + vis > SCREEN_W ? SCREEN_W - x : vis;
    if (i1 > i0) blit_keyed_rows(x, y, w, h, i0, i1, disp_data + disp_ofs[d]);
    mark(y < 0 ? 0 : y, y + h > SCREEN_H ? SCREEN_H : y + h);
}

static void draw_disp(int t, int cx, int cy) { draw_disp_part(t, cx, cy, -1); }
static int disp_width(int t) { return disp_of_text[t] == NONE ? 0 : disp_w[disp_of_text[t]]; }
static int disp_height(int t) { return disp_of_text[t] == NONE ? LINE_H : disp_h[disp_of_text[t]]; }
/* small Galmuri9 key hint, right-aligned at xr, top at y */
static void draw_hint_right(int t, int xr, int y) { draw_disp(t, xr - disp_width(t) / 2, y + disp_height(t) / 2); }

static int anim_t, anim_snap_t; /* background animation frame (see ambient_tick) */
static int breath_lift, breath_snap; /* idle breathing: portrait row offset, -2 (rest, 2 px lower) .. 0 */
static int blink_shut, blink_snap;   /* idle blink: eyes closed (blink_img patch shown) */

static void save_screen(void)
{
    plat_copy32(snapshot, fb, SCREEN_W * SCREEN_H / 2);
    anim_snap_t = anim_t;
    breath_snap = breath_lift;
    blink_snap = blink_shut;
}

static void restore_screen(void)
{
    plat_copy32(fb, snapshot, SCREEN_W * SCREEN_H / 2);
    if (anim_t != anim_snap_t) anim_bg_ok = 0;
    anim_t = anim_snap_t;
    breath_lift = breath_snap;
    blink_shut = blink_snap;
    mark(0, SCREEN_H);
}

/* ------------------------------------------------------------------ text */

static const u16 *txt(int id) { return text_data + text_ofs[id]; }

static void draw_glyph(int x, int y, int g, u16 c, int scale)
{
    const u16 *rows = glyph_bits + g * GLYPH_H;
    for (int r = 0; r < GLYPH_H; r++) {
        u16 bits = rows[r];
        for (int b = 0; bits; b++, bits <<= 1) {
            if (!(bits & 0x8000)) continue;
            if (scale == 1)
                px(x + b, y + r, c);
            else
                for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++) px(x + b * scale + sx, y + r * scale + sy, c);
        }
    }
    mark(y, y + GLYPH_H * scale);
}

static void glyph_shadowed(int x, int y, int g, u16 c, int scale)
{
    draw_glyph(x + scale, y + scale, g, C_SHADOW, scale);
    draw_glyph(x, y, g, c, scale);
}

static int line_width(const u16 *s, int scale)
{
    int w = 0;
    for (; *s != TXT_END && *s != TXT_NL; s++)
        if (*s < TXT_EMPH_OFF) w += glyph_adv[*s] * scale;
    return w;
}

/* A line written as "-장소-" is a location caption: centred and gold. */
static int is_caption(const u16 *line)
{
    if (line[0] != GLYPH_DASH) return 0;
    const u16 *e = line;
    while (e[1] != TXT_END && e[1] != TXT_NL) e++;
    return e != line && *e == GLYPH_DASH;
}

static int text_width(int id, int scale)
{
    const u16 *s = txt(id);
    int best = 0;
    for (;;) {
        int w = line_width(s, scale);
        if (w > best) best = w;
        while (*s != TXT_END && *s != TXT_NL) s++;
        if (*s == TXT_END) return best;
        s++;
    }
}

/* Draws text; centred = each line centred around x. */
static void draw_text_ex(int x, int y, int id, u16 c, int scale, int centred)
{
    const u16 *s = txt(id);
    int caption = !centred && is_caption(s);
    int cx = centred ? x - line_width(s, scale) / 2 : caption ? (SCREEN_W - line_width(s, scale)) / 2 : x, emph = 0;
    for (; *s != TXT_END; s++) {
        if (*s == TXT_EMPH_ON || *s == TXT_EMPH_OFF) {
            emph = *s == TXT_EMPH_ON;
            continue;
        }
        if (*s == TXT_NL) {
            y += LINE_H * scale;
            caption = !centred && is_caption(s + 1);
            cx = centred ? x - line_width(s + 1, scale) / 2 : caption ? (SCREEN_W - line_width(s + 1, scale)) / 2 : x;
            continue;
        }
        glyph_shadowed(cx, y, *s, caption ? C_GOLD : emph ? C_EMPH : c, scale);
        cx += glyph_adv[*s] * scale;
    }
}

static void draw_text(int x, int y, int id, u16 c) { draw_text_ex(x, y, id, c, 1, 0); }

/* Typewriter effect; A or B finishes the page instantly. */
static void ambient_tick(void);

static void type_text(int x, int y, int id, u16 c)
{
    const u16 *s = txt(id);
    int caption = is_caption(s);
    int cx = caption ? (SCREEN_W - line_width(s, 1)) / 2 : x, instant = 0, n = 0, emph = 0;
    for (; *s != TXT_END; s++) {
        if (*s == TXT_EMPH_ON || *s == TXT_EMPH_OFF) {
            emph = *s == TXT_EMPH_ON;
            continue;
        }
        if (*s == TXT_NL) {
            y += LINE_H;
            caption = is_caption(s + 1);
            cx = caption ? (SCREEN_W - line_width(s + 1, 1)) / 2 : x;
            continue;
        }
        glyph_shadowed(cx, y, *s, caption ? C_GOLD : emph ? C_EMPH : c, 1);
        cx += glyph_adv[*s];
        if (!instant) { /* about 40 characters a second: 1 and 2 frames in turn */
            if ((++n & 1) == 0) plat_sfx(SFX_BLIP);
            for (int k = 0; k < 1 + (n & 1) && !instant; k++) {
                ambient_tick();
                frame();
                if (keys_new & (KEY_A | KEY_B)) instant = 1;
            }
        }
    }
}

/* ------------------------------------------------------------------ scene + text box */

static const u8 heart_bits[7] = {0x36, 0x7F, 0x7F, 0x7F, 0x3E, 0x1C, 0x08};

static void draw_heart(int x, int y, u16 c)
{
    for (int r = 0; r < 7; r++)
        for (int b = 0; b < 7; b++)
            if (heart_bits[r] & (0x40 >> b)) {
                px(x + b + 1, y + r + 1, C_SHADOW);
                px(x + b, y + r, c);
            }
    mark(y, y + 9);
}

/* Hearts are shown only while a mistake can cost one (deductions, presenting, cross-examinations,
 * the court record) and when one is lost. heart_y drops them below a title bar. */
static int hearts_shown, hearts_depth, heart_y = 3;
static int run_depth; /* script call depth: 1 = the main script, deeper = a called label */

static void draw_hearts(void)
{
    if (!in_game || !max_lives || !hearts_shown) return;
    for (int i = 0; i < max_lives; i++)
        draw_heart(SCREEN_W - 4 - (max_lives - i) * 10, heart_y, i < lives ? C_RED : RGB(8, 6, 8));
}

/* ---- looping background animation (snow, the view past the train windows) ----
 * anim_data holds per scene ANIM_FRAMES diffs; entry t turns frame t-1 into frame t. */
static int anim_scene = NONE, anim_on, portrait_dx, portrait_dither;
static int breath_t;

/* Writes diff t into the back buffer. Where the portrait covers a changed pixel, the
 * portrait pixel wins, so the character never needs a full redraw. */
/* one animation step written to a plain background buffer (no characters over it) */
FAST static void anim_apply_plain(u16 *dst, int t)
{
    const u16 *d = anim_data + anim_ofs[cur_scene * ANIM_FRAMES + t];
    int n = d[2];
    d += 3;
    while (n--) {
        u16 *o = dst + d[0];
        int len = d[1];
        d += 2;
        for (int i = 0; i < len; i++) o[i] = d[i];
        d += len;
    }
}

FAST static void anim_apply(int t, int *y0, int *y1)
{
    const u16 *d = anim_data + anim_ofs[cur_scene * ANIM_FRAMES + t];
    *y0 = d[0];
    *y1 = d[1];
    int n = d[2];
    d += 3;
    const u16 *por = (cur_portrait != NONE && !cut_mode) ? portrait_img[cur_portrait] : 0;
    int px0 = PORTRAIT_X + portrait_dx;
    while (n--) {
        int ofs = d[0], len = d[1];
        u16 *dst = fb + ofs;
        d += 2;
        int y = ofs / SCREEN_W, x = ofs - y * SCREEN_W;
        int sy = y - PORTRAIT_Y + breath_lift;
        const u16 *prow = (por && (unsigned)sy < PORTRAIT_H) ? por + sy * PORTRAIT_W : 0;
        for (int i = 0; i < len; i++) {
            int pxi = x + i - px0;
            u16 c = d[i];
            if (prow && (unsigned)pxi < PORTRAIT_W && prow[pxi] != TRANSPARENT && !(portrait_dither && ((x + i) ^ y) & 1)) {
                u16 p = prow[pxi];
                c = (p & 0x8000) ? (u16)(((c >> 1) & 0x3DEF) + ((p >> 1) & 0x3DEF)) : p; /* soft edge: 50% */
            }
            dst[i] = c;
        }
        d += len;
    }
}

static void blink_draw(int y0, int y1);

/* @inset: a small framed picture over the middle of the screen (gold rim, dark inner line). */
static int cur_inset = NONE;
#define INSET_X ((SCREEN_W - INSET_W) / 2)
#define INSET_Y 14
static void draw_inset(int y0, int y1)
{
    if (cur_inset == NONE || cut_mode) return;
    int top = INSET_Y - 2, bot = INSET_Y + INSET_H + 2;
    if (y0 < top) y0 = top;
    if (y1 > bot) y1 = bot;
    for (int j = y0; j < y1; j++) {
        u16 *row = fb + j * SCREEN_W + INSET_X - 2;
        if (j == top || j == bot - 1) {
            for (int i = 0; i < INSET_W + 4; i++) row[i] = C_BORDER;
            continue;
        }
        row[0] = row[INSET_W + 3] = C_BORDER;
        if (j == top + 1 || j == bot - 2) {
            for (int i = 1; i < INSET_W + 3; i++) row[i] = RGB(2, 2, 4);
            continue;
        }
        row[1] = row[INSET_W + 2] = RGB(2, 2, 4);
        const u16 *src = inset_img[cur_inset] + (j - INSET_Y) * INSET_W;
        for (int i = 0; i < INSET_W; i++) row[i + 2] = src[i];
    }
    if (y1 > y0) mark(y0, y1);
}

FAST static void blit_portrait_rows(int y0, int y1)
{
    if (cur_portrait == NONE || cut_mode) {
        draw_inset(y0, y1);
        return;
    }
    const u16 *src = portrait_img[cur_portrait];
    int x0 = PORTRAIT_X + portrait_dx;
    for (int j = y0; j < y1; j++) {
        int sj = j - PORTRAIT_Y + breath_lift; /* breathing shifts the whole picture */
        if ((unsigned)sj >= PORTRAIT_H) continue;
        const u16 *row = src + sj * PORTRAIT_W;
        u16 *dst = fb + j * SCREEN_W;
        for (int i = 0; i < PORTRAIT_W; i++) {
            int x = x0 + i;
            u16 p = row[i];
            if (p == TRANSPARENT || (unsigned)x >= SCREEN_W || (portrait_dither && ((x ^ j) & 1))) continue;
            dst[x] = (p & 0x8000) ? (u16)(((dst[x] >> 1) & 0x3DEF) + ((p >> 1) & 0x3DEF)) : p; /* soft edge: 50% */
        }
    }
    if (blink_shut && blink_img[cur_portrait]) blink_draw(y0, y1);
    draw_inset(y0, y1);
    mark(y0, y1);
}

/* Draws the eye rectangle (closed eyes while blink_shut, else the open portrait) over screen
 * rows y0..y1. Soft pixels are blended with the scene, so the patch can be redrawn freely. */
static void blink_draw(int y0, int y1)
{
    const u8 *r = blink_rect + cur_portrait * 4;
    const u16 *por = portrait_img[cur_portrait], *shut = blink_img[cur_portrait], *bg = scene_img[cur_scene];
    int x0 = PORTRAIT_X + portrait_dx + r[0];
    for (int k = 0; k < r[3]; k++) {
        int sy = r[1] + k, j = PORTRAIT_Y + sy - breath_lift;
        if (j < y0 || j >= y1) continue;
        const u16 *src = blink_shut ? shut + k * r[2] : por + sy * PORTRAIT_W + r[0];
        for (int i = 0; i < r[2]; i++) {
            int x = x0 + i;
            u16 p = src[i];
            if (p == TRANSPARENT || (unsigned)x >= SCREEN_W) continue;
            fb[j * SCREEN_W + x] = (p & 0x8000) ? (u16)(((bg[j * SCREEN_W + x] >> 1) & 0x3DEF) + ((p >> 1) & 0x3DEF)) : p;
        }
    }
}

static void draw_scene(void)
{
    if (anim_scene != cur_scene) {
        anim_scene = cur_scene;
        anim_t = 0;
        anim_bg_ok = 0;
    }
    if (anim_count[cur_scene] && !cut_mode) {
        if (!anim_bg_ok) { /* rebuild the background at the current animation step */
            plat_copy32(scratch, scene_img[cur_scene], SCREEN_W * SCREEN_H / 2);
            for (int t = 1; t <= anim_t; t++) anim_apply_plain(scratch, t);
            anim_bg_ok = 1;
        }
        plat_copy32(fb, scratch, SCREEN_W * SCREEN_H / 2);
    } else {
        plat_copy32(fb, scene_img[cur_scene], SCREEN_W * SCREEN_H / 2);
    }
    mark(0, SCREEN_H);
    blit_portrait_rows(0, SCREEN_H);
    draw_hearts();
}

/* Scene pixel with the portrait (row offset `off`) over it, as blit_portrait_rows draws it. */
static inline u16 composed(int x, int j, int off)
{
    u16 c = scene_img[cur_scene][j * SCREEN_W + x];
    int sj = j - PORTRAIT_Y + off, i = x - PORTRAIT_X - portrait_dx;
    if ((unsigned)sj >= PORTRAIT_H || (unsigned)i >= PORTRAIT_W) return c;
    u16 p = portrait_img[cur_portrait][sj * PORTRAIT_W + i];
    if (p == TRANSPARENT) return c;
    return (p & 0x8000) ? (u16)(((c >> 1) & 0x3DEF) + ((p >> 1) & 0x3DEF)) : p;
}

/* Moves the portrait under the translucent dialogue box from offset `from` to the current one.
 * A pixel still equal to the old shaded picture is replaced; anything else (text, the arrow)
 * was drawn on top and is kept. */
FAST static void breathe_box(int from)
{
    u16 half = (C_BOX >> 1) & 0x3DEF;
    int x0 = PORTRAIT_X + portrait_dx;
    for (int j = BOX_Y + 1; j < SCREEN_H; j++)
        for (int x = x0 < 0 ? 0 : x0; x < x0 + PORTRAIT_W && x < SCREEN_W; x++) {
            u16 *d = &fb[j * SCREEN_W + x];
            u16 was = ((composed(x, j, from) >> 1) & 0x3DEF) + half;
            if (*d == was) *d = ((composed(x, j, breath_lift) >> 1) & 0x3DEF) + half;
        }
}

/* Idle breathing: a 96-frame cycle; the whole picture rests 2 px low and rises 1, 2 px. */
static int box_spk = NONE;
static void draw_name_tag(int spk);

static void breathe_tick(void)
{
    /* over an animated background (the falling petals) the bust keeps still: redrawing it would
     * need the whole animation replayed */
    if (cur_portrait == NONE || cut_mode || !portrait_breathe[cur_portrait] || portrait_dx || anim_count[cur_scene])
        return;
    breath_t = (breath_t + 1) % 96;
    int lift = breath_t < 40 ? -2 : breath_t < 48 ? -1 : breath_t < 88 ? 0 : -1;
    if (lift == breath_lift) return;
    int from = breath_lift;
    breath_lift = lift;
    plat_copy32(fb, scene_img[cur_scene], BOX_Y * SCREEN_W / 2);
    blit_portrait_rows(0, BOX_Y);
    draw_hearts();
    draw_name_tag(box_spk);
    breathe_box(from);
    mark(0, SCREEN_H);
}

/* Idle blink: eyes open 2-4 s, closed for 7 frames (~120 ms). */
static int blink_wait = 150;
static void blink_tick(void)
{
    if (cur_portrait == NONE || cut_mode || !blink_img[cur_portrait] || portrait_dx || cur_inset != NONE || --blink_wait > 0) return;
    blink_shut ^= 1;
    blink_wait = blink_shut ? 7 : 120 + (int)((frame_count * 37u) % 120);
    const u8 *r = blink_rect + cur_portrait * 4;
    int y0 = PORTRAIT_Y + r[1] - 2, y1 = PORTRAIT_Y + r[1] + r[3];
    blink_draw(y0 < 0 ? 0 : y0, y1);
    mark(y0 < 0 ? 0 : y0, y1);
}

/* Called every frame while a dialogue page is on screen: breathing, blinking, background loop. */
static void ambient_tick(void)
{
    if (!anim_on) return;
    breathe_tick();
    blink_tick();
    if (cut_mode || !anim_count[cur_scene] || anim_scene != cur_scene || (frame_count & 1)) return;
    int y0, y1;
    anim_t = (anim_t + 1) % ANIM_FRAMES;
    anim_apply(anim_t, &y0, &y1);
    if (anim_bg_ok) anim_apply_plain(scratch, anim_t);
    if (y1 <= y0) return;
    if (y0 < 12) draw_hearts();
    mark(y0, y1);
}

/* An @inset picture is shown for one line, then folds back into the middle. */
static void inset_close(void)
{
    int shown = cur_inset, y0 = INSET_Y - 2, y1 = INSET_Y + INSET_H + 2, cy = INSET_Y + INSET_H / 2;
    for (int h = INSET_H / 2 - 10; ; h -= 14) {
        cur_inset = NONE;
        plat_copy32(fb + y0 * SCREEN_W, scene_img[cur_scene] + y0 * SCREEN_W, (y1 - y0) * SCREEN_W / 2);
        blit_portrait_rows(y0, y1);
        draw_hearts();
        draw_name_tag(box_spk);
        mark(y0, y1);
        if (h <= 0) break;
        cur_inset = shown;
        draw_inset(cy - h, cy + h);
        frame();
    }
    frame();
    save_screen(); /* popups after this line restore the screen without the picture */
}

static void set_speaker_portrait(int spk)
{
    cur_portrait = (spk != NONE) ? char_portrait[spk] : NONE;
}

static void draw_name_tag(int spk)
{
    if (spk != NONE) {
        int w = disp_width(char_name[spk]) + 12;
        fill(4, BOX_Y - 13, w, 13, char_color[spk]);
        frame_rect(4, BOX_Y - 13, w, 13, C_BORDER);
        draw_disp(char_name[spk], 4 + w / 2, BOX_Y - 6);
    }
}

static void draw_box(int spk)
{
    shade(0, BOX_Y, SCREEN_W, SCREEN_H - BOX_Y, C_BOX);
    fill(0, BOX_Y, SCREEN_W, 1, C_BORDER);
    box_spk = spk;
    draw_name_tag(spk);
}

static void draw_arrow(int x, int y, u16 c)
{
    for (int r = 0; r < 4; r++) fill(x + r, y + r, 7 - 2 * r, 1, c);
}

static void draw_cursor(int x, int y, u16 c)
{
    for (int r = 0; r < 4; r++) fill(x + r, y + r, 1, 7 - 2 * r, c);
}

static int record(int present, int question);

/* Countdown for the next @ask / @present (set by @timer). 0 = no time limit. */
static int timer_left, timer_total;
#define TIMEOUT (-2)

static void draw_timer(void)
{
    if (!timer_total) return;
    int w = (SCREEN_W - 8) * timer_left / timer_total;
    u16 c = timer_left * 4 < timer_total ? ((frame_count & 8) ? C_RED : C_WHITE) : C_GOLD;
    fill(4, 0, SCREEN_W - 8, 3, RGB(6, 2, 2));
    fill(4, 0, w, 3, c);
}

/* Called once per frame while a timed question is open; returns 1 when time runs out. */
static int timer_tick(void)
{
    if (!timer_total) return 0;
    if (timer_left > 0) timer_left--;
    draw_timer();
    if (timer_left == 0) {
        plat_debug_event("timeout", 0);
        return 1;
    }
    if (timer_left % 60 == 0 && timer_left <= 300) plat_sfx(SFX_MOVE); /* last five seconds tick */
    return 0;
}

static void restore_rect(int x, int y, int w, int h)
{
    for (int j = y; j < y + h; j++)
        for (int i = x; i < x + w; i++) fb[j * SCREEN_W + i] = snapshot[j * SCREEN_W + i];
    mark(y, y + h);
}

/* Wait for A on a finished page (snapshot must hold the page). START opens the court record. */
static void save_slot(int slot, u16 pc);
static void save_prompt(void);
static u16 top_pc;

static void wait_advance(void)
{
    plat_debug_event("page", 0);
    int dbg_save = plat_debug_choice(DBG_SAVE, 0); /* test harness: save here */
    if (dbg_save > 0) save_slot(dbg_save, top_pc);
    for (;;) {
        restore_rect(224, 154, 8, 5);
        if ((frame_count >> 4) & 1) draw_arrow(225, 154, C_WHITE);
        ambient_tick();
        frame();
        if (keys_new & KEY_A) return;
        if (keys_new & KEY_START) record(0, NONE);
    }
}

static void got_item(int title, int name, const u16 *img, int w, int h);
static void popup_window(int x, int y, int w, int h);
static void wait_a(int min_frames);

/* Adds a character to the court record (after they first talk, or via @meet). */
static void meet(int c, int title)
{
    prof_flags |= 1u << c;
    if (char_portrait[c] == NONE) return;
    /* a small card in the middle: "인물 파일 추가!/갱신!" on top, the face, the name under it */
    int name = char_name[c];
    int tw = text_width(name, 1), hw = text_width(title, 1);
    int w = (tw > THUMB_W ? tw : THUMB_W), h = THUMB_H + 43;
    if (hw > w) w = hw;
    w += 16;
    int x = (SCREEN_W - w) / 2, y = (SCREEN_H - h) / 2;
    save_screen();
    plat_sfx(SFX_GET);
    popup_window(x, y, w, h);
    draw_text(SCREEN_W / 2 - hw / 2, y + 5, title, C_GOLD);
    fill(SCREEN_W / 2 - THUMB_W / 2, y + 22, THUMB_W, THUMB_H, RGB(1, 1, 3));
    blit_keyed(SCREEN_W / 2 - THUMB_W / 2, y + 22, THUMB_W, THUMB_H, portrait_thumb[char_portrait[c]]);
    draw_text(SCREEN_W / 2 - tw / 2, y + THUMB_H + 25, name, C_WHITE);
    plat_debug_event("get", 0);
    wait_a(10);
    plat_sfx(SFX_OK);
    restore_screen();
}

static int last_spk = NONE;

static void say(int spk, int t, int portrait)
{
    /* a new speaker slides in from the right, fading in (dithered) over the first steps */
    int slide = portrait != NONE && spk != last_spk && !cut_mode &&
                !anim_count[cur_scene]; /* over falling petals the speaker just appears: they keep falling */
    last_spk = spk;
    if (portrait != cur_portrait) {
        breath_t = blink_shut = 0, blink_wait = 90;
        breath_lift = portrait != NONE && portrait_breathe[portrait] ? -2 : 0;
    }
    cur_portrait = portrait;
    if (slide) {
        static const signed char steps[] = {36, 20, 10, 4, 1};
        cur_portrait = NONE;
        draw_scene();              /* background (and its animation phase) once */
        save_screen();
        cur_portrait = portrait;
        for (unsigned i = 0; i < sizeof steps; i++) {
            portrait_dx = steps[i];
            portrait_dither = i < 2;
            restore_screen();
            blit_portrait_rows(0, SCREEN_H);
            draw_box(spk);
            frame();
        }
        portrait_dx = portrait_dither = 0;
    }
    draw_scene();
    draw_box(spk);
    anim_on = 1;
    type_text(TEXT_X, TEXT_Y, t, C_WHITE);
    save_screen();
    wait_advance();
    anim_on = 0;
    if (cur_inset != NONE) inset_close();
    if (spk != NONE && prof_text[spk] != NONE && !(prof_flags & (1u << spk))) meet(spk, UI_MEET);
}

/* ------------------------------------------------------------------ popups & effects */

static void popup_window(int x, int y, int w, int h)
{
    fill(x, y, w, h, C_PANEL);
    frame_rect(x, y, w, h, C_BORDER);
    frame_rect(x + 2, y + 2, w - 4, h - 4, RGB(12, 9, 4));
}

static void wait_a(int min_frames)
{
    for (int i = 0; ; i++) {
        frame();
        if (i >= min_frames && (keys_new & (KEY_A | KEY_START))) return;
    }
}

static void got_item(int title, int name, const u16 *img, int w, int h)
{
    save_screen();
    plat_sfx(SFX_GET);
    int wy = 16, wh = h + 16; /* names up to GOT_NAME_W px fit (checked by build_assets.py) */
    popup_window(10, wy, 220, wh);
    fill(18, wy + 8, w, h, RGB(1, 1, 3));
    blit_keyed(18, wy + 8, w, h, img);
    draw_text(92, wy + 14, title, C_GOLD);
    draw_text(92, wy + 34, name, C_WHITE);
    plat_debug_event("get", 0);
    wait_a(10);
    plat_sfx(SFX_OK);
    restore_screen();
}

static void flash(u16 c, int frames)
{
    save_screen();
    fill(0, 0, SCREEN_W, SCREEN_H, c);
    wait_frames(frames);
    restore_screen();
    frame();
}

static void shake(int frames, int amp)
{
    for (int i = 0; i < frames; i++) {
        int a = amp * (frames - i) / frames;
        plat_offset((i & 1) ? a : -a, (i & 2) ? a / 2 : -a / 2);
        frame();
    }
    plat_offset(0, 0);
}

static void do_fx(int kind)
{
    switch (kind) {
    case FX_FLASH:
        flash(C_WHITE, 3);
        break;
    case FX_SHOCK:
        plat_sfx(SFX_SHOCK);
        flash(C_WHITE, 2);
        shake(16, 4);
        break;
    case FX_SHAKE:
        shake(20, 3);
        break;
    case FX_RED:
        plat_sfx(SFX_SHOCK);
        save_screen();
        for (int i = 0; i < 3; i++) shade(0, 0, SCREEN_W, SCREEN_H, RGB(28, 0, 2));
        shake(24, 4);
        wait_frames(20);
        restore_screen();
        frame();
        break;
    case FX_DUN: /* a dramatic entrance: "du-dun!" like the chapter eyecatch */
        plat_sfx(SFX_DUN);
        wait_frames(7);
        plat_sfx(SFX_DUN);
        wait_frames(10);
        break;
    case FX_BOOM: /* the explosion on the train roof: blinding flash, then a long rumble */
        plat_sfx(SFX_SHOCK);
        flash(C_WHITE, 4);
        shake(12, 8);
        plat_sfx(SFX_SHOCK);
        flash(C_WHITE, 2);
        shake(36, 5);
        break;
    }
}

/* "잠깐!", "그건 모순이야!", "수수께끼는 모두 풀렸어!": the scene dims and the words slam in
 * one after another (disp_cuts: where each word ends), each with a boom and a jolt. */
static void do_shout(int spk, int t, int portrait)
{
    /* @shout (a character's declaration): only the background dims, the speaker stays lit and
     * the words slam in low, under the face. Objections dim everything, words in the middle. */
    int own = spk != NONE, cy = own ? 136 : 70;
    cur_portrait = own ? NONE : portrait;
    draw_scene();
    shade(0, 0, SCREEN_W, SCREEN_H, 0);
    shade(0, 0, SCREEN_W, SCREEN_H, 0);
    cur_portrait = portrait;
    if (own) blit_portrait_rows(0, SCREEN_H);
    frame();
    int d = disp_of_text[t], n = 0;
    const u8 *cut = d == NONE ? 0 : disp_cuts + d * 3;
    while (cut && n < 3 && cut[n]) n++;
    for (int k = 0; k <= n; k++) {
        int last = k == n;
        plat_sfx(last ? SFX_OBJECTION : SFX_SHOCK);
        if (last && k == 0) flash(C_WHITE, 2);
        draw_disp_part(t, SCREEN_W / 2, cy, last ? -1 : cut[k]);
        shake(last ? 18 : 8, last ? 6 : 4);
        if (!last) wait_frames(4);
    }
    plat_debug_event("shout", 0);
    for (int i = 0; i < 70; i++) {
        frame();
        if (i > 12 && (keys_new & KEY_A)) break;
    }
}

static int lose_life(void)
{
    plat_sfx(SFX_WRONG);
    if (lives > 0) lives--;
    if (!hearts_shown) hearts_depth = run_depth; /* hidden again after this part of the script */
    hearts_shown = 1;
    save_screen();
    for (int i = 0; i < 3; i++) shade(0, 0, SCREEN_W, SCREEN_H, RGB(20, 0, 0));
    draw_disp(UI_WRONG, SCREEN_W / 2, 72);
    draw_hearts();
    for (int i = 0; i < 24; i++) {
        /* blink the heart that was lost */
        draw_heart(SCREEN_W - 4 - (max_lives - lives) * 10, heart_y, (i & 4) ? C_RED : RGB(8, 6, 8));
        plat_offset((i & 1) ? 3 : -3, 0);
        frame();
    }
    plat_offset(0, 0);
    wait_frames(20);
    if (lives == 1) { /* last heart: make it hurt */
        fill(0, BOX_Y - 24, SCREEN_W, 22, RGB(10, 0, 0));
        draw_text_ex(SCREEN_W / 2, BOX_Y - 19, UI_DANGER, C_RED, 1, 1);
        for (int i = 0; i < 70; i++) {
            if ((i % 30) == 0 || (i % 30) == 8) plat_sfx(SFX_SHOCK); /* heartbeat */
            frame();
        }
    }
    restore_screen();
    draw_hearts();
    frame();
    return lives == 0;
}

/* Location caption ("-장소-" in the script): a band across the middle of the screen, typed out in gold. */
static void place_caption(int t)
{
    save_screen();
    int w = disp_width(t), x = (SCREEN_W - w) / 2, cy = 66;
    shade(0, cy - 14, SCREEN_W, 28, 0);
    shade(0, cy - 14, SCREEN_W, 28, 0);
    fill(0, cy - 15, SCREEN_W, 1, C_BORDER);
    fill(0, cy + 14, SCREEN_W, 1, C_BORDER);
    fill(x - 34, cy, 24, 1, C_BORDER);
    fill(x + w + 10, cy, 24, 1, C_BORDER);
    plat_sfx(SFX_GET);
    for (int v = 0; v < w + 6; v += 6) {   /* wipe the name in from the left */
        draw_disp_part(t, SCREEN_W / 2, cy, v);
        frame();
        if (keys_new & (KEY_A | KEY_B)) break;
    }
    draw_disp(t, SCREEN_W / 2, cy);
    plat_debug_event("place", 0);
    for (int i = 0; i < 100; i++) {
        frame();
        if (i > 12 && (keys_new & (KEY_A | KEY_START))) break;
    }
    restore_screen();
    frame();
}

/* Character introduction card (the anime's name captions): the character's portrait, then a band
 * slides in with the epithet above the name. */
static void intro_card(int spk, int portrait, int epi, int name)
{
    cur_portrait = portrait;
    draw_scene();
    save_screen();
    plat_sfx(SFX_GET);
    for (int f = 0; f <= 14; f++) {
        int off = (14 - f) * 18;
        restore_screen();
        shade(0, 112, SCREEN_W, 48, 0);
        shade(0, 112, SCREEN_W, 48, 0);
        fill(0, 111, SCREEN_W, 1, C_BORDER);
        fill(0, 159, SCREEN_W, 1, C_BORDER);
        draw_disp(epi, SCREEN_W / 2 + off, 123);
        draw_disp(name, SCREEN_W / 2 - off, 143);
        frame();
    }
    plat_debug_event("intro", spk);
    for (int i = 0; i < 150; i++) {
        frame();
        if (i > 16 && (keys_new & (KEY_A | KEY_START))) break;
    }
    if (spk != NONE && !(prof_flags & (1u << spk))) meet(spk, UI_MEET);
    draw_scene();
    frame();
}

/* Chapter-change eyecatch: Kindaichi's glowing silhouette spins, then lands with "du-dun!".
 * The spin is done by the display hardware (BG2 affine horizontal scale), so it is smooth at 60fps. */
static void eyecatch(void)
{
#if HAVE_EYECATCH
    static const s16 cosq[65] = {256, 256, 256, 255, 255, 254, 253, 252, 251, 250, 248, 247, 245, 243, 241, 239, 237, 234, 231, 229, 226, 223, 220, 216, 213, 209, 206, 202, 198, 194, 190, 185, 181, 177, 172, 167, 162, 157, 152, 147, 142, 137, 132, 126, 121, 115, 109, 104, 98, 92, 86, 80, 74, 68, 62, 56, 50, 44, 38, 31, 25, 19, 13, 6, 0}; /* cos over a quarter turn, 1/256 turn steps, x256 */
    fade_out();
    plat_copy32(fb, eyecatch_img, SCREEN_W * SCREEN_H / 2);
    mark(0, SCREEN_H);
    plat_hscale(32767);   /* edge-on: nothing visible yet */
    frame();
    plat_fade(0);
    plat_debug_event("eyecatch", 0);
    for (int a = 64; a <= 256; a += 8) {   /* 1/4 turn (edge-on) .. full turn, 25 frames */
        int q = a & 255, c;
        if (q <= 64) c = cosq[q];
        else if (q <= 128) c = -cosq[128 - q];
        else if (q <= 192) c = -cosq[q - 128];
        else c = cosq[256 - q];
        plat_hscale(c > 1 || c < -1 ? 65536 / c : 32767);
        frame();
    }
    plat_hscale(256);
    plat_sfx(SFX_DUN);          /* du- */
    wait_frames(7);
    plat_sfx(SFX_DUN);          /* -dun! */
    flash(C_WHITE, 2);
    shake(8, 2);
    wait_frames(45);
#endif
}

static void chapter_save_ask(void);

static void chapter_card(int t, int card)
{
    eyecatch();
    fade_out();
    int h = disp_height(t);
    if (card != NONE) {
        /* illustrated card: the picture stays visible, the title sits on a dark band near the bottom */
        int y0 = 138 - h - 8, y1 = 140;
        plat_copy32(fb, scene_img[card], SCREEN_W * SCREEN_H / 2);
        mark(0, SCREEN_H);
        shade(0, y0, SCREEN_W, SCREEN_H - y0, 0);
        shade(0, y0, SCREEN_W, SCREEN_H - y0, 0);
        fill(0, y0 - 3, SCREEN_W, 1, C_RED);
        fill(0, y0 - 1, SCREEN_W, 1, C_BORDER);
        fill(0, y1, SCREEN_W, 1, C_BORDER);
        draw_disp(t, SCREEN_W / 2, y0 + 4 + h / 2);
    } else {
        fill(0, 0, SCREEN_W, SCREEN_H, 0);
        fill(40, 76 - h / 2 - 8, 160, 1, C_BORDER);
        fill(40, 76 + h / 2 + 8, 160, 1, C_BORDER);
        draw_disp(t, SCREEN_W / 2, 76);
    }
    fade_in();
    plat_debug_event("chapter", 0);
    for (int i = 0; i < 240; i++) {
        frame();
        if (i > 60 && (keys_new & (KEY_A | KEY_START))) break;
    }
    chapter_save_ask();
    fade_out();
    cur_scene = SCENE_BLACK;
    cut_mode = 0;
    cur_portrait = NONE;
    draw_scene();
    frame();
    plat_fade(0);
}

/* ------------------------------------------------------------------ menus */

/* texts: option text ids. extra: optional trailing option (UI text) or NONE.
 * dbg_id/traps only feed the host test harness (which option set this is, which ones to avoid). */
static int choose_start; /* cursor position the next choose() opens at */

static int choose(int spk, int q, const u16 *texts, int n, u32 greyed, int extra, int dbg_kind, int dbg_id,
                  u32 traps)
{
    int total = n + (extra != NONE);
    int sel = choose_start < total ? choose_start : 0, w = 120;
    choose_start = 0;
    /* six or more options (the talk menus) are laid out in two columns */
    int cols = total > 5 ? 2 : 1, rows = (total + cols - 1) / cols, colw = 0;
    for (int i = 0; i < total; i++) {
        int tw = text_width(i < n ? texts[i] : extra, 1) + 26;
        if (tw > colw) colw = tw;
    }
    w = cols * colw + 8;
    if (w < 120) w = colw = 120;
    if (w > SCREEN_W - 8) {
        w = SCREEN_W - 8;
        colw = (w - 8) / cols;
    }
    int row = 14;
    int h = rows * row + 8;
    int x = (SCREEN_W - w) / 2, y = (BOX_Y - 14 - h) / 2 + 4;
    if (y < 12) y = 12;
    if (y + h > BOX_Y - 2) y = BOX_Y - 2 - h;
    if (y < 2) y = 2;

    set_speaker_portrait(spk);
    draw_scene();
    draw_box(spk);
    draw_text(TEXT_X, TEXT_Y, q, C_WHITE);
    popup_window(x, y, w, h);
    save_screen();

    for (int redraw = 1;;) {
        if (redraw) {
            restore_screen();
            for (int i = 0; i < total; i++) {
                int oy = y + 4 + (i % rows) * row, ox = x + 4 + (i / rows) * colw;
                int t = i < n ? texts[i] : extra;
                if (i == sel) {
                    fill(ox, oy, cols > 1 ? colw : w - 8, row, C_HILITE);
                    draw_cursor(ox + 4, oy + 3, C_GOLD);
                }
                u16 c = (i < n && (greyed & (1u << i))) ? C_GREY : (i == sel ? C_GOLD : C_WHITE);
                draw_text(ox + 14, oy, t, c);
            }
            redraw = 0;
        }
        plat_debug_event("menu", sel);
        int d = dbg_id >= 0 ? plat_debug_menu(dbg_id, total, traps) : plat_debug_choice(dbg_kind, total);
        if (d >= 0 && d < total) {
            plat_sfx(SFX_OK);
            return d;
        }
        frame();
        if (timer_tick()) return TIMEOUT;
        if (keys_new & KEY_UP) {
            sel = (sel + total - 1) % total;
            redraw = 1;
            plat_sfx(SFX_MOVE);
        } else if (keys_new & KEY_DOWN) {
            sel = (sel + 1) % total;
            redraw = 1;
            plat_sfx(SFX_MOVE);
        } else if ((keys_new & (KEY_LEFT | KEY_RIGHT)) && cols > 1) {
            int r = sel % rows, c = sel / rows ^ 1; /* the same row in the other column */
            if (c * rows + r < total) sel = c * rows + r;
            else sel = total - 1;
            redraw = 1;
            plat_sfx(SFX_MOVE);
        } else if (keys_new & KEY_A) {
            plat_sfx(SFX_OK);
            return sel;
        } else if ((keys_new & KEY_B) && extra == UI_BACK) {
            plat_sfx(SFX_MOVE);   /* B = "돌아간다" in sub-menus */
            return n;
        } else if (keys_new & KEY_START) {
            record(0, NONE);
        }
    }
}

/* opts: pairs (text id, label), as used by @ask / @accuse. */
static int menu(int spk, int q, const u16 *opts, int n, u32 greyed, int extra, int dbg_kind)
{
    u16 texts[8];
    for (int i = 0; i < n && i < 8; i++) texts[i] = opts[i * 2];
    return choose(spk, q, texts, n, greyed, extra, dbg_kind, -1, 0);
}

/* ------------------------------------------------------------------ court record (증거물 / 인물) */

static int collect(int tab, u8 *list)
{
    int n = 0;
    if (tab == 0) {
        for (int i = 0; i < EVIDENCE_COUNT; i++)
            if (ev_flags & EV_BIT(i)) list[n++] = i;
    } else {
        for (int i = 0; i < CHAR_COUNT; i++)
            if ((prof_flags & (1u << i)) && prof_text[i] != NONE) list[n++] = i;
    }
    return n;
}

static void draw_tab(int x, int label, int active)
{
    int w = text_width(label, 1) + 14;
    fill(x, 3, w, 15, active ? C_BORDER : RGB(6, 6, 10));
    if (active) {
        /* black on the gold tab, without the drop shadow (a black shadow would smear it) */
        int cx = x + 7;
        for (const u16 *g = txt(label); *g != TXT_END && *g != TXT_NL; g++) {
            draw_glyph(cx, 4, *g, C_SHADOW, 1);
            cx += glyph_adv[*g];
        }
    } else {
        draw_text(x + 7, 4, label, C_GREY);
    }
}

/* present = 1: pick evidence to present (returns evidence id). Otherwise browse; returns -1.
 * The caller's screen is saved and restored here. */
/* the notebook's striped backdrop over a rectangle */
static void rec_bg(int x, int y, int w, int h)
{
    for (int j = y; j < y + h; j++) fill(x, j, w, 1, (j & 3) ? RGB(3, 3, 7) : RGB(4, 4, 9));
}

static int record(int present, int question)
{
    u16 *saved_screen = scratch;
    anim_bg_ok = 0; /* the notebook borrows the animated background's buffer */
    static int tab, sel[2];
    u8 list[64];
    int top = 0, result = -1;

    plat_copy32(saved_screen, fb, SCREEN_W * SCREEN_H / 2);
    plat_sfx(SFX_OK);
    if (present) tab = 0;
    /* animation: the notebook opens from the middle, the highlight bar glides and the picture
     * slides in when the selection changes; only the moving parts are redrawn meanwhile */
    int open_t = 0, bar_y = -1, pic_dx = 16, full = 1;

    for (int redraw = 1;;) {
        int n = collect(tab, list);
        if (sel[tab] >= n) sel[tab] = n ? n - 1 : 0;
        if (sel[tab] < top) top = sel[tab];
        if (sel[tab] >= top + REC_ROWS) top = sel[tab] - REC_ROWS + 1;

        if (redraw) {
          if (full) {
            rec_bg(0, 0, SCREEN_W, SCREEN_H);
            if (present) {
                fill(0, 0, SCREEN_W, 19, C_RED);
                draw_text(8, 3, question, C_WHITE);
            } else {
                fill(0, 18, SCREEN_W, 1, C_BORDER);
                draw_tab(6, UI_TAB_EVIDENCE, tab == 0);
                draw_tab(6 + text_width(UI_TAB_EVIDENCE, 1) + 18, UI_TAB_PROFILE, tab == 1);
                int shown = hearts_shown;
                hearts_shown = 1; /* the notebook always shows how many are left */
                draw_hearts();
                hearts_shown = shown;
            }
            int hint = present ? UI_PRESENT_HINT : UI_RECORD_HINT;
            draw_hint_right(hint, 146, 88);
            if (!present && in_game) draw_hint_right(UI_RECORD_SAVE, SCREEN_W - 6 - max_lives * 10, 6);

          } else {
            rec_bg(0, REC_LIST_Y, 164, REC_ROWS * 13);   /* the list */
            rec_bg(164, 18, SCREEN_W - 164, REC_DESC_Y - 18); /* the picture */
          }
            if (n == 0) {
                draw_text(12, REC_LIST_Y + 4, UI_EMPTY, C_GREY);
            }
            int target_y = REC_LIST_Y + (sel[tab] - top) * 13;
            if (bar_y < 0) bar_y = target_y;
            if (n) {
                fill(4, bar_y, 156, 13, C_HILITE);
                draw_cursor(7, bar_y + 3, C_GOLD);
            }
            for (int r = 0; r < REC_ROWS && top + r < n; r++) {
                int i = top + r, y = REC_LIST_Y + r * 13;
                int name = tab == 0 ? ev_name[list[i]] : char_name[list[i]];
                draw_text(16, y, name, i == sel[tab] ? C_GOLD : C_WHITE);
            }
            if (top > 0) draw_arrow(150, REC_LIST_Y - 2, C_GOLD);
            if (top + REC_ROWS < n) {
                for (int r = 0; r < 4; r++) fill(150 + r, REC_LIST_Y + REC_ROWS * 13 + 3 - r, 7 - 2 * r, 1, C_GOLD);
            }

            fill(0, REC_DESC_Y, SCREEN_W, SCREEN_H - REC_DESC_Y, C_BOX);
            fill(0, REC_DESC_Y, SCREEN_W, 1, C_BORDER);
            if (n) {
                int item = list[sel[tab]];
                u16 edge = pic_dx > 10 ? C_WHITE : C_BORDER; /* the frame flashes as a new picture comes in */
                if (tab == 0) {
                    fill(168, 22, ICON_SIZE + 4, ICON_SIZE + 4, edge);
                    fill(170, 24, ICON_SIZE, ICON_SIZE, RGB(1, 1, 3));
                    blit_keyed(170 + pic_dx, 24, ICON_SIZE, ICON_SIZE, icon_img + ev_icon[item] * ICON_SIZE * ICON_SIZE);
                    fill(170 + ICON_SIZE, 22, 2, ICON_SIZE + 4, edge);
                    rec_bg(172 + ICON_SIZE, 22, SCREEN_W - 172 - ICON_SIZE, ICON_SIZE + 4);
                    draw_text(8, REC_DESC_Y + 3, ev_desc[item], C_WHITE);
                } else {
                    fill(168, 18, THUMB_W + 4, THUMB_H + 2, edge);
                    fill(170, 20, THUMB_W, THUMB_H, char_color[item]);
                    if (char_portrait[item] != NONE)
                        blit_keyed(170 + pic_dx, 20, THUMB_W, THUMB_H, portrait_thumb[char_portrait[item]]);
                    fill(170 + THUMB_W, 18, 2, THUMB_H + 2, edge);
                    rec_bg(172 + THUMB_W, 18, SCREEN_W - 172 - THUMB_W, THUMB_H + 2);
                    draw_text(8, REC_DESC_Y + 3, prof_text[item], C_WHITE);
                }
            }
            redraw = 0;
            full = 0;
            /* step the animations; anything still moving redraws next frame */
            if (open_t < 5) { /* opening: only a band around the middle shows the notebook yet */
                int half = (open_t + 1) * SCREEN_H / 10;
                for (int y = 0; y < SCREEN_H; y++)
                    if (y < SCREEN_H / 2 - half || y >= SCREEN_H / 2 + half)
                        plat_copy32(fb + y * SCREEN_W, saved_screen + y * SCREEN_W, SCREEN_W / 2);
                mark(0, SCREEN_H);
                open_t++;
                redraw = full = 1;
            }
            if (bar_y != target_y) {
                int d = (target_y - bar_y) * 2 / 3;
                bar_y += d ? d : (target_y > bar_y ? 1 : -1);
                redraw = 1;
            }
            if (pic_dx > 0) {
                pic_dx = pic_dx / 2;
                redraw = 1;
            }
        }

        plat_debug_event("record", tab);
        if (present) {
            int d = plat_debug_choice(DBG_PRESENT, EVIDENCE_COUNT);
            if (d >= 0) {
                /* only evidence in the record can be presented; anything else stands for "the wrong item" */
                result = d < EVIDENCE_COUNT && (ev_flags & EV_BIT(d)) ? d : NONE;
                break;
            }
        }
        frame();
        if (present && timer_tick()) {
            result = TIMEOUT;
            break;
        }
        if (keys_new & (KEY_UP | KEY_DOWN)) {
            if (n) {
                int old_top = top;
                sel[tab] = (sel[tab] + ((keys_new & KEY_UP) ? n - 1 : 1)) % n;
                if (sel[tab] < top) top = sel[tab];
                if (sel[tab] >= top + REC_ROWS) top = sel[tab] - REC_ROWS + 1;
                if (top != old_top) bar_y = -1; /* the list scrolled: the bar jumps */
                pic_dx = 16;
                plat_sfx(SFX_MOVE);
                redraw = 1;
            }
        } else if (!present && (keys_new & (KEY_L | KEY_R | KEY_LEFT | KEY_RIGHT))) {
            tab ^= 1;
            top = 0;
            bar_y = -1, pic_dx = 16, full = 1;
            plat_sfx(SFX_MOVE);
            redraw = 1;
        } else if (present && (keys_new & KEY_A) && n) {
            result = list[sel[tab]];
            plat_sfx(SFX_OK);
            for (int k = 0; k < 12; k++) { /* the chosen row flashes, the picture shakes */
                int y = REC_LIST_Y + (sel[tab] - top) * 13;
                fill(4, y, 156, 13, (k & 2) ? C_WHITE : C_HILITE);
                draw_text(16, y, ev_name[result], (k & 2) ? C_BOX : C_GOLD);
                fill(168, 22, ICON_SIZE + 4, ICON_SIZE + 4, (k & 2) ? C_WHITE : C_GOLD);
                fill(170, 24, ICON_SIZE, ICON_SIZE, RGB(1, 1, 3));
                blit_keyed(170 + ((k & 1) ? 2 : -2) * (k < 8), 24, ICON_SIZE, ICON_SIZE,
                           icon_img + ev_icon[result] * ICON_SIZE * ICON_SIZE);
                frame();
            }
            break;
        } else if (!present && (keys_new & (KEY_B | KEY_START))) {
            plat_sfx(SFX_CANCEL);
            break;
        } else if (!present && in_game && (keys_new & KEY_SELECT)) {
            save_prompt();
            redraw = full = 1;
        }
    }
    plat_copy32(fb, saved_screen, SCREEN_W * SCREEN_H / 2);
    mark(0, SCREEN_H);
    return result;
}

/* ------------------------------------------------------------------ save data (SRAM) */

#define SAVE_MAGIC (0x354D4A4Bu ^ SCRIPT_HASH) /* "KJM5" + script: saves from another build are ignored */
#define SLOT_COUNT 4   /* 0 = automatic (older versions, load only), 1-3 = saved by the player */
#define SLOT_SIZE 512

typedef struct {
    u32 magic;
    u32 ev, ev_hi, prof;
    u16 pc, scene, gameover;
    u8 lives, max_lives;
    u16 prof_text[32];
    u32 flags[32];
    u16 chapter, place; /* for the slot list: chapter title and location caption text ids */
    u16 cut, pad;
    u32 check;
} SaveData;

/* where a save made right now resumes: the top-level statement being executed (a dialogue line,
 * or the investigation / menu / deduction that the player is inside) */
static u16 cur_chapter = NONE, cur_place = NONE;

static u32 save_sum(const SaveData *s)
{
    u32 sum = s->ev * 3 + s->ev_hi * 23 + s->prof * 5 + s->pc * 7 + s->scene * 11 + s->gameover * 13 + s->lives * 17 +
              s->max_lives * 19 + s->chapter * 29 + s->place * 31 + s->cut * 41 + 0x1234;
    for (int i = 0; i < 32; i++) sum = sum * 31 + s->prof_text[i];
    for (int i = 0; i < 32; i++) sum = sum * 37 + s->flags[i];
    return sum;
}

static void save_slot(int slot, u16 pc)
{
    SaveData s;
    s.magic = SAVE_MAGIC;
    s.ev = (u32)ev_flags;
    s.ev_hi = (u32)(ev_flags >> 32);
    s.prof = prof_flags;
    s.pc = pc;
    s.scene = cur_scene;
    s.cut = cut_mode;
    s.pad = 0;
    s.gameover = gameover_pc;
    s.lives = lives;
    s.max_lives = max_lives;
    s.chapter = cur_chapter;
    s.place = cur_place;
    for (int i = 0; i < 32; i++) s.prof_text[i] = prof_text[i];
    for (int i = 0; i < 32; i++) s.flags[i] = flags[i];
    s.check = save_sum(&s);
    plat_sram_write(&s, slot * SLOT_SIZE, sizeof s);
    plat_debug_event("saved", slot);
}

static int load_slot(int slot, SaveData *s)
{
    plat_sram_read(s, slot * SLOT_SIZE, sizeof *s);
    return s->magic == SAVE_MAGIC && s->check == save_sum(s);
}


/* First line of a text only (slot list labels). Returns the x after it. */
static int draw_first_line(int x, int y, int id, u16 c)
{
    for (const u16 *g = txt(id); *g != TXT_END && *g != TXT_NL; g++) {
        if (*g >= TXT_EMPH_OFF) continue;
        glyph_shadowed(x, y, *g, c, 1);
        x += glyph_adv[*g];
    }
    return x;
}

/* Slot list for saving (slots 1-3) or loading (all slots). Returns the slot or -1 for cancel. */
static int slot_menu(int saving)
{
    static const u16 names[SLOT_COUNT] = {UI_SLOT_AUTO, UI_SLOT_1, UI_SLOT_2, UI_SLOT_3};
    SaveData d[SLOT_COUNT];
    int ok[SLOT_COUNT], first, sel;
    for (int i = 0; i < SLOT_COUNT; i++) ok[i] = load_slot(i, &d[i]);
    first = sel = (saving || !ok[0]) ? 1 : 0; /* slot 0: automatic saves from older versions */
    if (!saving)
        while (sel < SLOT_COUNT - 1 && !ok[sel]) sel++;
    save_screen();
    for (int redraw = 1;;) {
        if (redraw) {
            restore_screen();
            int rows = SLOT_COUNT - first, y0 = 22;
            popup_window(10, y0 - 4, SCREEN_W - 20, 18 + rows * 28);
            draw_text(18, y0, saving ? UI_SAVE_Q : UI_LOAD_Q, C_GOLD);
            for (int i = first; i < SLOT_COUNT; i++) {
                int y = y0 + 16 + (i - first) * 28;
                if (i == sel) {
                    fill(14, y - 1, SCREEN_W - 28, 27, C_HILITE);
                    draw_cursor(17, y + 3, C_GOLD);
                }
                u16 c = (!saving && !ok[i]) ? C_GREY : (i == sel ? C_GOLD : C_WHITE);
                draw_text(28, y, names[i], c);
                if (ok[i]) {
                    for (int h = 0; h < d[i].lives; h++) draw_heart(SCREEN_W - 26 - h * 9, y + 3, C_RED);
                    int x = 28;
                    if (d[i].chapter != NONE) x = draw_first_line(x, y + 13, d[i].chapter, C_WHITE);
                    if (d[i].place != NONE) {
                        x = draw_first_line(x, y + 13, UI_SLOT_SEP, C_GREY);
                        draw_first_line(x, y + 13, d[i].place, C_WHITE);
                    }
                } else {
                    draw_text(28, y + 13, UI_SLOT_EMPTY, C_GREY);
                }
            }
            redraw = 0;
        }
        plat_debug_event("slots", sel);
        int dbg = plat_debug_choice(DBG_SLOT, SLOT_COUNT);
        if (dbg >= 0) {
            sel = dbg;
            break;
        }
        frame();
        if (keys_new & (KEY_UP | KEY_DOWN)) {
            int step = (keys_new & KEY_UP) ? -1 : 1;
            sel = first + (sel - first + step + (SLOT_COUNT - first)) % (SLOT_COUNT - first);
            plat_sfx(SFX_MOVE);
            redraw = 1;
        } else if (keys_new & KEY_A) {
            if (!saving && !ok[sel]) {
                plat_sfx(SFX_WRONG);
                continue;
            }
            break;
        } else if (keys_new & KEY_B) {
            sel = -1;
            break;
        }
    }
    plat_sfx(sel >= 0 ? SFX_OK : SFX_CANCEL);
    restore_screen();
    return sel;
}

/* Chapter start: "저장하시겠습니까?" 예 / 아니오, then the slot list. Saves resume after the card. */
static u16 chapter_save_pc;
static int chapter_ask;
static void chapter_save(int slot)
{
    int scene = cur_scene, cut = cut_mode;
    cur_scene = SCENE_BLACK; /* loading starts on a black screen, like after the card */
    cut_mode = 0;
    save_slot(slot, chapter_save_pc);
    cur_scene = scene;
    cut_mode = cut;
}

static void chapter_save_ask(void)
{
    if (!chapter_ask) return;
    int dbg = plat_debug_choice(DBG_CHSAVE, 0); /* test harness: slot to save in, 0 = no */
    if (dbg >= 0) {
        if (dbg > 0) chapter_save(dbg);
        return;
    }
    save_screen();
    int yes = 1, x = 60, y = 58, w = SCREEN_W - 120;
    for (int redraw = 1;;) {
        if (redraw) {
            restore_screen();
            popup_window(x, y, w, 46);
            draw_text_ex(SCREEN_W / 2, y + 8, UI_CHSAVE_Q, C_GOLD, 1, 1);
            for (int i = 0; i < 2; i++) {
                int cx = SCREEN_W / 2 + (i ? 30 : -30), on = (i == 0) == yes;
                if (on) {
                    fill(cx - 24, y + 25, 48, 15, C_HILITE);
                    draw_cursor(cx - 21, y + 29, C_GOLD);
                }
                draw_text_ex(cx + 3, y + 27, i ? UI_NO : UI_YES, on ? C_GOLD : C_WHITE, 1, 1);
            }
            redraw = 0;
        }
        frame();
        if (keys_new & (KEY_LEFT | KEY_RIGHT | KEY_UP | KEY_DOWN)) {
            yes ^= 1;
            plat_sfx(SFX_MOVE);
            redraw = 1;
        } else if (keys_new & (KEY_A | KEY_B)) {
            if (keys_new & KEY_B) yes = 0;
            plat_sfx(yes ? SFX_OK : SFX_CANCEL);
            break;
        }
    }
    restore_screen();
    frame();
    if (!yes) return;
    int slot = slot_menu(1);
    if (slot < 1) return;
    chapter_save(slot);
    save_screen();
    popup_window(60, 64, SCREEN_W - 120, 26);
    draw_text_ex(SCREEN_W / 2, 70, UI_SAVED, C_GOLD, 1, 1);
    wait_a(20);
    restore_screen();
}

static void save_prompt(void)
{
    int slot = slot_menu(1);
    if (slot < 1) return;
    save_slot(slot, top_pc);
    save_screen();
    popup_window(60, 64, SCREEN_W - 120, 26);
    draw_text_ex(SCREEN_W / 2, 70, UI_SAVED, C_GOLD, 1, 1);
    wait_a(20);
    restore_screen();
}

/* ------------------------------------------------------------------ interpreter */

static int run(u16 pc);

/* Option menu shared by @investigate and @menu. opts: n entries of (text, label, cond, mark) where
 * mark = flag remembering the option was chosen (greys it out), bit 15 = trap (test harness avoids it).
 * need: evidence required before the exit option works (0 = exit any time). */
/* Last option picked in each exploration menu, so the cursor comes back to it after a talk. */
static u16 menu_mem_id[32], menu_mem_opt[32];
static int menu_mem_next;

static int menu_mem_slot(int id, int create)
{
    for (int i = 0; i < 32; i++)
        if (menu_mem_id[i] == id + 1) return i;
    if (!create) return -1;
    int i = menu_mem_next++ & 31;
    menu_mem_id[i] = id + 1;
    return i;
}

static int option_menu(int id, int spk, int q, int exit_text, u64 need, int n, const u16 *opts)
{
    for (;;) {
        u16 texts[12], idx[12];
        u32 grey = 0, traps = 0;
        int m = 0;
        for (int i = 0; i < n && m < 11; i++) {
            const u16 *o = opts + i * 4;
            if (!cond_true(o[2])) continue;
            if (flag_get(o[3] & 0x7FFF)) grey |= 1u << m;
            if (o[3] & 0x8000) traps |= 1u << m;
            texts[m] = o[0];
            idx[m++] = i;
        }
        int slot = menu_mem_slot(id, 0);
        if (slot >= 0)
            for (int k = 0; k < m; k++)
                if (idx[k] == menu_mem_opt[slot]) choose_start = k;
        int sel = choose(spk, q, texts, m, grey, exit_text, DBG_MENU, id, traps);
        if (sel < m) menu_mem_opt[menu_mem_slot(id, 1)] = idx[sel];
        if (sel >= m) {
            if ((ev_flags & need) == need) return RET_RETURN;
            say(NONE, UI_INVEST_NOTYET, NONE);
            continue;
        }
        const u16 *o = opts + idx[sel] * 4;
        flag_set(o[3] & 0x7FFF);
        if (run(o[1]) == RET_TITLE) return RET_TITLE;
    }
}

/* @examine: the board picture with a magnifier cursor. The D-pad moves it, A examines the spot
 * under it (the smallest rectangle containing the cursor), B leaves, START opens the notebook.
 * The top bar shows the question and keys, or the name of the spot under the cursor.
 * opts: n entries of (name, label, cond, mark, x, y, w, h); mark bit 15 = trap (test harness),
 * bit 14 = the spot ends the examination. */
#define EX_BAR_H 15
static int ex_x = SCREEN_W / 2, ex_y = SCREEN_H / 2;

static void draw_magnifier(int x, int y, u16 c)
{
    for (int pass = 0; pass < 2; pass++) { /* dark shadow first, then the lens */
        int o = pass ? 0 : 1;
        u16 col = pass ? c : C_SHADOW;
        for (int dy = -6; dy <= 6; dy++)
            for (int dx = -6; dx <= 6; dx++) {
                int r = dx * dx + dy * dy;
                if (r >= 18 && r <= 32) px(x + dx + o, y + dy + o, col);
            }
        for (int k = 4; k <= 8; k++) {
            px(x + k + o, y + k + o, col);
            px(x + k + 1 + o, y + k + o, col);
        }
    }
    mark(y - 7, y + 11);
}

static int ex_spot_at(int x, int y, int m, const u16 *idx, const u16 *opts)
{
    int best = -1, area = 1 << 30;
    for (int k = 0; k < m; k++) {
        const u16 *o = opts + idx[k] * 8;
        if (x >= o[4] && x < o[4] + o[6] && y >= o[5] && y < o[5] + o[7] && o[6] * o[7] < area) {
            best = k;
            area = o[6] * o[7];
        }
    }
    return best;
}

/* set when the player walked out (B) of an @examine that has an answering spot: the script
 * after it (e.g. "@get video") is skipped, and a video stop does not count as solved */
static int examine_quit;

static int examine(int id, int q, int n, const u16 *opts)
{
    int has_end = 0;
    for (int i = 0; i < n; i++) has_end |= (opts[i * 8 + 3] & 0x4000) != 0;
    examine_quit = 0;
    for (;;) {
        u16 idx[12];
        u32 traps = 0;
        int m = 0;
        for (int i = 0; i < n && m < 12; i++) {
            const u16 *o = opts + i * 8;
            if (!cond_true(o[2])) continue;
            if (o[3] & 0x8000) traps |= 1u << m;
            idx[m++] = i;
        }
        cur_portrait = NONE;
        heart_y = EX_BAR_H + 4; /* under the question bar */
        draw_scene();
        heart_y = 3;
        shade(0, 0, SCREEN_W, EX_BAR_H, 0);
        shade(0, 0, SCREEN_W, EX_BAR_H, 0);
        fill(0, EX_BAR_H, SCREEN_W, 1, C_BORDER);
        save_screen();
        int sel = -1, pick = -1, hover = -2, ox = ex_x, oy = ex_y;
        for (int redraw = 1;;) {
            int h = ex_spot_at(ex_x, ex_y, m, idx, opts);
            if (redraw || h != hover || ox != ex_x || oy != ex_y) {
                int rx = ox - 7 < 0 ? 0 : ox - 7, ry = oy - 7 < 0 ? 0 : oy - 7;
                restore_rect(rx, ry, rx + 19 > SCREEN_W ? SCREEN_W - rx : 19, ry + 19 > SCREEN_H ? SCREEN_H - ry : 19);
                restore_rect(0, 0, SCREEN_W, EX_BAR_H);
                /* only the question: naming what is under the cursor would give the answers away */
                draw_text(6, 1, q, C_GOLD);
                if (6 + text_width(q, 1) + 8 < SCREEN_W - 6 - disp_width(UI_EXAMINE_HINT) / 2)
                    draw_hint_right(UI_EXAMINE_HINT, SCREEN_W - 6, 3);
                draw_magnifier(ex_x, ex_y, h >= 0 ? C_GOLD : C_WHITE);
                hover = h;
                ox = ex_x;
                oy = ex_y;
                redraw = 0;
            }
            plat_debug_event("menu", h);
            if (pick >= 0) {
                sel = pick;
                break;
            }
            int d = plat_debug_menu(id, m + 1, traps);
            if (d >= 0) { /* test harness: point at the picked spot, then take it */
                if (d >= m) {
                    examine_quit = has_end;
                    return RET_RETURN;
                }
                const u16 *o = opts + idx[d] * 8;
                ex_x = o[4] + o[6] / 2;
                ex_y = o[5] + o[7] / 2;
                pick = d;
                continue;
            }
            frame();
            int dx = (keys_held & KEY_RIGHT) ? 2 : (keys_held & KEY_LEFT) ? -2 : 0;
            int dy = (keys_held & KEY_DOWN) ? 2 : (keys_held & KEY_UP) ? -2 : 0;
            ex_x += dx;
            ex_y += dy;
            if (ex_x < 4) ex_x = 4;
            if (ex_x > SCREEN_W - 12) ex_x = SCREEN_W - 12;
            if (ex_y < EX_BAR_H + 8) ex_y = EX_BAR_H + 8;
            if (ex_y > SCREEN_H - 4) ex_y = SCREEN_H - 4;
            if (keys_new & KEY_A) {
                if (hover >= 0) {
                    plat_sfx(SFX_OK);
                    sel = hover;
                    break;
                }
                plat_sfx(SFX_CANCEL);
            } else if (keys_new & KEY_B) {
                plat_sfx(SFX_MOVE);
                examine_quit = has_end;
                return RET_RETURN;
            } else if (keys_new & KEY_START) {
                record(0, NONE);
                restore_screen();
                redraw = 1;
            }
        }
        const u16 *o = opts + idx[sel] * 8;
        flag_set(o[3] & 0x3FFF);
        if (run(o[1]) == RET_TITLE) return RET_TITLE;
        if (o[3] & 0x4000) return RET_RETURN; /* the spot that answers it */
    }
}

/* "조사 개시!" / "추리 개시!" / "추궁 개시!" / "추궁 성공!": the screen darkens and the two words
 * slam in one after the other, each with a boom and a jolt. */
static void banner(int kind)
{
    static const u16 first[4] = {UI_SLAM_INVEST, UI_SLAM_DEDUCE, UI_SLAM_CROSS, UI_SLAM_CROSS};
    int a = first[kind], b = kind == 3 ? UI_SLAM_DONE : UI_SLAM_START;
    int wa = disp_width(a), wb = disp_width(b), gap = 6, x = SCREEN_W / 2 - (wa + gap + wb) / 2, cy = 72;
    save_screen();
    shade(0, 0, SCREEN_W, SCREEN_H, 0);
    shade(0, 0, SCREEN_W, SCREEN_H, 0);
    frame();
    wait_frames(4);
    plat_sfx(SFX_SHOCK);
    draw_disp(a, x + wa / 2, cy);
    shake(10, 5);
    wait_frames(6);
    plat_sfx(kind == 3 ? SFX_OBJECTION : SFX_SHOCK);
    draw_disp(b, x + wa + gap + wb / 2, cy);
    plat_debug_event("banner", kind);
    shake(14, 6);
    for (int f = 0; f < 50; f++) {
        frame();
        if (f > 8 && (keys_new & KEY_A)) break;
    }
    restore_screen();
    frame();
}

/* Video player: step through frames and stop on the suspicious one. Returns frame index or -1. */
static int video(int title, int n, const u16 *frames, int cur)
{
    int rec_x = SCREEN_W - 80;
    for (int redraw = 1;;) {
        if (redraw) {
            const u16 *f = frames + cur * 3;
            plat_copy32(fb, scene_img[f[0]], SCREEN_W * SCREEN_H / 2);
            mark(0, SCREEN_H);
            for (int y = 0; y < BOX_Y; y += 2) shade(0, y, SCREEN_W, 1, RGB(0, 6, 2)); /* scanlines */
            fill(0, 0, SCREEN_W, 18, RGB(1, 2, 1));
            fill(0, 18, SCREEN_W, 1, RGB(10, 31, 12));
            draw_text(8, 3, title, RGB(10, 31, 12));
            rec_x = SCREEN_W - 8 - text_width(f[1], 1);
            draw_text(rec_x, 3, f[1], RGB(10, 31, 12));
            rec_x -= 12;
            if (cur > 0) for (int r = 0; r < 4; r++) fill(4 + r, 56 - r, 1, 1 + 2 * r, C_WHITE);
            if (cur < n - 1) for (int r = 0; r < 4; r++) fill(SCREEN_W - 5 - r, 56 - r, 1, 1 + 2 * r, C_WHITE);
            shade(0, BOX_Y, SCREEN_W, SCREEN_H - BOX_Y, C_BOX);
            fill(0, BOX_Y, SCREEN_W, 1, RGB(10, 31, 12));
            draw_text(TEXT_X, TEXT_Y, f[2], C_WHITE);
            draw_hint_right(UI_VIDEO_HINT, SCREEN_W - 6, 149);
            save_screen();
            redraw = 0;
        }
        restore_rect(rec_x - 1, 5, 9, 9);
        if ((frame_count >> 4) & 1) fill(rec_x, 6, 7, 7, C_RED); /* REC lamp, just left of the timecode */
        plat_debug_event("video", cur);
        int d = plat_debug_choice(DBG_VIDEO, n);
        if (d != -1) return d < n ? d : -1;
        frame();
        if ((keys_new & KEY_LEFT) && cur > 0) {
            cur--;
            plat_sfx(SFX_MOVE);
            redraw = 1;
        } else if ((keys_new & KEY_RIGHT) && cur < n - 1) {
            cur++;
            plat_sfx(SFX_MOVE);
            redraw = 1;
        } else if (keys_new & KEY_A) {
            plat_sfx(SFX_OK);
            return cur;
        } else if (keys_new & KEY_B) {
            plat_sfx(SFX_CANCEL);
            return -1;
        }
    }
}

/* Button-mash crisis: fill the gauge with A before time runs out. Returns 1 on success. */
static int mash(int t, int frames)
{
    int gauge = 0; /* 0..1000 */
    set_speaker_portrait(NONE);
    draw_scene();
    draw_box(NONE);
    draw_text(TEXT_X, TEXT_Y, t, C_WHITE);
    save_screen();
    plat_sfx(SFX_SHOCK);
    int forced = plat_debug_choice(DBG_MASH, 0);
    for (int f = 0; f < frames; f++) {
        restore_rect(20, 60, 200, 40);
        popup_window(20, 60, 200, 40);
        draw_text_ex(SCREEN_W / 2, 64, UI_MASH_HINT, (f & 8) ? C_GOLD : C_WHITE, 1, 1);
        fill(30, 82, 180, 8, RGB(4, 2, 2));
        fill(30, 82, 180 * gauge / 1000, 8, gauge > 700 ? C_GOLD : C_RED);
        fill(30, 92, 180 * (frames - f) / frames, 2, C_GREY); /* time left */
        plat_offset((f & 2) ? 1 : -1, 0);
        frame();
        if (forced == 1) continue;                      /* test harness: let it fail */
        if (keys_new & KEY_A) {
            gauge += 90;
            plat_sfx(SFX_BLIP);
        }
        gauge -= 6;
        if (gauge < 0) gauge = 0;
        if (gauge >= 1000) {
            plat_offset(0, 0);
            plat_sfx(SFX_OK);
            plat_debug_event("mash", 1);
            restore_screen();
            return 1;
        }
    }
    plat_offset(0, 0);
    plat_debug_event("mash", 0);
    restore_screen();
    return 0;
}

/* Cross-examination. stmts: n x (text, press label, contradicting evidence or NONE).
 * Returns RET_RETURN when the contradiction is found, RET_TITLE, or 2 when the last heart is lost. */
static int testimony(int spk, int title, int n, const u16 *stmts, int wrong)
{
    int cur = 0;
    for (int redraw = 1;;) {
        const u16 *st = stmts + cur * 3;
        if (redraw) {
            set_speaker_portrait(spk);
            draw_scene();
            fill(0, 0, SCREEN_W, 19, RGB(2, 10, 4));
            fill(0, 19, SCREEN_W, 1, RGB(10, 31, 12));
            draw_text(8, 3, title, RGB(16, 31, 16));
            heart_y = 23; /* under the title bar */
            draw_hearts();
            heart_y = 3;
            draw_box(spk);
            draw_text(TEXT_X, TEXT_Y, st[0], RGB(20, 31, 20));
            draw_hint_right(UI_TESTI_HINT, SCREEN_W - 6, 149);
            for (int i = 0; i < n; i++) fill(8 + i * 8, 148, 5, 5, i == cur ? RGB(16, 31, 16) : C_GREY);
            save_screen();
            redraw = 0;
        }
        plat_debug_event("testimony", cur);
        int d = plat_debug_choice(DBG_TESTIMONY, n);
        int press = -1, present = 0;
        if (d >= 1000) {
            cur = (d - 1000) / 100 % n;
            st = stmts + cur * 3;
            present = 1;
        } else if (d >= 0) {
            cur = d % n;
            st = stmts + cur * 3;
            press = cur;
        } else {
            frame();
            if (keys_new & KEY_RIGHT) {
                cur = (cur + 1) % n;
                plat_sfx(SFX_MOVE);
                redraw = 1;
                continue;
            }
            if (keys_new & KEY_LEFT) {
                cur = (cur + n - 1) % n;
                plat_sfx(SFX_MOVE);
                redraw = 1;
                continue;
            }
            if (keys_new & KEY_A) press = cur;
            else if (keys_new & (KEY_R | KEY_SELECT)) present = 1;
            else if (keys_new & KEY_START) record(0, NONE);
            if (press < 0 && !present) continue;
        }
        if (press >= 0) { /* 추궁: dig into this statement */
            do_shout(NONE, UI_PRESS_SHOUT, char_portrait[0]);
            if (run(st[1]) == RET_TITLE) return RET_TITLE;
            redraw = 1;
            continue;
        }
        int ev = d >= 1000 ? d % 100 : record(1, title);
        if (st[2] != NONE && ev == st[2]) {
            do_shout(NONE, UI_OBJECTION, char_portrait[0]);
            plat_debug_event("contradiction", cur);
            return RET_RETURN;
        }
        lose_life();
        if (run(wrong) == RET_TITLE) return RET_TITLE;
        if (lives == 0) return 2;
        redraw = 1;
    }
}

static void ending(int kind, int t)
{
    fade_out();
    fill(0, 0, SCREEN_W, SCREEN_H, 0);
    if (kind) { /* 1 = TRUE END, 2 = GOOD END, 3 = BEST END, 4 = NORMAL END */
        static const u16 labels[5] = {0, UI_TRUE_END, UI_GOOD_END, UI_BEST_END, UI_NORMAL_END};
        u16 c = kind == 3 ? RGB(31, 31, 20) : C_GOLD;
        for (int i = 0; i < 60 + kind * 30; i++) px((i * 97) % SCREEN_W, (i * 53) % 90, c);
        draw_disp(labels[kind <= 4 ? kind : 1], SCREEN_W / 2, 44);
        draw_disp(t, SCREEN_W / 2, 84);
        draw_text_ex(SCREEN_W / 2, 112, UI_THANKS, C_GOLD, 1, 1);
    } else {
        fill(0, 26, SCREEN_W, 34, RGB(10, 0, 2));
        draw_disp(UI_BAD_END, SCREEN_W / 2, 43);
        draw_disp(t, SCREEN_W / 2, 84);
    }
    draw_disp(UI_PRESS_A, SCREEN_W / 2, 146);
    fade_in();
    static const char *const events[5] = {"bad_end", "true_end", "good_end", "best_end", "normal_end"};
    plat_debug_event(events[kind <= 4 ? kind : 1], t);
    wait_a(40);
    fade_out();
}

static int run_inner(u16 pc);

static int run(u16 pc)
{
    run_depth++;
    int r = run_inner(pc);
    run_depth--;
    return r;
}

static int run_inner(u16 pc)
{
    const u16 *S = script;
    for (;;) {
        u16 op_pc = pc;
        if (run_depth == 1) top_pc = op_pc;
        u16 op = S[pc++];
        int risky = op == OP_ASK || op == OP_PRESENT || op == OP_PRESENT_CHOICE || op == OP_ACCUSE ||
                    op == OP_TESTIMONY || op == OP_VIDEO || op == OP_MASH;
        if (risky) {
            if (!hearts_shown || run_depth < hearts_depth) hearts_depth = run_depth;
            hearts_shown = 1;
        } else if (hearts_shown && run_depth <= hearts_depth) {
            hearts_shown = 0; /* the risky part is over (its wrong-answer talks run deeper) */
        }
        switch (op) {
        case OP_SAY: {
            int spk = S[pc], t = S[pc + 1], por = S[pc + 2];
            pc += 3;
            say(spk, t, por);
            break;
        }
        case OP_SCENE: {
            int quick = S[pc] & 0x4000; /* @scene KEY quick: change the picture in place, no fade */
            if (!quick) fade_out();
            cut_mode = S[pc] >> 15;
            cur_scene = S[pc++] & 0x3FFF;
            cur_portrait = NONE;
            cur_inset = NONE;
            draw_scene();
            if (quick) {
                frame();
                break;
            }
            wait_frames(6); /* a beat of black between places */
            fade_in();
            break;
        }
        case OP_GET: {
            int e = S[pc++];
            if (ev_flags & EV_BIT(e)) break; /* re-examined spot: already in the record */
            ev_flags |= EV_BIT(e);
            got_item(UI_GOT, ev_name[e], icon_img + ev_icon[e] * ICON_SIZE * ICON_SIZE, ICON_SIZE, ICON_SIZE);
            break;
        }
        case OP_MEET: {
            int c = S[pc++];
            if (!(prof_flags & (1u << c))) meet(c, UI_MEET);
            break;
        }
        case OP_PROFILE: {
            int c = S[pc], t = S[pc + 1];
            pc += 2;
            prof_text[c] = t;
            meet(c, UI_PROFILE_UPDATED);
            break;
        }
        case OP_FX:
            do_fx(S[pc++]);
            break;
        case OP_SHOUT: {
            int spk = S[pc], t = S[pc + 1], por = S[pc + 2];
            pc += 3;
            do_shout(spk, t, por);
            break;
        }
        case OP_INSET:
            cur_inset = S[pc++];
            if (cur_inset == NONE) {
                /* the next line redraws the screen without it */
                break;
            }
            plat_sfx(SFX_MOVE);
            for (int h = 8; h <= INSET_H / 2 + 2; h += 12) { /* opens from the middle */
                draw_inset(INSET_Y + INSET_H / 2 - h, INSET_Y + INSET_H / 2 + h);
                frame();
            }
            draw_inset(0, SCREEN_H);
            frame();
            break;
        case OP_INTRO:
            intro_card(S[pc], S[pc + 1], S[pc + 2], S[pc + 3]);
            pc += 4;
            break;
        case OP_PLACE:
            cur_place = S[pc];
            place_caption(S[pc++]);
            break;
        case OP_CHAPTER:
            chapter_ask = cur_chapter != NONE; /* no save question on the very first card */
            cur_chapter = S[pc];
            cur_place = NONE;
            cur_inset = NONE;
            chapter_save_pc = pc + 2; /* a save made here resumes after the card */
            chapter_card(S[pc], S[pc + 1]);
            pc += 2;
            break;
        case OP_INVEST: {
            int n = S[pc];
            u64 need = 0;
            for (int w = 0; w < 4; w++) need |= (u64)S[pc + 1 + w] << (16 * w);
            const u16 *opts = &S[pc + 5];
            pc += 5 + n * 4;
            banner(0);
            if (option_menu(op_pc, NONE, UI_INVEST_Q, UI_INVEST_DONE, need, n, opts) == RET_TITLE) return RET_TITLE;
            break;
        }
        case OP_EXAMINE: {
            int q = S[pc], n = S[pc + 1];
            const u16 *opts = &S[pc + 2];
            pc += 2 + n * 8;
            if (examine(op_pc, q, n, opts) == RET_TITLE) return RET_TITLE;
            if (examine_quit) return RET_RETURN; /* left without an answer: skip what follows */
            break;
        }
        case OP_MENU: {
            int spk = S[pc], q = S[pc + 1], exit_text = S[pc + 2], n = S[pc + 3];
            const u16 *opts = &S[pc + 4];
            pc += 4 + n * 4;
            if (option_menu(op_pc, spk, q, exit_text, 0, n, opts) == RET_TITLE) return RET_TITLE;
            break;
        }
        case OP_SET:
            flag_set(S[pc++]);
            break;
        case OP_IF:
            pc = cond_true(S[pc]) ? S[pc + 1] : pc + 2;
            break;
        case OP_PENALTY:
            lose_life();
            if (lives == 0) pc = gameover_pc;
            break;
        case OP_BANNER:
            banner(S[pc++]);
            break;
        case OP_VIDEO: {
            int title = S[pc], n = S[pc + 1], correct = S[pc + 2], ok = S[pc + 3], wrong = S[pc + 4];
            const u16 *frames = &S[pc + 5];
            pc += 5 + n * 3;
            /* a wrong stop comments on the frame, then the tape keeps playing from there (B = stop watching) */
            int back = cur_scene; /* after a stop the talk (and @examine) happens over that frame */
            for (int cur = 0;;) {
                int sel = video(title, n, frames, cur);
                if (sel < 0) break;
                cur_scene = frames[sel * 3];
                examine_quit = 0;
                if (run(sel == correct ? ok : wrong) == RET_TITLE) return RET_TITLE;
                if (sel == correct && !examine_quit) break; /* backed out of the stop: keep watching */
                cur = sel;
            }
            cur_scene = back;
            break;
        }
        case OP_TIMER:
            timer_total = S[pc++] * 60; /* armed for the next @ask / @present */
            break;
        case OP_MASH: {
            int t = S[pc], frames = S[pc + 1] * 60, fail = S[pc + 2];
            pc += 3;
            if (!mash(t, frames) && run(fail) == RET_TITLE) return RET_TITLE;
            if (lives == 0) pc = gameover_pc;
            break;
        }
        case OP_TESTIMONY: {
            int spk = S[pc], title = S[pc + 1], n = S[pc + 2], wrong = S[pc + 3];
            const u16 *stmts = &S[pc + 4];
            pc += 4 + n * 3;
            int r = testimony(spk, title, n, stmts, wrong);
            if (r == RET_TITLE) return RET_TITLE;
            if (r == 2) pc = gameover_pc;
            break;
        }
        case OP_RETURN:
            return RET_RETURN;
        case OP_ASK: {
            int spk = S[pc], q = S[pc + 1], n = S[pc + 2];
            const u16 *opts = &S[pc + 3];
            pc += 3 + n * 2;
            int limit = timer_total;
            for (;;) {
                timer_left = timer_total = limit;
                int sel = menu(spk, q, opts, n, 0, NONE, DBG_MENU);
                timer_total = 0;
                if (sel != TIMEOUT && opts[sel * 2 + 1] == NONE) break;
                lose_life();
                if (sel == TIMEOUT)
                    say(NONE, UI_TIMEOUT, NONE);
                else if (run(opts[sel * 2 + 1]) == RET_TITLE)
                    return RET_TITLE;
                if (lives == 0) {
                    pc = gameover_pc;
                    break;
                }
            }
            break;
        }
        case OP_PRESENT: {
            int spk = S[pc], q = S[pc + 1], target = S[pc + 2], wrong = S[pc + 3];
            pc += 4;
            int limit = timer_total;
            timer_total = 0;
            for (;;) {
                set_speaker_portrait(spk);
                say(spk, q, cur_portrait);
                timer_left = timer_total = limit;
                int chosen = record(1, q);
                timer_total = 0;
                if (chosen == target) {
                    plat_sfx(SFX_OBJECTION);
                    flash(C_WHITE, 2);
                    break;
                }
                lose_life();
                if (chosen == TIMEOUT)
                    say(NONE, UI_TIMEOUT, NONE);
                else if (run(wrong) == RET_TITLE)
                    return RET_TITLE;
                if (lives == 0) {
                    pc = gameover_pc;
                    break;
                }
            }
            break;
        }
        case OP_PRESENT_CHOICE: {
            /* one chance, no life cost: the right evidence opens a story branch, anything else another */
            int spk = S[pc], q = S[pc + 1], target = S[pc + 2], ok = S[pc + 3], miss = S[pc + 4];
            int limit = timer_total;
            timer_total = 0;
            set_speaker_portrait(spk);
            say(spk, q, cur_portrait);
            timer_left = timer_total = limit;
            int chosen = record(1, q);
            timer_total = 0;
            if (chosen == target) {
                plat_sfx(SFX_OBJECTION);
                flash(C_WHITE, 2);
                pc = ok;
            } else {
                if (chosen == TIMEOUT) say(NONE, UI_TIMEOUT_BRANCH, NONE);
                pc = miss;
            }
            break;
        }
        case OP_ACCUSE: {
            int spk = S[pc], q = S[pc + 1], n = S[pc + 2];
            const u16 *opts = &S[pc + 3];
            int sel = menu(spk, q, opts, n, 0, NONE, DBG_ACCUSE);
            pc = opts[sel * 2 + 1];
            break;
        }
        case OP_GOTO:
            pc = S[pc];
            break;
        case OP_ENDING:
            ending(S[pc], S[pc + 1]);
            return RET_TITLE;
        case OP_LIVES:
            lives = max_lives = S[pc++];
            break;
        case OP_GAMEOVER:
            gameover_pc = S[pc++];
            break;
        case OP_WAIT:
            wait_frames(S[pc++]);
            break;
        default:
            return RET_TITLE; /* end of script or corrupt data */
        }
    }
}

/* ------------------------------------------------------------------ title */

static void disclaimer(void)
{
    fill(0, 0, SCREEN_W, SCREEN_H, 0);
    draw_text_ex(SCREEN_W / 2, 40, UI_DISCLAIMER, C_WHITE, 1, 1);
    fade_in();
    for (int i = 0; i < 240; i++) {
        frame();
        if (i > 20 && (keys_new & (KEY_A | KEY_START))) break;
    }
    fade_out();
}

/* Title background: white fog drifting over black, faint lights moving behind it. Two fog
 * layers and a light layer (256 wide, tileable) scroll at their own speeds; title_lut turns the
 * (fog, light) amounts into a colour. A fixed 4x4 dither hides the banding. */
FAST static void title_fog_draw(int o1, int o2, int o3)
{
    static const u8 dither[4][4] = {{0, 8, 2, 10}, {12, 4, 14, 6}, {3, 11, 1, 9}, {15, 7, 13, 5}};
    for (int y = 0; y < SCREEN_H; y += 2) { /* soft fog: one colour per 2x2 block */
        const u8 *f1 = title_fog1 + y * 256, *f2 = title_fog2 + y * 256, *l = title_lights + y * 256;
        const u8 *d = dither[(y >> 1) & 3];
        u32 *dst = (u32 *)(fb + y * SCREEN_W), *dst2 = dst + SCREEN_W / 2;
        for (int x = 0; x < SCREEN_W; x += 2) {
            int a = f1[(x + o1) & 255] + f2[(x + o2) & 255] + d[(x >> 1) & 3];
            if (a > 255) a = 255;
            u32 c = title_lut[(a >> 3) << 5 | l[(x + o3) & 255] >> 3];
            dst[x >> 1] = dst2[x >> 1] = c | c << 16;
        }
    }
    mark(0, SCREEN_H);
}

/* menu < 0: "PRESS START" blinks; otherwise the 처음부터 / 이어하기 box with a blinking cursor */
static int title_blink0; /* fog step when the blink last restarted (a key press shows the cursor at once) */
static void title_draw(int t, int menu, int has_save)
{
    int blink = ((t - title_blink0) >> 2) & 1; /* on/off about every half second */
    title_fog_draw(t, -(t * 2 / 3), t / 3);
    draw_disp(UI_TITLE_MAIN, SCREEN_W / 2, 18 + disp_height(UI_TITLE_MAIN) / 2);
    if (menu < 0) {
        if (!blink) draw_disp(UI_PRESS_START, SCREEN_W / 2, 124);
    } else {
        int y0 = 106;
        shade(SCREEN_W / 2 - 56, y0, 112, 34, 0);
        shade(SCREEN_W / 2 - 56, y0, 112, 34, 0);
        frame_rect(SCREEN_W / 2 - 56, y0, 112, 34, C_BORDER);
        for (int i = 0; i < 2; i++) {
            int tx = i ? UI_CONTINUE : UI_NEW, y = y0 + 3 + i * 14;
            u16 c = (i == 1 && !has_save) ? C_GREY : (i == menu ? C_GOLD : C_WHITE);
            if (i == menu && !blink) draw_cursor(SCREEN_W / 2 - 40, y + 3, C_GOLD);
            draw_text_ex(SCREEN_W / 2, y, tx, c, 1, 1);
        }
    }
    draw_disp(UI_TITLE_FAN, SCREEN_W / 2, 151);
}

static int title_screen(int has_save)
{
    int sel = has_save ? 1 : 0, t = 0, menu = -1;
    title_blink0 = 0;
    in_game = 0;
    cur_scene = SCENE_TITLE;
    cur_portrait = NONE;
    title_draw(t, menu, has_save);
    fade_in();
    for (int f = 1;; f++) {
        plat_debug_event("title", has_save);
        int d = plat_debug_choice(DBG_TITLE, 2);
        if (d >= 0) {
            sel = d;
            break;
        }
        frame();
        int redraw = (f & 3) == 0; /* the fog moves one step every 4 frames */
        if (redraw) t++;
        if (menu < 0) {
            if (keys_new & (KEY_START | KEY_A)) {
                plat_sfx(SFX_OK);
                menu = sel;
                title_blink0 = t;
                redraw = 1;
            }
        } else {
            if ((keys_new & (KEY_UP | KEY_DOWN)) && has_save) {
                sel ^= 1;
                menu = sel;
                plat_sfx(SFX_MOVE);
                title_blink0 = t;
                redraw = 1;
            }
            if (keys_new & (KEY_A | KEY_START)) break;
        }
        if (redraw) title_draw(t, menu, has_save);
    }
    plat_sfx(SFX_OK);
    fade_out();
    return sel;
}

int main(void)
{
    SaveData s;
    plat_init();
    fb = plat_fb();
    plat_fade(16);
    disclaimer();
    for (;;) {
        int has_save = 0;
        for (int i = 0; i < SLOT_COUNT; i++) has_save |= load_slot(i, &s);
        int choice = title_screen(has_save), slot = -1;
        if (choice == 1 && has_save) {
            plat_fade(0);           /* the list sits on the title picture */
            slot = slot_menu(0);
            fade_out();
            if (slot < 0) continue;
            load_slot(slot, &s);
        }
        u16 pc = 0;
        lives = max_lives = 5;
        ev_flags = prof_flags = 0;
        gameover_pc = 0;
        cur_scene = SCENE_BLACK;
        cut_mode = 0;
        cur_chapter = cur_place = NONE;
        for (int i = 0; i < 32; i++) prof_text[i] = i < CHAR_COUNT ? char_profile[i] : NONE;
        for (int i = 0; i < 32; i++) flags[i] = 0;
        if (slot >= 0) {
            ev_flags = s.ev | ((u64)s.ev_hi << 32);
            prof_flags = s.prof;
            lives = s.lives;
            max_lives = s.max_lives;
            gameover_pc = s.gameover;
            for (int i = 0; i < 32; i++) prof_text[i] = s.prof_text[i];
            for (int i = 0; i < 32; i++) flags[i] = s.flags[i];
            cur_scene = s.scene;
            cut_mode = s.cut;
            cur_chapter = s.chapter;
            cur_place = s.place;
            pc = s.pc;
        }
        in_game = 1;
        cur_portrait = NONE;
        draw_scene();
        frame();
        plat_fade(0);
        run(pc);
        plat_debug_event("to_title", 0);
    }
}
