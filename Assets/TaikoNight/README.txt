敲鼓 · 星轨统一版 —— Unity 使用说明

1. 导入 TaikoNight.unitypackage（Unity 2021.3 及以上，内置管线或 URP 均可）
2. 菜单 Tools > Taiko Night > Build Prefab + Demo Scene
   - 自动把 Sprites 设为 Sprite、200 PPU（2x 美术，1 设计 px = 0.01 unit）
   - 自动组装并生成 Prefabs/TaikoNight.prefab 与 Scenes/TaikoNight_Demo.unity
3. 打开 TaikoNight_Demo，Game 视图设为手机横屏比例（如 2532x1170），点 Play

设计尺寸 844x390 设计 px（与圆环 C 相同），判定圈 = 鼓面中心 (422, 232)。

层级结构（TaikoNight 预制体）
  Background   与圆环 C 相同的夜空：5 颗闪烁星芒、星座亮星呼吸、流星、小孔明灯浮动
  CoachSlot    左侧深色玻璃方块；教练小人放到 CoachAnchor 下（可用区 144x144，中心 (110, 200)），并关闭 tn_coach_placeholder
  Drum         鼓身与鼓面、8 颗金色铆钉闪烁、鼓面闪白、粉 / 蓝鼓面涟漪
  Lane         音轨底板 + LaneMask（圆角遮罩，音符滚进判定圈后消失）+ NoteStrip（抬起音符 → 3 秒长按条 → 放下音符，循环滚动）
  JudgeCircle  保持金光、判定圈细环、判定球、保持进度环（两条 LineRenderer：金色宽光 + 白色细线）、粉 / 蓝命中波纹、PERFECT
  Sticks       左右鼓槌（精灵以握点为中心，绕手部旋转）、击打星光
  Foreground   底部前景云
  HUD          暂停按钮（Sprite）、分数与 COMBO（UI Text，Fredoka）

演示节奏（TaikoNightChart，挂在根节点，6 秒循环，音符速度 110 px/秒）
  1.0 秒      抬起音符到达判定圈：左鼓槌蓄力敲下、粉色波纹与鼓面涟漪、鼓面闪白、PERFECT
  1.0 – 4.0 秒  保持：判定圈外的金色进度环顺时针走满 3 秒，金光逐渐增强
  4.0 秒      放下音符到达判定圈：右鼓槌敲下、蓝色波纹与鼓面涟漪、PERFECT
  4.0 – 7.0 秒  休息（循环）

接入正式玩法
  - 把 DesignSpace.Now 换成音乐播放时间
  - 按真实谱面生成音符：音符中心到达 x = 422（判定圈中心）的时刻即判定时刻
  - TaikoNightChart 的 liftHit / lowerHit 是演示用命中时间；Ring / Spark / DrawRing 可直接复用为命中与长按反馈

脚本（Scripts/，命名空间 TaikoNight）
  NightLayout       由设计稿自动生成的图层中心坐标、层级、铆钉位置、星芒节奏
  TaikoNightChart   演示谱面与全部命中反馈
  Twinkle / MeteorLoop / FloatBob   背景循环动效
  DesignSpace       坐标换算与缓动

字体 Fredoka 为 SIL OFL 开源字体（见 Fonts/OFL.txt）；中文文字已烘焙进图片。
