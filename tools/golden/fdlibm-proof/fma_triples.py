# Writes fma-triples.bin: records of 4 big-endian u64 (a, b, c, math.fma(a,b,c)), >= 1.25M triples.
import math, random, struct, sys
R = random.Random(20261112)
def fb(b): return struct.unpack('>d', struct.pack('>Q', b & 0xFFFFFFFFFFFFFFFF))[0]
def bt(x): return struct.unpack('>Q', struct.pack('>d', x))[0]
def wexp(lo, hi): return fb((R.getrandbits(1) << 63) | (R.randint(lo, hi) << 52) | R.getrandbits(52))
def rbits(): return fb(R.getrandbits(64))
def ulp_off(x, d):
    b = bt(x); s = b & (1 << 63); m = (b & ~(1 << 63)) + d
    m = max(0, min(m, 0x7FEFFFFFFFFFFFFF)); return fb(s | m)
out = []
def add(a, b, c): out.append((a, b, c))
for _ in range(200000): add(rbits(), rbits(), rbits())
for _ in range(200000): add(wexp(1023-60, 1023+60), wexp(1023-60, 1023+60), wexp(1023-120, 1023+120))
for _ in range(200000):  # near-cancellation: c = -round(a*b) moved a few ulps
    a, b = wexp(1023-200, 1023+200), wexp(1023-200, 1023+200); p = a*b
    if math.isinf(p) or p == 0: p = 1.0
    add(a, b, ulp_off(-p, R.randint(-8, 8)))
for _ in range(100000):  # exact cancellation of the rounded product
    a, b = wexp(1023-300, 1023+300), wexp(1023-300, 1023+300); add(a, b, -(a*b))
for _ in range(100000):  # c close to a*b in size, other sign, relative change
    a, b = wexp(1023-30, 1023+30), wexp(1023-30, 1023+30); add(a, b, -(a*b)*(1 + R.uniform(-1e-3, 1e-3)))
for _ in range(150000):  # subnormal and min-normal results
    a = wexp(1023-540, 1023-500); b = fb(R.getrandbits(1) << 63 | (R.randint(0, 80) << 52) | R.getrandbits(52))
    b = ulp_off(b, 0) if b != 0 else 5e-324
    k = R.randint(0, 3)
    c = 0.0 if k == 0 else (-0.0 if k == 1 else (wexp(0, 3) if k == 2 else -(a*b)*R.choice([0.5, 1.0, 1.5, 0.999999])))
    add(a, b, c)
for _ in range(50000):  # overflow region
    a, b = wexp(1023+500, 1023+530), wexp(1023+490, 1023+500); add(a, b, -R.choice([1.0, 1e308, 1.7976931348623157e308, -1e308]) * a/abs(a) * b/abs(b))
for _ in range(50000):  # product underflowing to a signed zero, z = +-0
    a, b = wexp(1, 300), wexp(1, 300); add(a, b, R.choice([0.0, -0.0]))
for _ in range(100000):  # very different exponents
    if R.getrandbits(1): add(wexp(1023-10, 1023+10), wexp(1023-10, 1023+10), wexp(1023+100, 1023+700))
    else: add(wexp(1023-10, 1023+10), wexp(1023-10, 1023+10), wexp(1023-700, 1023-100))
for _ in range(100000):  # integer ties: products near 2^53..2^56 plus small integers
    a = float(R.randint(1 << 26, 1 << 27)) * R.choice([1, -1]); b = float(R.randint(1 << 26, 1 << 28))
    add(a, b, float(R.randint(-(1 << 20), 1 << 20)) + R.choice([0.0, 0.5, 0.25]))
for _ in range(50000):  # ties at double precision: a*b = 1 + 2^-53 patterns
    a = 1.0 + R.randint(0, (1 << 26) - 1) * 2.0**-26; b = 1.0 + R.randint(0, (1 << 26) - 1) * 2.0**-26
    add(a, b, R.choice([0.0, 2.0**-53, -2.0**-53, 2.0**-54, -1.0, 1.0, -a*b]))
sp = [0.0, -0.0, math.inf, -math.inf, math.nan, 1.0, -1.0, 5e-324, -5e-324, 2.2250738585072014e-308,
      1.7976931348623157e308, -1.7976931348623157e308, 0.5, 3.0, 1e-300, 1e300]
for a in sp:
    for b in sp:
        for c in sp: add(a, b, c)
ovf=[0]; inv=[0]
with open(sys.argv[1], 'wb') as fh:
    for a, b, c in out:
        try:
            r = math.fma(a, b, c)
        except OverflowError:  # finite inputs, |exact| rounds past DBL_MAX: +-inf with the exact sign
            from fractions import Fraction
            r = math.inf if Fraction(a) * Fraction(b) + Fraction(c) > 0 else -math.inf
            ovf[0] += 1
        except ValueError:  # invalid (inf*0, inf-inf): NaN
            r = math.nan
            inv[0] += 1
        fh.write(struct.pack('>4Q', bt(a), bt(b), bt(c), bt(r)))
print(len(out), 'triples;', ovf[0], 'overflow,', inv[0], 'invalid')
