#!/usr/bin/env python3
"""Replay of V8 13.6.233.10 fdlibm sin, cos, atan2, exp in the exact expression order of
Assets/_Game/Sim/Core/Fdlibm.cs (F-02c, U-049), with math.fma at exactly the sites that node v24.11.1 arm64's own
binary fuses (fmadd / fmsub / fnmadd / fnmsub in `otool -arch arm64 -tV $(which node)`, functions
v8::base::ieee754::{sin,cos,atan,atan2,exp} and (anonymous)::__ieee754_rem_pio2, which inlines __kernel_rem_pio2,
__kernel_sin and __kernel_cos are inlined into sin and cos).

    python3 tools/golden/fdlibm_replay.py              # golden file: plain order vs fused order, both counted
    node tools/golden/gen-jsmath-large.mjs --full > /tmp/large.json   # the seeded large set, every vector (~44 MB)
    python3 tools/golden/fdlibm_replay.py --large /tmp/large.json     # fused replay of all 1,003,479 vectors

Each fused site is tagged `# FMA-n` with the same number as the `// FMA-n` comment in Fdlibm.cs.
Exit 0 only when the fused replay matches every vector. Measured 2026-11-12 (U-049): plain order 12 sin, 8 cos,
2 exp, 2 atan2 mismatches on the golden file (exactly the Unity run's 24), fused 0; on the large set plain 1,922,
fused 0 of 1,003,479.
"""
import json
import math
import os
import struct
import sys

FUSED = True


def F(a, b, c):
    """a*b + c at a site node's binary fuses."""
    return math.fma(a, b, c) if FUSED else a * b + c


def bits(x):
    return struct.unpack('>Q', struct.pack('>d', x))[0]


def from_bits(b):
    return struct.unpack('>d', struct.pack('>Q', b & 0xFFFFFFFFFFFFFFFF))[0]


def hi_word(x):  # int32
    h = bits(x) >> 32
    return h - (1 << 32) if h & 0x80000000 else h


def hi_word_u(x):
    return bits(x) >> 32


def lo_word(x):
    return bits(x) & 0xFFFFFFFF


def insert_words(hi, lo):
    return from_bits(((hi & 0xFFFFFFFF) << 32) | (lo & 0xFFFFFFFF))


def trunc_int(x):  # C (int)double, in range
    return int(x)


two_over_pi = [
    0xA2F983, 0x6E4E44, 0x1529FC, 0x2757D1, 0xF534DD, 0xC0DB62, 0x95993C, 0x439041, 0xFE5163, 0xABDEBB, 0xC561B7,
    0x246E3A, 0x424DD2, 0xE00649, 0x2EEA09, 0xD1921C, 0xFE1DEB, 0x1CB129, 0xA73EE8, 0x8235F5, 0x2EBB44, 0x84E99C,
    0x7026B4, 0x5F7E41, 0x3991D6, 0x398353, 0x39F49C, 0x845F8B, 0xBDF928, 0x3B1FF8, 0x97FFDE, 0x05980F, 0xEF2F11,
    0x8B5A0A, 0x6D1F6D, 0x367ECF, 0x27CB09, 0xB74F46, 0x3F669E, 0x5FEA2D, 0x7527BA, 0xC7EBE5, 0xF17B3D, 0x0739F7,
    0x8A5292, 0xEA6BFB, 0x5FB11F, 0x8D5D08, 0x560330, 0x46FC7B, 0x6BABF0, 0xCFBC20, 0x9AF436, 0x1DA9E3, 0x91615E,
    0xE61B08, 0x659985, 0x5F14A0, 0x68408D, 0xFFD880, 0x4D7327, 0x310606, 0x1556CA, 0x73A8C9, 0x60E27B, 0xC08C6B]
npio2_hw = [
    0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB, 0x402921FB, 0x402C463A,
    0x402F6A7A, 0x4031475C, 0x4032D97C, 0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB, 0x403AB41B, 0x403C463A,
    0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C, 0x4043A28C, 0x40446B9C, 0x404534AC,
    0x4045FDBB, 0x4046C6CB, 0x40478FDB, 0x404858EB, 0x404921FB]
init_jk = [2, 3, 4, 6]
PIo2 = [1.57079625129699707031e+00, 7.54978941586159635335e-08, 5.39030252995776476554e-15,
        3.28200341580791294123e-22, 1.27065575308067607349e-29, 1.22933308981111328932e-36,
        2.73370053816464559624e-44, 2.16741683877804819444e-51]
atanhi = [4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01,
          1.57079632679489655800e+00]
atanlo = [2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17,
          6.12323399573676603587e-17]
aT = [3.33333333333329318027e-01, -1.99999999998764832476e-01, 1.42857142725034663711e-01,
      -1.11111104054623557880e-01, 9.09088713343650656196e-02, -7.69187620504482999495e-02,
      6.66107313738753120669e-02, -5.83357013379057348645e-02, 4.97687799461593236017e-02,
      -3.65315727442169155270e-02, 1.62858201153657823623e-02]
ln2HI = [6.93147180369123816490e-01, -6.93147180369123816490e-01]
ln2LO = [1.90821492927058770002e-10, -1.90821492927058770002e-10]
halF = [0.5, -0.5]


def scalbn(x, n):
    return math.ldexp(x, n)


def kernel_rem_pio2(x, y, e0, nx, prec):
    two24 = 1.67772160000000000000e+07
    twon24 = 5.96046447753906250000e-08
    ipio2 = two_over_pi
    iq = [0] * 20
    f = [0.0] * 20
    fq = [0.0] * 20
    q = [0.0] * 20
    jk = init_jk[prec]
    jp = jk
    jx = nx - 1
    jv = int((e0 - 3) / 24)
    if jv < 0:
        jv = 0
    q0 = e0 - 24 * (jv + 1)
    j = jv - jx
    m = jx + jk
    for i in range(m + 1):
        f[i] = 0.0 if j < 0 else float(ipio2[j])
        j += 1
    for i in range(jk + 1):
        fw = 0.0
        for j in range(jx + 1):
            fw = F(x[j], f[jx + i - j], fw)  # FMA-5
        q[i] = fw
    jz = jk
    while True:  # recompute:
        i = 0
        j = jz
        z = q[jz]
        while j > 0:
            fw = float(trunc_int(twon24 * z))
            iq[i] = trunc_int(F(-two24, fw, z))  # FMA-6
            z = q[j - 1] + fw
            i += 1
            j -= 1
        z = scalbn(z, q0)
        z = F(-8.0, math.floor(z * 0.125), z)  # FMA-7
        n = trunc_int(z)
        z -= float(n)
        ih = 0
        if q0 > 0:
            i = iq[jz - 1] >> (24 - q0)
            n += i
            iq[jz - 1] -= i << (24 - q0)
            ih = iq[jz - 1] >> (23 - q0)
        elif q0 == 0:
            ih = iq[jz - 1] >> 23
        elif z >= 0.5:
            ih = 2
        if ih > 0:
            n += 1
            carry = 0
            for i in range(jz):
                j = iq[i]
                if carry == 0:
                    if j != 0:
                        carry = 1
                        iq[i] = 0x1000000 - j
                else:
                    iq[i] = 0xFFFFFF - j
            if q0 > 0:
                if q0 == 1:
                    iq[jz - 1] &= 0x7FFFFF
                elif q0 == 2:
                    iq[jz - 1] &= 0x3FFFFF
            if ih == 2:
                z = 1.0 - z
                if carry != 0:
                    z -= scalbn(1.0, q0)
        if z == 0.0:
            j = 0
            for i in range(jz - 1, jk - 1, -1):
                j |= iq[i]
            if j == 0:
                k = 1
                while jk >= k and iq[jk - k] == 0:
                    k += 1
                for i in range(jz + 1, jz + k + 1):
                    f[jx + i] = float(ipio2[jv + i])
                    fw = 0.0
                    for j in range(jx + 1):
                        fw = F(x[j], f[jx + i - j], fw)  # FMA-8
                    q[i] = fw
                jz += k
                continue
        break
    if z == 0.0:
        jz -= 1
        q0 -= 24
        while iq[jz] == 0:
            jz -= 1
            q0 -= 24
    else:
        z = scalbn(z, -q0)
        if z >= two24:
            fw = float(trunc_int(twon24 * z))
            iq[jz] = trunc_int(F(-two24, fw, z))  # FMA-9
            jz += 1
            q0 += 24
            iq[jz] = trunc_int(fw)
        else:
            iq[jz] = trunc_int(z)
    fw = scalbn(1.0, q0)
    for i in range(jz, -1, -1):
        q[i] = fw * iq[i]
        fw *= twon24
    for i in range(jz, -1, -1):
        fw = 0.0
        k = 0
        while k <= jp and k <= jz - i:
            fw = F(PIo2[k], q[i + k], fw)  # FMA-10
            k += 1
        fq[jz - i] = fw
    assert prec == 2
    fw = 0.0
    for i in range(jz, -1, -1):
        fw += fq[i]
    y[0] = fw if ih == 0 else -fw
    fw = fq[0] - fw
    for i in range(1, jz + 1):
        fw += fq[i]
    y[1] = fw if ih == 0 else -fw
    return n & 7


def rem_pio2(x, y):
    zero = 0.0
    half = 0.5
    two24 = 1.67772160000000000000e+07
    invpio2 = 6.36619772367581382433e-01
    pio2_1 = 1.57079632673412561417e+00
    pio2_1t = 6.07710050650619224932e-11
    pio2_2 = 6.07710050630396597660e-11
    pio2_2t = 2.02226624879595063154e-21
    pio2_3 = 2.02226624871116645580e-21
    pio2_3t = 8.47842766036889956997e-32
    tx = [0.0] * 3
    hx = hi_word(x)
    ix = hx & 0x7FFFFFFF
    if ix <= 0x3FE921FB:
        y[0] = x
        y[1] = 0.0
        return 0
    if ix < 0x4002D97C:
        if hx > 0:
            z = x - pio2_1
            if ix != 0x3FF921FB:
                y[0] = z - pio2_1t
                y[1] = (z - y[0]) - pio2_1t
            else:
                z -= pio2_2
                y[0] = z - pio2_2t
                y[1] = (z - y[0]) - pio2_2t
            return 1
        else:
            z = x + pio2_1
            if ix != 0x3FF921FB:
                y[0] = z + pio2_1t
                y[1] = (z - y[0]) + pio2_1t
            else:
                z += pio2_2
                y[0] = z + pio2_2t
                y[1] = (z - y[0]) + pio2_2t
            return -1
    if ix <= 0x413921FB:
        t = abs(x)
        n = trunc_int(F(t, invpio2, half))  # FMA-1
        fn = float(n)
        r = F(-fn, pio2_1, t)  # FMA-2
        w = fn * pio2_1t
        if n < 32 and ix != npio2_hw[n - 1]:
            y[0] = r - w
        else:
            j = ix >> 20
            y[0] = r - w
            high = hi_word_u(y[0])
            i = j - ((high >> 20) & 0x7FF)
            if i > 16:
                t = r
                w = fn * pio2_2
                r = t - w
                w = F(fn, pio2_2t, -((t - r) - w))  # FMA-3
                y[0] = r - w
                high = hi_word_u(y[0])
                i = j - ((high >> 20) & 0x7FF)
                if i > 49:
                    t = r
                    w = fn * pio2_3
                    r = t - w
                    w = F(fn, pio2_3t, -((t - r) - w))  # FMA-4
                    y[0] = r - w
        y[1] = (r - y[0]) - w
        if hx < 0:
            y[0] = -y[0]
            y[1] = -y[1]
            return -n
        return n
    if ix >= 0x7FF00000:
        y[0] = y[1] = x - x
        return 0
    low = lo_word(x)
    e0 = (ix >> 20) - 1046
    z = insert_words(ix - ((e0 << 20) & 0xFFFFFFFF), low)
    for i in range(2):
        tx[i] = float(trunc_int(z))
        z = (z - tx[i]) * two24
    tx[2] = z
    nx = 3
    while tx[nx - 1] == zero:
        nx -= 1
    n = kernel_rem_pio2(tx, y, e0, nx, 2)
    if hx < 0:
        y[0] = -y[0]
        y[1] = -y[1]
        return -n
    return n


def kernel_cos(x, y):
    one = 1.0
    C1 = 4.16666666666666019037e-02
    C2 = -1.38888888888741095749e-03
    C3 = 2.48015872894767294178e-05
    C4 = -2.75573143513906633035e-07
    C5 = 2.08757232129817482790e-09
    C6 = -1.13596475577881948265e-11
    ix = hi_word(x) & 0x7FFFFFFF
    if ix < 0x3E400000:
        if trunc_int(x) == 0:
            return one
    z = x * x
    r = z * F(z, F(z, F(z, F(z, F(z, C6, C5), C4), C3), C2), C1)  # FMA-11..15
    if ix < 0x3FD33333:
        return one - F(0.5, z, -F(z, r, -(x * y)))  # FMA-16 (outer), FMA-17 (z*r)
    else:
        if ix > 0x3FE90000:
            qx = 0.28125
        else:
            qx = insert_words(ix - 0x00200000, 0)
        iz = F(0.5, z, -qx)  # FMA-18
        a = one - qx
        return a - (iz - F(z, r, -(x * y)))  # FMA-19


def kernel_sin(x, y, iy):
    half = 0.5
    S1 = -1.66666666666666324348e-01
    S2 = 8.33333333332248946124e-03
    S3 = -1.98412698298579493134e-04
    S4 = 2.75573137070700676789e-06
    S5 = -2.50507602534068634195e-08
    S6 = 1.58969099521155010221e-10
    ix = hi_word(x) & 0x7FFFFFFF
    if ix < 0x3E400000:
        if trunc_int(x) == 0:
            return x
    z = x * x
    v = z * x
    r = F(z, F(z, F(z, F(z, S6, S5), S4), S3), S2)  # FMA-20..23
    if iy == 0:
        return F(v, F(z, r, S1), x)  # FMA-24 (outer), FMA-25 (z*r)
    else:
        return x - F(-v, S1, F(z, F(half, y, -(v * r)), -y))  # FMA-26 (v*S1), FMA-27 (z*..), FMA-28 (half*y)


def atan(x):
    one = 1.0
    huge = 1.0e300
    hx = hi_word(x)
    ix = hx & 0x7FFFFFFF
    if ix >= 0x44100000:
        low = lo_word(x)
        if ix > 0x7FF00000 or (ix == 0x7FF00000 and low != 0):
            return x + x
        if hx > 0:
            return atanhi[3] + atanlo[3]
        else:
            return -atanhi[3] - atanlo[3]
    if ix < 0x3FDC0000:
        if ix < 0x3E400000:
            if huge + x > one:
                return x
        id_ = -1
    else:
        x = abs(x)
        if ix < 0x3FF30000:
            if ix < 0x3FE60000:
                id_ = 0
                x = F(2.0, x, -one) / (2.0 + x)  # FMA-29
            else:
                id_ = 1
                x = (x - one) / (x + one)
        else:
            if ix < 0x40038000:
                id_ = 2
                x = (x - 1.5) / F(1.5, x, one)  # FMA-30
            else:
                id_ = 3
                x = -1.0 / x
    z = x * x
    w = z * z
    s1 = z * F(w, F(w, F(w, F(w, F(w, aT[10], aT[8]), aT[6]), aT[4]), aT[2]), aT[0])  # FMA-31..35
    s2 = w * F(w, F(w, F(w, F(w, aT[9], aT[7]), aT[5]), aT[3]), aT[1])  # FMA-36..39
    if id_ < 0:
        return F(-x, s1 + s2, x)  # FMA-40
    else:
        z = atanhi[id_] - (F(x, s1 + s2, -atanlo[id_]) - x)  # FMA-41
        return -z if hx < 0 else z


def atan2(y, x):
    tiny = 1.0e-300
    zero = 0.0
    pi_o_4 = 7.8539816339744827900E-01
    pi_o_2 = 1.5707963267948965580E+00
    pi = 3.1415926535897931160E+00
    pi_lo = 1.2246467991473531772E-16
    hx = hi_word(x)
    lx = lo_word(x)
    ix = hx & 0x7FFFFFFF
    hy = hi_word(y)
    ly = lo_word(y)
    iy = hy & 0x7FFFFFFF
    if (ix | ((lx | ((-lx) & 0xFFFFFFFF)) >> 31)) > 0x7FF00000 or \
            (iy | ((ly | ((-ly) & 0xFFFFFFFF)) >> 31)) > 0x7FF00000:
        return x + y
    if (((hx - 0x3FF00000) & 0xFFFFFFFF) | lx) == 0:
        return atan(y)
    m = ((hy >> 31) & 1) | ((hx >> 30) & 2)
    if (iy | ly) == 0:
        if m in (0, 1):
            return y
        if m == 2:
            return pi + tiny
        return -pi - tiny
    if (ix | lx) == 0:
        return -pi_o_2 - tiny if hy < 0 else pi_o_2 + tiny
    if ix == 0x7FF00000:
        if iy == 0x7FF00000:
            return [pi_o_4 + tiny, -pi_o_4 - tiny, 3.0 * pi_o_4 + tiny, -3.0 * pi_o_4 - tiny][m]
        else:
            return [zero, -zero, pi + tiny, -pi - tiny][m]
    if iy == 0x7FF00000:
        return -pi_o_2 - tiny if hy < 0 else pi_o_2 + tiny
    k = (iy - ix) >> 20
    if k > 60:
        z = F(0.5, pi_lo, pi_o_2)  # FMA-42
        m &= 1
    elif hx < 0 and k < -60:
        z = 0.0
    else:
        z = atan(abs(y / x))
    if m == 0:
        return z
    if m == 1:
        return -z
    if m == 2:
        return pi - (z - pi_lo)
    return (z - pi_lo) - pi


def cos(x):
    y = [0.0, 0.0]
    ix = hi_word(x) & 0x7FFFFFFF
    if ix <= 0x3FE921FB:
        return kernel_cos(x, 0.0)
    elif ix >= 0x7FF00000:
        return x - x
    n = rem_pio2(x, y)
    n &= 3
    if n == 0:
        return kernel_cos(y[0], y[1])
    if n == 1:
        return -kernel_sin(y[0], y[1], 1)
    if n == 2:
        return -kernel_cos(y[0], y[1])
    return kernel_sin(y[0], y[1], 1)


def sin(x):
    y = [0.0, 0.0]
    ix = hi_word(x) & 0x7FFFFFFF
    if ix <= 0x3FE921FB:
        return kernel_sin(x, 0.0, 0)
    elif ix >= 0x7FF00000:
        return x - x
    n = rem_pio2(x, y)
    n &= 3
    if n == 0:
        return kernel_sin(y[0], y[1], 1)
    if n == 1:
        return kernel_cos(y[0], y[1])
    if n == 2:
        return -kernel_sin(y[0], y[1], 1)
    return -kernel_cos(y[0], y[1])


def exp(x):
    one = 1.0
    o_threshold = 7.09782712893383973096e+02
    u_threshold = -7.45133219101941108420e+02
    invln2 = 1.44269504088896338700e+00
    P1 = 1.66666666666666019037e-01
    P2 = -2.77777777770155933842e-03
    P3 = 6.61375632143793436117e-05
    P4 = -1.65339022054652515390e-06
    P5 = 4.13813679705723846039e-08
    E = 2.718281828459045
    huge = 1.0e+300
    twom1000 = 9.33263618503218878990e-302
    two1023 = 8.988465674311579539e307
    hi = 0.0
    lo = 0.0
    k = 0
    hx = hi_word_u(x)
    xsb = (hx >> 31) & 1
    hx &= 0x7FFFFFFF
    if hx >= 0x40862E42:
        if hx >= 0x7FF00000:
            lx = lo_word(x)
            if ((hx & 0xFFFFF) | lx) != 0:
                return x + x
            return x if xsb == 0 else 0.0
        if x > o_threshold:
            return huge * huge
        if x < u_threshold:
            return twom1000 * twom1000
    if hx > 0x3FD62E42:
        if hx < 0x3FF0A2B2:
            if x == 1.0:
                return E
            hi = x - ln2HI[xsb]
            lo = ln2LO[xsb]
            k = 1 - xsb - xsb
        else:
            k = trunc_int(F(invln2, x, halF[xsb]))  # FMA-43
            t = float(k)
            hi = F(-t, ln2HI[0], x)  # FMA-44
            lo = t * ln2LO[0]
        x = hi - lo
    elif hx < 0x3E300000:
        if huge + x > one:
            return one + x
    else:
        k = 0
    t = x * x
    if k >= -1021:
        twopk = insert_words(0x3FF00000 + ((k << 20) & 0xFFFFFFFF), 0)
    else:
        twopk = insert_words(0x3FF00000 + ((k + 1000) << 20), 0)
    c = F(-t, F(t, F(t, F(t, F(t, P5, P4), P3), P2), P1), x)  # FMA-45 (outer), FMA-46..49
    if k == 0:
        return one - ((x * c) / (c - 2.0) - x)
    y = one - ((lo - (x * c) / (2.0 - c)) - hi)
    if k >= -1021:
        if k == 1024:
            return y * 2.0 * two1023
        return y * twopk
    return y * twopk * twom1000


FNS = {'sin': sin, 'cos': cos, 'exp': exp, 'atan2': atan2}
CANON_NAN = 0x7FF8000000000000


def out_bits(v):
    return CANON_NAN if v != v else bits(v)


def check_vectors(table):
    """table: {fn: [[in hex..., out hex]]}; returns {fn: [mismatch strings]}"""
    bad = {}
    for fn, f in FNS.items():
        miss = []
        for v in table[fn]:
            ins = [from_bits(int(h, 16)) for h in v[:-1]]
            exp_b = int(v[-1], 16)
            got = f(*ins)
            expd = from_bits(exp_b)
            ok = (got != got) if expd != expd else bits(got) == exp_b
            if not ok:
                miss.append('%s -> %s, got %016x' % (','.join(v[:-1]), v[-1], bits(got)))
        bad[fn] = miss
    return bad


def main(argv):
    global FUSED
    root = os.path.dirname(os.path.abspath(__file__))
    if '--large' in argv:
        path = argv[argv.index('--large') + 1]
        with open(path) as fh:
            table = json.load(fh)
        bad = check_vectors(table)
        total = sum(len(table[fn]) for fn in FNS)
        for fn in FNS:
            print('fused %-5s %7d vectors, %d mismatches' % (fn, len(table[fn]), len(bad[fn])))
            for s in bad[fn][:10]:
                print('   ', s)
        print('total %d vectors, %d mismatches' % (total, sum(len(b) for b in bad.values())))
        return 0 if all(not b for b in bad.values()) else 1
    with open(os.path.join(root, 'jsmath-fdlibm.json')) as fh:
        table = json.load(fh)
    rc = 0
    for fused in (False, True):
        FUSED = fused
        bad = check_vectors(table)
        label = 'fused' if fused else 'plain'
        for fn in FNS:
            print('%s %-5s %5d vectors, %d mismatches' % (label, fn, len(table[fn]), len(bad[fn])))
        if fused and any(bad.values()):
            rc = 1
            for fn in FNS:
                for s in bad[fn][:10]:
                    print('   ', fn, s)
    return rc


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
