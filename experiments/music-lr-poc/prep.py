import subprocess, numpy as np, os, json
SR=44100; DUR=24
def load(path, ss, filt=None, dur=DUR+40):
    cmd=['ffmpeg','-v','error','-ss',str(ss),'-t',str(dur),'-i',path]
    if filt: cmd+=['-af',filt]
    cmd+=['-ac','2','-ar',str(SR),'-f','f32le','-']
    a=np.frombuffer(subprocess.run(cmd,capture_output=True,check=True).stdout,dtype=np.float32).reshape(-1,2)
    return a
def rms(x): return float(np.sqrt(np.mean(x.astype(np.float64)**2)))
os.makedirs('mixes',exist_ok=True); os.makedirs('win',exist_ok=True)
LEVELS=[None,-10,-15,-20,-25]  # None = music only control
meta=[]
for i in range(1,9):
    m=load(f'songs/s{i}.m4a',2,dur=DUR)[:SR*DUR]
    # speech: from 90 s, cap pauses ~0.3 s (vlogger-like continuous talk)
    s=load(f'speech/raw{i}.mp3',90,'silenceremove=stop_periods=-1:stop_duration=0.35:stop_threshold=-38dB:stop_silence=0.25')[:SR*DUR]
    assert len(m)==SR*DUR and len(s)==SR*DUR, (i,len(m),len(s))
    s=s*(10**(-20/20)/rms(s))           # speech RMS = -20 dBFS
    m_norm=m*(10**(-20/20)/rms(m))      # music at same RMS as speech (0 dB rel)
    for L in LEVELS:
        if L is None: x=m_norm.copy(); tag='music_only'
        else: x=s+m_norm*10**(L/20); tag=f'm{L}'
        pk=np.abs(x).max()
        if pk>0.99: x=x*0.99/pk
        name=f's{i}_{tag}'
        subprocess.run(['ffmpeg','-v','error','-y','-f','f32le','-ar',str(SR),'-ac','2','-i','-',f'mixes/{name}.wav'],input=x.astype(np.float32).tobytes(),check=True)
        # CircleFlow preprocessing: mono (L+R)/2, resample 16 kHz
        subprocess.run(['ffmpeg','-v','error','-y','-i',f'mixes/{name}.wav','-af','pan=mono|c0=0.5*c0+0.5*c1','-ar','16000','-c:a','pcm_s16le',f'mixes/{name}_16k.wav'],check=True)
        meta.append(dict(song=i,level=tag,file=name))
json.dump(meta,open('mixes/meta.json','w'))
print(len(meta))
