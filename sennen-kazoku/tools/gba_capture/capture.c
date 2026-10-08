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
            for (int i = 0; i < n; i++) { c->setKeys(c, keys); c->runFrame(c); frame++; g_frame = frame; }
        } else if (!strcmp(cmd, "tap") && sscanf(line, "%*s %i %d", &keys, &n) >= 1) {   /* 키 6프레임 누르고 n프레임 대기 */
            for (int i = 0; i < 6; i++) { c->setKeys(c, keys); c->runFrame(c); frame++; g_frame = frame; }
            for (int i = 0; i < (n ? n : 30); i++) { c->setKeys(c, 0); c->runFrame(c); frame++; g_frame = frame; }
        } else if (!strcmp(cmd, "shot") && sscanf(line, "%*s %199s", arg) == 1) {
            char p[512]; snprintf(p, sizeof p, "%s/%s.ppm", argv[2], arg);
            FILE* f = fopen(p, "wb"); fprintf(f, "P6 240 160 255\n");
            for (int i = 0; i < 240 * 160; i++) { unsigned v = pix[i]; unsigned char rgb[3] = { v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF }; fwrite(rgb, 1, 3, f); }
            fclose(f); printf("shot %s @%ld\n", arg, frame);
        } else if (!strcmp(cmd, "layer") && sscanf(line, "%*s %d %i", &n, &keys) == 2) {   /* layer <0-3 BG, 4 OBJ> <0|1> */
            c->enableVideoLayer(c, n, keys != 0);
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
                c->setKeys(c, keys); c->runFrame(c); frame++; g_frame = frame;
                if (i % every == 0) {
                    char p[512]; snprintf(p, sizeof p, "%s/%s_%06ld.mem", argv[2], arg, frame); FILE* f = fopen(p, "wb");
                    for (unsigned a = 0x05000000; a < 0x05000400; a++) fputc(c->busRead8(c, a), f);
                    for (unsigned a = 0x06000000; a < 0x06018000; a++) fputc(c->busRead8(c, a), f);
                    for (unsigned a = 0x07000000; a < 0x07000400; a++) fputc(c->busRead8(c, a), f);
                    for (unsigned a = 0x04000000; a < 0x04000060; a++) fputc(c->busRead8(c, a), f);
                    fclose(f);
                }
            }
        } else if (!strcmp(cmd, "runshot")) {   /* runshot <프레임> <키> <간격> <접두어> : 간격마다 화면 저장 */
            int every = 1; if (sscanf(line, "%*s %d %i %d %199s", &n, &keys, &every, arg) != 4) continue;
            for (int i = 0; i < n; i++) {
                c->setKeys(c, keys); c->runFrame(c); frame++; g_frame = frame;
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
                for (int k = 0; k < (getenv("SWEEP_SETTLE") ? atoi(getenv("SWEEP_SETTLE")) : 3); k++) { c->busWrite16(c, addr, (uint16_t)v); if (getenv("SWEEP_ALL")) { c->busWrite16(c, addr - 4, (uint16_t)v); c->busWrite16(c, addr - 8, (uint16_t)v); c->busWrite16(c, 0x03007dd8, (uint16_t)v); } c->setKeys(c, 0); c->runFrame(c); frame++; g_frame = frame; }
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
