from pathlib import Path
from zipfile import ZipFile
from lxml import etree
from pypdf import PdfReader
base=Path('C:/Users/23548/Documents/xwechat_files/wxid_dv2i6u8f201q12_fa7a/msg/file/2026-10')
out=Path('C:/Users/23548/Desktop/SpineRecovery_V2/tmp/patent_direction3')
files=[('direction1','增强现实镜像康复训练指导系统及方法_专利申请文件_v2(2).docx'),('direction2','一种基于动作偏差定位与限时振动反馈的肌骨康复训练引导方法及系统.docx'),('prior1','01_CN118448003A_处方分解与训练.pdf'),('prior2','02_CN109920517A_游戏化康复系统及其工作方法_原始公布文本.pdf')]
ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
for key,name in files:
 p=base/name
 if p.suffix=='.docx':
  with ZipFile(p) as z:
   root=etree.fromstring(z.read('word/document.xml'))
   lines=[''.join(x.itertext()) for x in []]
   lines=[''.join(el.xpath('.//w:t/text()',namespaces=ns)) for el in root.xpath('//w:p',namespaces=ns)]
   for member in z.namelist():
    if member.startswith(('word/header','word/footer','word/footnotes','word/endnotes')) and member.endswith('.xml'):
     r=etree.fromstring(z.read(member)); lines+=['PART '+member]+r.xpath('//w:t/text()',namespaces=ns)
  text='\n'.join(lines)
 else:
  d=PdfReader(p);text='\n\n'.join(f'===== PAGE {i+1} =====\n'+page.extract_text() for i,page in enumerate(d.pages))

 (out/f'{key}.txt').write_text(text,encoding='utf-8')
 print(key,len(text),str(out/f'{key}.txt'))

