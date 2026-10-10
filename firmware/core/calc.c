/* SPDX-License-Identifier: MIT - Original EmuWorks expression evaluator. */
#include "calc.h"

typedef struct { const char *p; float ans; int error, depth; } Parser;
static float absolute(float x) { return x < 0.0f ? -x : x; }
static void spaces(Parser *p) { while (*p->p == ' ') ++p->p; }
static float expression(Parser *p);
static float unary(Parser *p);
static float checked(Parser *p, float x) {
    if (!(x == x) || absolute(x) > 1.0e30f) p->error = CALC_RANGE;
    return x;
}
static float primary(Parser *p) {
    spaces(p);
    if (p->error) return 0.0f;
    if (++p->depth > 24) { p->error = CALC_DEPTH; --p->depth; return 0.0f; }
    float value = 0.0f;
    if (*p->p == '(') {
        ++p->p; value = expression(p); spaces(p);
        if (*p->p == ')') ++p->p; else p->error = CALC_SYNTAX;
    } else if (p->p[0] == 'A' && p->p[1] == 'N' && p->p[2] == 'S') {
        p->p += 3; value = p->ans;
    } else {
        int digits = 0;
        while (*p->p >= '0' && *p->p <= '9') {
            value = checked(p, value * 10.0f + (float)(*p->p++ - '0')); ++digits;
        }
        if (*p->p == '.') {
            ++p->p; float place = 0.1f;
            while (*p->p >= '0' && *p->p <= '9') {
                value += (float)(*p->p++ - '0') * place; place *= 0.1f; ++digits;
            }
        }
        if (!digits) p->error = CALC_SYNTAX;
        if (*p->p == 'e' || *p->p == 'E') {
            ++p->p; int sign = 1, exponent = 0, count = 0;
            if (*p->p == '+' || *p->p == '-') { if (*p->p == '-') sign = -1; ++p->p; }
            while (*p->p >= '0' && *p->p <= '9') {
                if (exponent < 100) exponent = exponent * 10 + (*p->p - '0');
                ++p->p; ++count;
            }
            if (!count) p->error = CALC_SYNTAX;
            if (exponent > 30) p->error = CALC_RANGE;
            if (!p->error) for (int i = 0; i < exponent; ++i) value = checked(p, value * (sign > 0 ? 10.0f : 0.1f));
        }
    }
    --p->depth;
    return checked(p, value);
}
static float power(Parser *p) {
    float base = primary(p); spaces(p);
    if (*p->p != '^' || p->error) return base;
    ++p->p;
    float exponent = unary(p);
    if (p->error) return 0.0f;
    if (absolute(exponent) > 100.0f || exponent != (float)(int)exponent) { p->error = CALC_POWER; return 0.0f; }
    int n = (int)exponent;
    if (base == 0.0f && n <= 0) { p->error = n == 0 ? CALC_POWER : CALC_ZERO; return 0.0f; }
    if (n < 0) { base = checked(p, 1.0f / base); n = -n; }
    float result = 1.0f;
    while (n && !p->error) {
        if (n & 1) result = checked(p, result * base);
        n >>= 1;
        if (n) base = checked(p, base * base);
    }
    return result;
}
static float unary(Parser *p) {
    spaces(p);
    if (++p->depth > 24) { p->error = CALC_DEPTH; --p->depth; return 0.0f; }
    float value;
    if (*p->p == '+' || *p->p == '-') {
        int negative = *p->p++ == '-'; value = unary(p); if (negative) value = -value;
    } else value = power(p);
    --p->depth; return value;
}
static float product(Parser *p) {
    float value = unary(p); spaces(p);
    while (!p->error && (*p->p == '*' || *p->p == '/')) {
        char op = *p->p++; float rhs = unary(p);
        if (op == '/' && rhs == 0.0f) { p->error = CALC_ZERO; return 0.0f; }
        value = checked(p, op == '*' ? value * rhs : value / rhs); spaces(p);
    }
    return value;
}
static float expression(Parser *p) {
    float value = product(p); spaces(p);
    while (!p->error && (*p->p == '+' || *p->p == '-')) {
        char op = *p->p++; float rhs = product(p);
        value = checked(p, op == '+' ? value + rhs : value - rhs); spaces(p);
    }
    return value;
}
int calculate(const char *text, float ans, float *value) {
    Parser p = { text, ans, CALC_OK, 0 };
    float result = expression(&p); spaces(&p);
    if (*p.p && !p.error) p.error = CALC_SYNTAX;
    if (!p.error) *value = result;
    return p.error;
}

/* Six significant displayed digits. The input parser accepts this notation. */
void number_text(float value, char out[24]) {
    int at = 0, exponent = 0;
    if (value == 0.0f) { out[0] = '0'; out[1] = 0; return; }
    if (value < 0.0f) { out[at++] = '-'; value = -value; }
    while (value >= 10.0f) { value *= 0.1f; ++exponent; }
    while (value < 1.0f) { value *= 10.0f; --exponent; }
    unsigned n = (unsigned)(value * 100000.0f + 0.5f);
    if (n >= 1000000) { n /= 10; ++exponent; }
    char digits[6];
    for (int i = 5; i >= 0; --i) { digits[i] = (char)('0' + n % 10); n /= 10; }
    int last = 5; while (last > 0 && digits[last] == '0') --last;
    if (exponent >= -3 && exponent <= 5) {
        if (exponent < 0) {
            out[at++] = '0'; out[at++] = '.';
            for (int i = -1; i > exponent; --i) out[at++] = '0';
        }
        int count = last > exponent ? last : exponent;
        for (int i = 0; i <= count; ++i) {
            out[at++] = i <= last ? digits[i] : '0';
            if (i == exponent && i < last) out[at++] = '.';
        }
    } else {
        out[at++] = digits[0];
        if (last) out[at++] = '.';
        for (int i = 1; i <= last; ++i) out[at++] = digits[i];
        out[at++] = 'E'; out[at++] = exponent < 0 ? '-' : '+';
        if (exponent < 0) exponent = -exponent;
        if (exponent >= 10) out[at++] = (char)('0' + exponent / 10);
        out[at++] = (char)('0' + exponent % 10);
    }
    out[at] = 0;
}
