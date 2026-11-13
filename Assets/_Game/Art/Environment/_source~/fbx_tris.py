import struct, zlib, sys, os, json
def parse(path):
    data=open(path,'rb').read(); ver=struct.unpack_from('<I',data,23)[0]; big=ver>=7500
    hdr='<QQQ' if big else '<III'; hs=struct.calcsize(hdr)
    def props(o,n):
        out=[]
        for _ in range(n):
            t=chr(data[o]); o+=1
            if t in 'YCIFDL':
                fmt={'Y':'<h','C':'<B','I':'<i','F':'<f','D':'<d','L':'<q'}[t]; out.append(struct.unpack_from(fmt,data,o)[0]); o+=struct.calcsize(fmt)
            elif t in 'fdlib':
                ln,enc,cl=struct.unpack_from('<III',data,o); o+=12; raw=data[o:o+cl]; o+=cl
                if enc: raw=zlib.decompress(raw)
                fmt={'f':'f','d':'d','l':'q','i':'i','b':'B'}[t]; out.append(struct.unpack('<%d%s'%(ln,fmt),raw))
            elif t in 'SR':
                ln=struct.unpack_from('<I',data,o)[0]; o+=4; out.append(data[o:o+ln]); o+=ln
        return out
    def node(o):
        end,np_,pl=struct.unpack_from(hdr,data,o); 
        if end==0: return None,o+hs+1
        nl=data[o+hs]; name=data[o+hs+1:o+hs+1+nl].decode(); po=o+hs+1+nl
        pr=props(po,np_); co=po+pl; kids=[]
        while co<end:
            k,co2=node(co)
            if k is None: co=co2; break
            kids.append(k); co=co2
        return (name,pr,kids),end
    o=27; top=[]
    while o<len(data)-hs:
        n,o2=node(o)
        if n is None: break
        top.append(n); o=o2
    return top
def tris(path):
    top=parse(path); objs=[n for n in top if n[0]=='Objects'][0][2]
    geo={}; models={}
    for n in objs:
        nm=n[1][1].split(b'\x00')[0].decode('utf8','replace')
        if n[0]=='Geometry':
            pvi=[k for k in n[2] if k[0]=='PolygonVertexIndex']
            if not pvi: continue
            t=0;c=0
            for i in pvi[0][1][0]:
                c+=1
                if i<0: t+=c-2; c=0
            geo[n[1][0]]=(nm,t)
        elif n[0]=='Model': models[n[1][0]]=nm
    conns=[n for n in top if n[0]=='Connections'][0][2]
    out=[]
    for c in conns:
        if c[1][1] in geo and c[1][2] in models: out.append((models[c[1][2]],geo[c[1][1]][1]))
    return out
if __name__=='__main__':
    res={}
    for p in sys.argv[1:]:
        res[os.path.basename(p)[:-4]]=tris(p)
    print(json.dumps(res))
