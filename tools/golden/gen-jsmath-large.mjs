#!/usr/bin/env node
// Large seeded vector set for F-02c (U-049): node's Math.sin, Math.cos, Math.atan2, Math.exp over at least 200,000
// inputs per function, built to reach every branch of V8's fdlibm (tiny, the kernel range, near multiples of pi/2,
// the medium and the big (__kernel_rem_pio2) reduction paths, negative, subnormal, infinities and NaN, exp overflow
// and underflow edges and the subnormal-result path, every atan reduction band, atan2's |y/x| > 2^60 and < 2^-60
// and x == 1 shortcuts). The full set is ~30 MB, so the committed form is the seed plus SHA-256 hashes:
//   node tools/golden/gen-jsmath-large.mjs > tools/golden/jsmath-fdlibm-large.json     (compact, committed)
//   node tools/golden/gen-jsmath-large.mjs --full > /some/scratch/large.json           (every vector, not committed)
// The inputs come only from integer operations on a mulberry32 stream and IEEE-754 basic operations, so the C#
// test (JsMathFdlibmTests.LargeSeededSetMatchesNodeHashes) and tools/golden/fdlibm_replay.py rebuild the same inputs
// and hash their own outputs. Hash = SHA-256 over each output's 8 bytes, big-endian, every NaN written as
// 7ff8000000000000; the inputs are hashed the same way, so a generator drift is told apart from a math drift.
import { createHash } from 'node:crypto'

export const SEED = 0x5eed2026
const PI = Math.PI
const view = new DataView(new ArrayBuffer(8))
const toBits = (x) => { view.setFloat64(0, x); return view.getBigUint64(0) }
const fromBits = (b) => { view.setBigUint64(0, BigInt.asUintN(64, b)); return view.getFloat64(0) }
const fromWords = (hi, lo) => fromBits((BigInt(hi >>> 0) << 32n) | BigInt(lo >>> 0))

let s = SEED >>> 0
const u32 = () => { s = (s + 0x6D2B79F5) >>> 0; let t = s; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return (t ^ (t >>> 14)) >>> 0 }
const unit = () => u32() / 4294967296                       // [0, 1), exact
const uniform = (a) => (unit() * 2 - 1) * a                 // [-a, a)
const below = (n) => u32() % n                              // 0..n-1
const withExp = (e) => {                                     // random sign and mantissa, biased exponent e
  const sign = u32() & 1, mhi = u32() & 0xFFFFF, lo = u32()
  return fromWords(((sign << 31) | (e << 20) | mhi) >>> 0, lo)
}
const randBits = () => { const hi = u32(), lo = u32(); return fromWords(hi, lo) }
const offsetUlps = (x, d) => {                               // move |x| by d units in the last place, keep the sign
  const b = toBits(x), sign = b & 0x8000000000000000n, mag = b & 0x7FFFFFFFFFFFFFFFn
  let m = mag + BigInt(d)
  if (m < 0n) m = 0n
  if (m > 0x7FEFFFFFFFFFFFFFn) m = 0x7FEFFFFFFFFFFFFFn
  return fromBits(sign | m)
}

const SPECIALS = [0, -0, Infinity, -Infinity, NaN, fromBits(0xFFF8000000000000n), fromBits(0x7FF0000000000001n),
  5e-324, -5e-324, 2.2250738585072014e-308, -2.2250738585072014e-308, 2.225073858507201e-308, 1e-300, 1e-8, -1e-8,
  1, -1, 0.5, -0.5, 2, 1e300, -1e300, 1.7976931348623157e308, -1.7976931348623157e308]
const ANGLES = [PI, -PI, PI / 2, -PI / 2, PI / 4, -PI / 4, 3 * PI / 4, 2 * PI, 1e5, 1e6, 1e9, 1e15, 1e22, 2 ** 52,
  2 ** 1023, 0.7853981633974483, 2.356194490192345, 3.9269908169872414, 5.497787143782138, 1.5707963267948966,
  4.71238898038469, 823549.6, 823550, 1.4901161193847656e-8, 7.450580596923828e-9]
const SIN_THRESHOLDS = [0x3E400000, 0x3E500000, 0x3FD33333, 0x3FE90000, 0x3FE921FB, 0x3FF921FB, 0x4002D97C,
  0x400921FB, 0x4012D97C, 0x413921FB, 0x41500000, 0x7FEFFFFF]

function sinInputs() {
  const out = [...SPECIALS, ...ANGLES]
  for (let i = 0; i < 20000; i++) out.push(withExp(below(0x3E4)))                 // tiny, |x| < 2^-27, subnormal
  for (let i = 0; i < 30000; i++) out.push(withExp(0x3E4 + below(0x3FE - 0x3E4 + 1)))  // kernel range up to 1
  for (let i = 0; i < 40000; i++) out.push(uniform(8 * PI))                        // the Sim's angles
  for (let i = 0; i < 50000; i++) {                                                // near multiples of pi/2
    const k = (i & 1) ? below(2001) - 1000 : below(1048577) - 524288
    const x = k * (PI / 2)
    out.push(x === 0 ? withExp(below(0x3FF)) : offsetUlps(x, below(2049) - 1024))
  }
  for (let i = 0; i < 30000; i++) out.push(uniform(823549.6))                      // medium reduction
  for (let i = 0; i < 10000; i++) out.push(withExp(0x413 + below(11)))             // just past 2^19*pi/2
  for (let i = 0; i < 50000; i++) out.push(withExp(0x41E + below(0x7FE - 0x41E + 1)))  // huge: __kernel_rem_pio2
  for (let i = 0; i < 12000; i++) {                                                // high-word thresholds
    const hw = SIN_THRESHOLDS[i % SIN_THRESHOLDS.length] + below(5) - 2, lo = u32()
    out.push(fromWords((((u32() & 1) << 31) | hw) >>> 0, lo))
  }
  for (const hw of [0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB, 0x402921FB,
    0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C, 0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB, 0x403AB41B,
    0x403C463A, 0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C, 0x4043A28C, 0x40446B9C,
    0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB, 0x404858EB, 0x404921FB]) {     // npio2_hw: the 2nd/3rd rounds
    for (let i = 0; i < 300; i++) out.push(fromWords((((u32() & 1) << 31) | hw) >>> 0, u32()))
  }
  for (let i = 0; i < 20000; i++) out.push(randBits())                              // any bit pattern
  return out
}

function expInputs() {
  const out = [...SPECIALS, 709.782712893384, 709.7827128933841, -745.1332191019411, -745.1332191019412,
    0.6931471805599453, -0.6931471805599453, 0.34657359027997264, 1.0397207708399179, 7.09782712893383973096e+02,
    -7.45133219101941108420e+02, -708.3964185322641, -708.3964185322642, 1024 * 0.6931471805599453]
  for (let i = 0; i < 20000; i++) out.push(withExp(below(0x3E3)))                  // |x| < 2^-28, subnormal
  for (let i = 0; i < 30000; i++) out.push(uniform(0.34657359027997264))           // k = 0 path
  for (let i = 0; i < 30000; i++) {                                                // 0.5 ln2 .. 1.5 ln2
    const x = 0.34657359027997264 + unit() * (1.0397207708399179 - 0.34657359027997264)
    out.push((u32() & 1) ? -x : x)
  }
  for (let i = 0; i < 40000; i++) out.push(uniform(20))
  for (let i = 0; i < 50000; i++) out.push(-745.2 + unit() * (709.9 + 745.2))      // the whole finite range
  for (let i = 0; i < 10000; i++) out.push(offsetUlps(709.782712893384, below(200001) - 100000))   // overflow edge
  for (let i = 0; i < 10000; i++) out.push(offsetUlps(-745.1332191019411, below(200001) - 100000)) // underflow edge
  for (let i = 0; i < 20000; i++) out.push(-708 - unit() * 37.14)                 // subnormal results, k < -1021
  for (let i = 0; i < 20000; i++) {                                                // where k changes
    const k = below(2048) - 1024
    out.push(offsetUlps((k + 0.5) * 0.6931471805599453, below(65) - 32))
  }
  for (let i = 0; i < 20000; i++) out.push(randBits())
  return out
}

function atan2Inputs() {
  const axis = [0, -0, 1, -1, Infinity, -Infinity, NaN, 5e-324, -5e-324, 1e300, -1e300, 2.2250738585072014e-308]
  const out = []
  for (const y of axis) for (const x of axis) out.push([y, x])
  for (let i = 0; i < 50000; i++) { const y = uniform(120); out.push([y, uniform(120)]) }   // map-sized deltas
  for (let i = 0; i < 40000; i++) { const y = randBits(); out.push([y, randBits()]) }
  const bands = [[0, 0.4375], [0.4375, 0.6875], [0.6875, 1.1875], [1.1875, 2.4375], [2.4375, 1048576],
    [1048576, 7.378697629483821e19]]                                             // every atan reduction band
  for (let i = 0; i < 60000; i++) {
    const [lo, hi] = bands[i % bands.length]
    let x = uniform(1000)
    if (x === 0) x = 1
    const r = lo + unit() * (hi - lo)
    out.push([(u32() & 1) ? -x * r : x * r, x])
  }
  for (let i = 0; i < 20000; i++) {                                                // |y/x| near 2^60 and 2^-60
    const e1 = 0x100 + below(0x500), d = 55 + below(12)
    const e2 = (i & 1) ? e1 + d : e1 - d
    const y = withExp(e2), x = withExp(e1)
    out.push((i & 2) ? [x, y] : [y, x])
  }
  for (let i = 0; i < 20000; i++) out.push([(i & 1) ? randBits() : uniform(10), 1])     // x == 1: atan(y)
  for (let i = 0; i < 20000; i++) { const y = withExp(below(0x31)); out.push([y, withExp(below(0x31))]) }
  return out
}

const CANON_NAN = 0x7FF8000000000000n
const hashOf = (values) => {
  const h = createHash('sha256'), b = Buffer.alloc(8)
  for (const v of values) { b.writeBigUInt64BE(Number.isNaN(v) ? CANON_NAN : toBits(v)); h.update(b) }
  return h.digest('hex')
}
const hex = (x) => toBits(x).toString(16).padStart(16, '0')

const sinIn = sinInputs(), expIn = expInputs(), atan2In = atan2Inputs()
const sinOut = sinIn.map((x) => Math.sin(x)), cosOut = sinIn.map((x) => Math.cos(x))
const expOut = expIn.map((x) => Math.exp(x)), atan2Out = atan2In.map(([y, x]) => Math.atan2(y, x))

if (process.argv.includes('--full')) {
  process.stdout.write(JSON.stringify({ generator: 'tools/golden/gen-jsmath-large.mjs --full', node: process.version,
    v8: process.versions.v8, arch: process.arch, seed: SEED,
    sin: sinIn.map((x, i) => [hex(x), hex(sinOut[i])]), cos: sinIn.map((x, i) => [hex(x), hex(cosOut[i])]),
    exp: expIn.map((x, i) => [hex(x), hex(expOut[i])]),
    atan2: atan2In.map(([y, x], i) => [hex(y), hex(x), hex(atan2Out[i])]) }) + '\n')
} else {
  process.stdout.write(JSON.stringify({ generator: 'tools/golden/gen-jsmath-large.mjs', node: process.version,
    v8: process.versions.v8, arch: process.arch, seed: SEED,
    note: 'sha256 over 8-byte big-endian IEEE-754 bits, NaN as 7ff8000000000000; inputs of atan2 are y then x per pair',
    counts: { sin: sinIn.length, cos: sinIn.length, exp: expIn.length, atan2: atan2In.length },
    inputs: { sin: hashOf(sinIn), exp: hashOf(expIn), atan2: hashOf(atan2In.flat()) },
    outputs: { sin: hashOf(sinOut), cos: hashOf(cosOut), exp: hashOf(expOut), atan2: hashOf(atan2Out) } }, null, 1) + '\n')
}
