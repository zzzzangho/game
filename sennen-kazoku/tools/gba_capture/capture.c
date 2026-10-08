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

static void quiet(struct mLogger* l, int cat, enum mLogLevel lv, const char* fmt, va_list a) { (void)l; (void)cat; (void)lv; (void)fmt; (void)a; }

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
            for (int i = 0; i < n; i++) { c->setKeys(c, keys); c->runFrame(c); frame++; }
        } else if (!strcmp(cmd, "tap") && sscanf(line, "%*s %i %d", &keys, &n) >= 1) {   /* 키 6프레임 누르고 n프레임 대기 */
            for (int i = 0; i < 6; i++) { c->setKeys(c, keys); c->runFrame(c); frame++; }
            for (int i = 0; i < (n ? n : 30); i++) { c->setKeys(c, 0); c->runFrame(c); frame++; }
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
