from pathlib import Path
import sys,json
from lxml import etree
R=Path(__file__).resolve().parent
# Reuse the retained template's authoring functions, preserving its styles and sections.
source=(R/'build_doc.py').read_text(encoding='utf8')
exec(source[:source.index('g=Diagram(1200,1460)')])
ROOT=R/'v3'
sys.path.insert(0,str(R/'v2_math_libs'))
from latex2mathml.converter import convert

g=Diagram(1500,1640)
labels=['S1  读取整份医生处方\n项目  动作  剂量  执行关系','S2  对每个动作回答属性问题\n形成答案向量并校验一致性','S3  按答案分类并生成原语图\n导出通道与接口需求','S4  匹配游戏模板  写入项目剂量\n按处方顺序与休息编排任务图','S5  运动数据驱动游戏\n有效事件更新项目剂量并推进处方']
for i,t in enumerate(labels):
 y=40+i*315;g.box((70,y,1430,y+215),t,48)
 if i<4:g.arrow([(750,y+215),(750,y+315)])
g.save(1)

g=Diagram(1700,1860)
g.box((50,20,1650,190),'动作描述与本项目剂量\n读取已登记字段  或按明确选项完成登记',46)
g.arrow([(850,190),(850,220),(430,220),(430,260)])
g.arrow([(850,220),(1270,220),(1270,260)])
g.box((50,260,810,850),'Q1  一个目标／多个目标\nQ2  保持／往复／到达／混合\nQ3  单关节／复合／整体特征\nQ4  零维／一维／多维\nQ5  独立／同步／交替／组合',43)
g.box((890,260,1650,850),'Q6  返回要求\nQ7  保持与计时方式\nQ8  多目标组织关系\nQ9  支撑体位是否变化\n各选项附参数与判定条件',43)
g.arrow([(430,850),(430,940),(850,940),(850,1010)])
g.arrow([(1270,850),(1270,940),(850,940)])
g.box((180,1010,1520,1200),'答案向量与一致性校验\n每题一个适用答案  缺失或冲突返回补充',47)
g.arrow([(850,1200),(850,1280)])
g.box((180,1280,1520,1480),'主类别 ＋ 附加属性 ＋ 原语结构\n保留保持  返回  协同  稳定与体位要求',47)
g.arrow([(850,1480),(850,1560)])
g.box((180,1560,1520,1790),'游戏接口需求\n控制槽位  节点  联合计时  配对与轮换\n监测能力与处方项目参数',46)
g.save(2)

g=Diagram(1600,1940)
g.box((70,20,1530,165),'输入：通过一致性校验的全部问题答案',46)
rules=[('异构阶段／混合结构\n或有序体位转换','顺序组合类'),('对侧组合交替','对侧交替类'),('双侧同步','双侧同步类'),('保持型且零维位移控制','姿态保持类'),('多维连续控制','多通道轨迹类'),('一维且必须返回','单通道往返类'),('一维且不要求返回','单通道到达类')]
for i,(question,cat) in enumerate(rules):
 y=230+i*205
 g.box((70,y,880,y+135),question,40)
 g.box((1060,y,1530,y+135),cat,42)
 g.arrow([(880,y+68),(1060,y+68)],'是',(970,y+32))
 if i==0:g.arrow([(475,165),(475,230)])
 if i<6:g.arrow([(475,y+135),(475,y+205)],'否',(520,y+169))
g.arrow([(475,1595),(475,1670)],'否',(520,1630))
g.box((70,1670,880,1795),'其余有效结构 → 组合扩展类',40)
g.text(1240,1710,'各组合类递归处理\n有限子动作',38)
g.box((70,1850,1530,1930),'全部类别均保留附加属性；分类成功后仍须检查模板兼容性',37)
g.save(3)

g=Diagram(1700,1760)
g.box((80,20,1620,210),'答案生成主体结构\n往复：到达 → 保持 → 返回\n到达：到达 → 保持 → 退出   保持：保持 → 退出',44)
g.arrow([(850,210),(850,300)])
g.box((80,300,1620,485),'对侧交替答案 → 两个组合 ＋ 交替调度\n每次只激活一个组合\n组合内部联合判定  不分别累计后合并',42)
g.arrow([(850,485),(850,555),(440,555),(440,625)])
g.arrow([(850,555),(1260,555),(1260,625)])
g.box((80,625,800,825),'组合一\n右上肢 ＋ 左下肢\n各自目标与约束分别检查',44)
g.box((900,625,1620,825),'组合二\n左上肢 ＋ 右下肢\n各自目标与约束分别检查',44)
g.arrow([(440,825),(440,925)]);g.arrow([(1260,825),(1260,925)])
g.box((80,925,800,1125),'全部条件同时满足\n共同保持计时\n中断则本次连续计时清零',43)
g.box((900,925,1620,1125),'全部条件同时满足\n共同保持计时\n中断则本次连续计时清零',43)
g.arrow([(440,1125),(440,1225)]);g.arrow([(1260,1125),(1260,1225)])
g.box((80,1225,800,1385),'全部返回并确认\n提交组合一有效事件',45)
g.box((900,1225,1620,1385),'全部返回并确认\n提交组合二有效事件',45)
g.arrow([(440,1385),(440,1480),(850,1480),(850,1570)])
g.arrow([(1260,1385),(1260,1480),(850,1480)])
g.box((80,1570,1620,1735),'按处方交替与有限重复\n分别记录组合次数  组间休息  达到剂量后结束',46)
g.save(4)

g=Diagram(1740,1730)
cols=[(30,560,'鸟狗式','多个镜像目标\n往复  复合关节\n多维  对侧交替\n返回  连续保持\n四点支撑'),(605,1135,'死虫式','多个镜像目标\n往复  复合关节\n多维  对侧交替\n返回  连续保持\n仰卧'),(1180,1710,'坐位伸膝','单目标\n往复  单关节\n一维  单侧\n返回  连续保持\n坐位')]
for x0,x1,name,answers in cols:
 g.box((x0,25,x1,150),name,49)
 g.arrow([((x0+x1)/2,150),((x0+x1)/2,230)])
 g.box((x0,230,x1,700),answers,41)
 g.arrow([((x0+x1)/2,700),((x0+x1)/2,800)])
g.box((30,800,1135,1010),'相同主类别：对侧交替类\n相同原语拓扑  各自绑定特征与剂量',43)
g.box((1180,800,1710,1010),'主类别\n单通道往返类',45)
g.arrow([(580,1010),(580,1110)]);g.arrow([(1445,1010),(1445,1110)])
g.box((30,1110,1135,1350),'共同模板族：交替双通道协作\n需分别支持体位  传感及稳定条件\n条件不符则不复用该模板',43)
g.box((1180,1110,1710,1350),'模板族\n升降控制\n或单轨运输',44)
g.box((30,1470,1710,1680),'关节参与数量不等于控制维度\n复合关节也可由一个参数驱动，但必须保留并验证耦合约束\n两个独立肢体要求不能通过取平均值强行压缩成一维',43)
g.save(5)

g=Diagram(1700,1830)
g.box((70,20,1630,210),'整份处方 P\n项目 I1 鸟狗式 → 项目 I2 死虫式 → 项目 I3 伸膝\n每项保留自己的动作  剂量  侧别  休息与执行顺序',44)
g.arrow([(850,210),(850,300)])
g.box((70,300,1630,520),'各项目分别问答分类并产生候选模板\nI1：TA／TB   I2：TA／TB   I3：TC\n兼容条件是硬要求  冗余与场景切换用于优选',44)
g.arrow([(850,520),(850,610)])
g.box((70,610,1630,800),'联合选择示例：TA → TA → TC\n保持处方项目顺序  不通过重排动作降低切换成本',47)
g.arrow([(850,800),(850,890)])
g.box((70,890,1630,1090),'组装处方游戏任务图\nI1 子图 → 休息及体位确认 → I2 子图\n→ 休息及体位确认 → I3 子图 → 处方结束',44)
g.arrow([(850,1090),(850,1180)])
g.box((70,1180,1630,1390),'运行与项目级记录\nI1 剂量记录   I2 剂量记录   I3 剂量记录\n同模板分别绑定  各项有效完成量不得互相抵扣',45)
g.box((70,1510,1630,1770),'循环处方的另一编排示例\n医生规定：A、B 各一组，循环两轮\n展开为 A1 → B1 → A2 → B2\n轮次  项目  组  侧别共同标识剂量记录',43)
g.save(6)

# Retain the former runtime figure, relabeled as figure 7.
import shutil
shutil.copy2(R/'fig3.png',ROOT/'fig7.png')

g=Diagram(1600,1820)
labs=['新动作登记\n体位  特征  阶段  约束与当前项目剂量','逐项回答属性问题\n明确答案域  适用条件  来源与规则版本','一致性校验通过\n主类别 ＋ 附加属性 ＋ 原语图与接口需求','检查已有模板及当前测量能力\n全部必需条件可执行才允许绑定','绑定项目剂量并加入处方任务图\n实时运行  分项目提交有效事件']
for i,t in enumerate(labs):
 y=30+i*350;g.box((70,y,1150,y+230),t,43)
 if i<4:g.arrow([(610,y+230),(610,y+350)])
g.box((1220,370,1550,680),'缺失或冲突\n返回修正\n不猜测答案',39)
g.arrow([(1150,485),(1220,485)])
g.box((1220,1050,1550,1370),'无兼容模板\n输出缺失项\n扩展后复检',39)
g.arrow([(1150,1195),(1220,1195)])
g.text(800,1770,'分类有答案，不等于当前设备或游戏必然能执行',39)
g.save(8)

equations={e['key']:e for e in json.loads((ROOT/'equations.json').read_text(encoding='utf8'))}
transform=etree.XSLT(etree.parse('C:/Program Files/Microsoft Office/root/Office16/MML2OMML.XSL'))
math_ns='http://schemas.openxmlformats.org/officeDocument/2006/math'

def math_element(latex,display='inline'):
 # Converter supports array rather than amsmath aligned; preserve the source LaTeX.
 if r'\begin{aligned}' in latex:
  latex=latex.replace(r'\begin{aligned}',r'\begin{array}{l}').replace(r'\end{aligned}',r'\end{array}').replace('&','')
 latex=latex.replace(r'\operatorname*',r'\operatorname')
 latex=latex.replace(r'\|B\mathbf u(t)\|',r'\left\lVert B\mathbf u(t)\right\rVert')
 mml=convert(latex,display=display)
 math=transform(etree.fromstring(mml.encode())).getroot()
 # Preserve upright operator names even when the converter coalesces adjacent runs.
 op_re=re.compile(r'\b(min|max|argmin|Repeat|Seq|diag|clip)\b')
 for run in list(math.findall('.//{'+math_ns+'}r')):
  tx=run.find('{'+math_ns+'}t')
  if tx is None or not op_re.search(tx.text or ''):continue
  parent=run.getparent();idx=parent.index(run)
  for token in re.split(op_re,tx.text):
   if not token:continue
   nr=deepcopy(run);nr.find('{'+math_ns+'}t').text=token
   if op_re.fullmatch(token):
    rp=nr.find('{'+math_ns+'}rPr')
    if rp is None:rp=OxmlElement('m:rPr');nr.insert(0,rp)
    for old in rp.findall('{'+math_ns+'}sty'):rp.remove(old)
    sty=OxmlElement('m:sty');sty.set(qn('m:val'),'p');rp.append(sty)
   parent.insert(idx,nr);idx+=1
  parent.remove(run)
 # Native Word math, preserving editable fractions, limits, cases and matrices.
 for run in math.findall('.//{'+math_ns+'}r'):
  wr=OxmlElement('w:rPr');fonts=OxmlElement('w:rFonts')
  fonts.set(qn('w:ascii'),'Cambria Math');fonts.set(qn('w:hAnsi'),'Cambria Math');wr.append(fonts)
  size=OxmlElement('w:sz');size.set(qn('w:val'),'24');wr.append(size);run.insert(0,wr)
  tx=run.find('{'+math_ns+'}t')
  if tx is not None and (tx.text or '') in ['min','max','argmin','arg min','Repeat','Seq','diag','clip']:
   mrp=run.find('{'+math_ns+'}rPr')
   if mrp is None:mrp=OxmlElement('m:rPr');run.insert(0,mrp)
   sty=OxmlElement('m:sty');sty.set(qn('m:val'),'p');mrp.append(sty)
 return math

def equation(key):
 e=equations[key]
 math=math_element(e['latex'],'block')
 p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER
 p.paragraph_format.first_line_indent=Pt(0);p.paragraph_format.line_spacing=1.15
 p.paragraph_format.space_before=Pt(6);p.paragraph_format.space_after=Pt(6)
 p.paragraph_format.keep_together=True
 p._p.append(math)
 p.add_run('  （'+str(e['number'])+'）').font.size=Pt(11)

inline_map={
 '𝒳_val':r'\mathcal X_{\mathrm{val}}','q_j(A_i,D_i)':r'q_j(A_i,D_i)','A_i':r'A_i','D_i':r'D_i',
 '𝒜_val':r'\mathcal A_{\mathrm{val}}','Ω_j':r'\Omega_j','φ_jv':r'\phi_{jv}',
 'a_ij':r'a_{ij}','b_ℓ':r'b_\ell','ρ_k':r'\rho_k','c_i':r'c_i','𝓛_i':r'\mathcal L_i','λ_j':r'\lambda_j',
 'R_j(a_ij)':r'R_j(a_{ij})','R_i^A':r'R_i^A','R_i^D':r'R_i^D',
 '𝒢_i':r'\mathcal G_i','V_i':r'V_i','E_i':r'E_i','γ_i':r'\gamma_i','𝓑_i':r'\mathcal B_i',
 'N_i':r'N_i','G_i':r'G_i','E_P':r'E_P','R_P':r'R_P','Dᵢ':r'D_i',
 'B_igab':r'B_{igab}','X_igab':r'X_{igab}','K_ig':r'K_{ig}','x_ig':r'x_{ig}',
 'c_ig':r'c_{ig}','s_gh':r's_{gh}','F_i(g)':r'F_i(g)','F_1(g)':r'F_1(g)','c_1g':r'c_{1g}',
 '𝒯_P':r'\mathcal T_P','T_A':r'T_A','T_B':r'T_B','T_C':r'T_C',
 'x_a(t)':r'x_a(t)','x_a^0':r'x_a^0','x_a^⋆':r'x_a^\star','ε_a':r'\epsilon_a',
 'y_min':r'\mathbf y_{\min}','y_max':r'\mathbf y_{\max}','u_k':r'\mathbf u_k',
 '𝒲_k':r'\mathcal W_k','Δs_max':r'\Delta s_{\max}','s_k':r's_k',
 'μ_a':r'\mu_a','τ_a':r'\tau_a','κ_b':r'\kappa_b','η_b':r'\eta_b','w_b':r'w_b',
 'b_k':r'b_k','d_k':r'd_k','n^⋆':r'\mathbf n^\star','n_k':r'\mathbf n_k','e_r':r'\mathbf e_r','ξ_k':r'\xi_k',
}
pattern=re.compile('|'.join(re.escape(k) for k in sorted(inline_map,key=len,reverse=True)))
def add_inline(p,t,bold=False,small=False):
 pos=0
 for match in pattern.finditer(t):
  if match.start()>pos:
   r=p.add_run(t[pos:match.start()]);r.bold=bold;r.font.size=Pt(10.5 if small else 12)
  p._p.append(math_element(inline_map[match.group(0)]));pos=match.end()
 if pos<len(t):
  r=p.add_run(t[pos:]);r.bold=bold;r.font.size=Pt(10.5 if small else 12)

tail=source[source.index("text=(ROOT/'draft.txt')"):]
tail=tail.replace("r=p.add_run(normal_text);r.bold=bold;r.font.size=Pt(10.5 if small else 12)","add_inline(p,normal_text,bold,small)")
tail=tail.replace("elif line.startswith('='):para(line[1:],center=True)","elif line.startswith('@EQ '):equation(line[4:])\n elif line.startswith('='):para(line[1:],center=True)")
tail=tail.replace('range(1,6)','range(1,9)')
tail=tail.replace("final=OUT/'处方游戏动作映射算法_专利申请文件_含文献引用_v1.docx'","final=OUT/'康复处方到游戏的映射_专利申请文件_v3.docx'")
tail=tail.replace("'figures':5","'figures':8")
tail=tail.replace("doc.add_page_break();chapter('说   明   书   附   图')", "chapter('说   明   书   附   图');doc.paragraphs[-1].paragraph_format.page_break_before=True")
tail=tail.replace("else:para(line)", "elif re.match(r'^（[一二三四五六七八九十]+）',line):para(line,bold=True,keep=True)\n else:para(line)")
exec(tail)
with ZipFile(final) as z:
 root=etree.fromstring(z.read('word/document.xml'))
 print('NATIVE_EQUATIONS',len(root.findall('.//{'+math_ns+'}oMath')))
