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
#define BOX_Y 96  /* dialogue box: 4 lines of text */
#define TEXT_X 8
#define TEXT_Y 100
#define PORTRAIT_X ((SCREEN_W - PORTRAIT_W) / 2)
#define PORTRAIT_Y 0 /* bust hangs from the top; the translucent text box covers only the chest */

#define REC_ROWS 5
#define REC_LIST_Y 21
#define REC_DESC_Y 100

enum { RET_RETURN, RET_TITLE };

static u16 *fb;
static u16 snapshot[SCREEN_W * SCREEN_H] EWRAM_BSS;
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
    for (int l = 2; l <= 16; l += 2) {
        plat_fade(l);
        frame();
    }
}

static void fade_in(void)
{
    frame(); /* make sure the new picture is on screen first */
    for (int l = 14; l >= 0; l -= 2) {
        plat_fade(l);
        frame();
    }
}

/* ------------------------------------------------------------------ drawing */

static inline void px(int x, int y, u16 c)
{
    if ((unsigned)x < SCREEN_W && (unsigned)y < SCREEN_H) fb[y * SCREEN_W + x] = c;
}

static void fill(int x, int y, int w, int h, u16 c)
{
    for (int j = y; j < y + h; j++)
        for (int i = x; i < x + w; i++) px(i, j, c);
    mark(y, y + h);
}

/* 50% blend toward colour c (translucent panels). */
static void shade(int x, int y, int w, int h, u16 c)
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
static void draw_disp_part(int t, int cx, int cy, int vis)
{
    int d = disp_of_text[t];
    if (d == NONE) {
        draw_text_ex(cx, cy - 6, t, C_GOLD, 1, 1);
        return;
    }
    int w = disp_w[d], h = disp_h[d], x = cx - w / 2, y = cy - h / 2;
    const u16 *src = disp_data + disp_ofs[d];
    if (vis < 0 || vis > w) vis = w;
    for (int j = 0; j < h; j++)
        for (int i = 0; i < vis; i++) {
            u16 c = src[j * w + i];
            if (c != TRANSPARENT) px(x + i, y + j, c);
        }
    mark(y, y + h);
}

static void draw_disp(int t, int cx, int cy) { draw_disp_part(t, cx, cy, -1); }
static int disp_width(int t) { return disp_of_text[t] == NONE ? 0 : disp_w[disp_of_text[t]]; }
static int disp_height(int t) { return disp_of_text[t] == NONE ? LINE_H : disp_h[disp_of_text[t]]; }

static void save_screen(void) { plat_copy32(snapshot, fb, SCREEN_W * SCREEN_H / 2); }

static void restore_screen(void)
{
    plat_copy32(fb, snapshot, SCREEN_W * SCREEN_H / 2);
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
        if (!instant && (++n & 1) == 0) {
            if ((n & 3) == 0) plat_sfx(SFX_BLIP);
            frame();
            if (keys_new & (KEY_A | KEY_B)) instant = 1;
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

static void draw_hearts(void)
{
    if (!in_game || !max_lives) return;
    for (int i = 0; i < max_lives; i++)
        draw_heart(SCREEN_W - 4 - (max_lives - i) * 10, 3, i < lives ? C_RED : RGB(8, 6, 8));
}

static void draw_scene(void)
{
    plat_copy32(fb, scene_img[cur_scene], SCREEN_W * SCREEN_H / 2);
    mark(0, SCREEN_H);
    if (cur_portrait != NONE && !cut_mode) blit_keyed(PORTRAIT_X, PORTRAIT_Y, PORTRAIT_W, PORTRAIT_H, portrait_img[cur_portrait]);
    draw_hearts();
}

static void set_speaker_portrait(int spk)
{
    cur_portrait = (spk != NONE) ? char_portrait[spk] : NONE;
}

static void draw_box(int spk)
{
    shade(0, BOX_Y, SCREEN_W, SCREEN_H - BOX_Y, C_BOX);
    fill(0, BOX_Y, SCREEN_W, 1, C_BORDER);
    if (spk != NONE) {
        int w = disp_width(char_name[spk]) + 12;
        fill(4, BOX_Y - 13, w, 13, char_color[spk]);
        frame_rect(4, BOX_Y - 13, w, 13, C_BORDER);
        draw_disp(char_name[spk], 4 + w / 2, BOX_Y - 6);
    }
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
static void wait_advance(void)
{
    plat_debug_event("page", 0);
    for (;;) {
        restore_rect(224, 154, 8, 5);
        if ((frame_count >> 4) & 1) draw_arrow(225, 154, C_WHITE);
        frame();
        if (keys_new & KEY_A) return;
        if (keys_new & KEY_START) record(0, NONE);
    }
}

static void got_item(int title, int name, const u16 *img, int w, int h);

/* Adds a character to the court record (after they first talk, or via @meet). */
static void meet(int c, int title)
{
    prof_flags |= 1u << c;
    if (char_portrait[c] != NONE)
        got_item(title, char_name[c], portrait_thumb[char_portrait[c]], THUMB_W, THUMB_H);
}

static void say(int spk, int t, int portrait)
{
    cur_portrait = portrait;
    draw_scene();
    draw_box(spk);
    type_text(TEXT_X, TEXT_Y, t, C_WHITE);
    save_screen();
    wait_advance();
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
    int wy = 16, wh = h + 16;
    popup_window(24, wy, 192, wh);
    fill(32, wy + 8, w, h, RGB(1, 1, 3));
    blit_keyed(32, wy + 8, w, h, img);
    draw_text(108, wy + 14, title, C_GOLD);
    draw_text(108, wy + 34, name, C_WHITE);
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
    }
}

static void do_shout(int spk, int t, int portrait)
{
    cur_portrait = portrait;
    draw_scene();
    plat_sfx(SFX_OBJECTION);
    flash(C_WHITE, 2);
    fill(0, 48, SCREEN_W, 44, C_WHITE);
    fill(0, 50, SCREEN_W, 2, C_RED);
    fill(0, 88, SCREEN_W, 2, C_RED);
    for (int i = 0; i < SCREEN_W; i += 12) {
        fill(i, 44, 6, 4, C_WHITE);
        fill(i + 6, 92, 6, 4, C_WHITE);
    }
    draw_disp(t, SCREEN_W / 2, 70);
    shake(20, 5);
    plat_debug_event("shout", 0);
    for (int i = 0; i < 90; i++) {
        frame();
        if (i > 15 && (keys_new & KEY_A)) break;
    }
}

static int lose_life(void)
{
    plat_sfx(SFX_WRONG);
    if (lives > 0) lives--;
    save_screen();
    for (int i = 0; i < 3; i++) shade(0, 0, SCREEN_W, SCREEN_H, RGB(20, 0, 0));
    draw_disp(UI_WRONG, SCREEN_W / 2, 72);
    for (int i = 0; i < 24; i++) {
        /* blink the heart that was lost */
        draw_heart(SCREEN_W - 4 - (max_lives - lives) * 10, 3, (i & 4) ? C_RED : RGB(8, 6, 8));
        plat_offset((i & 1) ? 3 : -3, 0);
        frame();
    }
    plat_offset(0, 0);
    wait_frames(20);
    if (lives == 1) { /* last heart: make it hurt */
        fill(0, 96, SCREEN_W, 22, RGB(10, 0, 0));
        draw_text_ex(SCREEN_W / 2, 101, UI_DANGER, C_RED, 1, 1);
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

static void chapter_card(int t, int card)
{
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
    draw_text_ex(SCREEN_W / 2, 145, UI_SAVED, C_GREY, 1, 1);
    fade_in();
    plat_debug_event("chapter", 0);
    for (int i = 0; i < 180; i++) {
        frame();
        if (i > 20 && (keys_new & (KEY_A | KEY_START))) break;
    }
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
static int choose(int spk, int q, const u16 *texts, int n, u32 greyed, int extra, int dbg_kind, int dbg_id,
                  u32 traps)
{
    int total = n + (extra != NONE);
    int sel = 0, w = 120;
    for (int i = 0; i < total; i++) {
        int tw = text_width(i < n ? texts[i] : extra, 1) + 30;
        if (tw > w) w = tw;
    }
    int row = total > 6 ? 13 : 14; /* compact rows so 7 options still clear the text box */
    int h = total * row + 8;
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
                int oy = y + 4 + i * row;
                int t = i < n ? texts[i] : extra;
                if (i == sel) {
                    fill(x + 4, oy, w - 8, row, C_HILITE);
                    draw_cursor(x + 8, oy + 3, C_GOLD);
                }
                u16 c = (i < n && (greyed & (1u << i))) ? C_GREY : (i == sel ? C_GOLD : C_WHITE);
                draw_text(x + 18, oy, t, c);
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
static int record(int present, int question)
{
    static u16 saved_screen[SCREEN_W * SCREEN_H] EWRAM_BSS;
    static int tab, sel[2];
    u8 list[64];
    int top = 0, result = -1;

    plat_copy32(saved_screen, fb, SCREEN_W * SCREEN_H / 2);
    plat_sfx(SFX_OK);
    if (present) tab = 0;

    for (int redraw = 1;;) {
        int n = collect(tab, list);
        if (sel[tab] >= n) sel[tab] = n ? n - 1 : 0;
        if (sel[tab] < top) top = sel[tab];
        if (sel[tab] >= top + REC_ROWS) top = sel[tab] - REC_ROWS + 1;

        if (redraw) {
            fill(0, 0, SCREEN_W, SCREEN_H, RGB(3, 3, 7));
            for (int y = 0; y < SCREEN_H; y += 4) fill(0, y, SCREEN_W, 1, RGB(4, 4, 9));
            if (present) {
                fill(0, 0, SCREEN_W, 19, C_RED);
                draw_text(8, 3, question, C_WHITE);
            } else {
                fill(0, 18, SCREEN_W, 1, C_BORDER);
                draw_tab(6, UI_TAB_EVIDENCE, tab == 0);
                draw_tab(6 + text_width(UI_TAB_EVIDENCE, 1) + 18, UI_TAB_PROFILE, tab == 1);
                draw_hearts();
            }
            int hint = present ? UI_PRESENT_HINT : UI_RECORD_HINT;
            draw_text(144 - text_width(hint, 1), 86, hint, C_GREY);

            if (n == 0) {
                draw_text(12, REC_LIST_Y + 4, UI_EMPTY, C_GREY);
            }
            for (int r = 0; r < REC_ROWS && top + r < n; r++) {
                int i = top + r, y = REC_LIST_Y + r * 13;
                int name = tab == 0 ? ev_name[list[i]] : char_name[list[i]];
                if (i == sel[tab]) {
                    fill(4, y, 156, 13, C_HILITE);
                    draw_cursor(7, y + 3, C_GOLD);
                }
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
                if (tab == 0) {
                    fill(168, 22, ICON_SIZE + 4, ICON_SIZE + 4, C_BORDER);
                    fill(170, 24, ICON_SIZE, ICON_SIZE, RGB(1, 1, 3));
                    blit_keyed(170, 24, ICON_SIZE, ICON_SIZE, icon_img + ev_icon[item] * ICON_SIZE * ICON_SIZE);
                    draw_text(8, REC_DESC_Y + 3, ev_desc[item], C_WHITE);
                } else {
                    fill(168, 18, THUMB_W + 4, THUMB_H + 2, C_BORDER);
                    fill(170, 20, THUMB_W, THUMB_H, char_color[item]);
                    if (char_portrait[item] != NONE)
                        blit_keyed(170, 20, THUMB_W, THUMB_H, portrait_thumb[char_portrait[item]]);
                    draw_text(8, REC_DESC_Y + 3, prof_text[item], C_WHITE);
                }
            }
            redraw = 0;
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
                sel[tab] = (sel[tab] + ((keys_new & KEY_UP) ? n - 1 : 1)) % n;
                plat_sfx(SFX_MOVE);
                redraw = 1;
            }
        } else if (!present && (keys_new & (KEY_L | KEY_R | KEY_LEFT | KEY_RIGHT))) {
            tab ^= 1;
            top = 0;
            plat_sfx(SFX_MOVE);
            redraw = 1;
        } else if (present && (keys_new & KEY_A) && n) {
            result = list[sel[tab]];
            break;
        } else if (!present && (keys_new & (KEY_B | KEY_START))) {
            plat_sfx(SFX_CANCEL);
            break;
        }
    }
    plat_copy32(fb, saved_screen, SCREEN_W * SCREEN_H / 2);
    mark(0, SCREEN_H);
    return result;
}

/* ------------------------------------------------------------------ save data (SRAM) */

#define SAVE_MAGIC 0x344D4A4Bu /* "KJM4" (v4: 64 evidence items) */

typedef struct {
    u32 magic;
    u32 ev, ev_hi, prof;
    u16 pc, scene, gameover;
    u8 lives, max_lives;
    u16 prof_text[32];
    u32 flags[32];
    u32 check;
} SaveData;

static u32 save_sum(const SaveData *s)
{
    u32 sum = s->ev * 3 + s->ev_hi * 23 + s->prof * 5 + s->pc * 7 + s->scene * 11 + s->gameover * 13 + s->lives * 17 +
              s->max_lives * 19 + 0x1234;
    for (int i = 0; i < 32; i++) sum = sum * 31 + s->prof_text[i];
    for (int i = 0; i < 32; i++) sum = sum * 37 + s->flags[i];
    return sum;
}

static void save_game(u16 pc)
{
    SaveData s;
    s.magic = SAVE_MAGIC;
    s.ev = (u32)ev_flags;
    s.ev_hi = (u32)(ev_flags >> 32);
    s.prof = prof_flags;
    s.pc = pc;
    s.scene = cur_scene;
    s.gameover = gameover_pc;
    s.lives = lives;
    s.max_lives = max_lives;
    for (int i = 0; i < 32; i++) s.prof_text[i] = prof_text[i];
    for (int i = 0; i < 32; i++) s.flags[i] = flags[i];
    s.check = save_sum(&s);
    plat_sram_write(&s, 0, sizeof s);
}

static int load_game(SaveData *s)
{
    plat_sram_read(s, 0, sizeof *s);
    return s->magic == SAVE_MAGIC && s->check == save_sum(s);
}

/* ------------------------------------------------------------------ interpreter */

static int run(u16 pc);

/* Option menu shared by @investigate and @menu. opts: n entries of (text, label, cond, mark) where
 * mark = flag remembering the option was chosen (greys it out), bit 15 = trap (test harness avoids it).
 * need: evidence required before the exit option works (0 = exit any time). */
static int option_menu(int id, int spk, int q, int exit_text, u64 need, int n, const u16 *opts)
{
    for (;;) {
        u16 texts[8], idx[8];
        u32 grey = 0, traps = 0;
        int m = 0;
        for (int i = 0; i < n && m < 7; i++) {
            const u16 *o = opts + i * 4;
            if (!cond_true(o[2])) continue;
            if (flag_get(o[3] & 0x7FFF)) grey |= 1u << m;
            if (o[3] & 0x8000) traps |= 1u << m;
            texts[m] = o[0];
            idx[m++] = i;
        }
        int sel = choose(spk, q, texts, m, grey, exit_text, DBG_MENU, id, traps);
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

static void banner(int kind)
{
    int t = kind ? UI_BANNER_DEDUCE : UI_BANNER_INVEST;
    u16 band = kind ? RGB(20, 2, 4) : RGB(3, 6, 18);
    save_screen();
    plat_sfx(kind ? SFX_OBJECTION : SFX_GET);
    for (int f = 0; f < 90; f++) {
        int off = f < 12 ? (12 - f) * 20 : f > 78 ? -(f - 78) * 20 : 0;
        restore_screen();
        shade(0, 0, SCREEN_W, SCREEN_H, 0);
        fill(off, 54, SCREEN_W, 52, band);
        fill(off, 54, SCREEN_W, 2, C_GOLD);
        fill(off, 104, SCREEN_W, 2, C_GOLD);
        /* magnifying glass (invest) or exclamation (deduce) */
        int gx = off + 26, gy = 66;
        if (!kind) {
            for (int a = 0; a < 64; a++) {
                static const s16 c[16] = {0, 38, 71, 92, 100, 92, 71, 38, 0, -38, -71, -92, -100, -92, -71, -38};
                int dx = c[a & 15] * 11 / 100, dy = c[(a + 4) & 15] * 11 / 100;
                fill(gx + 12 + dx, gy + 12 + dy, 2, 2, C_WHITE);
            }
            for (int i = 0; i < 10; i++) fill(gx + 20 + i, gy + 20 + i, 3, 3, C_GOLD);
        } else {
            fill(gx + 10, gy, 6, 20, C_WHITE);
            fill(gx + 10, gy + 24, 6, 6, C_WHITE);
        }
        draw_disp(t, off + SCREEN_W / 2 + 14, 80);
        if (f == 12) {
            plat_debug_event("banner", kind);
            shake(6, 2);
        }
        frame();
        if (f > 20 && f < 78 && (keys_new & KEY_A)) f = 78;
    }
    restore_screen();
    frame();
}

/* Video player: step through frames and stop on the suspicious one. Returns frame index or -1. */
static int video(int title, int n, const u16 *frames)
{
    int cur = 0;
    for (int redraw = 1;;) {
        if (redraw) {
            const u16 *f = frames + cur * 3;
            plat_copy32(fb, scene_img[f[0]], SCREEN_W * SCREEN_H / 2);
            mark(0, SCREEN_H);
            for (int y = 0; y < BOX_Y; y += 2) shade(0, y, SCREEN_W, 1, RGB(0, 6, 2)); /* scanlines */
            fill(0, 0, SCREEN_W, 16, RGB(1, 2, 1));
            draw_text(8, 2, title, RGB(10, 31, 12));
            draw_text(SCREEN_W - 8 - text_width(f[1], 1), 2, f[1], RGB(10, 31, 12));
            if (cur > 0) for (int r = 0; r < 4; r++) fill(4 + r, 56 - r, 1, 1 + 2 * r, C_WHITE);
            if (cur < n - 1) for (int r = 0; r < 4; r++) fill(SCREEN_W - 5 - r, 56 - r, 1, 1 + 2 * r, C_WHITE);
            shade(0, BOX_Y, SCREEN_W, SCREEN_H - BOX_Y, C_BOX);
            fill(0, BOX_Y, SCREEN_W, 1, RGB(10, 31, 12));
            draw_text(TEXT_X, TEXT_Y, f[2], C_WHITE);
            draw_text(SCREEN_W - 8 - text_width(UI_VIDEO_HINT, 1), 146, UI_VIDEO_HINT, C_GREY);
            save_screen();
            redraw = 0;
        }
        restore_rect(SCREEN_W - 70, 2, 10, 10);
        if ((frame_count >> 4) & 1) fill(SCREEN_W - 68, 4, 7, 7, C_RED); /* REC lamp */
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
            fill(0, 6, SCREEN_W, 14, RGB(2, 10, 4));
            fill(0, 6, SCREEN_W, 1, RGB(10, 31, 12));
            fill(0, 19, SCREEN_W, 1, RGB(10, 31, 12));
            draw_text(8, 7, title, RGB(16, 31, 16));
            draw_hearts();
            draw_box(spk);
            draw_text(TEXT_X, TEXT_Y, st[0], RGB(20, 31, 20));
            draw_text(SCREEN_W - 8 - text_width(UI_TESTI_HINT, 1), 146, UI_TESTI_HINT, C_GREY);
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
    draw_text_ex(SCREEN_W / 2, 140, UI_PRESS_A, C_GREY, 1, 1);
    fade_in();
    static const char *const events[5] = {"bad_end", "true_end", "good_end", "best_end", "normal_end"};
    plat_debug_event(events[kind <= 4 ? kind : 1], t);
    wait_a(40);
    fade_out();
}

static int run(u16 pc)
{
    const u16 *S = script;
    for (;;) {
        u16 op_pc = pc;
        u16 op = S[pc++];
        switch (op) {
        case OP_SAY: {
            int spk = S[pc], t = S[pc + 1], por = S[pc + 2];
            pc += 3;
            say(spk, t, por);
            break;
        }
        case OP_SCENE:
            fade_out();
            cut_mode = S[pc] >> 15;
            cur_scene = S[pc++] & 0x7FFF;
            cur_portrait = NONE;
            draw_scene();
            fade_in();
            break;
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
        case OP_PLACE:
            place_caption(S[pc++]);
            break;
        case OP_CHAPTER:
            save_game(op_pc);
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
            int sel = video(title, n, frames);
            if (sel >= 0 && run(sel == correct ? ok : wrong) == RET_TITLE) return RET_TITLE;
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

static int title_screen(int has_save)
{
    int sel = has_save ? 1 : 0;
    in_game = 0;
    cur_scene = SCENE_TITLE;
    cur_portrait = NONE;
    draw_scene();
    shade(0, 22, SCREEN_W, 58, 0);
    draw_disp(UI_TITLE_SERIES, SCREEN_W / 2, 33);
    draw_disp(UI_TITLE_MAIN, SCREEN_W / 2, 58);
    shade(SCREEN_W / 2 - 56, 86, 112, 34, 0);
    shade(SCREEN_W / 2 - 56, 86, 112, 34, 0);
    frame_rect(SCREEN_W / 2 - 56, 86, 112, 34, C_BORDER);
    shade(0, 140, SCREEN_W, 16, 0);
    draw_text_ex(SCREEN_W / 2, 142, UI_TITLE_FAN, C_GREY, 1, 1);
    save_screen();
    fade_in();
    for (int redraw = 1;;) {
        if (redraw) {
            restore_screen();
            for (int i = 0; i < 2; i++) {
                int t = i ? UI_CONTINUE : UI_NEW, y = 89 + i * 14;
                u16 c = (i == 1 && !has_save) ? C_GREY : (i == sel ? C_GOLD : C_WHITE);
                if (i == sel) draw_cursor(SCREEN_W / 2 - 40, y + 3, C_GOLD);
                draw_text_ex(SCREEN_W / 2, y, t, c, 1, 1);
            }
            redraw = 0;
        }
        plat_debug_event("title", has_save);
        int d = plat_debug_choice(DBG_TITLE, 2);
        if (d >= 0) {
            sel = d;
            break;
        }
        frame();
        if ((keys_new & (KEY_UP | KEY_DOWN)) && has_save) {
            sel ^= 1;
            plat_sfx(SFX_MOVE);
            redraw = 1;
        }
        if (keys_new & (KEY_A | KEY_START)) break;
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
        int has_save = load_game(&s);
        int choice = title_screen(has_save);
        u16 pc = 0;
        lives = max_lives = 5;
        ev_flags = prof_flags = 0;
        gameover_pc = 0;
        cur_scene = SCENE_BLACK;
        cut_mode = 0;
        for (int i = 0; i < 32; i++) prof_text[i] = i < CHAR_COUNT ? char_profile[i] : NONE;
        for (int i = 0; i < 32; i++) flags[i] = 0;
        if (choice == 1 && has_save) {
            ev_flags = s.ev | ((u64)s.ev_hi << 32);
            prof_flags = s.prof;
            lives = s.lives;
            max_lives = s.max_lives;
            gameover_pc = s.gameover;
            for (int i = 0; i < 32; i++) prof_text[i] = s.prof_text[i];
            for (int i = 0; i < 32; i++) flags[i] = s.flags[i];
            cur_scene = s.scene;
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
