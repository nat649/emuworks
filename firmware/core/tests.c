/* SPDX-License-Identifier: MIT - Runs on the emulated ARM CPU. */
#include <stdint.h>
#include "calc.h"
volatile uint32_t test_report[4];
struct Case { const char *text; float expected; int error; };
static const struct Case cases[] = {
    {"2+3*4", 14, 0}, {"(2+3)*4", 20, 0}, {"7/2", 3.5f, 0},
    {"-2^2", -4, 0}, {"(-2)^2", 4, 0}, {"2^-3", 0.125f, 0},
    {"2^3^2", 512, 0}, {"0.1+0.2", 0.3f, 0}, {".5+1.", 1.5f, 0},
    {"ANS*2", 84, 0}, {" 1 + 2 ", 3, 0}, {"1E3+2e-2", 1000.02f, 0},
    {"2--3", 5, 0}, {"-0", 0, 0}, {"0^2", 0, 0}, {"2^0", 1, 0},
    {"1/0", 0, CALC_ZERO}, {"0^-1", 0, CALC_ZERO}, {"0^0", 0, CALC_POWER},
    {"2^.5", 0, CALC_POWER}, {"2^101", 0, CALC_POWER}, {"1E31", 0, CALC_RANGE},
    {"1E30*10", 0, CALC_RANGE}, {"1E", 0, CALC_SYNTAX}, {"", 0, CALC_SYNTAX},
    {".", 0, CALC_SYNTAX}, {"2+", 0, CALC_SYNTAX}, {"(1", 0, CALC_SYNTAX},
    {"1)", 0, CALC_SYNTAX}, {"2(3)", 0, CALC_SYNTAX}, {"1..2", 0, CALC_SYNTAX},
    {"A", 0, CALC_SYNTAX}, {"AN", 0, CALC_SYNTAX}, {"ANSx", 0, CALC_SYNTAX},
    {"------------------------------1", 0, CALC_DEPTH},
};
static int equal(const char *a, const char *b) { while (*a && *a == *b) { ++a; ++b; } return *a == *b; }
int main(void) {
    test_report[0] = 0; test_report[1] = 0; test_report[2] = 0; test_report[3] = 0;
    for (unsigned i = 0; i < sizeof(cases)/sizeof(cases[0]); ++i) {
        float result = 123.0f;
        int error = calculate(cases[i].text, 42.0f, &result);
        float diff = result - cases[i].expected; if (diff < 0) diff = -diff;
        float magnitude = cases[i].expected; if (magnitude < 0) magnitude = -magnitude;
        if (error != cases[i].error || (!error && diff > 0.00001f*(1+magnitude))) {
            ++test_report[2]; test_report[3] = i+1;
        }
        ++test_report[1];
    }
    static const float values[] = {0, -4, 20, 0.125f, 0.3f, 1000, 1e10f, 1e-5f, -0.001f, 999999};
    static const char *expected[] = {"0", "-4", "20", "0.125", "0.3", "1000", "1E+10", "1E-5", "-0.001", "999999"};
    for (unsigned i = 0; i < sizeof(values)/sizeof(values[0]); ++i) {
        char text[24]; number_text(values[i], text);
        if (!equal(text, expected[i])) { ++test_report[2]; test_report[3] = 100+i; }
        ++test_report[1];
    }
    test_report[0] = 0xC0DEC0DE;
    for (;;) __asm volatile("nop");
}
