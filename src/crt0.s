@ Minimal GBA startup: header space, stacks, .data copy, .bss clear, jump to main.
    .section .crt0, "ax"
    .global _start
    .arm
_start:
    b       rom_start
    .fill   188, 1, 0          @ 0x04-0xBF: header, filled in by gbafix

rom_start:
    mov     r0, #0x12          @ IRQ mode
    msr     cpsr_c, r0
    ldr     sp, =0x03007FA0
    mov     r0, #0x1F          @ System mode
    msr     cpsr_c, r0
    ldr     sp, =0x03007F00

    ldr     r0, =__data_lma
    ldr     r1, =__data_start
    ldr     r2, =__data_end
1:  cmp     r1, r2
    ldrlo   r3, [r0], #4
    strlo   r3, [r1], #4
    blo     1b

    ldr     r1, =__bss_start
    ldr     r2, =__bss_end
    mov     r3, #0
2:  cmp     r1, r2
    strlo   r3, [r1], #4
    blo     2b

    ldr     r3, =main
    mov     lr, pc
    bx      r3
3:  b       3b

    .pool

    @ Save-type marker so emulators and flash carts detect battery SRAM.
    .section .rodata.savetype, "a"
    .align 2
    .ascii "SRAM_V113"
    .align 2
