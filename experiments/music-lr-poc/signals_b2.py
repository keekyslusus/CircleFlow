import json, numpy as np, torch
from signals_lib import load16, wr, model, SR, get_speech_timestamps
def soft(x, atten_db=15, thr=0.8, fade_ms=20):
    ts=get_speech_timestamps(torch.from_numpy(x.astype(np.float32)),model,sampling_rate=SR,threshold=thr,min_silence_duration_ms=50,speech_pad_ms=0,min_speech_duration_ms=100)
    g=np.ones(len(x)); low=10**(-atten_db/20); f=int(SR*fade_ms/1000)
    for t in ts: g[t['start']:t['end']]=low
    k=np.ones(f)/f; g=np.convolve(g,k,mode='same')  # ~20 ms fades
    return x*g, sum(t['end']-t['start'] for t in ts)/len(x)
info={}
for m in json.load(open('mixes/meta.json')):
    st=load16(f"mixes/{m['file']}.wav"); mono=(st[:,0]+st[:,1])/2
    for cp in (4,8,12):
        v,fr=soft(mono[:cp*SR]); wr(f"sig/{m['file']}_vad2_cp{cp}.wav",v)
    info[m['file']]=round(fr,2)
json.dump(info,open('sig/info_vad2.json','w'),indent=1); print(info)
