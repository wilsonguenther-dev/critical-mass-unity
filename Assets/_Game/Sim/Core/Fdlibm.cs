// The following is adapted from fdlibm (http://www.netlib.org/fdlibm).
//
// ====================================================
// Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
//
// Developed at SunSoft, a Sun Microsystems, Inc. business.
// Permission to use, copy, modify, and distribute this
// software is freely granted, provided that this notice
// is preserved.
// ====================================================
//
// The original source code covered by the above license above has been
// modified significantly by Google Inc.
// Copyright 2016 the V8 project authors. All rights reserved.
//
// C# port of V8 src/base/ieee754.cc at tag 13.6.233.10 (the V8 of node v24.11.1), item F-02c (U-032): sin, cos,
// atan2 and exp bit-exact with node. Ported line by line: same constant literals, same branch order, same operation
// order in every expression. The C int32_t / uint32_t locals keep their signedness as int / uint.

using System;

namespace CriticalMass.Sim.Core
{
    /// <summary>V8's fdlibm port of sin, cos, atan2 and exp. Callers go through <see cref="JsMath"/>.</summary>
    internal static class Fdlibm
    {
        // ---- word access (GET_HIGH_WORD, GET_LOW_WORD, EXTRACT_WORDS, INSERT_WORDS, SET_HIGH_WORD, SET_LOW_WORD) ----

        private static int GetHighWord(double d)
        {
            long b = BitConverter.DoubleToInt64Bits(d);
            return (int)(b >> 32);
        }

        private static uint GetHighWordU(double d)
        {
            long b = BitConverter.DoubleToInt64Bits(d);
            return unchecked((uint)(b >> 32));
        }

        private static uint GetLowWord(double d)
        {
            long b = BitConverter.DoubleToInt64Bits(d);
            return unchecked((uint)b);
        }

        private static double InsertWords(int hi, uint lo)
        {
            return BitConverter.Int64BitsToDouble(((long)hi << 32) | lo);
        }

        private static double SetHighWord(double d, int hi)
        {
            return InsertWords(hi, GetLowWord(d));
        }

        private static double SetLowWord(double d, uint lo)
        {
            return InsertWords(GetHighWord(d), lo);
        }

        // scalbn from <cmath> (used by __kernel_rem_pio2): x * 2^n, exact as the C library's (musl's algorithm).
        private static double Scalbn(double x, int n)
        {
            double y = x;
            double p1023 = BitConverter.Int64BitsToDouble(0x7FE0000000000000L); /* 0x1p1023 */
            double pm969 = BitConverter.Int64BitsToDouble(0x0360000000000000L); /* 0x1p-1022 * 0x1p53 */
            if (n > 1023)
            {
                y *= p1023;
                n -= 1023;
                if (n > 1023)
                {
                    y *= p1023;
                    n -= 1023;
                    if (n > 1023) n = 1023;
                }
            }
            else if (n < -1022)
            {
                y *= pm969;
                n += 1022 - 53;
                if (n < -1022)
                {
                    y *= pm969;
                    n += 1022 - 53;
                    if (n < -1022) n = -1022;
                }
            }
            return y * BitConverter.Int64BitsToDouble((long)(0x3ff + n) << 52);
        }

        // ---- tables ----

        /*
         * Table of constants for 2/pi, 396 Hex digits (476 decimal) of 2/pi
         */
        private static readonly int[] two_over_pi =
        {
            0xA2F983, 0x6E4E44, 0x1529FC, 0x2757D1, 0xF534DD, 0xC0DB62, 0x95993C,
            0x439041, 0xFE5163, 0xABDEBB, 0xC561B7, 0x246E3A, 0x424DD2, 0xE00649,
            0x2EEA09, 0xD1921C, 0xFE1DEB, 0x1CB129, 0xA73EE8, 0x8235F5, 0x2EBB44,
            0x84E99C, 0x7026B4, 0x5F7E41, 0x3991D6, 0x398353, 0x39F49C, 0x845F8B,
            0xBDF928, 0x3B1FF8, 0x97FFDE, 0x05980F, 0xEF2F11, 0x8B5A0A, 0x6D1F6D,
            0x367ECF, 0x27CB09, 0xB74F46, 0x3F669E, 0x5FEA2D, 0x7527BA, 0xC7EBE5,
            0xF17B3D, 0x0739F7, 0x8A5292, 0xEA6BFB, 0x5FB11F, 0x8D5D08, 0x560330,
            0x46FC7B, 0x6BABF0, 0xCFBC20, 0x9AF436, 0x1DA9E3, 0x91615E, 0xE61B08,
            0x659985, 0x5F14A0, 0x68408D, 0xFFD880, 0x4D7327, 0x310606, 0x1556CA,
            0x73A8C9, 0x60E27B, 0xC08C6B,
        };

        private static readonly int[] npio2_hw =
        {
            0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C,
            0x4025FDBB, 0x402921FB, 0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C,
            0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB, 0x403AB41B, 0x403C463A,
            0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C,
            0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB,
            0x404858EB, 0x404921FB,
        };

        private static readonly int[] init_jk = { 2, 3, 4, 6 }; /* initial value for jk */

        private static readonly double[] PIo2 =
        {
            1.57079625129699707031e+00, /* 0x3FF921FB, 0x40000000 */
            7.54978941586159635335e-08, /* 0x3E74442D, 0x00000000 */
            5.39030252995776476554e-15, /* 0x3CF84698, 0x80000000 */
            3.28200341580791294123e-22, /* 0x3B78CC51, 0x60000000 */
            1.27065575308067607349e-29, /* 0x39F01B83, 0x80000000 */
            1.22933308981111328932e-36, /* 0x387A2520, 0x40000000 */
            2.73370053816464559624e-44, /* 0x36E38222, 0x80000000 */
            2.16741683877804819444e-51, /* 0x3569F31D, 0x00000000 */
        };

        private static readonly double[] atanhi =
        {
            4.63647609000806093515e-01, /* atan(0.5)hi 0x3FDDAC67, 0x0561BB4F */
            7.85398163397448278999e-01, /* atan(1.0)hi 0x3FE921FB, 0x54442D18 */
            9.82793723247329054082e-01, /* atan(1.5)hi 0x3FEF730B, 0xD281F69B */
            1.57079632679489655800e+00, /* atan(inf)hi 0x3FF921FB, 0x54442D18 */
        };

        private static readonly double[] atanlo =
        {
            2.26987774529616870924e-17, /* atan(0.5)lo 0x3C7A2B7F, 0x222F65E2 */
            3.06161699786838301793e-17, /* atan(1.0)lo 0x3C81A626, 0x33145C07 */
            1.39033110312309984516e-17, /* atan(1.5)lo 0x3C700788, 0x7AF0CBBD */
            6.12323399573676603587e-17, /* atan(inf)lo 0x3C91A626, 0x33145C07 */
        };

        private static readonly double[] aT =
        {
            3.33333333333329318027e-01,  /* 0x3FD55555, 0x5555550D */
            -1.99999999998764832476e-01, /* 0xBFC99999, 0x9998EBC4 */
            1.42857142725034663711e-01,  /* 0x3FC24924, 0x920083FF */
            -1.11111104054623557880e-01, /* 0xBFBC71C6, 0xFE231671 */
            9.09088713343650656196e-02,  /* 0x3FB745CD, 0xC54C206E */
            -7.69187620504482999495e-02, /* 0xBFB3B0F2, 0xAF749A6D */
            6.66107313738753120669e-02,  /* 0x3FB10D66, 0xA0D03D51 */
            -5.83357013379057348645e-02, /* 0xBFADDE2D, 0x52DEFD9A */
            4.97687799461593236017e-02,  /* 0x3FA97B4B, 0x24760DEB */
            -3.65315727442169155270e-02, /* 0xBFA2B444, 0x2C6A6C2F */
            1.62858201153657823623e-02,  /* 0x3F90AD3A, 0xE322DA11 */
        };

        private static readonly double[] halF = { 0.5, -0.5 };

        private static readonly double[] ln2HI =
        {
            6.93147180369123816490e-01,  /* 0x3FE62E42, 0xFEE00000 */
            -6.93147180369123816490e-01, /* 0xBFE62E42, 0xFEE00000 */
        };

        private static readonly double[] ln2LO =
        {
            1.90821492927058770002e-10,  /* 0x3DEA39EF, 0x35793C76 */
            -1.90821492927058770002e-10, /* 0xBDEA39EF, 0x35793C76 */
        };

        /* __ieee754_rem_pio2(x,y)
         *
         * return the remainder of x rem pi/2 in y[0]+y[1]
         * use __kernel_rem_pio2()
         */
        private static int RemPio2(double x, double[] y)
        {
            /*
             * invpio2:  53 bits of 2/pi
             * pio2_1:   first  33 bit of pi/2
             * pio2_1t:  pi/2 - pio2_1
             * pio2_2:   second 33 bit of pi/2
             * pio2_2t:  pi/2 - (pio2_1+pio2_2)
             * pio2_3:   third  33 bit of pi/2
             * pio2_3t:  pi/2 - (pio2_1+pio2_2+pio2_3)
             */
            const double
                zero = 0.00000000000000000000e+00,    /* 0x00000000, 0x00000000 */
                half = 5.00000000000000000000e-01,    /* 0x3FE00000, 0x00000000 */
                two24 = 1.67772160000000000000e+07,   /* 0x41700000, 0x00000000 */
                invpio2 = 6.36619772367581382433e-01, /* 0x3FE45F30, 0x6DC9C883 */
                pio2_1 = 1.57079632673412561417e+00,  /* 0x3FF921FB, 0x54400000 */
                pio2_1t = 6.07710050650619224932e-11, /* 0x3DD0B461, 0x1A626331 */
                pio2_2 = 6.07710050630396597660e-11,  /* 0x3DD0B461, 0x1A600000 */
                pio2_2t = 2.02226624879595063154e-21, /* 0x3BA3198A, 0x2E037073 */
                pio2_3 = 2.02226624871116645580e-21,  /* 0x3BA3198A, 0x2E000000 */
                pio2_3t = 8.47842766036889956997e-32; /* 0x397B839A, 0x252049C1 */

            unchecked
            {
                double z, w, t, r, fn;
                double[] tx = new double[3];
                int e0, i, j, nx, n, ix, hx;
                uint low;

                z = 0;
                hx = GetHighWord(x); /* high word of x */
                ix = hx & 0x7FFFFFFF;
                if (ix <= 0x3FE921FB)
                { /* |x| ~<= pi/4 , no need for reduction */
                    y[0] = x;
                    y[1] = 0;
                    return 0;
                }
                if (ix < 0x4002D97C)
                { /* |x| < 3pi/4, special case with n=+-1 */
                    if (hx > 0)
                    {
                        z = x - pio2_1;
                        if (ix != 0x3FF921FB)
                        { /* 33+53 bit pi is good enough */
                            y[0] = z - pio2_1t;
                            y[1] = (z - y[0]) - pio2_1t;
                        }
                        else
                        { /* near pi/2, use 33+33+53 bit pi */
                            z -= pio2_2;
                            y[0] = z - pio2_2t;
                            y[1] = (z - y[0]) - pio2_2t;
                        }
                        return 1;
                    }
                    else
                    { /* negative x */
                        z = x + pio2_1;
                        if (ix != 0x3FF921FB)
                        { /* 33+53 bit pi is good enough */
                            y[0] = z + pio2_1t;
                            y[1] = (z - y[0]) + pio2_1t;
                        }
                        else
                        { /* near pi/2, use 33+33+53 bit pi */
                            z += pio2_2;
                            y[0] = z + pio2_2t;
                            y[1] = (z - y[0]) + pio2_2t;
                        }
                        return -1;
                    }
                }
                if (ix <= 0x413921FB)
                { /* |x| ~<= 2^19*(pi/2), medium size */
                    t = Math.Abs(x);
                    n = (int)(t * invpio2 + half);
                    fn = (double)n;
                    r = t - fn * pio2_1;
                    w = fn * pio2_1t; /* 1st round good to 85 bit */
                    if (n < 32 && ix != npio2_hw[n - 1])
                    {
                        y[0] = r - w; /* quick check no cancellation */
                    }
                    else
                    {
                        uint high;
                        j = ix >> 20;
                        y[0] = r - w;
                        high = GetHighWordU(y[0]);
                        i = j - (int)((high >> 20) & 0x7FF);
                        if (i > 16)
                        { /* 2nd iteration needed, good to 118 */
                            t = r;
                            w = fn * pio2_2;
                            r = t - w;
                            w = fn * pio2_2t - ((t - r) - w);
                            y[0] = r - w;
                            high = GetHighWordU(y[0]);
                            i = j - (int)((high >> 20) & 0x7FF);
                            if (i > 49)
                            { /* 3rd iteration need, 151 bits acc */
                                t = r; /* will cover all possible cases */
                                w = fn * pio2_3;
                                r = t - w;
                                w = fn * pio2_3t - ((t - r) - w);
                                y[0] = r - w;
                            }
                        }
                    }
                    y[1] = (r - y[0]) - w;
                    if (hx < 0)
                    {
                        y[0] = -y[0];
                        y[1] = -y[1];
                        return -n;
                    }
                    else
                    {
                        return n;
                    }
                }
                /*
                 * all other (large) arguments
                 */
                if (ix >= 0x7FF00000)
                { /* x is inf or NaN */
                    y[0] = y[1] = x - x;
                    return 0;
                }
                /* set z = scalbn(|x|,ilogb(x)-23) */
                low = GetLowWord(x);
                z = SetLowWord(z, low);
                e0 = (ix >> 20) - 1046; /* e0 = ilogb(z)-23; */
                z = SetHighWord(z, ix - (int)((uint)e0 << 20));
                for (i = 0; i < 2; i++)
                {
                    tx[i] = (double)(int)z;
                    z = (z - tx[i]) * two24;
                }
                tx[2] = z;
                nx = 3;
                while (tx[nx - 1] == zero) nx--; /* skip zero term */
                n = KernelRemPio2(tx, y, e0, nx, 2);
                if (hx < 0)
                {
                    y[0] = -y[0];
                    y[1] = -y[1];
                    return -n;
                }
                return n;
            }
        }

        /* __kernel_cos( x,  y )
         * kernel cos function on [-pi/4, pi/4], pi/4 ~ 0.785398164
         * Input x is assumed to be bounded by ~pi/4 in magnitude.
         * Input y is the tail of x.
         * (Algorithm notes: see ieee754.cc.)
         */
        private static double KernelCos(double x, double y)
        {
            const double
                one = 1.00000000000000000000e+00, /* 0x3FF00000, 0x00000000 */
                C1 = 4.16666666666666019037e-02,  /* 0x3FA55555, 0x5555554C */
                C2 = -1.38888888888741095749e-03, /* 0xBF56C16C, 0x16C15177 */
                C3 = 2.48015872894767294178e-05,  /* 0x3EFA01A0, 0x19CB1590 */
                C4 = -2.75573143513906633035e-07, /* 0xBE927E4F, 0x809C52AD */
                C5 = 2.08757232129817482790e-09,  /* 0x3E21EE9E, 0xBDB4B1C4 */
                C6 = -1.13596475577881948265e-11; /* 0xBDA8FAE9, 0xBE8838D4 */

            unchecked
            {
                double a, iz, z, r, qx;
                int ix;
                ix = GetHighWord(x);
                ix &= 0x7FFFFFFF; /* ix = |x|'s high word*/
                if (ix < 0x3E400000)
                { /* if x < 2**27 */
                    if ((int)x == 0) return one; /* generate inexact */
                }
                z = x * x;
                r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
                if (ix < 0x3FD33333)
                { /* if |x| < 0.3 */
                    return one - (0.5 * z - (z * r - x * y));
                }
                else
                {
                    if (ix > 0x3FE90000)
                    { /* x > 0.78125 */
                        qx = 0.28125;
                    }
                    else
                    {
                        qx = InsertWords(ix - 0x00200000, 0); /* x/4 */
                    }
                    iz = 0.5 * z - qx;
                    a = one - qx;
                    return a - (iz - (z * r - x * y));
                }
            }
        }

        /* __kernel_rem_pio2(x,y,e0,nx,prec,ipio2)
         * double x[],y[]; int e0,nx,prec; int ipio2[];
         *
         * __kernel_rem_pio2 return the last three digits of N with
         *              y = x - N*pi/2
         * so that |y| < pi/2.
         * (Method and local-variable notes: see ieee754.cc.) ipio2 is two_over_pi, the only table V8 passes.
         */
        private static int KernelRemPio2(double[] x, double[] y, int e0, int nx, int prec)
        {
            /* Constants:
             * The hexadecimal values are the intended ones for the following
             * constants. The decimal values may be used, provided that the
             * compiler will convert from decimal to binary accurately enough
             * to produce the hexadecimal values shown.
             */
            const double
                zero = 0.0,
                one = 1.0,
                two24 = 1.67772160000000000000e+07,  /* 0x41700000, 0x00000000 */
                twon24 = 5.96046447753906250000e-08; /* 0x3E700000, 0x00000000 */

            int[] ipio2 = two_over_pi;

            unchecked
            {
                int jz, jx, jv, jp, jk, carry, n, i, j, k, m, q0, ih;
                int[] iq = new int[20];
                double z, fw;
                double[] f = new double[20];
                double[] fq = new double[20];
                double[] q = new double[20];

                /* initialize jk*/
                jk = init_jk[prec];
                jp = jk;

                /* determine jx,jv,q0, note that 3>q0 */
                jx = nx - 1;
                jv = (e0 - 3) / 24;
                if (jv < 0) jv = 0;
                q0 = e0 - 24 * (jv + 1);

                /* set up f[0] to f[jx+jk] where f[jx+jk] = ipio2[jv+jk] */
                j = jv - jx;
                m = jx + jk;
                for (i = 0; i <= m; i++, j++)
                {
                    f[i] = (j < 0) ? zero : (double)ipio2[j];
                }

                /* compute q[0],q[1],...q[jk] */
                for (i = 0; i <= jk; i++)
                {
                    for (j = 0, fw = 0.0; j <= jx; j++) fw += x[j] * f[jx + i - j];
                    q[i] = fw;
                }

                jz = jk;
            recompute:
                /* distill q[] into iq[] reversingly */
                for (i = 0, j = jz, z = q[jz]; j > 0; i++, j--)
                {
                    fw = (double)(int)(twon24 * z);
                    iq[i] = (int)(z - two24 * fw);
                    z = q[j - 1] + fw;
                }

                /* compute n */
                z = Scalbn(z, q0);                /* actual value of z */
                z -= 8.0 * Math.Floor(z * 0.125); /* trim off integer >= 8 */
                n = (int)z;
                z -= (double)n;
                ih = 0;
                if (q0 > 0)
                { /* need iq[jz-1] to determine n */
                    i = (iq[jz - 1] >> (24 - q0));
                    n += i;
                    iq[jz - 1] -= i << (24 - q0);
                    ih = iq[jz - 1] >> (23 - q0);
                }
                else if (q0 == 0)
                {
                    ih = iq[jz - 1] >> 23;
                }
                else if (z >= 0.5)
                {
                    ih = 2;
                }

                if (ih > 0)
                { /* q > 0.5 */
                    n += 1;
                    carry = 0;
                    for (i = 0; i < jz; i++)
                    { /* compute 1-q */
                        j = iq[i];
                        if (carry == 0)
                        {
                            if (j != 0)
                            {
                                carry = 1;
                                iq[i] = 0x1000000 - j;
                            }
                        }
                        else
                        {
                            iq[i] = 0xFFFFFF - j;
                        }
                    }
                    if (q0 > 0)
                    { /* rare case: chance is 1 in 12 */
                        switch (q0)
                        {
                            case 1:
                                iq[jz - 1] &= 0x7FFFFF;
                                break;
                            case 2:
                                iq[jz - 1] &= 0x3FFFFF;
                                break;
                        }
                    }
                    if (ih == 2)
                    {
                        z = one - z;
                        if (carry != 0) z -= Scalbn(one, q0);
                    }
                }

                /* check if recomputation is needed */
                if (z == zero)
                {
                    j = 0;
                    for (i = jz - 1; i >= jk; i--) j |= iq[i];
                    if (j == 0)
                    { /* need recomputation */
                        for (k = 1; jk >= k && iq[jk - k] == 0; k++)
                        {
                            /* k = no. of terms needed */
                        }

                        for (i = jz + 1; i <= jz + k; i++)
                        { /* add q[jz+1] to q[jz+k] */
                            f[jx + i] = ipio2[jv + i];
                            for (j = 0, fw = 0.0; j <= jx; j++) fw += x[j] * f[jx + i - j];
                            q[i] = fw;
                        }
                        jz += k;
                        goto recompute;
                    }
                }

                /* chop off zero terms */
                if (z == 0.0)
                {
                    jz -= 1;
                    q0 -= 24;
                    while (iq[jz] == 0)
                    {
                        jz--;
                        q0 -= 24;
                    }
                }
                else
                { /* break z into 24-bit if necessary */
                    z = Scalbn(z, -q0);
                    if (z >= two24)
                    {
                        fw = (double)(int)(twon24 * z);
                        iq[jz] = (int)(z - two24 * fw);
                        jz += 1;
                        q0 += 24;
                        iq[jz] = (int)fw;
                    }
                    else
                    {
                        iq[jz] = (int)z;
                    }
                }

                /* convert integer "bit" chunk to floating-point value */
                fw = Scalbn(one, q0);
                for (i = jz; i >= 0; i--)
                {
                    q[i] = fw * iq[i];
                    fw *= twon24;
                }

                /* compute PIo2[0,...,jp]*q[jz,...,0] */
                for (i = jz; i >= 0; i--)
                {
                    for (fw = 0.0, k = 0; k <= jp && k <= jz - i; k++) fw += PIo2[k] * q[i + k];
                    fq[jz - i] = fw;
                }

                /* compress fq[] into y[] */
                switch (prec)
                {
                    case 0:
                        fw = 0.0;
                        for (i = jz; i >= 0; i--) fw += fq[i];
                        y[0] = (ih == 0) ? fw : -fw;
                        break;
                    case 1:
                    case 2:
                        fw = 0.0;
                        for (i = jz; i >= 0; i--) fw += fq[i];
                        y[0] = (ih == 0) ? fw : -fw;
                        fw = fq[0] - fw;
                        for (i = 1; i <= jz; i++) fw += fq[i];
                        y[1] = (ih == 0) ? fw : -fw;
                        break;
                    case 3: /* painful */
                        for (i = jz; i > 0; i--)
                        {
                            fw = fq[i - 1] + fq[i];
                            fq[i] += fq[i - 1] - fw;
                            fq[i - 1] = fw;
                        }
                        for (i = jz; i > 1; i--)
                        {
                            fw = fq[i - 1] + fq[i];
                            fq[i] += fq[i - 1] - fw;
                            fq[i - 1] = fw;
                        }
                        for (fw = 0.0, i = jz; i >= 2; i--) fw += fq[i];
                        if (ih == 0)
                        {
                            y[0] = fq[0];
                            y[1] = fq[1];
                            y[2] = fw;
                        }
                        else
                        {
                            y[0] = -fq[0];
                            y[1] = -fq[1];
                            y[2] = -fw;
                        }
                        break;
                }
                return n & 7;
            }
        }

        /* __kernel_sin( x, y, iy)
         * kernel sin function on [-pi/4, pi/4], pi/4 ~ 0.7854
         * Input x is assumed to be bounded by ~pi/4 in magnitude.
         * Input y is the tail of x.
         * Input iy indicates whether y is 0. (if iy=0, y assume to be 0).
         * (Algorithm notes: see ieee754.cc.)
         */
        private static double KernelSin(double x, double y, int iy)
        {
            const double
                half = 5.00000000000000000000e-01, /* 0x3FE00000, 0x00000000 */
                S1 = -1.66666666666666324348e-01,  /* 0xBFC55555, 0x55555549 */
                S2 = 8.33333333332248946124e-03,   /* 0x3F811111, 0x1110F8A6 */
                S3 = -1.98412698298579493134e-04,  /* 0xBF2A01A0, 0x19C161D5 */
                S4 = 2.75573137070700676789e-06,   /* 0x3EC71DE3, 0x57B1FE7D */
                S5 = -2.50507602534068634195e-08,  /* 0xBE5AE5E6, 0x8A2B9CEB */
                S6 = 1.58969099521155010221e-10;   /* 0x3DE5D93A, 0x5ACFD57C */

            unchecked
            {
                double z, r, v;
                int ix;
                ix = GetHighWord(x);
                ix &= 0x7FFFFFFF; /* high word of x */
                if (ix < 0x3E400000)
                { /* |x| < 2**-27 */
                    if ((int)x == 0) return x;
                } /* generate inexact */
                z = x * x;
                v = z * x;
                r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
                if (iy == 0)
                {
                    return x + v * (S1 + z * r);
                }
                else
                {
                    return x - ((z * (half * y - v * r) - y) - v * S1);
                }
            }
        }

        /* atan(x) (ieee754.cc; private here, item F-02c lists no public atan) */
        private static double Atan(double x)
        {
            const double one = 1.0;
            double huge = 1.0e300;

            unchecked
            {
                double w, s1, s2, z;
                int ix, hx, id;

                hx = GetHighWord(x);
                ix = hx & 0x7FFFFFFF;
                if (ix >= 0x44100000)
                { /* if |x| >= 2^66 */
                    uint low;
                    low = GetLowWord(x);
                    if (ix > 0x7FF00000 || (ix == 0x7FF00000 && (low != 0)))
                        return x + x; /* NaN */
                    if (hx > 0)
                        return atanhi[3] + atanlo[3];
                    else
                        return -atanhi[3] - atanlo[3];
                }
                if (ix < 0x3FDC0000)
                { /* |x| < 0.4375 */
                    if (ix < 0x3E400000)
                    { /* |x| < 2^-27 */
                        if (huge + x > one) return x; /* raise inexact */
                    }
                    id = -1;
                }
                else
                {
                    x = Math.Abs(x);
                    if (ix < 0x3FF30000)
                    { /* |x| < 1.1875 */
                        if (ix < 0x3FE60000)
                        { /* 7/16 <=|x|<11/16 */
                            id = 0;
                            x = (2.0 * x - one) / (2.0 + x);
                        }
                        else
                        { /* 11/16<=|x|< 19/16 */
                            id = 1;
                            x = (x - one) / (x + one);
                        }
                    }
                    else
                    {
                        if (ix < 0x40038000)
                        { /* |x| < 2.4375 */
                            id = 2;
                            x = (x - 1.5) / (one + 1.5 * x);
                        }
                        else
                        { /* 2.4375 <= |x| < 2^66 */
                            id = 3;
                            x = -1.0 / x;
                        }
                    }
                }
                /* end of argument reduction */
                z = x * x;
                w = z * z;
                /* break sum from i=0 to 10 aT[i]z**(i+1) into odd and even poly */
                s1 = z * (aT[0] +
                          w * (aT[2] + w * (aT[4] + w * (aT[6] + w * (aT[8] + w * aT[10])))));
                s2 = w * (aT[1] + w * (aT[3] + w * (aT[5] + w * (aT[7] + w * aT[9]))));
                if (id < 0)
                {
                    return x - x * (s1 + s2);
                }
                else
                {
                    z = atanhi[id] - ((x * (s1 + s2) - atanlo[id]) - x);
                    return (hx < 0) ? -z : z;
                }
            }
        }

        /* atan2(y,x)
         * Method :
         *  1. Reduce y to positive by atan2(y,x)=-atan2(-y,x).
         *  2. Reduce x to positive by (if x and y are unexceptional):
         *    ARG (x+iy) = arctan(y/x)       ... if x > 0,
         *    ARG (x+iy) = pi - arctan[y/(-x)]   ... if x < 0,
         * (Special cases: see ieee754.cc.)
         */
        public static double Atan2(double y, double x)
        {
            double tiny = 1.0e-300;
            double zero = 0.0; // a runtime value, so -zero is -0.0 for certain
            const double
                pi_o_4 = 7.8539816339744827900E-01, /* 0x3FE921FB, 0x54442D18 */
                pi_o_2 = 1.5707963267948965580E+00, /* 0x3FF921FB, 0x54442D18 */
                pi = 3.1415926535897931160E+00;     /* 0x400921FB, 0x54442D18 */
            double pi_lo =
                1.2246467991473531772E-16; /* 0x3CA1A626, 0x33145C07 */

            unchecked
            {
                double z;
                int k, m, hx, hy, ix, iy;
                uint lx, ly;

                hx = GetHighWord(x);
                lx = GetLowWord(x);
                ix = hx & 0x7FFFFFFF;
                hy = GetHighWord(y);
                ly = GetLowWord(y);
                iy = hy & 0x7FFFFFFF;
                if (((uint)ix | ((lx | (uint)(-(int)lx)) >> 31)) > 0x7FF00000 ||
                    ((uint)iy | ((ly | (uint)(-(int)ly)) >> 31)) > 0x7FF00000)
                {
                    return x + y; /* x or y is NaN */
                }
                if (((uint)(hx - 0x3FF00000) | lx) == 0)
                {
                    return Atan(y); /* x=1.0 */
                }
                m = ((hy >> 31) & 1) | ((hx >> 30) & 2); /* 2*sign(x)+sign(y) */

                /* when y = 0 */
                if ((iy | (int)ly) == 0)
                {
                    switch (m)
                    {
                        case 0:
                        case 1:
                            return y; /* atan(+-0,+anything)=+-0 */
                        case 2:
                            return pi + tiny; /* atan(+0,-anything) = pi */
                        case 3:
                            return -pi - tiny; /* atan(-0,-anything) =-pi */
                    }
                }
                /* when x = 0 */
                if ((ix | (int)lx) == 0) return (hy < 0) ? -pi_o_2 - tiny : pi_o_2 + tiny;

                /* when x is INF */
                if (ix == 0x7FF00000)
                {
                    if (iy == 0x7FF00000)
                    {
                        switch (m)
                        {
                            case 0:
                                return pi_o_4 + tiny; /* atan(+INF,+INF) */
                            case 1:
                                return -pi_o_4 - tiny; /* atan(-INF,+INF) */
                            case 2:
                                return 3.0 * pi_o_4 + tiny; /*atan(+INF,-INF)*/
                            case 3:
                                return -3.0 * pi_o_4 - tiny; /*atan(-INF,-INF)*/
                        }
                    }
                    else
                    {
                        switch (m)
                        {
                            case 0:
                                return zero; /* atan(+...,+INF) */
                            case 1:
                                return -zero; /* atan(-...,+INF) */
                            case 2:
                                return pi + tiny; /* atan(+...,-INF) */
                            case 3:
                                return -pi - tiny; /* atan(-...,-INF) */
                        }
                    }
                }
                /* when y is INF */
                if (iy == 0x7FF00000) return (hy < 0) ? -pi_o_2 - tiny : pi_o_2 + tiny;

                /* compute y/x */
                k = (iy - ix) >> 20;
                if (k > 60)
                { /* |y/x| >  2**60 */
                    z = pi_o_2 + 0.5 * pi_lo;
                    m &= 1;
                }
                else if (hx < 0 && k < -60)
                {
                    z = 0.0; /* 0 > |y|/x > -2**-60 */
                }
                else
                {
                    z = Atan(Math.Abs(y / x)); /* safe to do y/x */
                }
                switch (m)
                {
                    case 0:
                        return z; /* atan(+,+) */
                    case 1:
                        return -z; /* atan(-,+) */
                    case 2:
                        return pi - (z - pi_lo); /* atan(+,-) */
                    default: /* case 3 */
                        return (z - pi_lo) - pi; /* atan(-,-) */
                }
            }
        }

        /* cos(x)
         * Return cosine function of x.
         *
         * kernel function:
         *      __kernel_sin            ... sine function on [-pi/4,pi/4]
         *      __kernel_cos            ... cosine function on [-pi/4,pi/4]
         *      __ieee754_rem_pio2      ... argument reduction routine
         */
        public static double Cos(double x)
        {
            double[] y = new double[2];
            double z = 0.0;
            int n, ix;

            ix = GetHighWord(x);

            ix &= 0x7FFFFFFF;
            if (ix <= 0x3FE921FB)
            {
                return KernelCos(x, z);
            }
            else if (ix >= 0x7FF00000)
            {
                return x - x;
            }
            else
            {
                n = RemPio2(x, y);
                switch (n & 3)
                {
                    case 0:
                        return KernelCos(y[0], y[1]);
                    case 1:
                        return -KernelSin(y[0], y[1], 1);
                    case 2:
                        return -KernelCos(y[0], y[1]);
                    default:
                        return KernelSin(y[0], y[1], 1);
                }
            }
        }

        /* exp(x)
         * Returns the exponential of x. (Method: see ieee754.cc.)
         */
        public static double Exp(double x)
        {
            const double
                one = 1.0,
                o_threshold = 7.09782712893383973096e+02,  /* 0x40862E42, 0xFEFA39EF */
                u_threshold = -7.45133219101941108420e+02, /* 0xC0874910, 0xD52D3051 */
                invln2 = 1.44269504088896338700e+00,       /* 0x3FF71547, 0x652B82FE */
                P1 = 1.66666666666666019037e-01,           /* 0x3FC55555, 0x5555553E */
                P2 = -2.77777777770155933842e-03,          /* 0xBF66C16C, 0x16BEBD93 */
                P3 = 6.61375632143793436117e-05,           /* 0x3F11566A, 0xAF25DE2C */
                P4 = -1.65339022054652515390e-06,          /* 0xBEBBBD41, 0xC5D26BF1 */
                P5 = 4.13813679705723846039e-08,           /* 0x3E663769, 0x72BEA4D0 */
                E = 2.718281828459045;                     /* 0x4005BF0A, 0x8B145769 */

            double
                huge = 1.0e+300,
                twom1000 = 9.33263618503218878990e-302, /* 2**-1000=0x01700000,0*/
                two1023 = 8.988465674311579539e307;     /* 0x1p1023 */

            unchecked
            {
                double y, hi = 0.0, lo = 0.0, c, t, twopk;
                int k = 0, xsb;
                uint hx;

                hx = GetHighWordU(x);
                xsb = (int)((hx >> 31) & 1); /* sign bit of x */
                hx &= 0x7FFFFFFF;            /* high word of |x| */

                /* filter out non-finite argument */
                if (hx >= 0x40862E42)
                { /* if |x|>=709.78... */
                    if (hx >= 0x7FF00000)
                    {
                        uint lx;
                        lx = GetLowWord(x);
                        if (((hx & 0xFFFFF) | lx) != 0)
                            return x + x; /* NaN */
                        else
                            return (xsb == 0) ? x : 0.0; /* exp(+-inf)={inf,0} */
                    }
                    if (x > o_threshold) return huge * huge;         /* overflow */
                    if (x < u_threshold) return twom1000 * twom1000; /* underflow */
                }

                /* argument reduction */
                if (hx > 0x3FD62E42)
                { /* if  |x| > 0.5 ln2 */
                    if (hx < 0x3FF0A2B2)
                    { /* and |x| < 1.5 ln2 */
                        /* TODO(rtoy): We special case exp(1) here to return the correct
                         * value of E, as the computation below would get the last bit
                         * wrong. We should probably fix the algorithm instead.
                         */
                        if (x == 1.0) return E;
                        hi = x - ln2HI[xsb];
                        lo = ln2LO[xsb];
                        k = 1 - xsb - xsb;
                    }
                    else
                    {
                        k = (int)(invln2 * x + halF[xsb]);
                        t = k;
                        hi = x - t * ln2HI[0]; /* t*ln2HI is exact here */
                        lo = t * ln2LO[0];
                    }
                    x = hi - lo;
                }
                else if (hx < 0x3E300000)
                { /* when |x|<2**-28 */
                    if (huge + x > one) return one + x; /* trigger inexact */
                }
                else
                {
                    k = 0;
                }

                /* x is now in primary range */
                t = x * x;
                if (k >= -1021)
                {
                    twopk = InsertWords(0x3FF00000 + (int)((uint)k << 20), 0);
                }
                else
                {
                    twopk = InsertWords((int)(0x3FF00000 + ((uint)(k + 1000) << 20)), 0);
                }
                c = x - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
                if (k == 0)
                {
                    return one - ((x * c) / (c - 2.0) - x);
                }
                else
                {
                    y = one - ((lo - (x * c) / (2.0 - c)) - hi);
                }
                if (k >= -1021)
                {
                    if (k == 1024) return y * 2.0 * two1023;
                    return y * twopk;
                }
                else
                {
                    return y * twopk * twom1000;
                }
            }
        }

        /* sin(x)
         * Return sine function of x.
         *
         * kernel function:
         *      __kernel_sin            ... sine function on [-pi/4,pi/4]
         *      __kernel_cos            ... cose function on [-pi/4,pi/4]
         *      __ieee754_rem_pio2      ... argument reduction routine
         */
        public static double Sin(double x)
        {
            double[] y = new double[2];
            double z = 0.0;
            int n, ix;

            /* High word of x. */
            ix = GetHighWord(x);

            /* |x| ~< pi/4 */
            ix &= 0x7FFFFFFF;
            if (ix <= 0x3FE921FB)
            {
                return KernelSin(x, z, 0);
            }
            else if (ix >= 0x7FF00000)
            {
                /* sin(Inf or NaN) is NaN */
                return x - x;
            }
            else
            {
                /* argument reduction needed */
                n = RemPio2(x, y);
                switch (n & 3)
                {
                    case 0:
                        return KernelSin(y[0], y[1], 1);
                    case 1:
                        return KernelCos(y[0], y[1]);
                    case 2:
                        return -KernelSin(y[0], y[1], 1);
                    default:
                        return -KernelCos(y[0], y[1]);
                }
            }
        }
    }
}
