from pathlib import Path
from pdf2image import convert_from_path
from pypdf import PdfReader
from PIL import Image,ImageDraw
root=Path('tmp/patent_direction3');r=PdfReader(root/'final_render.pdf');print('PAGES',len(r.pages))
for i,p in enumerate(r.pages,1):
 t=p.extract_text();print(i,len(t),t[:35].replace('\n',' '),t[-35:].replace('\n',' '))
pages=convert_from_path(root/'final_render.pdf',dpi=110);f=root/'final_pages';f.mkdir(exist_ok=True)
for i,p in enumerate(pages,1):p.save(f/f'page-{i}.png')
