/* SPDX-License-Identifier: MIT */
#ifndef EMUWORKS_CALC_H
#define EMUWORKS_CALC_H
/* Single-precision arithmetic; no libc or external math library. */
enum CalcError { CALC_OK, CALC_SYNTAX, CALC_ZERO, CALC_RANGE, CALC_POWER, CALC_DEPTH };
int calculate(const char *expression, float ans, float *value);
void number_text(float value, char out[24]);
#endif
