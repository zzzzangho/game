/* 원작 화면 관찰용 헤드리스 캡처 (로컬 전용, 결과물을 저장소에 커밋하지 말 것).
   사용: capture <rom.gba> <출력디렉터리> <스크립트>   [save=<sav 경로>]
   스크립트 줄: "run <프레임> <키마스크>" | "shot <이름>" | "savestate <이름>" | "loadstate <이름>"
   키: A=1 B=2 SELECT=4 START=8 →=16 ←=32 ↑=64 ↓=128 R=256 L=512 */
#include <stdarg.h>
#include <mgba/core/core.h>
#include <mgba/core/log.h>
#include <mgba/core/serialize.h>
#include <mgba/gba/core.h>
#include <mgba-util/vfs.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>

static long g_frame = 0;
static void quiet(struct mLogger* l, int cat, enum mLogLevel lv, const char* fmt, va_list a) {
    (void)l; (void)cat; (void)lv;
    if (getenv("CAP_LOG") && (strstr(fmt, "SWI") || strstr(fmt, "DMA"))) { printf("[%ld] ", g_frame); vprintf(fmt, a); printf("\n"); }
}

#include <mgba/debugger/debugger.h>
/* ---- 쓰기 감시점: 원작 코드가 특정 RAM 을 쓰는 순간의 PC·LR·값 기록 (규칙 역분석용) ---- */
static FILE* g_wlog; static struct mCore* g_core;
static void bp_log(void);
static void wp_entered(struct mDebugger* d, enum mDebuggerEntryReason r, struct mDebuggerEntryInfo* info) {
    if (g_wlog && r == DEBUGGER_ENTER_BREAKPOINT) bp_log();
    if (g_wlog && info && r == DEBUGGER_ENTER_WATCHPOINT) {
        uint32_t pc = 0, lr = 0; g_core->readRegister(g_core, "pc", &pc); g_core->readRegister(g_core, "lr", &lr);
        fprintf(g_wlog, "%ld %08x pc=%08x lr=%08x %u->%u\n", g_frame, info->address, pc, lr, info->type.wp.oldValue, info->type.wp.newValue);
    }
    d->state = DEBUGGER_RUNNING;
}
static void wp_paused(struct mDebugger* d) { d->state = DEBUGGER_RUNNING; }
/* 실행 중단점: 그 주소를 실행하는 순간의 프레임·PC·LR·r0~r3 기록 */
static void bp_log(void) {
    uint32_t r[6] = {0}; const char* nm[6] = { "pc", "lr", "r0", "r1", "r2", "r3" };
    for (int i = 0; i < 6; i++) g_core->readRegister(g_core, nm[i], &r[i]);
    fprintf(g_wlog, "%ld BP pc=%08x lr=%08x r0=%08x r1=%08x r2=%08x r3=%08x\n", g_frame, r[0], r[1], r[2], r[3], r[4], r[5]);
    static int ndump = 0; const char* dd = getenv("CAP_BPDUMP");   /* 중단점마다 RAM 덤프 (앞 20개) */
    if (dd && ndump < 20) {
        char p[512]; snprintf(p, sizeof p, "%s_%02d_%08x.ram", dd, ndump++, r[0]); FILE* f = fopen(p, "wb");
        for (unsigned a = 0x02000000; a < 0x02040000; a++) fputc(g_core->busRead8(g_core, a), f);
        for (unsigned a = 0x03000000; a < 0x03008000; a++) fputc(g_core->busRead8(g_core, a), f);
        fclose(f);
    }
}
static struct mDebugger g_dbg;

int main(int argc, char** argv) {
    static struct mLogger lg = { .log = quiet }; mLogSetDefaultLogger(&lg);
    if (argc < 4) { fprintf(stderr, "usage\n"); return 2; }
    struct mCore* c = GBACoreCreate(); c->init(c); mCoreConfigInit(&c->config, NULL);
    color_t* pix = calloc(240 * 160, sizeof(color_t)); c->setVideoBuffer(c, pix, 240);
    if (!mCoreLoadFile(c, argv[1])) { fprintf(stderr, "rom load fail\n"); return 2; }
    if (argc > 4 && !strncmp(argv[4], "save=", 5)) c->loadTemporarySave(c, VFileOpen(argv[4] + 5, O_RDONLY));
    c->reset(c);
    FILE* sc = fopen(argv[3], "r"); char line[256]; long frame = 0;
    while (fgets(line, sizeof line, sc)) {
        char cmd[32], arg[200]; int n = 0, keys = 0;
        if (sscanf(line, "%31s", cmd) != 1 || cmd[0] == '#') continue;
        if (!strcmp(cmd, "run") && sscanf(line, "%*s %d %i", &n, &keys) >= 1) {
            for (int i = 0; i < n; i++) { c->setKeys(c, keys); (g_dbg.platform ? mDebuggerRunFrame(&g_dbg) : c->runFrame(c)); frame++; g_frame = frame; }
        } else if (!strcmp(cmd, "tap") && sscanf(line, "%*s %i %d", &keys, &n) >= 1) {   /* 키 6프레임 누르고 n프레임 대기 */
            for (int i = 0; i < 6; i++) { c->setKeys(c, keys); (g_dbg.platform ? mDebuggerRunFrame(&g_dbg) : c->runFrame(c)); frame++; g_frame = frame; }
            for (int i = 0; i < (n ? n : 30); i++) { c->setKeys(c, 0); (g_dbg.platform ? mDebuggerRunFrame(&g_dbg) : c->runFrame(c)); frame++; g_frame = frame; }
        } else if (!strcmp(cmd, "shot") && sscanf(line, "%*s %199s", arg) == 1) {
            char p[512]; snprintf(p, sizeof p, "%s/%s.ppm", argv[2], arg);
            FILE* f = fopen(p, "wb"); fprintf(f, "P6 240 160 255\n");
            for (int i = 0; i < 240 * 160; i++) { unsigned v = pix[i]; unsigned char rgb[3] = { v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF }; fwrite(rgb, 1, 3, f); }
            fclose(f); printf("shot %s @%ld\n", arg, frame);
        } else if (!strcmp(cmd, "layer") && sscanf(line, "%*s %d %i", &n, &keys) == 2) {   /* layer <0-3 BG, 4 OBJ> <0|1> */
            c->enableVideoLayer(c, n, keys != 0);
        } else if (!strcmp(cmd, "ramdump") && sscanf(line, "%*s %199s", arg) == 1) {       /* EWRAM(256K)+IWRAM(32K) 덤프 */
            char p[512]; snprintf(p, sizeof p, "%s/%s.ram", argv[2], arg); FILE* f = fopen(p, "wb");
            for (unsigned a = 0x02000000; a < 0x02040000; a++) fputc(c->busRead8(c, a), f);
            for (unsigned a = 0x03000000; a < 0x03008000; a++) fputc(c->busRead8(c, a), f);
            fclose(f); printf("ramdump %s @%ld\n", arg, frame);
        } else if (!strcmp(cmd, "dump") && sscanf(line, "%*s %199s", arg) == 1) {          /* VRAM·팔레트·OAM·IO 원시 덤프 */
            char p[512]; snprintf(p, sizeof p, "%s/%s.mem", argv[2], arg); FILE* f = fopen(p, "wb");
            for (unsigned a = 0x05000000; a < 0x05000400; a++) fputc(c->busRead8(c, a), f);
            for (unsigned a = 0x06000000; a < 0x06018000; a++) fputc(c->busRead8(c, a), f);
            for (unsigned a = 0x07000000; a < 0x07000400; a++) fputc(c->busRead8(c, a), f);
            for (unsigned a = 0x04000000; a < 0x04000060; a++) fputc(c->busRead8(c, a), f);
            fclose(f);
        } else if (!strcmp(cmd, "rundump")) {   /* rundump <프레임> <키> <간격> <접두어> : 간격마다 메모리 덤프 */
            int every = 1; if (sscanf(line, "%*s %d %i %d %199s", &n, &keys, &every, arg) != 4) continue;
            for (int i = 0; i < n; i++) {
                c->setKeys(c, keys); (g_dbg.platform ? mDebuggerRunFrame(&g_dbg) : c->runFrame(c)); frame++; g_frame = frame;
                if (i % every == 0) {
                    char p[512]; snprintf(p, sizeof p, "%s/%s_%06ld.mem", argv[2], arg, frame); FILE* f = fopen(p, "wb");
                    for (unsigned a = 0x05000000; a < 0x05000400; a++) fputc(c->busRead8(c, a), f);
                    for (unsigned a = 0x06000000; a < 0x06018000; a++) fputc(c->busRead8(c, a), f);
                    for (unsigned a = 0x07000000; a < 0x07000400; a++) fputc(c->busRead8(c, a), f);
                    for (unsigned a = 0x04000000; a < 0x04000060; a++) fputc(c->busRead8(c, a), f);
                    fclose(f);
                }
            }
        } else if (!strcmp(cmd, "rtc")) {   /* rtc <0 없음|1 고정|2 가짜 기점|3 실제시계+차이> <값(ms)> : 카트리지 시계 덮어쓰기 */
            long long v = 0; if (sscanf(line, "%*s %d %lld", &n, &v) != 2) continue;
            c->rtc.override = (enum mRTCGenericType)n; c->rtc.value = v;
            printf("rtc %d %lld\n", n, v);
        } else if (!strcmp(cmd, "poke")) {   /* poke <주소> <값> <크기 1|2|4> : 메모리 값 바꾸기 (원작 경로를 일부러 일으켜 관찰할 때) */
            unsigned addr = 0, val = 0; int sz = 1; if (sscanf(line, "%*s %i %i %d", &addr, &val, &sz) < 2) continue;
            if (sz == 4) c->busWrite32(c, addr, val); else if (sz == 2) c->busWrite16(c, addr, val); else c->busWrite8(c, addr, val);
            printf("poke %08x=%x\n", addr, val);
        } else if (!strcmp(cmd, "bp")) {   /* bp <주소> <파일> : 실행 중단점 추가(기록 파일은 watch 와 같음) */
            unsigned addr = 0; if (sscanf(line, "%*s %i %199s", &addr, arg) != 2) continue;
            if (!g_dbg.platform) {
                memset(&g_dbg, 0, sizeof g_dbg); g_dbg.entered = wp_entered; g_dbg.paused = wp_paused; g_core = c;
                mDebuggerAttach(&g_dbg, c); g_dbg.state = DEBUGGER_RUNNING;
                char p[512]; snprintf(p, sizeof p, "%s/%s", argv[2], arg); g_wlog = fopen(p, "w");
            }
            struct mBreakpoint bp; memset(&bp, 0, sizeof bp); bp.address = addr & ~1u; bp.segment = -1; bp.type = BREAKPOINT_HARDWARE;
            g_dbg.platform->setBreakpoint(g_dbg.platform, &bp);
        } else if (!strcmp(cmd, "watch")) {   /* watch <주소> <파일> : 쓰기 감시점 추가(이후 run/autolog 중 기록) */
            unsigned addr = 0; if (sscanf(line, "%*s %i %199s", &addr, arg) != 2) continue;
            if (!g_dbg.platform) {
                memset(&g_dbg, 0, sizeof g_dbg); g_dbg.entered = wp_entered; g_dbg.paused = wp_paused; g_core = c;
                mDebuggerAttach(&g_dbg, c); g_dbg.state = DEBUGGER_RUNNING;
                char p[512]; snprintf(p, sizeof p, "%s/%s", argv[2], arg); g_wlog = fopen(p, "w");
            }
            struct mWatchpoint wp; memset(&wp, 0, sizeof wp); wp.address = addr; wp.segment = -1; wp.type = WATCHPOINT_WRITE;
            g_dbg.platform->setWatchpoint(g_dbg.platform, &wp);
            printf("watch %08x\n", addr);
        } else if (!strcmp(cmd, "autolog")) {
            /* autolog <프레임> <누르고 있을 키> <간격> <주소> <길이> <파일> : 화면 아래쪽에 흰 글상자가 보일 때만 A 를 눌러
               대사를 넘긴다(본화면에서 A 는 활쏘기라 누르지 않는다). 간격마다 RAM 구간 기록. */
            unsigned addr = 0, len = 0; int every = 1, hold = 0;
            if (sscanf(line, "%*s %d %i %d %i %i %199s", &n, &hold, &every, &addr, &len, arg) != 6) continue;
            char p[512]; snprintf(p, sizeof p, "%s/%s", argv[2], arg); FILE* f = fopen(p, "ab");
            int press = 0, presses = 0;
            for (int i = 0; i < n; i++) {
                if (press == 0 && i % 20 == 0) {
                    int white = 0;
                    for (int y = 112; y < 158; y++) for (int x = 8; x < 232; x++) {
                        unsigned v = pix[y * 240 + x]; if ((v & 0xFF) > 230 && ((v >> 8) & 0xFF) > 230 && ((v >> 16) & 0xFF) > 230) white++;
                    }
                    if (white > 4500) { press = 12; presses++; }
                }
                c->setKeys(c, hold | (press > 6 ? 1 : 0)); if (press) press--;
                (g_dbg.platform ? mDebuggerRunFrame(&g_dbg) : c->runFrame(c)); frame++; g_frame = frame;
                if (i % every == 0) {
                    unsigned fr = (unsigned)frame; fwrite(&fr, 4, 1, f);
                    for (unsigned a = addr; a < addr + len; a++) fputc(c->busRead8(c, a), f);
                }
            }
            fclose(f); printf("autolog presses %d\n", presses);
        } else if (!strcmp(cmd, "ramlog")) {   /* ramlog <프레임> <키> <간격> <주소> <길이> <파일> : 간격마다 RAM 구간을 [u32 프레임][바이트] 로 이어 붙인다 */
            unsigned addr = 0, len = 0; int every = 1;
            if (sscanf(line, "%*s %d %i %d %i %i %199s", &n, &keys, &every, &addr, &len, arg) != 6) continue;
            char p[512]; snprintf(p, sizeof p, "%s/%s", argv[2], arg); FILE* f = fopen(p, "ab");
            for (int i = 0; i < n; i++) {
                c->setKeys(c, keys); (g_dbg.platform ? mDebuggerRunFrame(&g_dbg) : c->runFrame(c)); frame++; g_frame = frame;
                if (i % every == 0) {
                    unsigned fr = (unsigned)frame; fwrite(&fr, 4, 1, f);
                    for (unsigned a = addr; a < addr + len; a++) fputc(c->busRead8(c, a), f);
                }
            }
            fclose(f);
        } else if (!strcmp(cmd, "runshot")) {   /* runshot <프레임> <키> <간격> <접두어> : 간격마다 화면 저장 */
            int every = 1; if (sscanf(line, "%*s %d %i %d %199s", &n, &keys, &every, arg) != 4) continue;
            for (int i = 0; i < n; i++) {
                c->setKeys(c, keys); (g_dbg.platform ? mDebuggerRunFrame(&g_dbg) : c->runFrame(c)); frame++; g_frame = frame;
                if (i % every == 0) {
                    char p[512]; snprintf(p, sizeof p, "%s/%s_%06ld.ppm", argv[2], arg, frame);
                    FILE* f = fopen(p, "wb"); fprintf(f, "P6 240 160 255\n");
                    for (int k = 0; k < 240 * 160; k++) { unsigned v = pix[k]; unsigned char rgb[3] = { v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF }; fwrite(rgb, 1, 3, f); }
                    fclose(f);
                    if (getenv("CAP_RAM")) {   /* 같은 이름으로 WRAM 도 저장(카메라 변수 탐색용) */
                        snprintf(p, sizeof p, "%s/%s_%06ld.ram", argv[2], arg, frame); f = fopen(p, "wb");
                        for (unsigned a = 0x02000000; a < 0x02040000; a++) fputc(c->busRead8(c, a), f);
                        for (unsigned a = 0x03000000; a < 0x03008000; a++) fputc(c->busRead8(c, a), f);
                        fclose(f);
                    }
                }
            }
        } else if (!strcmp(cmd, "sweep")) {   /* sweep <주소> <시작> <끝> <간격> <접두어> : 16비트 변수를 바꿔 가며 화면 저장 */
            unsigned addr; int v0, v1, st; if (sscanf(line, "%*s %i %d %d %d %199s", &addr, &v0, &v1, &st, arg) != 5) continue;
            for (int v = v0; v <= v1; v += st) {
                if (getenv("SWEEP_RELOAD")) {   /* 위치마다 같은 상태에서 시작 → 시간대(팔레트) 고정 */
                    char sp[512]; snprintf(sp, sizeof sp, "%s/%s.state", argv[2], getenv("SWEEP_RELOAD"));
                    struct VFile* vf = VFileOpen(sp, O_RDONLY); mCoreLoadStateNamed(c, vf, SAVESTATE_ALL); vf->close(vf);
                }
                for (int k = 0; k < (getenv("SWEEP_SETTLE") ? atoi(getenv("SWEEP_SETTLE")) : 3); k++) { c->busWrite16(c, addr, (uint16_t)v); if (getenv("SWEEP_ALL")) { c->busWrite16(c, addr - 4, (uint16_t)v); c->busWrite16(c, addr - 8, (uint16_t)v); c->busWrite16(c, 0x03007dd8, (uint16_t)v); } c->setKeys(c, 0); (g_dbg.platform ? mDebuggerRunFrame(&g_dbg) : c->runFrame(c)); frame++; g_frame = frame; }
                char p[512]; snprintf(p, sizeof p, "%s/%s_%05d.ppm", argv[2], arg, v);
                FILE* f = fopen(p, "wb"); fprintf(f, "P6 240 160 255\n");
                for (int k = 0; k < 240 * 160; k++) { unsigned q = pix[k]; unsigned char rgb[3] = { q & 0xFF, (q >> 8) & 0xFF, (q >> 16) & 0xFF }; fwrite(rgb, 1, 3, f); }
                fclose(f);
            }
        } else if (!strcmp(cmd, "poke8")) {   /* poke8 <주소> <값> */
            unsigned addr, val; if (sscanf(line, "%*s %i %i", &addr, &val) == 2) c->busWrite8(c, addr, (uint8_t)val);
        } else if (!strcmp(cmd, "savepoke")) {   /* savepoke <세이브 오프셋> <값> : 세이브(플래시) 데이터 직접 수정 */
            unsigned off, val; void* sram = NULL;
            if (sscanf(line, "%*s %i %i", &off, &val) == 2) {
                size_t n = c->savedataClone(c, &sram);
                if (sram && off < n) { ((uint8_t*)sram)[off] = (uint8_t)val; c->savedataRestore(c, sram, n, true); }
                free(sram);
            }
        } else if (!strcmp(cmd, "savedump") && sscanf(line, "%*s %199s", arg) == 1) {
            void* sram = NULL; size_t n = c->savedataClone(c, &sram);
            char p[512]; snprintf(p, sizeof p, "%s/%s.sav", argv[2], arg); FILE* f = fopen(p, "wb"); if (sram) fwrite(sram, 1, n, f); fclose(f); free(sram);
            printf("savedump %zu\n", n);
        } else if (!strcmp(cmd, "reset")) {
            c->reset(c);
        } else if (!strcmp(cmd, "savestate") && sscanf(line, "%*s %199s", arg) == 1) {
            char p[512]; snprintf(p, sizeof p, "%s/%s.state", argv[2], arg);
            struct VFile* vf = VFileOpen(p, O_CREAT | O_TRUNC | O_RDWR); mCoreSaveStateNamed(c, vf, SAVESTATE_ALL); vf->close(vf);
        } else if (!strcmp(cmd, "loadstate") && sscanf(line, "%*s %199s", arg) == 1) {
            char p[512]; snprintf(p, sizeof p, "%s/%s.state", argv[2], arg);
            struct VFile* vf = VFileOpen(p, O_RDONLY); mCoreLoadStateNamed(c, vf, SAVESTATE_ALL); vf->close(vf);
        }
    }
    return 0;
}
