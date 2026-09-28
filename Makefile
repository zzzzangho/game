# 소년탐정 김전일 - 마술열차 살인사건 (GBA fan game)
#   make          -> kindaichi_magic_train.gba
#   make test     -> host build + automated playthroughs of every ending
#   make preview  -> PNG previews of all generated art in build/preview

PREFIX  ?= arm-none-eabi-
CC      := $(PREFIX)gcc
OBJCOPY := $(PREFIX)objcopy
HOSTCC  ?= cc
PYTHON  ?= python3

TARGET  := kindaichi_magic_train
BUILD   := build
FONT    := assets/fonts/Galmuri11-Condensed.bdf
STORY   := story/story.txt

ARCH    := -mthumb -mthumb-interwork -mcpu=arm7tdmi -mtune=arm7tdmi
CFLAGS  := $(ARCH) -O2 -Wall -Wextra -Wno-unused-parameter -ffreestanding -fno-builtin \
           -fno-tree-loop-distribute-patterns -ffunction-sections -fdata-sections \
           -Isrc -I$(BUILD)
LDFLAGS := $(ARCH) -T src/gba.ld -nostartfiles -nostdlib -Wl,--gc-sections

OBJS := $(BUILD)/crt0.o $(BUILD)/main.o $(BUILD)/plat_gba.o $(BUILD)/gen_data.o

.PHONY: all clean test preview

all: $(TARGET).gba

ASSETS  := $(wildcard assets/portraits/* assets/scenes/* assets/icons/*)
KMT_PIXEL ?= gbc
export KMT_PIXEL

$(BUILD)/gen_data.c $(BUILD)/gen_data.h: $(STORY) $(FONT) tools/build_assets.py tools/art.py $(ASSETS) assets/portraits assets/scenes assets/icons
	@mkdir -p $(BUILD)
	$(PYTHON) tools/build_assets.py --story $(STORY) --font $(FONT) --out $(BUILD)

$(BUILD)/crt0.o: src/crt0.s
	@mkdir -p $(BUILD)
	$(CC) $(ARCH) -c $< -o $@

$(BUILD)/main.o: src/main.c src/platform.h $(BUILD)/gen_data.h
	$(CC) $(CFLAGS) -c $< -o $@

$(BUILD)/plat_gba.o: src/plat_gba.c src/platform.h
	$(CC) $(CFLAGS) -c $< -o $@

$(BUILD)/gen_data.o: $(BUILD)/gen_data.c $(BUILD)/gen_data.h
	$(CC) $(CFLAGS) -O0 -c $< -o $@

$(BUILD)/$(TARGET).elf: $(OBJS) src/gba.ld
	$(CC) $(OBJS) $(LDFLAGS) -lgcc -o $@

$(BUILD)/gbafix: tools/gbafix.c
	$(HOSTCC) -O2 -o $@ $<

$(TARGET).gba: $(BUILD)/$(TARGET).elf $(BUILD)/gbafix
	$(OBJCOPY) -O binary $< $@
	$(BUILD)/gbafix $@ -tKINDAICHI_MT -cKMTJ -mFN -r0
	@ls -l $@

# ---- host test harness ----
$(BUILD)/host_game: src/main.c test/plat_pc.c src/platform.h $(BUILD)/gen_data.c $(BUILD)/gen_data.h
	$(HOSTCC) -O1 -g -DPC_BUILD -Wall -Isrc -I$(BUILD) src/main.c test/plat_pc.c $(BUILD)/gen_data.c -o $@

test: $(BUILD)/host_game
	$(PYTHON) test/playthrough.py $(BUILD)/host_game

preview:
	$(PYTHON) -c "import sys; sys.path.insert(0,'tools'); import art; art.write_previews('$(BUILD)/preview')"

clean:
	rm -rf $(BUILD) $(TARGET).gba
