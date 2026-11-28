using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One settlement layout for all games; only the metrics vary by task type.</summary>
public sealed class TrainingResultPanel : MonoBehaviour
{
    CanvasScaler scaler;
    TMP_FontAsset font;
    TMP_Text title,score,scoreCaption,quality,holdTime,followingTime,repetitions,workoutStatus;
    Action onReturn;
    public TMP_Text ScoreLabel => score;
    public Button ReturnButton { get; private set; }

    public static TrainingResultPanel Create(TMP_FontAsset font,Action onReturn)
    {
        var root=new GameObject("Training Quality Result",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        var panel=root.AddComponent<TrainingResultPanel>();panel.font=font;panel.onReturn=onReturn;panel.Build();root.SetActive(false);return panel;
    }
    void Build()
    {
        var canvas=GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=2500;
        scaler=GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        var bg=Box("Background",Vector2.zero,new Vector2(1920,1080),new Color(.045f,.04f,.14f));
        bg.anchorMin=Vector2.zero;bg.anchorMax=Vector2.one;bg.sizeDelta=Vector2.zero;
        Box("Card",Vector2.zero,new Vector2(1660,920),new Color(.12f,.10f,.25f));
        title=Label("本组训练报告",new Vector2(0,375),new Vector2(1500,70),48);title.color=new Color(.94f,.89f,1);
        score=Label("—",new Vector2(-540,65),new Vector2(480,200),150);score.color=new Color(1,.82f,.39f);
        scoreCaption=Label("总成绩 / 100",new Vector2(-540,-70),new Vector2(480,65),32);
        quality=Metric("动作质量",230);holdTime=Metric("合格保持时长",80);followingTime=Metric("合格跟随时长",-70);repetitions=Metric("合格次数",-220);
        var buttonRect=Box("Return",new Vector2(0,-390),new Vector2(360,68),new Color(.40f,.31f,.67f));
        ReturnButton=buttonRect.gameObject.AddComponent<Button>();ReturnButton.targetGraphic=buttonRect.GetComponent<Image>();
        ReturnButton.onClick.AddListener(ReturnOrContinue);
        var training=SpineRecovery.App.AppBootstrap.Instance?.Training;
        bool prescribed=training?.IsV2Prescription==true;
        var caption=Label(prescribed?training.GameContinueLabel:"返回选择",new Vector2(0,-390),new Vector2(350,65),30);
        if(prescribed&&!training.HasNextStepInItem&&training.HasNextPrescriptionItem)
        {
            buttonRect.anchoredPosition+=Vector2.left*205;
            caption.rectTransform.anchoredPosition=buttonRect.anchoredPosition;
            var at=new Vector2(205,-390);
            var rect=Box("Calibrate",at,buttonRect.sizeDelta,ReturnButton.targetGraphic.color);
            var calibrate=rect.gameObject.AddComponent<Button>();calibrate.targetGraphic=rect.GetComponent<Image>();
            Label("校准后填写问卷",at,rect.sizeDelta,30);
            var calibration=gameObject.AddComponent<BetweenActionCalibrationPanel>();
            calibration.Initialize(font,()=>training.ContinuePrescription());
            calibrate.onClick.AddListener(()=>{if(SpineFlowTrainingSession.CurrentPrescriptionGameCompleted)calibration.Open();});
        }
        workoutStatus=Label("",new Vector2(0,-318),new Vector2(1450,42),26);
    }
    public void Show(string actionName,FinalTrainingScore s,bool preview=false)
    {
        Refresh(actionName,s,preview);gameObject.SetActive(true);
    }
    private void ReturnOrContinue()
    {
        var training=SpineRecovery.App.AppBootstrap.Instance?.Training;
        if(training?.IsV2Prescription==true){training.ContinuePrescription();return;}
        Hide();if(GameWorkoutSession.Instance.IsRunning)GameWorkoutSession.Instance.Cancel();onReturn?.Invoke();
    }
    public void Hide()=>gameObject.SetActive(false);
    public void Refresh(string actionName,FinalTrainingScore s,bool preview=false)
    {
        bool available=s.CanPublish&&!preview,hold=s.Kind==FinalTrainingScore.Mode.TimedHold;
        title.text=actionName+" · 本组训练报告";
        score.text=preview?"流程测试":available?s.Score.ToString():"数据不足";score.fontSize=available?150:60;
        scoreCaption.text=preview?"本次未记录训练成绩":hold?"保持达标率 / 100":"往复总成绩 / 100";
        if(hold)
        {
            quality.text=$"合格保持   {s.ValidHold:0.0} / {s.TargetHold:0.0} 秒";
            holdTime.text=$"最长连续保持   {s.Longest:0.0} 秒";
            followingTime.text=$"连续目标完成   {s.ContinuousCompleted} / {s.Planned} 次";
            repetitions.text=$"有效数据覆盖   {s.Coverage*100:0.0}%";
        }
        else
        {
            quality.text=$"到位 / 目标保持   {s.TargetQuality:0.0} / 100　（80%）";
            holdTime.text=$"往复过程   {s.ProcessQuality:0.0} / 100　（5%）";
            followingTime.text=$"回位表现   {s.ReturnQuality:0.0} / 100　（15%）";
            repetitions.text=$"完整往返   {s.Complete} / {s.Planned} 次　·　数据覆盖 {s.Coverage*100:0.0}%";
        }
    }
    void LateUpdate()
    {
        var session=GameWorkoutSession.Instance;
        workoutStatus.text=session.IsRunning&&session.RestSecondsRemaining>0?$"组间休息 · {session.RestSecondsRemaining} 秒后准备下一组":"";
    }
    TMP_Text Metric(string name,float y)
    {
        Box(name,new Vector2(250,y),new Vector2(970,125),new Color(.19f,.16f,.34f));
        var text=Label(name,new Vector2(250,y),new Vector2(910,110),34);text.alignment=TextAlignmentOptions.MidlineLeft;return text;
    }
    RectTransform Box(string name,Vector2 position,Vector2 size,Color color)
    {
        var rect=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();rect.SetParent(transform,false);
        rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=size;rect.anchoredPosition=position;rect.GetComponent<Image>().color=color;return rect;
    }
    TMP_Text Label(string text,Vector2 position,Vector2 size,float fontSize)
    {
        var rect=new GameObject("Result Label",typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(transform,false);
        rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=size;rect.anchoredPosition=position;
        var label=rect.gameObject.AddComponent<TextMeshProUGUI>();label.font=font;label.text=text;label.fontSize=fontSize;
        label.color=new Color(.90f,.86f,.97f);label.alignment=TextAlignmentOptions.Center;label.enableWordWrapping=true;label.raycastTarget=false;return label;
    }
}
