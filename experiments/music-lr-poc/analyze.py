import csv, json, collections
rows=list(csv.DictReader(open('raw_results2.csv')))
info=json.load(open('sig/info.json'))
first={}; nreq=collections.Counter(); seen=set()
for r in rows:
    k=(r['file'],r['stream'])
    if (k,r['checkpoint']) in seen: continue
    seen.add((k,r['checkpoint'])); nreq[k]+=1
    if r['matched_title'] and k not in first: first[k]=(int(r['checkpoint'][2:])//4, r['correct']=='1')
files=sorted({r['file'] for r in rows})
INF=99
def fm(f,s):
    v=first.get((f,s)); return (v[0],v[1]) if v else (INF,False)
def complete(f,s): return (f,s) in first or nreq[(f,s)]==3
out=[]; levels=['music_only','m-10','m-15','m-20','m-25']
for f in files:
    song,lvl=f.split('_',1)
    if not all(complete(f,s) for s in ('mono','side','vad')): continue
    (km,cm),(ks,cs),(kv,cv)=fm(f,'mono'),fm(f,'side'),fm(f,'vad')
    def combo(ka,ca,kb,cb):
        j=min(ka,kb)
        if j==INF: return 0,0,6
        ok = ca if ka==j else cb
        wrong = int(not ok)
        return int(ok),wrong,2*(j-1)+(1 if ka==j else 2)
    A=combo(km,cm,ks,cs); AB=combo(km,cm,kv,cv)
    out.append(dict(song=song,level=lvl,side_to_mid_db=round(info[f]['side_to_mid_db'],1),
        base_match=int(cm),base_wrong=int(km<INF and not cm),base_cp=(km*4 if km<INF else ''),base_req=nreq[(f,'mono')],
        side_match=int(cs),side_wrong=int(ks<INF and not cs),side_cp=(ks*4 if ks<INF else ''),side_req=nreq[(f,'side')],
        A_match=A[0],A_wrong=A[1],A_req=A[2],
        vad_match=int(cv),vad_wrong=int(kv<INF and not cv),vad_cp=(kv*4 if kv<INF else ''),vad_req=nreq[(f,'vad')],
        vad_speech_frac=round(info[f]['vad_speech_frac_cp12'],2),
        monoORvad_match=AB[0],monoORvad_req=AB[2]))
w=csv.DictWriter(open('results.csv','w',newline=''),fieldnames=list(out[0].keys())); w.writeheader(); w.writerows(out)
print(f"{'level':11} n  base   A(mono|L-R)  L-R only  B(VAD)  mono|VAD   | req: base A  L-R  VAD")
for L in levels:
    g=[o for o in out if o['level']==L]
    if not g: continue
    s=lambda k:sum(o[k] for o in g)
    print(f"{L:11} {len(g)}  {s('base_match')}/{len(g)}   {s('A_match')}/{len(g)}         {s('side_match')}/{len(g)}       {s('vad_match')}/{len(g)}     {s('monoORvad_match')}/{len(g)}       | {s('base_req')} {s('A_req')} {s('side_req')} {s('vad_req')}   wrong(b/s/v)={s('base_wrong')}/{s('side_wrong')}/{s('vad_wrong')}")
print('total requests actually sent:', sum(1 for r in rows if r['reused']=='0'))
