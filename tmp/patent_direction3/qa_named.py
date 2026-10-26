from pathlib import Path
from pdf2image import convert_from_path
from pypdf import PdfReader
from PIL import Image,ImageChops
root=Path('C:/Users/23548/Desktop/SpineRecovery_V2/tmp/patent_direction3')
pages=convert_from_path(root/'named_render.pdf',dpi=110)
out=root/'named_pages';out.mkdir(exist_ok=True)
changed=[]
for i,p in enumerate(pages,1):
    p.save(out/f'page-{i}.png')
    previous=Image.open(root/f'final_pages/page-{i}.png')
    if p.size!=previous.size or ImageChops.difference(p.convert('RGB'),previous.convert('RGB')).getbbox():
        changed.append(i)
print('Pages:',len(pages),'Visually changed pages:',changed)
