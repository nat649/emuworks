/* SPDX-License-Identifier: MIT */
ENTRY(Reset_Handler)
MEMORY {
  FLASH (rx) : ORIGIN = 0x08000000, LENGTH = 64K
  RAM (rwx) : ORIGIN = 0x20000000, LENGTH = 256K
}
SECTIONS {
  .vectors : { KEEP(*(.vectors)) } > FLASH
  .identity 0x08000100 : { KEEP(*(.identity)) } > FLASH
  .text : { *(.text*) *(.rodata*) . = ALIGN(4); } > FLASH
  .data : { . = ALIGN(4); _sdata = .; *(.data*) . = ALIGN(4); _edata = .; } > RAM AT > FLASH
  _sidata = LOADADDR(.data);
  .bss (NOLOAD) : { . = ALIGN(4); _sbss = .; *(.bss*) *(COMMON) . = ALIGN(4); _ebss = .; } > RAM
  /DISCARD/ : { *(.comment*) *(.ARM.exidx*) *(.ARM.extab*) }
  ASSERT(_ebss < 0x2003C000, "Keep 16K for the stack")
}
