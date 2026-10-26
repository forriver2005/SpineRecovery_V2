from pathlib import Path
import json,re,shutil

R=Path(__file__).resolve().parent
out=R/'v3'
out.mkdir(exist_ok=True)
t=(R/'v2/draft.txt').read_text(encoding='utf-8')
t=t.replace('一种基于多属性问答的康复处方游戏映射','一种康复处方到游戏的映射')

def replace(old,new):
 global t
 assert old in t,old[:80]
 t=t.replace(old,new)

replace('设第ℓ项跨问题校验规则为b_ℓ，则整体一致性为：\n@EQ CHECK',
        '系统逐项执行跨问题一致性检查；只有全部必需检查通过，才将该项目送入分类步骤。任一检查失败均返回具体冲突及其关联字段。')
replace('主类别用于检索相应模板族；真正的适配条件来自全部答案及处方约束。设答案a_ij产生的接口需求集合为R_j(a_ij)，动作监测和本项目剂量所需的附加能力为R_i^A与R_i^D，则：\n@EQ REQ',
        '主类别用于检索相应模板族；真正的适配条件来自全部答案及处方约束。系统汇集各问题答案产生的接口需求，再加入动作监测及本项目剂量所需的能力，形成完整需求清单。重复需求合并，全部必需条件保留，并记录每项需求来自哪个问题或处方字段。')
replace('@EQ FEAS','模板选择变量仅取0或1；每个项目恰好选择一个模板，并且只能选择兼容标志K_ig为1的模板。上述条件作为求解式（9）时必须满足的约束。')
replace('对于单条顺序处方，可用动态规划求得同一目标的最小值；设F_i(g)为前i个项目且第i项使用模板g的最小代价，递推为：\n@EQ DP\n第一项的F_1(g)=c_1g，不兼容状态设为正无穷；保存最优前驱并回溯获得模板序列。相同代价按固定模板标识顺序选择，保证结果可复现。若某项目候选为空，求解结果为不可执行并返回缺失项，不通过删除处方项目获得可行解。对于有限轮次，先按处方展开项目实例及其邻接关系后使用同一方法。',
        '对于单条顺序处方，可采用动态规划：先记录第一项目各兼容模板的适配代价；此后，对当前项目的每个候选模板，逐一比较前一项目已保存的累计代价、两模板间切换代价及当前适配代价之和，保留最小值及对应前驱，最后回溯获得模板序列。相同代价按固定模板标识顺序选择。若某项目没有候选模板，返回该项目的缺失项。对于有限轮次，先按处方展开项目实例及其邻接关系后使用同一方法。')
replace('对于一个或多个游戏槽位，将其范围记为向量y_min与y_max，则显示位置为：\n@EQ DISPLAY',
        '对于一个或多个游戏槽位，分别配置显示起点和终点。将各通道零至一的显示控制量按比例映射到对应显示区间：零对应起点，一对应终点，中间值对应相同比例的显示行程。该显示变换仅决定画面位置，处方的真实运动目标保持独立。')
replace('同步维护式（13）的显示变换','同步维护显示控制量与画面起终点之间的比例变换')

mapping='''（七）动作类别与游戏类型的对应关系
本实施例设置以下动作类别及对应游戏类型。动作类别描述控制与完成结构，游戏类型描述可复用的交互机制；同一类别可以匹配多个游戏主题，最终仍按完整接口需求检查兼容性。
单通道往返类，对应“单轨运输”或“升降控制”类型游戏，例如“升降运输”。一个活动通道控制对象沿单一轨道移动，完成目标、保持及返回条件后提交一次任务，适用于满足该控制结构的伸膝、肩屈曲或颈部单侧转动等动作。
多通道轨迹类，对应“路径引导”或“多轴操控”类型游戏。多个独立控制量分别驱动不同方向或对象，并按规定路径、阶段顺序及通道约束确认完成，不能仅凭对象接近终点结算。
姿态保持类，对应“稳定充能”类型游戏。全部必需姿态条件满足时累计有效保持，充能过程体现处方时长；连续保持任务在条件中断时按要求清零。
双侧同步类，对应“双通道共同运输”类型游戏。左右活动通道共同驱动运输过程，同时检查各自目标与同步条件，任一侧未达标时不能以另一侧的进度替代。
对侧交替类，对应“交替双通道协作”类型游戏，例如“交替双通道运输”。一个对侧组合中的两个活动通道联合完成，到达、保持及返回确认后切换另一组合；鸟狗式和死虫式可在体位及约束兼容时复用该类游戏。
单通道到达类，对应“单次到达”或“目标触达”类型游戏。单一通道驱动对象到达规定目标，并在保持及退出条件满足后提交任务；处方未规定返回时，不额外附加往返要求。
顺序组合类，对应“顺序任务”或“组合闯关”类型游戏。按动作内部规定的子阶段顺序串接前述基础游戏机制，并在体位转换时插入准备确认。该类别针对一个动作的内部结构；一份处方包含多个独立动作时，由处方级任务图编排，不因此将所有动作改为顺序组合类。
组合扩展类，对应“自定义组合任务”类型游戏。依据已登记的有序、并行及交替关系组合基础机制；所需原语或接口尚未实现时，保留类别名称及扩展需求，完成兼容性检查后方可执行。
上述名称同时保存在动作分类表与游戏模板目录中。系统先对处方内各动作分别分类并选择游戏类型，再把各动作对应的目标范围、次数、组数、保持与休息等剂量写入游戏任务，最后按照处方规定的顺序或轮次组装整份处方的游戏流程。
'''
replace('##四 模板兼容性与剂量编译',mapping+'##四 模板兼容性与剂量编译')
eq=json.loads((R/'v2/equations.json').read_text(encoding='utf-8'))
removed={'CHECK','REQ','FEAS','DP','DISPLAY'}
eq=[e for e in eq if e['key'] not in removed]
renumber={e['number']:i for i,e in enumerate(eq,1)}
t=re.sub(r'式（(\d+)）',lambda m:'式（'+str(renumber[int(m[1])])+'）',t)
for e in eq:e['number']=renumber[e['number']]
assert len(eq)==12
assert re.findall(r'^@EQ (\w+)$',t,re.M)==[e['key'] for e in eq]
assert 'CN118448003A' not in t and 'CN109920517A' not in t
assert '一种基于多属性问答' not in t
(out/'draft.txt').write_text(t,encoding='utf-8')
(out/'equations.json').write_text(json.dumps(eq,ensure_ascii=False,indent=2),encoding='utf-8')
(out/'equations.tex').write_text('\n\n'.join('\\begin{equation}\n'+e['latex']+'\n\\tag{'+str(e['number'])+'}\n\\end{equation}' for e in eq),encoding='utf-8')
shutil.copyfile(R/'v2/qa.py',out/'qa.py')
b=(R/'build_v2.py').read_text(encoding='utf-8')
b=b.replace("ROOT=R/'v2'","ROOT=R/'v3'")
b=b.replace('基于多属性问答的康复处方游戏映射_专利申请文件_v2.docx','康复处方到游戏的映射_专利申请文件_v3.docx')
b=b.replace('exec(tail)', '''tail=tail.replace("doc.add_page_break();chapter('说   明   书   附   图')", "chapter('说   明   书   附   图');doc.paragraphs[-1].paragraph_format.page_break_before=True")
tail=tail.replace("else:para(line)", "elif re.match(r'^（[一二三四五六七八九十]+）',line):para(line,bold=True,keep=True)\\n else:para(line)")
exec(tail)''')
(R/'build_v3.py').write_text(b,encoding='utf-8')
print('Prepared v3: 12 equations, 8 action classes, named game mappings.')
