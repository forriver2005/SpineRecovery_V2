using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Connects the shared result panel to the existing end-canvas lifecycle.</summary>
public sealed class TrainingResultView : MonoBehaviour
{
    GameTrainingAssessment assessment;
    GameObject oldEnd;
    TrainingResultPanel panel;
    bool wasVisible,wasFinished;
    public GameObject Root => panel!=null?panel.gameObject:null;
    public TMP_Text ScoreLabel => panel!=null?panel.ScoreLabel:null;
    public void Initialize(GameTrainingAssessment source){assessment=source;}
    void Start()
    {
        foreach(var canvas in FindObjectsOfType<Canvas>(true))
            if(canvas.gameObject.scene==gameObject.scene&&canvas.name=="EndMenuCanvas")oldEnd=canvas.gameObject;
        if(oldEnd==null)return;
        foreach(var graphic in oldEnd.GetComponentsInChildren<Graphic>(true))graphic.enabled=false;
        panel=TrainingResultPanel.Create(assessment.Entry.font,()=>{oldEnd.SetActive(false);assessment.Entry.StopTraining();});
    }
    void LateUpdate()
    {
        if(panel==null)return;
        bool visible=oldEnd.activeInHierarchy&&!assessment.Entry.menu.IsTrainingMenuVisible;
        if(visible&&(!wasVisible||(!wasFinished&&assessment.IsFinished)))panel.Show(GameWorkoutPlan.ActionName(assessment.Entry.actionId),assessment.FinalScore);
        else if(!visible&&wasVisible)panel.Hide();
        wasVisible=visible;wasFinished=assessment.IsFinished;
    }
    public void Refresh()
    {
        if(panel!=null)panel.Refresh(GameWorkoutPlan.ActionName(assessment.Entry.actionId),assessment.FinalScore);
    }
    void OnDestroy(){if(panel!=null)Destroy(panel.gameObject);}
}
