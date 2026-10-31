from pathlib import Path
from zipfile import ZipFile
from lxml import etree
from docx import Document
import sys,json
sys.stdout.reconfigure(encoding='utf8')
root=Path(__file__).resolve().parent
p=root.parents[2]/'output/patent_direction3/基于多属性问答的康复处方游戏映射_专利申请文件_v2.docx'
with ZipFile(p) as z:
 r=etree.fromstring(z.read('word/document.xml'))
 ns={'m':'http://schemas.openxmlformats.org/officeDocument/2006/math','w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
 t=''.join(r.xpath('//w:t/text()',namespaces=ns))
 assert 'CN118448003A' not in t and 'CN109920517A' not in t
 assert '@EQ' not in t and '@@' not in t
 ps=r.xpath('//w:p[w:r/w:t[contains(.,"（15）")]]',namespaces=ns)
 eq15=ps[0].xpath('.//m:t/text()',namespaces=ns)
 assert 'B' in eq15 and 'u' in eq15,eq15
 assert len(Document(p).sections)==4
 print('Removals, formula content, markers and sections verified.')
 print('Equation 15:',eq15)
 print('DOI links:',len([e for e in etree.fromstring(z.read('word/_rels/document.xml.rels')) if e.get('Target','').startswith('https://doi.org/')]))
