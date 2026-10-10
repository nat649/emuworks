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
#define BG 0x10E5
#define PANEL 0x1968
#define TEXT 0xF7BE
#define MUTED 0x9535
#define ACCENT 0x7F38
#define ERROR 0xFBAA
#define INPUT_CAP 96
#define HISTORY_CAP 8

static char input[INPUT_CAP], result[24], history[HISTORY_CAP][INPUT_CAP];
static char history_result[HISTORY_CAP][24];
static unsigned length, history_count, cursor;
static int recalled = -1, last_error;
static float ans;
static uint64_t previous;
/* Compose in SRAM; only completed pixels reach the LCD. */
static uint16_t framebuffer[320*240];
static uint16_t dirty_left[240], dirty_right[240];
static int screen_ready, history_dirty = 1, rendered_error = -1;
static char rendered_result[24];
volatile unsigned lcd_pixels_sent;
static int same_text(const char *a, const char *b) {
    while (*a && *a == *b) { ++a; ++b; }
    return *a == *b;
}

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
    for (int py = y; py < y+h; ++py) for (int px = x; px < x+w; ++px) {
        if ((unsigned)px >= 320 || (unsigned)py >= 240) continue;
        unsigned offset = (unsigned)py*320 + (unsigned)px;
        if (framebuffer[offset] == color) continue;
        framebuffer[offset] = color;
        if (px < dirty_left[py]) dirty_left[py] = (uint16_t)px;
        if (px+1 > dirty_right[py]) dirty_right[py] = (uint16_t)(px+1);
    }
}
static void present(void) {
    for (int y = 0; y < 240; ++y) {
        int left = dirty_left[y], right = dirty_right[y];
        if (left >= right) continue;
        int end = y+1;
        while (end < 240 && dirty_left[end] == left && dirty_right[end] == right) ++end;
        command(0x2A); param((unsigned)left >> 8); param(left & 255);
        param((unsigned)(right-1) >> 8); param((right-1) & 255);
        command(0x2B); param((unsigned)y >> 8); param(y & 255);
        param((unsigned)(end-1) >> 8); param((end-1) & 255);
        command(0x2C);
        for (int row = y; row < end; ++row) {
            for (int x = left; x < right; ++x) param(framebuffer[row*320+x]);
            lcd_pixels_sent += (unsigned)(right-left);
            dirty_left[row] = 320; dirty_right[row] = 0;
        }
        y = end-1;
    }
}

static uint16_t blend(uint16_t foreground, uint16_t background, unsigned alpha) {
    unsigned r = (((foreground >> 11) & 31)*alpha + ((background >> 11) & 31)*(4-alpha) + 2)/4;
    unsigned g = (((foreground >> 5) & 63)*alpha + ((background >> 5) & 63)*(4-alpha) + 2)/4;
    unsigned b = ((foreground & 31)*alpha + (background & 31)*(4-alpha) + 2)/4;
    return (uint16_t)((r << 11) | (g << 5) | b);
}

/* Rounded cards, antialiased at their native 320 x 240 resolution. */
static void rounded(int x, int y, int w, int h, int radius, uint16_t color, uint16_t background) {
    rectangle(x+radius, y, w-2*radius, h, color);
    rectangle(x, y+radius, radius, h-2*radius, color);
    rectangle(x+w-radius, y+radius, radius, h-2*radius, color);
    for (int cy = 0; cy < radius; ++cy) {
        for (int cx = 0; cx < radius; ++cx) {
            unsigned coverage = 0;
            for (int sy = 1; sy <= 3; sy += 2) for (int sx = 1; sx <= 3; sx += 2) {
                int dx = radius*4 - (cx*4+sx), dy = radius*4 - (cy*4+sy);
                if (dx*dx + dy*dy <= radius*radius*16) ++coverage;
            }
            uint16_t pixel = blend(color, background, coverage);
            rectangle(x+cx, y+cy, 1, 1, pixel);
            rectangle(x+w-1-cx, y+cy, 1, 1, pixel);
            rectangle(x+cx, y+h-1-cy, 1, 1, pixel);
            rectangle(x+w-1-cx, y+h-1-cy, 1, 1, pixel);
        }
    }
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
#include "font.h"

static void smooth_character(int x, int y, char c, int scale, uint16_t color, uint16_t background) {
    if (scale != 2 && scale != 3) return;
    if (c >= 'a' && c <= 'z') c -= 'a'-'A';
    unsigned index = 0;
    while (smooth_chars[index] && smooth_chars[index] != c) ++index;
    if (!smooth_chars[index]) index = sizeof(smooth_chars)-2;
    int width = scale*4+1, height = scale*6+1;
    const unsigned char *pixels = scale == 2 ? smooth_2[index] : smooth_3[index];
    for (int py = 0; py < height; ++py) for (int px = 0; px < width; ++px) {
        unsigned alpha = pixels[py*width+px];
        if (alpha) rectangle(x+px, y+py, 1, 1, blend(color, background, alpha));
    }
}
static void text(int x, int y, const char *s, int scale, uint16_t color, uint16_t background, int max) {
    for (int i = 0; s[i] && i < max; ++i) {
        if (scale > 1) { smooth_character(x+i*5*scale, y, s[i], scale, color, background); continue; }
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
    if (!screen_ready) {
    rectangle(0, 0, 320, 240, BG);
    text(16, 12, "EmuWorks", 2, TEXT, BG, 20);
    rounded(246, 9, 58, 20, 10, PANEL, BG);
    text(256, 16, "CORE 0.1", 1, ACCENT, PANEL, 9);
    text(16, 43, "HISTORIQUE", 1, MUTED, BG, 30);
    text(214, 43, "HAUT/BAS : RAPPEL", 1, MUTED, BG, 18);
    rounded(10, 142, 300, 66, 14, 0x0883, BG);
    rounded(10, 140, 300, 66, 14, PANEL, BG);
    text(16, 222, "ENTREE : CALCULER", 1, MUTED, BG, 30);
    text(205, 222, "ECHAP : EFFACER", 1, MUTED, BG, 24);
    screen_ready = 1;
    }
    if (history_dirty) {
    rectangle(16, 61, 288, 68, BG);
    unsigned start = history_count > 3 ? history_count - 3 : 0;
    if (!history_count) {
        text(16, 76, "A vous de calculer", 2, TEXT, BG, 28);
        text(16, 101, "+ - * /   ( )   ^   ANS", 1, MUTED, BG, 50);
    }
    for (unsigned i = start; i < history_count; ++i) {
        int y = 61 + (int)(i-start)*24;
        unsigned size = text_length(history[i]);
        unsigned result_size = text_length(history_result[i]);
        int result_left = 302-(int)result_size*10;
        unsigned visible = (unsigned)(result_left-28)/5;
        if (visible > 30) visible = 30;
        text(16, y+3, history[i] + (size > visible ? size-visible : 0), 1, MUTED, BG, (int)visible);
        text(result_left, y, history_result[i], 2, TEXT, BG, 23);
    }
    history_dirty = 0;
    }
    rectangle(22, 151, 272, 18, PANEL);
    unsigned start_input = cursor > 26 ? cursor-26 : 0;
    text(22, 151, length ? input+start_input : "0", 2, TEXT, PANEL, 27);
    rectangle(22 + (int)(cursor-start_input)*10, 168, 7, 1, ACCENT);
    if (last_error != rendered_error || !same_text(result, rendered_result)) {
    rectangle(22, 178, 272, 19, PANEL);
    if (last_error) text(22, 184, error_text(last_error), 1, ERROR, PANEL, 54);
    else if (result[0]) {
        text(22, 183, "=", 2, MUTED, PANEL, 1);
        text(294-(int)text_length(result)*15, 178, result, 3, ACCENT, PANEL, 18);
    } else text(22, 187, "UNE EXPRESSION, PUIS ENTREE", 1, MUTED, PANEL, 50);
    rendered_error = last_error;
    copy_text(rendered_result, result);
    }
    present();
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
    history_dirty = 1;
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
    for (int y = 0; y < 240; ++y) dirty_left[y] = 320;
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
