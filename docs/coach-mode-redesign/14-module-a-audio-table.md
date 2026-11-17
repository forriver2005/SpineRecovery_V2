# 六动作静态音频清单

生成（2026-10-29 更新）：开发期 Microsoft Edge neural TTS / zh-CN-XiaoxiaoNeural；rate=+0%、volume=+0%、pitch=+0Hz。完整解码为 24000 Hz/16-bit/mono WAV，Unity pitch=1；无运行时云 API。声线由两句原音频的波形匹配确认，听感仍由用户验收。

台词均从项目 Profile/动画相位/诊断可观察部位组织；未宣称临床审核。旧文件未剪辑、未加速。听感和设备外放验收尚未完成。

## deadbug

DeadBugPractice 分段 Animator、activeRegions=6/9、TargetedExercisePoseGuidance

| key | 台词 | 实际秒数 | 播放节点 | WAV |
|---|---|---:|---|---|
| setup | 仰卧屈膝，肩背放松，腹部轻轻收紧。 | 4.152 | 独立 setup 兼容素材；相同起始信息已包含在 lesson，正式课不重复播放 | [Coach_deadbug_setup.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_setup.wav) |
| rep1 | 第一次，右臂和左腿准备。 | 3.048 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_deadbug_rep1.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_rep1.wav) |
| rep2 | 第二次，左臂和右腿准备。 | 3.024 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_deadbug_rep2.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_rep2.wav) |
| rep3 | 第三次，右臂和左腿准备。 | 3.120 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_deadbug_rep3.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_rep3.wav) |
| rep4 | 第四次，左臂和右腿准备。 | 3.096 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_deadbug_rep4.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_rep4.wav) |
| hold | 自然呼吸。 | 1.872 | CoreAction 到位后的原固定保持窗 | [Coach_deadbug_hold.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_hold.wav) |
| return | 手脚收回。 | 1.872 | ReturnTransition 动画开始 | [Coach_deadbug_return.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_return.wav) |
| adjust | 目标手臂和腿按教练示范调整。 | 3.648 | 腰背/臀桥：有效目标偏差持续 1 秒后休息窗；死虫/鸟狗旧兼容函数，正式纠正不用此泛化句 | [Coach_deadbug_adjust.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_adjust.wav) |
| lesson | 现在学习死虫动作，练习对侧手臂和腿的配合。仰卧屈膝，按教练摆好手臂和腿。第一和第三次动右臂、左腿，第二和第四次动左臂、右腿。缓慢伸出，到位保持躯干稳定，再收回起始位置。自然呼吸。注意跟随当前侧别。先看完整示范。 | 25.776 | 开始自动讲解，完整示范前 | [Coach_deadbug_lesson.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_lesson.wav) |
| move | 缓慢伸出。 | 1.872 | CoreAction 动画开始 | [Coach_deadbug_move.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_move.wav) |
| adjust_LeftArm | 左臂按教练示范调整。 | 2.688 | 对应部位有效偏差持续 1 秒后，最近回位休息窗 | [Coach_deadbug_adjust_LeftArm.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_adjust_LeftArm.wav) |
| adjust_RightArm | 右臂按教练示范调整。 | 2.688 | 对应部位有效偏差持续 1 秒后，最近回位休息窗 | [Coach_deadbug_adjust_RightArm.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_adjust_RightArm.wav) |
| adjust_LeftLeg | 左腿按教练示范调整。 | 2.688 | 对应部位有效偏差持续 1 秒后，最近回位休息窗 | [Coach_deadbug_adjust_LeftLeg.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_adjust_LeftLeg.wav) |
| adjust_RightLeg | 右腿按教练示范调整。 | 2.712 | 对应部位有效偏差持续 1 秒后，最近回位休息窗 | [Coach_deadbug_adjust_RightLeg.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_adjust_RightLeg.wav) |
| side1 | 右臂和左腿准备。 | 2.304 | 超出四次或组间侧别不匹配时 RepPrepare；不播错误次数 | [Coach_deadbug_side1.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_side1.wav) |
| side2 | 左臂和右腿准备。 | 2.232 | 超出四次或组间侧别不匹配时 RepPrepare；不播错误次数 | [Coach_deadbug_side2.wav](../../Assets/Resources/CoachLessons/Coach_deadbug_side2.wav) |

## birddog

BirdDogPractice 分段 Animator、activeRegions=6/9、TargetedExercisePoseGuidance

| key | 台词 | 实际秒数 | 播放节点 | WAV |
|---|---|---:|---|---|
| setup | 双手和双膝支撑，背部保持自然稳定。 | 4.080 | 独立 setup 兼容素材；相同起始信息已包含在 lesson，正式课不重复播放 | [Coach_birddog_setup.wav](../../Assets/Resources/CoachLessons/Coach_birddog_setup.wav) |
| rep1 | 第一次，准备右臂和左腿。 | 3.048 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_birddog_rep1.wav](../../Assets/Resources/CoachLessons/Coach_birddog_rep1.wav) |
| rep2 | 第二次，准备左臂和右腿。 | 2.976 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_birddog_rep2.wav](../../Assets/Resources/CoachLessons/Coach_birddog_rep2.wav) |
| rep3 | 第三次，准备右臂和左腿。 | 3.096 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_birddog_rep3.wav](../../Assets/Resources/CoachLessons/Coach_birddog_rep3.wav) |
| rep4 | 第四次，准备左臂和右腿。 | 3.120 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_birddog_rep4.wav](../../Assets/Resources/CoachLessons/Coach_birddog_rep4.wav) |
| hold | 自然呼吸。 | 1.872 | CoreAction 到位后的原固定保持窗 | [Coach_birddog_hold.wav](../../Assets/Resources/CoachLessons/Coach_birddog_hold.wav) |
| return | 收回支撑。 | 1.872 | ReturnTransition 动画开始 | [Coach_birddog_return.wav](../../Assets/Resources/CoachLessons/Coach_birddog_return.wav) |
| adjust | 目标手臂和腿按教练示范调整。 | 3.648 | 腰背/臀桥：有效目标偏差持续 1 秒后休息窗；死虫/鸟狗旧兼容函数，正式纠正不用此泛化句 | [Coach_birddog_adjust.wav](../../Assets/Resources/CoachLessons/Coach_birddog_adjust.wav) |
| lesson | 现在学习鸟狗动作，练习四点支撑时的对侧伸展。双手和双膝支撑，背部保持自然稳定。第一和第三次伸右臂、左腿，第二和第四次伸左臂、右腿。手臂向前，腿向后，随后收回支撑位置。自然呼吸，注意躯干稳定。先看完整示范。 | 24.648 | 开始自动讲解，完整示范前 | [Coach_birddog_lesson.wav](../../Assets/Resources/CoachLessons/Coach_birddog_lesson.wav) |
| move | 向前后伸展。 | 1.944 | CoreAction 动画开始 | [Coach_birddog_move.wav](../../Assets/Resources/CoachLessons/Coach_birddog_move.wav) |
| adjust_LeftArm | 左臂按教练示范调整。 | 2.688 | 对应部位有效偏差持续 1 秒后，最近回位休息窗 | [Coach_birddog_adjust_LeftArm.wav](../../Assets/Resources/CoachLessons/Coach_birddog_adjust_LeftArm.wav) |
| adjust_RightArm | 右臂按教练示范调整。 | 2.688 | 对应部位有效偏差持续 1 秒后，最近回位休息窗 | [Coach_birddog_adjust_RightArm.wav](../../Assets/Resources/CoachLessons/Coach_birddog_adjust_RightArm.wav) |
| adjust_LeftLeg | 左腿按教练示范调整。 | 2.688 | 对应部位有效偏差持续 1 秒后，最近回位休息窗 | [Coach_birddog_adjust_LeftLeg.wav](../../Assets/Resources/CoachLessons/Coach_birddog_adjust_LeftLeg.wav) |
| adjust_RightLeg | 右腿按教练示范调整。 | 2.712 | 对应部位有效偏差持续 1 秒后，最近回位休息窗 | [Coach_birddog_adjust_RightLeg.wav](../../Assets/Resources/CoachLessons/Coach_birddog_adjust_RightLeg.wav) |
| side1 | 右臂和左腿准备。 | 2.304 | 超出四次或组间侧别不匹配时 RepPrepare；不播错误次数 | [Coach_birddog_side1.wav](../../Assets/Resources/CoachLessons/Coach_birddog_side1.wav) |
| side2 | 左臂和右腿准备。 | 2.232 | 超出四次或组间侧别不匹配时 RepPrepare；不播错误次数 | [Coach_birddog_side2.wav](../../Assets/Resources/CoachLessons/Coach_birddog_side2.wav) |

## hipthrust

HipThrustCoachMode 22–27 / 36.5–40 秒、HipThrustPoseGuidance

| key | 台词 | 实际秒数 | 播放节点 | WAV |
|---|---|---:|---|---|
| setup | 仰卧屈膝，双脚平稳放好。 | 3.168 | 独立 setup 兼容素材；相同起始信息已包含在 lesson，正式课不重复播放 | [Coach_hipthrust_setup.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_setup.wav) |
| hold | 自然呼吸。 | 1.872 | CoreAction 到位后的原固定保持窗 | [Coach_hipthrust_hold.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_hold.wav) |
| return | 臀部放下。 | 1.872 | ReturnTransition 动画开始 | [Coach_hipthrust_return.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_return.wav) |
| adjust | 臀部位置按教练示范调整。 | 3.216 | 腰背/臀桥：有效目标偏差持续 1 秒后休息窗；死虫/鸟狗旧兼容函数，正式纠正不用此泛化句 | [Coach_hipthrust_adjust.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_adjust.wav) |
| lesson | 现在学习臀桥动作，练习臀部抬起和回落。仰卧屈膝，双脚平稳支撑。臀部在舒适范围缓慢抬起，到位后保持，再慢慢放下。自然呼吸，注意按示范控制臀部位置。先看完整示范。 | 19.056 | 开始自动讲解，完整示范前 | [Coach_hipthrust_lesson.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_lesson.wav) |
| move | 缓慢抬臀。 | 1.872 | CoreAction 动画开始 | [Coach_hipthrust_move.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_move.wav) |
| count1 | 第一次，准备。 | 2.016 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_hipthrust_count1.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_count1.wav) |
| count2 | 第二次，准备。 | 2.016 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_hipthrust_count2.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_count2.wav) |
| count3 | 第三次，准备。 | 2.088 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_hipthrust_count3.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_count3.wav) |
| count4 | 第四次，准备。 | 2.112 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_hipthrust_count4.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_count4.wav) |
| prepare | 准备下一次。 | 1.872 | 超出四次或组间侧别不匹配时 RepPrepare；不播错误次数 | [Coach_hipthrust_prepare.wav](../../Assets/Resources/CoachLessons/Coach_hipthrust_prepare.wav) |

## catcamel

LumbarCoachMode Profiles[0]：四点支撑、小幅背部活动、正常呼吸

| key | 台词 | 实际秒数 | 播放节点 | WAV |
|---|---|---:|---|---|
| setup | 双手和双膝舒适支撑。 | 2.712 | 独立 setup 兼容素材；相同起始信息已包含在 lesson，正式课不重复播放 | [Coach_catcamel_setup.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_setup.wav) |
| return | 回到支撑位。 | 1.872 | ReturnTransition 动画开始 | [Coach_catcamel_return.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_return.wav) |
| adjust | 背部活动按教练示范调整。 | 3.096 | 腰背/臀桥：有效目标偏差持续 1 秒后休息窗；死虫/鸟狗旧兼容函数，正式纠正不用此泛化句 | [Coach_catcamel_adjust.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_adjust.wav) |
| lesson | 现在学习卧位猫式，练习背部的小幅活动。双手和双膝舒适支撑。跟随示范缓慢向上拱背，再回到舒适支撑位。动作轻柔，小幅跟做，保持正常呼吸。先看完整示范。 | 17.568 | 开始自动讲解，完整示范前 | [Coach_catcamel_lesson.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_lesson.wav) |
| move | 小幅向上拱背。 | 2.064 | CoreAction 动画开始 | [Coach_catcamel_move.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_move.wav) |
| hold | 正常呼吸。 | 1.872 | CoreAction 到位后的原固定保持窗 | [Coach_catcamel_hold.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_hold.wav) |
| count1 | 第一次，准备。 | 2.016 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_catcamel_count1.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_count1.wav) |
| count2 | 第二次，准备。 | 2.016 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_catcamel_count2.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_count2.wav) |
| count3 | 第三次，准备。 | 2.088 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_catcamel_count3.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_count3.wav) |
| count4 | 第四次，准备。 | 2.112 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_catcamel_count4.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_count4.wav) |
| prepare | 准备下一次。 | 1.872 | 超出四次或组间侧别不匹配时 RepPrepare；不播错误次数 | [Coach_catcamel_prepare.wav](../../Assets/Resources/CoachLessons/Coach_catcamel_prepare.wav) |

## sidebridge

LumbarCoachMode Profiles[1]：侧卧支撑、短暂撑起、正常呼吸、骨盆回落；左右尚待视觉确认

| key | 台词 | 实际秒数 | 播放节点 | WAV |
|---|---|---:|---|---|
| setup | 左侧卧，按教练示范摆好支撑位置。 | 4.080 | 独立 setup 兼容素材；相同起始信息已包含在 lesson，正式课不重复播放 | [Coach_sidebridge_setup.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_setup.wav) |
| hold | 自然呼吸。 | 1.872 | CoreAction 到位后的原固定保持窗 | [Coach_sidebridge_hold.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_hold.wav) |
| return | 骨盆放下。 | 1.872 | ReturnTransition 动画开始 | [Coach_sidebridge_return.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_return.wav) |
| adjust | 躯干姿势按教练示范调整。 | 3.264 | 腰背/臀桥：有效目标偏差持续 1 秒后休息窗；死虫/鸟狗旧兼容函数，正式纠正不用此泛化句 | [Coach_sidebridge_adjust.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_adjust.wav) |
| lesson | 现在学习侧桥，练习左侧支撑时的撑起和回落。左侧卧，左臂支撑，按示范摆好双腿。缓慢撑起躯干，短暂支撑，正常呼吸，再慢慢放下骨盆。注意保持舒适，可以随时回落。先看完整示范。 | 20.112 | 开始自动讲解，完整示范前 | [Coach_sidebridge_lesson.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_lesson.wav) |
| move | 缓慢撑起。 | 1.872 | CoreAction 动画开始 | [Coach_sidebridge_move.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_move.wav) |
| count1 | 第一次，准备。 | 2.016 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_sidebridge_count1.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_count1.wav) |
| count2 | 第二次，准备。 | 2.016 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_sidebridge_count2.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_count2.wav) |
| count3 | 第三次，准备。 | 2.088 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_sidebridge_count3.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_count3.wav) |
| count4 | 第四次，准备。 | 2.112 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_sidebridge_count4.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_count4.wav) |
| prepare | 准备下一次。 | 1.872 | 超出四次或组间侧别不匹配时 RepPrepare；不播错误次数 | [Coach_sidebridge_prepare.wav](../../Assets/Resources/CoachLessons/Coach_sidebridge_prepare.wav) |

## curlup

LumbarCoachMode Profiles[2]：仰卧屈膝、小幅卷起上背、颈部舒适、正常呼吸

| key | 台词 | 实际秒数 | 播放节点 | WAV |
|---|---|---:|---|---|
| setup | 仰卧屈膝，按教练示范摆好手臂。 | 3.912 | 独立 setup 兼容素材；相同起始信息已包含在 lesson，正式课不重复播放 | [Coach_curlup_setup.wav](../../Assets/Resources/CoachLessons/Coach_curlup_setup.wav) |
| hold | 自然呼吸。 | 1.872 | CoreAction 到位后的原固定保持窗 | [Coach_curlup_hold.wav](../../Assets/Resources/CoachLessons/Coach_curlup_hold.wav) |
| return | 上背放下。 | 1.728 | ReturnTransition 动画开始 | [Coach_curlup_return.wav](../../Assets/Resources/CoachLessons/Coach_curlup_return.wav) |
| adjust | 上背位置按教练示范调整。 | 3.288 | 腰背/臀桥：有效目标偏差持续 1 秒后休息窗；死虫/鸟狗旧兼容函数，正式纠正不用此泛化句 | [Coach_curlup_adjust.wav](../../Assets/Resources/CoachLessons/Coach_curlup_adjust.wav) |
| lesson | 现在学习卷腹，练习上背的小幅卷起。仰卧屈膝，按示范摆好手臂，颈部保持舒适。上背小幅卷起，保持片刻，再慢慢放下。正常呼吸，注意不拉扯颈部。先看完整示范。 | 18.144 | 开始自动讲解，完整示范前 | [Coach_curlup_lesson.wav](../../Assets/Resources/CoachLessons/Coach_curlup_lesson.wav) |
| move | 上背卷起。 | 1.872 | CoreAction 动画开始 | [Coach_curlup_move.wav](../../Assets/Resources/CoachLessons/Coach_curlup_move.wav) |
| count1 | 第一次，准备。 | 2.016 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_curlup_count1.wav](../../Assets/Resources/CoachLessons/Coach_curlup_count1.wav) |
| count2 | 第二次，准备。 | 2.016 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_curlup_count2.wav](../../Assets/Resources/CoachLessons/Coach_curlup_count2.wav) |
| count3 | 第三次，准备。 | 2.088 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_curlup_count3.wav](../../Assets/Resources/CoachLessons/Coach_curlup_count3.wav) |
| count4 | 第四次，准备。 | 2.112 | 本次 RepPrepare；回位/准备姿势冻结，句尾后进入动作 | [Coach_curlup_count4.wav](../../Assets/Resources/CoachLessons/Coach_curlup_count4.wav) |
| prepare | 准备下一次。 | 1.872 | 超出四次或组间侧别不匹配时 RepPrepare；不播错误次数 | [Coach_curlup_prepare.wav](../../Assets/Resources/CoachLessons/Coach_curlup_prepare.wav) |
