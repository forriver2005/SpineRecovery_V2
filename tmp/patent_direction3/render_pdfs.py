from pdf2image import convert_from_path
from pathlib import Path
from PIL import Image,ImageDraw
base=Path('C:/Users/23548/Documents/xwechat_files/wxid_dv2i6u8f201q12_fa7a/msg/file/2026-10'); out=Path('tmp/patent_direction3')
for key,name,start in [('prior1','01_CN118448003A_处方分解与训练.pdf',12),('prior2','02_CN109920517A_游戏化康复系统及其工作方法_原始公布文本.pdf',9)]:
 pages=convert_from_path(base/name,dpi=100,first_page=start)
 for i,p in enumerate(pages,start):p.save(out/f'{key}-page{i}.png')
