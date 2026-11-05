"""Mixamo 캐릭터 FBX 안에 묻힌 텍스처를 꺼낸다 (Unity가 자동으로 못 찾음).
사용: python extract_textures.py Assets/Resources/Mixamo/Player.fbx Assets/Resources/Mixamo/Tex
Diffuse/Normal을 꺼내고, Hair는 Opacity를 알파로 합친 *_DiffuseA.png를 만든다 (Pillow 필요)."""
import os, re, sys
src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
data = open(src, 'rb').read()
name_re = re.compile(rb'([A-Za-z0-9]+_[A-Za-z]+_[A-Za-z]+\.png)')
saved = {}
for m in re.finditer(rb'\x89PNG\r\n\x1a\n', data):
    s = m.start(); e = data.find(b'IEND', s)
    if e < 0: continue
    prev = list(name_re.finditer(data, max(0, s - 3000), s))
    if not prev: continue
    nm = prev[-1].group(1).decode()
    if any(k in nm for k in ('Diffuse', 'Normal', 'Opacity')):
        saved[nm] = data[s:e + 8]
tmp = {}
for nm, blob in saved.items():
    if 'Opacity' in nm: tmp[nm] = blob; continue
    open(os.path.join(out, nm), 'wb').write(blob)
try:
    from PIL import Image
    import io
    for nm, blob in tmp.items():
        if 'Hair' not in nm: continue
        d = Image.open(os.path.join(out, nm.replace('Opacity', 'Diffuse'))).convert('RGB')
        a = Image.open(io.BytesIO(blob)).convert('L').resize(d.size)
        d.putalpha(a); d.save(os.path.join(out, nm.replace('Opacity', 'DiffuseA')))
except ImportError:
    print('Pillow가 없어 머리카락 알파는 건너뜀')
print('textures:', sorted(os.listdir(out)))
