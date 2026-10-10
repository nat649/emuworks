/* SPDX-License-Identifier: MIT - Original EmuWorks Core 0.1
 * Emulator target only: the platform already maps external memory and the LCD.
 * This image is not a firmware installer for a physical calculator.
 */
#include <stdint.h>
#include <stddef.h>
#include "calc.h"

__attribute__((section(".identity"), used))
const char identity[] = "EMUWORKS_CORE_V1";

#define REG32(a) (*(volatile uint32_t *)(a))
#define LCD_CMD (*(volatile uint16_t *)0x60000000)
#define LCD_DATA (*(volatile uint16_t *)0x60020000)
#define BG 0x10C5
#define PANEL 0x1928
#define TEXT 0xEF7D
#define MUTED 0x8CB3
#define ACCENT 0x4F36
#define ERROR 0xFBAA
#define INPUT_CAP 96
#define HISTORY_CAP 8

static char input[INPUT_CAP], result[24], history[HISTORY_CAP][INPUT_CAP];
static char history_result[HISTORY_CAP][24];
static unsigned length, history_count, cursor;
static int recalled = -1, last_error;
static float ans;
static uint64_t previous;

void *memcpy(void *dest, const void *source, size_t count) {
    unsigned char *d = dest; const unsigned char *s = source;
    while (count--) *d++ = *s++;
    return dest;
}
void *memset(void *dest, int value, size_t count) {
    unsigned char *d = dest; while (count--) *d++ = (unsigned char)value; return dest;
}
static unsigned text_length(const char *s) { unsigned n = 0; while (s[n]) ++n; return n; }
static void copy_text(char *d, const char *s) { while ((*d++ = *s++)) {} }
static void command(uint16_t c) { LCD_CMD = c; }
static void param(uint16_t p) { LCD_DATA = p; }
static void rectangle(int x, int y, int w, int h, uint16_t color) {
    command(0x2A); param((unsigned)x >> 8); param(x & 255); param((unsigned)(x+w-1) >> 8); param((x+w-1) & 255);
    command(0x2B); param((unsigned)y >> 8); param(y & 255); param((unsigned)(y+h-1) >> 8); param((y+h-1) & 255);
    command(0x2C);
    for (int i = 0; i < w*h; ++i) param(color);
}

/* Hand-drawn 4 x 6 pixel alphabet. Each hex nibble is one row, top first.
 * These patterns are part of this project's source, not an imported font. */
static uint32_t glyph(char c) {
    if (c >= 'a' && c <= 'z') c -= 'a' - 'A';
    switch (c) {
        case 'A': return 0x699F99; case 'B': return 0xE9E99E;
        case 'C': return 0x788887; case 'D': return 0xE9999E;
        case 'E': return 0xF8E88F; case 'F': return 0xF8E888;
        case 'G': return 0x788B97; case 'H': return 0x99F999;
        case 'I': return 0xE4444E; case 'J': return 0x711196;
        case 'K': return 0x9ACCA9; case 'L': return 0x88888F;
        case 'M': return 0x9FF999; case 'N': return 0x9DDBB9;
        case 'O': return 0x699996; case 'P': return 0xE99E88;
        case 'Q': return 0x6999B7; case 'R': return 0xE99EA9;
        case 'S': return 0x78861E; case 'T': return 0xF44444;
        case 'U': return 0x999996; case 'V': return 0x999964;
        case 'W': return 0x999FF9; case 'X': return 0x996699;
        case 'Y': return 0x996444; case 'Z': return 0xF1248F;
        case '0': return 0x69BD96; case '1': return 0x4C444E;
        case '2': return 0x69168F; case '3': return 0xE1611E;
        case '4': return 0xAAAF22; case '5': return 0xF8E11E;
        case '6': return 0x688E96; case '7': return 0xF12444;
        case '8': return 0x696996; case '9': return 0x697116;
        case '+': return 0x044F44; case '-': return 0x000F00;
        case '*': return 0x096690; case '/': return 0x112488;
        case '^': return 0x069000; case '(': return 0x244442;
        case ')': return 0x422224; case '=': return 0x00F0F0;
        case '.': return 0x000004; case ':': return 0x040040;
        case '>': return 0x842248; case '<': return 0x124421;
        case '!': return 0x444404; case '_': return 0x00000F;
        case ' ': return 0;
        default: return 0xE12404;
    }
}
static void text(int x, int y, const char *s, int scale, uint16_t color, int max) {
    for (int i = 0; s[i] && i < max; ++i) {
        uint32_t bits = glyph(s[i]);
        for (int row = 0; row < 6; ++row)
            for (int col = 0; col < 4; ++col)
                if (bits & (1u << (23 - row*4 - col))) rectangle(x + i*5*scale + col*scale, y + row*scale, scale, scale, color);
    }
}
static const char *error_text(int code) {
    switch (code) {
        case CALC_SYNTAX: return "EXPRESSION INVALIDE";
        case CALC_ZERO: return "DIVISION PAR ZERO";
        case CALC_RANGE: return "RESULTAT HORS LIMITES";
        case CALC_POWER: return "PUISSANCE ENTIERE -100 A 100";
        case CALC_DEPTH: return "EXPRESSION TROP COMPLEXE";
        default: return "";
    }
}
static void draw(void) {
    rectangle(0, 0, 320, 240, BG);
    rectangle(0, 0, 320, 30, PANEL);
    text(10, 9, "EMUWORKS CORE", 2, ACCENT, 26);
    text(273, 12, "0.1", 1, MUTED, 8);
    text(10, 38, "HISTORIQUE   HAUT/BAS : RAPPELER", 1, MUTED, 60);
    unsigned start = history_count > 3 ? history_count - 3 : 0;
    if (!history_count) {
        text(10, 65, "VOTRE CALCULATRICE LIBRE", 2, TEXT, 30);
        text(10, 91, "+ - * /   ( )   ^   ANS", 2, MUTED, 30);
    }
    for (unsigned i = start; i < history_count; ++i) {
        int y = 55 + (int)(i-start)*29;
        unsigned size = text_length(history[i]);
        text(10, y, history[i] + (size > 54 ? size-54 : 0), 1, MUTED, 54);
        text(10, y+11, "=", 2, ACCENT, 1);
        text(26, y+11, history_result[i], 2, TEXT, 26);
    }
    rectangle(8, 146, 304, 34, PANEL);
    unsigned start_input = cursor > 27 ? cursor-27 : 0;
    text(14, 154, length ? input+start_input : "0", 2, TEXT, 29);
    rectangle(14 + (int)(cursor-start_input)*10, 170, 8, 2, ACCENT);
    text(10, 190, last_error ? error_text(last_error) : result, last_error ? 1 : 2, last_error ? ERROR : ACCENT, last_error ? 60 : 30);
    text(10, 218, "ENTREE : CALCULER   ECHAP : EFFACER", 1, MUTED, 60);
    text(10, 230, "6 CHIFFRES AFFICHES   A : ANS", 1, MUTED, 60);
}
static void insert(const char *s) {
    unsigned size = text_length(s);
    if (length+size >= INPUT_CAP) return;
    for (unsigned i = length+1; i > cursor; --i) input[i+size-1] = input[i-1];
    for (unsigned i = 0; i < size; ++i) input[cursor+i] = s[i];
    cursor += size; length += size; recalled = -1; last_error = 0;
}
static void evaluate(void) {
    float value;
    if (!length) return;
    last_error = calculate(input, ans, &value);
    if (last_error) return;
    ans = value; number_text(value, result);
    if (history_count == HISTORY_CAP) {
        for (unsigned i = 1; i < HISTORY_CAP; ++i) {
            copy_text(history[i-1], history[i]); copy_text(history_result[i-1], history_result[i]);
        }
        --history_count;
    }
    copy_text(history[history_count], input); copy_text(history_result[history_count], result);
    ++history_count; recalled = -1; input[0] = 0; length = cursor = 0;
}
static void key(int key) {
    switch (key) {
        case 0: if (cursor) --cursor; break;
        case 3: if (cursor < length) ++cursor; break;
        case 1: case 2:
            if (!history_count) break;
            if (key == 1) { if (recalled < 0) recalled = (int)history_count-1; else if (recalled) --recalled; }
            else { if (recalled < 0) break; if (recalled < (int)history_count-1) ++recalled; else { recalled = -1; input[0] = 0; length = cursor = 0; break; } }
            copy_text(input, history[recalled]); cursor = length = text_length(input); last_error = 0; break;
        case 4: case 52: evaluate(); break;
        case 5: case 6: input[0] = result[0] = 0; length = cursor = 0; last_error = 0; recalled = -1; break;
        case 17:
            if (cursor) { for (unsigned i = cursor-1; i < length; ++i) input[i] = input[i+1]; --length; --cursor; }
            last_error = 0; break;
        case 23: insert("^"); break; case 29: insert("^2"); break;
        case 30: insert("7"); break; case 31: insert("8"); break; case 32: insert("9"); break;
        case 33: insert("("); break; case 34: insert(")"); break;
        case 36: insert("4"); break; case 37: insert("5"); break; case 38: insert("6"); break;
        case 39: insert("*"); break; case 40: insert("/"); break;
        case 42: insert("1"); break; case 43: insert("2"); break; case 44: insert("3"); break;
        case 45: insert("+"); break; case 46: insert("-"); break; case 48: insert("0"); break;
        case 49: case 22: insert("."); break; case 50: insert("E"); break; case 51: insert("ANS"); break;
        default: return;
    }
    draw();
}
static uint64_t scan(void) {
    uint64_t down = 0;
    for (unsigned row = 0; row < 9; ++row) {
        unsigned pin = row == 0 ? 1 : row == 1 ? 0 : row;
        REG32(0x40020014) = 0x1FF;
        REG32(0x40020014) = 0x1FF & ~(1u << pin);
        unsigned columns = ~REG32(0x40020810) & 0x3F;
        down |= (uint64_t)columns << (row*6);
    }
    REG32(0x40020014) = 0x1FF;
    return down;
}
int main(void) {
    REG32(0x40023830) |= 5;  /* GPIO A/C clocks */
    REG32(0x40020014) = 0x1FF;
    REG32(0x40020000) = (REG32(0x40020000) & ~0x3FFFFu) | 0x15555;
    REG32(0x40020800) &= ~0xFFFu;
    REG32(0x4002080C) = (REG32(0x4002080C) & ~0xFFFu) | 0x555;
    command(0x36); param(0xA0); command(0x3A); param(0x55); command(0x29);
    draw();
    for (;;) {
        uint64_t now = scan(), pressed = now & ~previous; previous = now;
        for (int i = 0; i < 54; ++i) if (pressed & ((uint64_t)1 << i)) key(i);
        for (volatile unsigned wait = 0; wait < 4000; ++wait) __asm volatile("nop");
    }
}
