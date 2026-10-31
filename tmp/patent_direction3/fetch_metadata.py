from urllib.request import urlopen,Request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import json
ids=['10.1089/g4h.2012.0073','10.3390/app11135935','10.2196/40054','10.1186/s12984-024-01373-2','10.1109/SeGAH49190.2020.9201871','10.1145/3772318.3790457']
def fetch(doi):
 try:
  m=json.load(urlopen(Request('https://api.crossref.org/works/'+doi,headers={'User-Agent':'PatentDraftResearch/1.0'}),timeout=30))['message']
  return {k:m.get(k) for k in ['DOI','title','author','container-title','published','volume','issue','page','article-number','type','link']}
 except Exception as e:return {'doi':doi,'error':str(e)}
a=list(ThreadPoolExecutor(6).map(fetch,ids));Path('tmp/patent_direction3/references_metadata.json').write_text(json.dumps(a,ensure_ascii=False,indent=2),encoding='utf8')
for m in a:print(json.dumps(m,ensure_ascii=False))
