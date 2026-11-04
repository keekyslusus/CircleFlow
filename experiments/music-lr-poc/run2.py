import asyncio, json, time, uuid, random, csv, os, sys, wave, numpy as np, aiohttp, re
from shazamio_core import Recognizer, SearchParams
WINDOWS=[('cp4',0,4),('cp8',0,8),('cp12',0,12),('w4_16',4,16),('w6_18',6,18),('w8_20',8,20),('w12_24',12,24)]
UAS=["Dalvik/2.1.0 (Linux; U; Android 12; Pixel 3a Build/SP2A.220505.002)","Dalvik/2.1.0 (Linux; U; Android 13; Pixel 4 Build/TP1A.221005.002)"]
LOC=dict(altitude=10,latitude=40.7128,longitude=-74.0060,tz="America/New_York")
songs=[l.split('|') for l in open('songs/list.txt').read().strip().splitlines()]
rec=Recognizer(); os.makedirs('win',exist_ok=True)
OUT='raw_results.csv'
done=set()
if os.path.exists(OUT):
    for r in csv.DictReader(open(OUT)): done.add((r['file'],r['window']))
last=[0.0]
def norm(s): return re.sub(r'[^a-z0-9]','',s.lower().split('(')[0])
async def post(sess, sig):
    ts=int(time.time()*1000)
    url=(f"https://amp.shazam.com/discovery/v5/en/US/android/-/tag/{str(uuid.uuid4()).upper()}/{uuid.uuid4()}"
         "?sync=true&webv3=true&sampling=true&connected=&shazamapiversion=v3&sharehub=true&video=v3")
    body=dict(geolocation=dict(altitude=LOC['altitude'],latitude=LOC['latitude'],longitude=LOC['longitude']),
              signature=dict(samplems=sig.signature.samples,timestamp=ts,uri=sig.signature.uri),timestamp=ts,timezone=LOC['tz'])
    backoff=60
    while True:
        wait=10.0-(time.time()-last[0])
        if wait>0: await asyncio.sleep(wait)
        last[0]=time.time()
        async with sess.post(url,json=body,headers={'User-Agent':random.choice(UAS),'Content-Language':'en_US'},timeout=aiohttp.ClientTimeout(total=15)) as r:
            if r.status==429:
                ra=r.headers.get('Retry-After'); d=int(ra) if ra and ra.isdigit() else backoff
                print('429, sleeping',d,flush=True); await asyncio.sleep(d); backoff=min(backoff*2,600); continue
            r.raise_for_status(); return await r.json(content_type=None)
async def main():
    meta=json.load(open('mixes/meta.json'))
    OUT2='raw_results2.csv'; prev={}
    for r in csv.DictReader(open('raw_results.csv')):
        if r['window'].startswith('cp'): prev[(r['file'],'mono',r['window'])]=r
    done2=set()
    if os.path.exists(OUT2):
        for r in csv.DictReader(open(OUT2)): done2.add((r['file'],r['stream'],r['checkpoint']))
    new=not os.path.exists(OUT2)
    f=open(OUT2,'a',newline=''); w=csv.writer(f)
    if new: w.writerow(['file','song','level','stream','checkpoint','expected','matched_title','matched_artist','correct','num_matches','offset_s','reused'])
    async with aiohttp.ClientSession() as sess:
        for m in meta:
            exp_artist,exp_title,_=[x.strip() for x in songs[m['song']-1]]
            for stream in ('mono','side','vad'):
                for cp in ('cp4','cp8','cp12'):
                    if (m['file'],stream,cp) in done2:
                        if any(r for r in csv.DictReader(open(OUT2)) if r['file']==m['file'] and r['stream']==stream and r['checkpoint']==cp and r['correct']=='1'): break
                        continue
                    key=(m['file'],stream,cp)
                    if key in prev:
                        r=prev[key]; t,ar,ok,nm,off,reused=r['matched_title'],r['matched_artist'],int(r['correct']),r['num_matches'],r['offset_s'],1
                    else:
                        sig=await rec.recognize_path(f"sig/{m['file']}_{stream}_{cp}.wav",SearchParams(segment_duration_seconds=int(cp[2:])))
                        for attempt in range(3):
                            try: res=await post(sess,sig); break
                            except Exception as e: print('err',e,flush=True); await asyncio.sleep(10)
                        else: res={'error':True}
                        tr=res.get('track') or {}; t,ar=tr.get('title',''),tr.get('subtitle','')
                        ok=int(bool(t) and norm(exp_title)[:8] in norm(t)); nm=len(res.get('matches') or []); off=(res.get('matches') or [{}])[0].get('offset',''); reused=0
                    w.writerow([m['file'],m['song'],m['level'],stream,cp,f"{exp_artist} - {exp_title}",t,ar,ok,nm,off,reused]); f.flush()
                    print(m['file'],stream,cp,'->',t,'|',ar,'ok' if ok else ('WRONG' if t else '-'),flush=True)
                    if t: break   # stop at first match (any track), like CircleFlow
asyncio.run(main())
