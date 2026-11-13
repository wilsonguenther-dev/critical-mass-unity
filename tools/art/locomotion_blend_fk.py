#!/usr/bin/env python3
"""Locomotion blend check by forward kinematics (fix/animation-reds, 2026-11-13). No Unity, no Blender.

Plays the game's locomotion maths (Locomotion.cs: six directions 60 degrees apart, one shared phase advanced by distance
over the blended stride, SampleTime with the left-plant markers, reversal) on the KayKit Knight.glb skeleton, mixing the
clips per bone the way an AnimationMixerPlayable does (weighted translation and scale, normalised weighted quaternions),
moves the body at 4.5 u/s for 60 frames at 30 Hz and measures the planted-foot slide the PlayMode test measures
(MotionSanityPlayTests.PlantedSlide: toe within 0.03 u of its lowest, consecutive frames of the same toe).
  old:  the batch 3 blend (neighbours always mixed). Reproduces the integrator's 0.98 u/u at +-90 degrees.
  new:  sideways sectors play one strafe and turn the legs by the remainder (ActorAnimator.ApplyLegWarp).
Also prints how far the planted toe points from the leg yaw the animator reports (the facing tests' cue).
Usage: python3 -I tools/art/locomotion_blend_fk.py KNIGHT_GLB [CLIP_FACTS_JSON]
"""
import sys, math, os, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import measure_clips as mc
if len(sys.argv) < 2:
    sys.exit(__doc__)
rig = mc.Rig(*mc.load_glb(sys.argv[1]))
CL = {'Run':'Running_A','StrafeL':'Running_Strafe_Left','StrafeR':'Running_Strafe_Right','Walk':'Walking_A','WalkBack':'Walking_Backwards'}
prep = {g: rig.prep(k) for g,k in CL.items()}
nodes = rig.nodes
def local(ch, i, t):
    n = nodes[i]
    T = n.get('translation',(0,0,0)); R = n.get('rotation',(0,0,0,1)); S = n.get('scale',(1,1,1))
    if (i,'translation') in ch: T = mc.sample(*ch[(i,'translation')], t, 3)
    if (i,'rotation') in ch: R = mc.sample(*ch[(i,'rotation')], t, 4)
    if (i,'scale') in ch: S = mc.sample(*ch[(i,'scale')], t, 3)
    return T,R,S
def blend_local(entries, i):
    # entries: list of (clipname, time, weight)
    T=[0,0,0]; S=[0,0,0]; R=[0,0,0,0]; ref=None; ws=0
    for c,t,w in entries:
        lt,lr,ls = local(prep[c][0], i, t)
        if ref is None: ref = lr
        if sum(a*b for a,b in zip(ref,lr))<0: lr=tuple(-x for x in lr)
        for k in range(3): T[k]+=w*lt[k]; S[k]+=w*ls[k]
        for k in range(4): R[k]+=w*lr[k]
        ws+=w
    ln=math.sqrt(sum(x*x for x in R)); R=[x/ln for x in R]
    return [x/ws for x in T], R, [x/ws for x in S]
def world(entries, names):
    cache={}
    def w(i):
        if i in cache: return cache[i]
        T,R,S = blend_local(entries,i)
        p = rig.parent.get(i)
        if p is None: r=(tuple(T),tuple(R),tuple(S))
        else:
            pt,pr,ps=w(p); rt=mc.qrot(pr,(T[0]*ps[0],T[1]*ps[1],T[2]*ps[2]))
            r=((pt[0]+rt[0],pt[1]+rt[1],pt[2]+rt[2]), mc.qmul(pr,R), (ps[0]*S[0],ps[1]*S[1],ps[2]*S[2]))
        cache[i]=r; return r
    return {nm: w(rig.idx[nm])[0] for nm in names}
facts = {c['clip']: c for c in json.load(open(sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, '..', '..', 'Assets', '_Game', 'Resources', 'Anim', 'clip_facts.json')))['clips']}
def frac(x): return x-math.floor(x)
def sample_time(phase,length,plant,refPlant,rev):
    n = plant+refPlant-phase if rev else phase-refPlant+plant
    return frac(n)*length
RUN = [('Run',False,0),('StrafeL',False,60),('StrafeR',True,120),('Run',True,180),('StrafeL',True,240),('StrafeR',False,300)]
def weights(a):
    a%=360; k=int(a//60)%6; u=(a-k*60)/60; return [(k,1-u),((k+1)%6,u)]
def simulate(leftDeg, speed=4.5, hz=30, n=60, override=None):
    ents=[]
    for k,w in weights(leftDeg):
        if w<=0: continue
        c,rev,_=RUN[k]
        if override: c,rev = override(k,c,rev)
        found=False
        for e in ents:
            if e[0]==c:
                if e[1]!=rev and w>e[2]: e[1]=rev
                e[2]+=w; found=True
        if not found: ents.append([c,rev,w])
    stride=sum(e[2]*facts[e[0]]['groundSpeed']*facts[e[0]]['length'] for e in ents)
    length=sum(e[2]*facts[e[0]]['length'] for e in ents)
    refPlant=facts['Run']['leftPlant']; phase=0.0
    a=math.radians(leftDeg); vdir=(math.sin(a), math.cos(a)) # glTF: left=+X, fwd=+Z
    L=[];R=[];B=[]
    for t in range(n):
        phase=frac(phase+speed/hz/stride)
        e=[(c, sample_time(phase,facts[c]['length'],facts[c]['leftPlant'],refPlant,rev), w) for c,rev,w in ents]
        p=world(e,['toes.l','toes.r'])
        body=(vdir[0]*speed*t/hz, vdir[1]*speed*t/hz)
        L.append((p['toes.l'][0]+body[0],p['toes.l'][1],p['toes.l'][2]+body[1]))
        R.append((p['toes.r'][0]+body[0],p['toes.r'][1],p['toes.r'][2]+body[1]))
        B.append((body[0],0,body[1]))
    ft=bt=0;pairs=0
    for P in (L,R):
        mn=min(p[1] for p in P)
        for t in range(1,n):
            if P[t][1]>mn+0.03 or P[t-1][1]>mn+0.03: continue
            ft+=math.hypot(P[t][0]-P[t-1][0],P[t][2]-P[t-1][2]); bt+=math.hypot(B[t][0]-B[t-1][0],B[t][2]-B[t-1][2]); pairs+=1
    return ents, (ft/bt if bt else 0), pairs

refPlant=facts['Run']['leftPlant']
LEGYAW={'Run':0,'StrafeL':60.1,'StrafeR':-60.1}
def choose(a, side):
    a%=360
    if 60<a<120:
        if side not in (1,2): side = 1 if a<=90 else 2
        if side==1 and a>=90+15: side=2
        if side==2 and a<=90-15: side=1
        return [(side,1.0)], a-RUN[side][2], side
    if 240<a<300:
        if side not in (4,5): side = 4 if a<270 else 5  # as Locomotion.DirectionWeights: exactly 270 runs toes-first (StrafeR)
        if side==4 and a>=270+15: side=5
        if side==5 and a<=270-15: side=4
        return [(side,1.0)], a-RUN[side][2], side
    return weights(a), 0.0, -1
def rot(x,z,d):
    r=math.radians(d); return x*math.cos(r)+z*math.sin(r), z*math.cos(r)-x*math.sin(r)
def run(a, side=-1, speed=4.5, hz=30, n=60):
    ws,warp,side=choose(a,side)
    ents=[[RUN[k][0],RUN[k][1],w] for k,w in ws if w>0]
    stride=sum(e[2]*facts[e[0]]['groundSpeed']*facts[e[0]]['length'] for e in ents)
    phase=0.0; ar=math.radians(a); vdir=(math.sin(ar),math.cos(ar))
    L=[];R=[];B=[];cues=[]
    legyaw=sum(e[2]*LEGYAW[e[0]] for e in ents)+warp
    for t in range(n):
        phase=frac(phase+speed/hz/stride)
        e=[(c,sample_time(phase,facts[c]['length'],facts[c]['leftPlant'],refPlant,rev),w) for c,rev,w in ents]
        p=world(e,['toes.l','toes.r','foot.l','foot.r'])
        q={k:(rot(v[0],v[2],warp),v[1]) for k,v in p.items()}
        body=(vdir[0]*speed*t/hz, vdir[1]*speed*t/hz)
        L.append((q['toes.l'][0][0]+body[0],q['toes.l'][1],q['toes.l'][0][1]+body[1]))
        R.append((q['toes.r'][0][0]+body[0],q['toes.r'][1],q['toes.r'][0][1]+body[1]))
        B.append((body[0],0,body[1]))
        lo='l' if q['toes.l'][1]<=q['toes.r'][1] else 'r'
        d=(q['toes.'+lo][0][0]-q['foot.'+lo][0][0], q['toes.'+lo][0][1]-q['foot.'+lo][0][1])
        cue=math.degrees(math.atan2(d[0],d[1]))
        err=abs((cue-legyaw+180)%360-180); cues.append(err)
    ft=bt=0;pairs=0
    for P in (L,R):
        mn=min(p[1] for p in P)
        for t in range(1,n):
            if P[t][1]>mn+0.03 or P[t-1][1]>mn+0.03: continue
            ft+=math.hypot(P[t][0]-P[t-1][0],P[t][2]-P[t-1][2]); bt+=math.hypot(B[t][0]-B[t-1][0],B[t][2]-B[t-1][2]); pairs+=1
    return ft/bt, pairs, warp, ents, sum(c>30 for c in cues)/n, max(cues), legyaw

if __name__ == '__main__':
    print('old blend (integration batch 3):')
    for d in [0, 45, 90, 135, 180, -135, -90, -45]:
        ents, s, p = simulate(d)
        print(f"  {d:5d} deg slide {s:.3f} u/u over {p:2d} pairs  {[(c, r, round(w, 2)) for c, r, w in ents]}")
    print('new (one strafe + leg warp in the sideways sectors):')
    for a in [0, 30, 45, 60, 75, 90, 105, 120, 135, 180, 225, 255, 270, 285, 315]:
        s, p, w, e, f30, mx, ly = run(a)
        print(f"  {a:5d} deg slide {s:.3f} u/u over {p:2d} pairs, warp {w:+5.1f}, leg yaw {ly:+6.1f}, toe vs leg yaw >30: {f30:.2f}  {[(c, r) for c, r, _ in e]}")
