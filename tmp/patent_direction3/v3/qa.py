from pathlib import Path
from pdf2image import convert_from_path
from pypdf import PdfReader
from PIL import Image,ImageDraw
import sys,json
sys.stdout.reconfigure(encoding='utf-8')
root=Path(__file__).resolve().parent
stem=sys.argv[1] if len(sys.argv)>1 else 'render1'
r=PdfReader(root/(stem+'.pdf'));print('PAGES',len(r.pages))
for i,p in enumerate(r.pages,1):
 t=p.extract_text();print(i,len(t),t[:35].replace('\n',' '),t[-30:].replace('\n',' '))
pages=convert_from_path(root/(stem+'.pdf'),dpi=110)
f=root/(stem+'_pages');f.mkdir(exist_ok=True)
for i,p in enumerate(pages,1):p.save(f/f'page-{i}.png')
for b in range(0,len(pages),9):
 c=Image.new('RGB',(750,1110),'#dddddd');d=ImageDraw.Draw(c)
 for j,p in enumerate(pages[b:b+9]):
  p=p.copy();p.thumbnail((240,340));x=(j%3)*250;y=(j//3)*370;c.paste(p,(x,y+20));d.text((x+8,y+3),str(b+j+1),fill='black')
 c.save(f/f'contact-{b//9+1}.png')
