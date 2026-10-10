/* SPDX-License-Identifier: MIT - EmuWorks contributors */
.syntax unified
.cpu cortex-m7
.thumb
.section .vectors,"a",%progbits
.global vectors
vectors:
  .word 0x20040000
  .word Reset_Handler
  .rept 14
  .word Fault_Handler
  .endr
.section .text.Reset_Handler,"ax",%progbits
.thumb_func
.global Reset_Handler
Reset_Handler:
  ldr r0, =0xE000ED88
  ldr r1, [r0]
  orr r1, r1, #0x00F00000
  str r1, [r0]
  dsb
  isb
  ldr r0, =_sidata
  ldr r1, =_sdata
  ldr r2, =_edata
1: cmp r1, r2
  bcs 2f
  ldr r3, [r0], #4
  str r3, [r1], #4
  b 1b
2: ldr r1, =_sbss
  ldr r2, =_ebss
  movs r3, #0
3: cmp r1, r2
  bcs 4f
  str r3, [r1], #4
  b 3b
4: bl main
  b Fault_Handler
.thumb_func
Fault_Handler:
  b Fault_Handler
