# Island change over a match from Critic.Census dumps: cells changed vs m0, by type transition and by zone; heatmap summary.
import sys, os, collections
O = '/Users/wilsonguenther/cm-incidents/critic-audit-2026-11-21/data/island/'
W, H = 112, 72
names = {0:'void',1:'floor',2:'wall',3:'brk',4:'cracked',5:'broken',6:'water',7:'bridge',8:'barricade',9:'lowwall'}
for seed in ['4242','7','31337']:
    p0 = O + f's{seed}-m0.cells'
    if not os.path.exists(p0): continue
    c0 = open(p0,'rb').read(); walk0 = sum(1 for v in c0 if v in (1,7))
    line = [f'seed {seed} walkable at start {walk0}']
    for t in ['m2','m4','m6','end']:
        p = O + f's{seed}-{t}.cells'
        if not os.path.exists(p): continue
        c = open(p,'rb').read(); ch = [(i,c0[i],c[i]) for i in range(len(c)) if c0[i]!=c[i]]
        tr = collections.Counter((names[a],names[b]) for _,a,b in ch)
        hill = sum(1 for i,a,b in ch if ((i%W+0.5-56)**2+(i//W+0.5-40)**2) < 15**2)
        line.append(f'  {t}: changed {len(ch)} ({100*len(ch)/walk0:.2f}% of walkable), within 15u of hill {hill}; ' + ', '.join(f'{a}->{b} {n}' for (a,b),n in tr.most_common(6)))
    hp = O + f's{seed}.heat'
    if os.path.exists(hp):
        hdr, data = open(hp).read().split('\n',1); v = [int(x) for x in data.split(',')]
        tot = sum(v); walk = [i for i in range(len(c0)) if c0[i] in (1,7)]
        never = sum(1 for i in walk if v[i]==0)
        srt = sorted((v[i] for i in walk), reverse=True); top10 = sum(srt[:len(srt)//10])
        line.append(f'  heat: samples {tot}; walkable cells never stood on {never} of {len(walk)} ({100*never/len(walk):.1f}%); busiest 10% of cells hold {100*top10/max(1,tot):.1f}% of presence')
    bp = O + f's{seed}.blasts'
    if os.path.exists(bp):
        b = [l.split(',') for l in open(bp).read().split('\n') if l]
        line.append('  blasts: ' + ', '.join(f'{k} {n}' for k,n in collections.Counter(x[1] for x in b).items()))
    print('\n'.join(line))
