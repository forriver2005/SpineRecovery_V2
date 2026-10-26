from pathlib import Path
from zipfile import ZipFile
import shutil
from lxml import etree

root=Path('C:/Users/23548/Desktop/SpineRecovery_V2')
p=root/'output/patent_direction3/处方游戏动作映射算法_专利申请文件_含文献引用_v1.docx'
backup=root/'tmp/patent_direction3/before_title_change.docx'
shutil.copy2(p,backup)
old='基于动作抽象的康复处方游戏映射'
new='基于动作分类与剂量约束的康复处方游戏映射'
counts={}
with ZipFile(backup) as zin, ZipFile(p,'w') as zout:
    for info in zin.infolist():
        data=zin.read(info.filename)
        if info.filename in ('word/document.xml','docProps/core.xml'):
            s=data.decode('utf-8')
            counts[info.filename]=s.count(old)
            s=s.replace(old,new)
            data=s.encode('utf-8')
            etree.fromstring(data)
        zout.writestr(info,data)
assert counts['word/document.xml']>=2,counts
with ZipFile(p) as z:
    doc=etree.fromstring(z.read('word/document.xml'))
    texts=doc.xpath('//w:t/text()',namespaces={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'})
    assert not any(old in t for t in texts)
    print('Updated occurrences:',counts)
    print('New title: 一种'+new+'方法及系统')
    print('Bytes:',p.stat().st_size)
