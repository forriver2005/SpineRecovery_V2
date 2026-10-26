from pdf2image import convert_from_path
from pathlib import Path
from PIL import Image,ImageDraw
out=Path('tmp/patent_direction3')
for ref in [1,2]:
 pages=convert_from_path(out/f'reference{ref}.pdf',dpi=100)
 print(ref,len(pages))
 folder=out/f'ref{ref}_pages';folder.mkdir(exist_ok=True)
 for i,p in enumerate(pages,1): p.save(folder/f'page-{i}.png')
 for start in range(0,len(pages),6):
  sheet=Image.new('RGB',(1050,1600),'#dddddd');dr=ImageDraw.Draw(sheet)
  for j,p in enumerate(pages[start:start+6]):
   p.thumbnail((500,480));x=(j%2)*525;y=(j//2)*530;sheet.paste(p,(x,y+25));dr.text((x+5,y+5),f'Ref {ref} page {start+j+1}',fill='black')
  sheet.save(out/f'ref{ref}-sheet{start//6+1}.png')
