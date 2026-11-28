using TMPro;
using UnityEngine;

/// <summary>Visible status for automatic recorded-hold calibration; no additional menu button.</summary>
public sealed class HoldCalibrationControls : MonoBehaviour
{
    public DeadBugGamingPoseScorer scorer;
    public DeadBugStartMenuController menu;
    public TMP_Text StatusLabel { get; private set; }
    void Start()
    {
        var original=menu.TrainingStartButton;if(original==null)return;
        var caption=original.GetComponentInChildren<TMP_Text>(true);
        var go=new GameObject("Calibration Status",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(original.transform.parent,false);
        StatusLabel=go.GetComponent<TMP_Text>();StatusLabel.font=caption.font;StatusLabel.fontSize=22;StatusLabel.color=new Color(.12f,.22f,.34f);StatusLabel.alignment=TextAlignmentOptions.Center;StatusLabel.raycastTarget=false;
        var statusRect=StatusLabel.rectTransform;statusRect.anchorMin=statusRect.anchorMax=Vector2.one*.5f;statusRect.anchoredPosition=new Vector2(-400,-315);statusRect.sizeDelta=new Vector2(600,105);
        scorer.CalibrationProgressChanged+=Progress;scorer.CalibrationStateChanged+=Calibrated;
        Progress(scorer.IsCalibrated?1:0,scorer.IsCalibrated?"校准完成，可以开始训练":scorer.CalibrationInstruction);
    }
    void Progress(float progress,string message){if(StatusLabel!=null)StatusLabel.text=message;}
    void Calibrated(bool ok){if(ok)Progress(1,"校准完成，可以开始训练");}
    void OnDestroy(){if(scorer!=null){scorer.CalibrationProgressChanged-=Progress;scorer.CalibrationStateChanged-=Calibrated;}if(StatusLabel!=null)Destroy(StatusLabel.gameObject);}
}
