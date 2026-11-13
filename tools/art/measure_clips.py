#!/usr/bin/env python3
"""Measures the KayKit clips the game imports and writes Assets/_Game/Resources/Anim/clip_facts.json (DG-72).

Forward kinematics on the source Knight.glb (the file build_kaykit_clips.py exports KayKit_Clips.fbx from), with no
dependencies: a GLB reader, linear/step key sampling with quaternion nlerp, and the bone chain multiplied out per frame.
Units are glTF metres = Unity units (the FBX export applies the unit scale). Every clip carries translation and scale keys
on every bone, so the game models play the Knight's exact strides whatever their mesh height.

Per clip (game name from kaykit_clip_map.tsv):
  length            seconds
  loop              from the map
  groundSpeed       planted-foot ground speed, u/s (stance = toe within 0.03 u of its lowest point, 120 Hz)
  moveDeg           direction the body travels relative to its facing, degrees, + = to the character's left
  leftPlant         normalised phase of the left foot's mid-stance (the locomotion sync marker)
  touchdown         normalised phases where the left and right toes reach the ground (footfall events)
  contact           seconds: weapon hand (handslot.r) peak speed (the hit frame of attack clips)
  lowest            seconds: weapon hand lowest point
  swingPlane        "horizontal" or "vertical" around the contact
  hipsDrift         hips XZ travel over the clip, u (root-motion policy: looping clips must stay under 0.05)
  headStart/headEnd head height at the first and last frame, u
Usage: python3 -I tools/art/measure_clips.py KNIGHT_GLB [OUT_JSON]
"""
import json
import math
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
FPS = 120


def load_glb(path):
    b = open(path, 'rb').read()
    assert b[:4] == b'glTF', path + ' is not a GLB'
    off, js, bn = 12, None, None
    while off < len(b):
        ln, typ = struct.unpack_from('<I4s', b, off)
        off += 8
        chunk = b[off:off + ln]
        off += ln
        if typ == b'JSON':
            js = json.loads(chunk)
        elif typ == b'BIN\x00':
            bn = chunk
    return js, bn


def clip_map():
    out = []
    for line in open(os.path.join(HERE, 'kaykit_clip_map.tsv'), encoding='utf-8'):
        if line.strip() and not line.startswith('#'):
            k, g, loop = line.rstrip('\n').split('\t')[:3]
            out.append((k, g, loop == '1'))
    return out


class Rig:
    def __init__(self, js, bn):
        self.js, self.bn = js, bn
        self.nodes = js['nodes']
        self.parent = {}
        for i, n in enumerate(self.nodes):
            for c in n.get('children', []):
                self.parent[c] = i
        self.idx = {n.get('name'): i for i, n in enumerate(self.nodes)}
        self.anims = {a['name']: a for a in js['animations']}

    def read(self, ai):
        a = self.js['accessors'][ai]
        bv = self.js['bufferViews'][a['bufferView']]
        n = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[a['type']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        st = bv.get('byteStride', 4 * n)
        return [struct.unpack_from('<' + 'f' * n, self.bn, off + i * st) for i in range(a['count'])]

    def prep(self, name):
        a = self.anims[name]
        ch, length = {}, 0.0
        for c in a['channels']:
            s = a['samplers'][c['sampler']]
            tm = [x[0] for x in self.read(s['input'])]
            ch[(c['target']['node'], c['target']['path'])] = (tm, self.read(s['output']), s.get('interpolation', 'LINEAR'))
            length = max(length, tm[-1])
        return ch, length

    def world(self, ch, t, names):
        cache = {}

        def w(i):
            if i in cache:
                return cache[i]
            n = self.nodes[i]
            T = n.get('translation', (0, 0, 0))
            R = n.get('rotation', (0, 0, 0, 1))
            S = n.get('scale', (1, 1, 1))
            if (i, 'translation') in ch:
                T = sample(*ch[(i, 'translation')], t, 3)
            if (i, 'rotation') in ch:
                R = sample(*ch[(i, 'rotation')], t, 4)
            if (i, 'scale') in ch:
                S = sample(*ch[(i, 'scale')], t, 3)
            p = self.parent.get(i)
            if p is None:
                r = (tuple(T), tuple(R), tuple(S))
            else:
                pt, pr, ps = w(p)
                rt = qrot(pr, (T[0] * ps[0], T[1] * ps[1], T[2] * ps[2]))
                r = ((pt[0] + rt[0], pt[1] + rt[1], pt[2] + rt[2]), qmul(pr, R), (ps[0] * S[0], ps[1] * S[1], ps[2] * S[2]))
            cache[i] = r
            return r
        return {nm: w(self.idx[nm])[0] for nm in names}


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx))


def sample(times, vals, interp, t, n):
    if interp == 'CUBICSPLINE':
        vals = vals[1::3]
    if t <= times[0]:
        return vals[0]
    if t >= times[-1]:
        return vals[-1]
    lo, hi = 0, len(times) - 1
    while hi - lo > 1:
        m = (lo + hi) // 2
        if times[m] <= t:
            lo = m
        else:
            hi = m
    if interp == 'STEP':
        return vals[lo]
    u = (t - times[lo]) / (times[hi] - times[lo])
    a, b = vals[lo], vals[hi]
    if n == 4:
        if sum(x * y for x, y in zip(a, b)) < 0:
            b = tuple(-x for x in b)
        r = tuple(x + (y - x) * u for x, y in zip(a, b))
        ln = math.sqrt(sum(x * x for x in r))
        return tuple(x / ln for x in r)
    return tuple(x + (y - x) * u for x, y in zip(a, b))


def circ_mean(frames, n):
    if not frames:
        return -1.0
    s = sum(math.sin(2 * math.pi * k / n) for k in frames)
    c = sum(math.cos(2 * math.pi * k / n) for k in frames)
    return round((math.atan2(s, c) / (2 * math.pi)) % 1.0, 4)


def measure(rig, kay, game, loop):
    ch, length = rig.prep(kay)
    n = max(2, int(round(length * FPS)))
    names = ['toes.l', 'toes.r', 'hips', 'head', 'handslot.r']
    P = [rig.world(ch, k * length / n, names) for k in range(n + 1)]
    dt = length / n
    f = {'clip': game, 'source': kay, 'length': round(length, 4), 'loop': loop}
    speeds, vx_all, vz_all, touch = [], 0.0, 0.0, []
    left_plant = -1.0
    for foot in ('toes.l', 'toes.r'):
        ys = [p[foot][1] for p in P]
        ymin = min(ys)
        planted = [k for k in range(n) if ys[k] < ymin + 0.03 and ys[k + 1] < ymin + 0.03]
        vx = vz = 0.0
        for k in planted:
            vx += (P[k + 1][foot][0] - P[k][foot][0]) / dt
            vz += (P[k + 1][foot][2] - P[k][foot][2]) / dt
        if planted:
            vx /= len(planted)
            vz /= len(planted)
        speeds.append(math.hypot(vx, vz))
        vx_all += vx
        vz_all += vz
        td = [k for k in range(1, n + 1) if ys[k] < ymin + 0.03 <= ys[k - 1]]
        touch.append(round(td[0] / n, 4) if td else -1.0)
        if foot == 'toes.l':
            left_plant = circ_mean([k for k in range(n) if ys[k] < ymin + 0.03], n) if len(planted) < n - 2 else -1.0
    moving = min(speeds) > 0.2
    f['groundSpeed'] = round(sum(speeds) / 2, 4) if moving else 0.0
    # glTF: the character faces +Z and its left is +X; the body moves opposite the planted foot.
    f['moveDeg'] = round(math.degrees(math.atan2(-vx_all, -vz_all)), 1) if moving else 0.0
    f['leftPlant'] = left_plant if moving else -1.0
    f['touchdown'] = touch if moving else [-1.0, -1.0]
    hs = [math.dist(P[k + 1]['handslot.r'], P[k]['handslot.r']) / dt for k in range(n)]
    pk = max(range(n), key=lambda k: hs[k])
    low = min(range(n + 1), key=lambda k: P[k]['handslot.r'][1])
    a, b = P[max(0, pk - 6)]['handslot.r'], P[min(n, pk + 6)]['handslot.r']
    f['contact'] = round(pk * dt, 4)
    f['contactSpeed'] = round(hs[pk], 3)
    f['lowest'] = round(low * dt, 4)
    f['swingPlane'] = 'horizontal' if math.hypot(b[0] - a[0], b[2] - a[2]) > abs(b[1] - a[1]) else 'vertical'
    f['hipsDrift'] = round(math.hypot(P[n]['hips'][0] - P[0]['hips'][0], P[n]['hips'][2] - P[0]['hips'][2]), 4)
    f['headStart'] = round(P[0]['head'][1], 3)
    f['headEnd'] = round(P[n]['head'][1], 3)
    return f


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    src = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, '..', '..', 'Assets', '_Game', 'Resources', 'Anim', 'clip_facts.json')
    rig = Rig(*load_glb(src))
    facts = [measure(rig, k, g, loop) for k, g, loop in clip_map()]
    doc = {'source': 'KayKit Adventurers 1.0 Knight.glb (CC0), tools/art/measure_clips.py', 'fps': FPS, 'clips': facts}
    with open(out, 'w', encoding='utf-8') as fh:
        json.dump(doc, fh, indent=1)
        fh.write('\n')
    for f in facts:
        print(f"{f['clip']:14s} len {f['length']:.3f} speed {f['groundSpeed']:.2f} move {f['moveDeg']:+6.1f} plant {f['leftPlant']:.3f} "
              f"contact {f['contact']:.3f} {f['swingPlane']:10s} drift {f['hipsDrift']:.3f}")


if __name__ == '__main__':
    main()
