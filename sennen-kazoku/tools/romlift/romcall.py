"""검증 전용: unicorn 으로 원작 ROM 함수를 실제 RAM 덤프 위에서 실행한다 (앱에는 쓰지 않음)."""
from unicorn import Uc, UC_ARCH_ARM, UC_MODE_THUMB, UcError, UC_HOOK_MEM_UNMAPPED
from unicorn.arm_const import *
import struct

B = 0x08000000
STOP = 0x03007F00


class RomCpu:
    def __init__(self, rom, ram):
        self.mu = Uc(UC_ARCH_ARM, UC_MODE_THUMB)
        self.mu.mem_map(B, 0x02000000)
        self.mu.mem_write(B, rom)
        self.mu.mem_map(0x02000000, 0x40000)
        self.mu.mem_map(0x03000000, 0x8000)
        self.mu.mem_map(0x04000000, 0x1000)
        self.mu.mem_map(0x05000000, 0x1000)
        self.mu.mem_map(0x06000000, 0x20000)
        self.mu.mem_map(0x07000000, 0x1000)
        self.mu.mem_map(0x0E000000, 0x10000)
        self.set_ram(ram)
        self.mirrors = []
        self.mu.hook_add(UC_HOOK_MEM_UNMAPPED, self._unmapped)

    def _unmapped(self, mu, acc, addr, size, val, ud):
        # EWRAM 은 0x02000000~0x02FFFFFF 에 256KB 단위로 반복된다 (실기 동작)
        if 0x02040000 <= addr < 0x03000000:
            page = addr & ~0xFFF
            mu.mem_map(page, 0x1000)
            src = 0x02000000 + (page & 0x3FFFF)
            mu.mem_write(page, bytes(mu.mem_read(src, 0x1000)))
            self.mirrors.append(page)
            return True
        return False

    def set_ram(self, ram):
        self.mu.mem_write(0x02000000, ram[:0x40000])
        self.mu.mem_write(0x03000000, ram[0x40000:0x48000])

    def ram(self):
        return bytes(self.mu.mem_read(0x02000000, 0x40000)) + bytes(self.mu.mem_read(0x03000000, 0x8000))

    def call(self, fn, *args, limit=2_000_000):
        mu = self.mu
        sp = 0x03007E00
        regs = [UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2, UC_ARM_REG_R3]
        stack_args = args[4:]
        sp -= 4 * len(stack_args)
        for k, a in enumerate(stack_args): mu.mem_write(sp + 4 * k, struct.pack('<I', a & 0xFFFFFFFF))
        for k, a in enumerate(args[:4]): mu.reg_write(regs[k], a & 0xFFFFFFFF)
        mu.reg_write(UC_ARM_REG_SP, sp)
        mu.reg_write(UC_ARM_REG_LR, STOP | 1)
        mu.mem_write(STOP, b'\x00\xbf\x00\xbf')
        try:
            mu.emu_start(fn | 1, STOP, count=limit)
        finally:
            for pg in self.mirrors: mu.mem_unmap(pg, 0x1000)
            self.mirrors = []
        return mu.reg_read(UC_ARM_REG_R0)

    def r8(self, a): return self.mu.mem_read(a, 1)[0]
    def r16(self, a): return struct.unpack('<H', self.mu.mem_read(a, 2))[0]
    def r32(self, a): return struct.unpack('<I', self.mu.mem_read(a, 4))[0]
    def w32(self, a, v): self.mu.mem_write(a, struct.pack('<I', v & 0xFFFFFFFF))
