"""Encode the approved image into Windows icon sizes; no drawing changes."""
from pathlib import Path
import json
from PIL import Image

R = Path(__file__).resolve().parent.parent
A = R / 'app/assets'
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
with Image.open(A / 'arc-head-source.png') as source:
    image = source.convert('RGBA')
    assert image.width == image.height
    assert image.getextrema()[3][0] == 0
    image.save(A / 'arcpet.ico', sizes=[(s, s) for s in sizes])
    image.resize((256, 256), Image.Resampling.LANCZOS).save(A / 'arc-head-preview.png')
with Image.open(A / 'arcpet.ico') as icon:
    assert icon.ico.sizes() == {(s, s) for s in sizes}
    for s in sizes:
        assert icon.ico.getimage((s, s)).getextrema()[3][0] == 0
print(json.dumps({'icon': 'app/assets/arcpet.ico', 'sizes': sizes, 'alpha': True}))
