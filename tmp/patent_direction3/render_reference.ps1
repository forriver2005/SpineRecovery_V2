$ErrorActionPreference='Stop'
$src='C:/Users/23548/Documents/xwechat_files/wxid_dv2i6u8f201q12_fa7a/msg/file/2026-10'
$out='C:/Users/23548/Desktop/SpineRecovery_V2/tmp/patent_direction3'
$word=New-Object -ComObject Word.Application
$word.Visible=$false
$word.DisplayAlerts=0
try {
 $files=@('增强现实镜像康复训练指导系统及方法_专利申请文件_v2(2).docx','一种基于动作偏差定位与限时振动反馈的肌骨康复训练引导方法及系统.docx')
 for($i=0;$i -lt $files.Count;$i++) {
  $doc=$word.Documents.Open((Join-Path $src $files[$i]),$false,$true)
  $doc.ExportAsFixedFormat((Join-Path $out ('reference'+($i+1)+'.pdf')),17)
  $doc.Close(0)
 }
} finally { $word.Quit() }
