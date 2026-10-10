# Third-party notices

EmuWorks Code's original port and UI are MIT-licensed; see `LICENSE`.

## MicroPython

- Upstream: https://github.com/micropython/micropython
- Version: 1.26.1, commit `647c8b96cae7e202c7a020395b7cfe65e5b8ce04`.
- License: MIT; full text in `licenses/MicroPython.txt`.
- The build compiles the interpreter core and shared Cortex-M garbage-collection helpers from the unmodified checkout fetched by `fetch.py`.

## Newlib and libm

The ARM build links the C and math libraries supplied with Arm GNU Toolchain 12.2 MPACBTI-Rel1. Newlib contains contributions under multiple permissive licenses. Their notice collection is reproduced in `licenses/Newlib.txt` from the upstream `newlib-4.3.0` source tree:

https://github.com/mirror/newlib-cygwin/blob/newlib-4.3.0/COPYING.NEWLIB

The bare-metal system-call stubs come from libgloss/libnosys in the same toolchain. Their notice collection is included in `licenses/Libgloss.txt` from the same upstream release.

## GCC runtime

The firmware also links GCC's low-level runtime (`libgcc`) from Arm GNU Toolchain. It is covered by GPLv3 with the GCC Runtime Library Exception 3.1. The license and exception are included in `licenses/GPL-3.0.txt` and `licenses/GCC-Runtime-Exception.txt`. The build uses GCC and GNU assembler from source-language inputs, without proprietary compiler plugins.

https://www.gnu.org/licenses/gcc-exception-3.1.html

These notices must accompany distributions of the compiled firmware, including the copy embedded in EmuWorks. The MIT license for our code does not replace a dependency's license. Epsilon and Omega are not included.
