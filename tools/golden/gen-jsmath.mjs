#!/usr/bin/env node
// Golden vectors for F-02c (U-035): V8's Math.sin, Math.cos, Math.atan2 and Math.exp (fdlibm, src/base/ieee754.cc;
// node builds V8 without V8_USE_LIBM_TRIG_FUNCTIONS, so sin and cos are fdlibm too). Every number is the 16-hex-digit
// IEEE-754 bit pattern (big-endian), so the C# test compares bits, never decimal text. Deterministic: inputs come from
// a fixed mulberry32 seed plus a fixed edge list. Run from the repo root:
//   node tools/golden/gen-jsmath.mjs > tools/golden/jsmath-fdlibm.json
const buf = new DataView(new ArrayBuffer(8))
const hex = (x) => { buf.setFloat64(0, x); return buf.getBigUint64(0).toString(16).padStart(16, '0') }
const fromHex = (h) => { buf.setBigUint64(0, BigInt('0x' + h)); return buf.getFloat64(0) }
let s = 0x2c0ffee >>> 0
const rnd = () => { s = (s + 0x6D2B79F5) >>> 0; let t = s; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296 }
const randBits = () => { const hi = Math.floor(rnd() * 2 ** 32) >>> 0, lo = Math.floor(rnd() * 2 ** 32) >>> 0; return fromHex(hi.toString(16).padStart(8, '0') + lo.toString(16).padStart(8, '0')) }
const PI = Math.PI
const specials = [0, -0, Infinity, -Infinity, NaN, 5e-324, -5e-324, 2.2250738585072014e-308, 1e-300, 1e-8, -1e-8,
  1, -1, 0.5, 2, 1e300, -1e300, 1.7976931348623157e308]
const angleEdges = [PI, -PI, PI / 2, -PI / 2, PI / 4, 3 * PI / 4, 2 * PI, 1e5, 1e6, 1e9, 1e15, 1e22, 2 ** 52, 2 ** 1023,
  0.7853981633974483, 2.356194490192345, 3.9269908169872414, 5.497787143782138, 1.5707963267948966, 4.71238898038469]
const sinIn = [...specials, ...angleEdges]
for (let i = 0; i < 1500; i++) sinIn.push((rnd() * 2 - 1) * 8 * PI)          // the Sim's range: angles of a few turns
for (let i = 0; i < 300; i++) sinIn.push((rnd() * 2 - 1) * 1e6)              // medium arguments (rem_pio2 medium path)
for (let i = 0; i < 200; i++) sinIn.push(randBits())                         // any bit pattern (large-argument path)
const expIn = [...specials, 709.782712893384, 709.7827128933841, -745.1332191019411, -745.1332191019412, 1, -1,
  0.6931471805599453, -0.6931471805599453, 0.34657359027997264, 1.0397207708399179, 7.09782712893383973096e+02]
for (let i = 0; i < 1500; i++) expIn.push((rnd() * 2 - 1) * 20)
for (let i = 0; i < 300; i++) expIn.push((rnd() * 2 - 1) * 745)
for (let i = 0; i < 200; i++) expIn.push((rnd() * 2 - 1) * 1e-6)
const atan2In = []
const axis = [0, -0, 1, -1, Infinity, -Infinity, NaN, 5e-324, 1e300]
for (const y of axis) for (const x of axis) atan2In.push([y, x])
for (let i = 0; i < 1500; i++) atan2In.push([(rnd() * 2 - 1) * 120, (rnd() * 2 - 1) * 120])  // map-sized deltas
for (let i = 0; i < 300; i++) atan2In.push([(rnd() * 2 - 1) * 1e-3, (rnd() * 2 - 1) * 1e3])
for (let i = 0; i < 200; i++) atan2In.push([randBits(), randBits()])
const out = { generator: 'tools/golden/gen-jsmath.mjs', node: process.version, v8: process.versions.v8,
  note: 'hex = IEEE-754 binary64 bits, big-endian; NaN outputs compare with double.IsNaN, every other output bit for bit',
  sin: sinIn.map((x) => [hex(x), hex(Math.sin(x))]),
  cos: sinIn.map((x) => [hex(x), hex(Math.cos(x))]),
  exp: expIn.map((x) => [hex(x), hex(Math.exp(x))]),
  atan2: atan2In.map(([y, x]) => [hex(y), hex(x), hex(Math.atan2(y, x))]) }
process.stdout.write(JSON.stringify(out) + '\n')
