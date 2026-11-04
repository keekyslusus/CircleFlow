import subprocess, numpy as np, json, wave, os
from silero_vad import load_silero_vad, get_speech_timestamps
import torch
model=load_silero_vad(onnx=True)
SR=16000
def load16(path):
    b=subprocess.run(['ffmpeg','-v','error','-i',path,'-ar','16000','-ac','2','-f','f32le','-'],capture_output=True,check=True).stdout
    return np.frombuffer(b,dtype=np.float32).reshape(-1,2).astype(np.float64)
def rms(x): return float(np.sqrt(np.mean(x**2))+1e-12)
def vad_mute(x, atten_db=35, fade_ms=20):
    ts=get_speech_timestamps(torch.from_numpy(x.astype(np.float32)),model,sampling_rate=SR,threshold=0.5,min_silence_duration_ms=100,speech_pad_ms=30)
    g=np.ones(len(x)); low=10**(-atten_db/20); f=int(SR*fade_ms/1000)
    for t in ts:
        a,b=t['start'],t['end']; g[a:b]=low
        ra=np.linspace(1,low,f); g[max(0,a-f):a]=np.minimum(g[max(0,a-f):a],ra[-(a-max(0,a-f)):] if a>0 else 1)
        rb=np.linspace(low,1,f); e=min(len(x),b+f); g[b:e]=np.minimum(g[b:e],rb[:e-b])
    return x*g, sum(t['end']-t['start'] for t in ts)/len(x)
def wr(p,x):
    x=np.clip(x,-1,1)
    with wave.open(p,'wb') as o: o.setnchannels(1);o.setsampwidth(2);o.setframerate(SR);o.writeframes((x*32767).astype(np.int16).tobytes())
