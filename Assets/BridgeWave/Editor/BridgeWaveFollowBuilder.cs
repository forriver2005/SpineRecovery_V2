using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor.Events;
using TMPro;

namespace BridgeWave.EditorTools
{
    public static class BridgeWaveFollowBuilder
    {
        public const string ScenePath = "Assets/Scenes/BridgeWave_StandardFollow.unity";
        public const string TrackPath = "Assets/BridgeWave/StandardHipThrustTrack.asset";
        static Font font;
        [MenuItem("Tools/Bridge Wave/Open Standard Follow Scene")]
        public static void OpenScene()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Tools/Bridge Wave/Build Standard Follow Scene")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first.");
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            BridgeWaveBuilder.ConfigureSprites();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var source = EditorSceneManager.OpenScene("Assets/Scenes/HipThrust_RingBridge_NewScoring.unity");
            var scorer = UnityEngine.Object.FindObjectOfType<DeadBugGamingPoseScorer>(true);
            var transport = UnityEngine.Object.FindObjectOfType<HipThrustTaikoController>(true);
            if(scorer==null||transport==null)throw new InvalidOperationException("Existing hip-thrust rig missing.");
            var patient = UnityEngine.Object.Instantiate(scorer.UserAnimator.gameObject);
            var sourceRoots = source.GetRootGameObjects();
            var startRoot = UnityEngine.Object.Instantiate(sourceRoots.Single(r => r.name == "StartMenuCanvas"));
            startRoot.name = "StartMenuCanvas";
            var previewRoot = UnityEngine.Object.Instantiate(sourceRoots.Single(r => r.name == "HipThrustPreview"));
            previewRoot.name = "HipThrustPreview";
            var countdownTemplate = UnityEngine.Object.Instantiate(sourceRoots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "CountDown").gameObject);
            countdownTemplate.transform.SetParent(null);
            var light = UnityEngine.Object.Instantiate(sourceRoots.Single(r => r.name == "Directional Light"));
            patient.transform.SetParent(null);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            foreach (var obj in new[] { patient, startRoot, previewRoot, countdownTemplate, light })
                SceneManager.MoveGameObjectToScene(obj, scene);
            EditorSceneManager.CloseScene(source,true); SceneManager.SetActiveScene(scene);
            patient.name="Patient input rig · hidden";
            foreach(var b in patient.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            foreach(var r in patient.GetComponentsInChildren<Renderer>(true))r.enabled=false;
            foreach(var b in previewRoot.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            foreach(var r in previewRoot.GetComponentsInChildren<Renderer>(true))r.enabled=true;
            foreach(var t in previewRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            patient.SetActive(true);previewRoot.SetActive(true);
            var track = BakeTrack();
            var patientAnimator=patient.GetComponent<Animator>();
            float standardHeightSpan=BakeHeightSpan(patientAnimator,track);
            patientAnimator.runtimeAnimatorController=null;
            patientAnimator.enabled=true;patientAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var cameraObject=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.95f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.045f,.13f);camera.transform.position=new Vector3(0,0,-10);
            camera.cullingMask = ~(1 << 31);
            var flowRoot=new GameObject("Bridge Wave Training Flow");
            var root=new GameObject("Bridge Wave · Standard Hip Thrust");
            BridgeWaveBuilder.BuildBackground(root.transform);
            var host=new GameObject("Track + geometric judgement");host.transform.SetParent(root.transform,false);
            var game=host.AddComponent<BridgeWaveGame>();game.standardTrack=track;game.controlledClock=true;
            game.totalCount=4;game.startCount=0;game.tolerance=.08f;game.speedPx=76;game.stepPx=1f;
            game.lineMaterial=BridgeWaveBuilder.LineMaterial();
            game.starSprite=Sprite("bw_item_star");game.noteSprite=Sprite("bw_item_note");
            BridgeWaveBuilder.BuildOrb(root.transform,game);BridgeWaveBuilder.BuildHud(root.transform,game);
            var input=flowRoot.AddComponent<BridgeWaveHeightInput>();input.patient=patientAnimator;input.referenceHeightSpan=standardHeightSpan;
            var controller=flowRoot.AddComponent<BridgeWaveFollowController>();controller.game=game;controller.input=input;
            controller.visualRoot=root;
            controller.skipCalibrationForFlowTest=false;
            var canvas=root.GetComponentInChildren<Canvas>();
            var viewport=cameraObject.AddComponent<BridgeWaveViewport>();viewport.view=camera;viewport.canvas=canvas;
            viewport.ConfigureCanvas();
            canvas.sortingOrder=100;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/SourceHanSans/SourceHanSansSC-Heavy.otf");
            Text Find(string name)=>canvas.GetComponentsInChildren<Text>().First(t=>t.name==name);
            Find("Count").text="0";Find("CountTotal").text="/ 4";
            controller.totalCountText=Find("CountTotal");
            UnityEngine.Object.DestroyImmediate(Find("Score").gameObject);
            UnityEngine.Object.DestroyImmediate(Find("Combo").gameObject);
            // The delivered pause artwork remains beneath the actual click target.
            controller.pauseButton=Button(canvas.transform,"Pause","Ⅱ",new Rect(18,8,44,44),out controller.pauseLabel);
            controller.pauseButton.GetComponent<Image>().color=Color.clear;
            controller.pauseLabel.color=Color.white;
            root.GetComponentsInChildren<SpriteRenderer>().First(r=>r.name=="bw_hud_pause").enabled=false;
            controller.pauseButton.GetComponent<Image>().color=new Color(.19f,.18f,.4f,.9f);
            controller.judgmentText=Text(canvas.transform,"Judgement","臀桥",new Rect(270,312,304,25),16,TextAnchor.MiddleCenter);
            controller.statusText=Text(canvas.transform,"Input status","未收到动作传感器信号，请连接设备",new Rect(160,365,524,18),9,TextAnchor.MiddleCenter);
            Text(canvas.transform,"Rep label","轨道次数",new Rect(380,4,84,10),7,TextAnchor.MiddleCenter).color=new Color(.8f,.8f,1,.75f);
            controller.menuButton=Button(canvas.transform,"Return setup","返回设置",new Rect(22,351,104,28),out _);
            ConfigureUnifiedMenu(startRoot, previewRoot, countdownTemplate, controller, canvas, camera);
            var finish=Panel(canvas.transform,"Result",new Rect(172,132,500,155),new Color(.06f,.06f,.18f,.97f));controller.finishPanel=finish;
            controller.resultText=Text(finish.transform,"Result text","",new Rect(12,10,476,92),15,TextAnchor.MiddleCenter);
            controller.replayButton=Button(finish.transform,"Replay","再来一组",new Rect(165,112,170,30),out _);
            finish.SetActive(false);
            root.SetActive(false);
            var workout=flowRoot.AddComponent<GameWorkoutScene>();
            workout.actionId="hipthrust_curve";workout.useRecordedCoachAnimations=true;
            workout.bridgeWave=controller;workout.menu=controller.startMenu;workout.gameCanvas=controller.startMenu.GetComponent<Canvas>();
            workout.font=controller.calibrationStatus.font;controller.workout=workout;
            new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            EditorSceneManager.SaveScene(scene,ScenePath);
            if(!EditorBuildSettings.scenes.Any(s=>s.path==ScenePath))
                EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(ScenePath,true)}).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("BRIDGE_WAVE_BUILD_OK: "+ScenePath);
        }
        static void ConfigureUnifiedMenu(GameObject startRoot, GameObject previewRoot, GameObject countdown,
            BridgeWaveFollowController controller, Canvas canvas, Camera camera)
        {
            var menu=startRoot.GetComponent<DeadBugStartMenuController>();
            controller.startMenu=menu;
            // Keep the original menu artwork, native buttons and difficulty selection controller.
            foreach(var b in startRoot.GetComponentsInChildren<MonoBehaviour>(true))
                if(!(b is UnityEngine.EventSystems.UIBehaviour) && !(b is DeadBugStartMenuController) && !(b is SceneSwitcher))
                    UnityEngine.Object.DestroyImmediate(b);
            var all=startRoot.GetComponentsInChildren<Transform>(true);
            Transform Named(string name)=>all.Single(t=>t.name==name);
            foreach(var button in startRoot.GetComponentsInChildren<Button>(true))button.onClick=new Button.ButtonClickedEvent();
            var menuCanvas=startRoot.GetComponent<Canvas>();
            menuCanvas.renderMode=RenderMode.ScreenSpaceOverlay;menuCanvas.worldCamera=null;
            startRoot.transform.localScale=Vector3.one;
            Set(menu,"gamingCanvasRoot",controller.visualRoot);
            Set(menu,"actionMenu",controller.visualRoot);
            Set(menu,"endMenuRoot",null);
            Set(menu,"poseScorer",null);
            Set(menu,"coachActionController",null);
            Set(menu,"countdownController",null);
            Set(menu,"rhythmSongClock",null);
            var serialized=new SerializedObject(menu);
            foreach(var name in new[]{"simpleTimes","standardTimes","hardTimes"})serialized.FindProperty(name).intValue=4;
            serialized.FindProperty("hideAllPracticeVisualsWhileMenuVisible").boolValue=false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Named("Title").GetComponent<TMP_Text>().text="臀桥";
            Named("Description").GetComponent<TMP_Text>().text="抬臀向上，放臀向下，让光球保持在轨道内";
            Named("Description").GetComponent<TMP_Text>().fontSize=20;
            Named("Logo").gameObject.SetActive(true);
            foreach(var name in new[]{"DBUI","DvsInfo","Break"})Named(name).gameObject.SetActive(false);
            controller.startButton=Named("StartTraining").GetComponent<Button>();
            controller.startLabel=controller.startButton.GetComponentInChildren<TMP_Text>(true);
            controller.startLabel.text="开始游戏";controller.startLabel.fontSize=24;
            var guide=Named("guide").GetComponent<Button>();
            guide.gameObject.SetActive(false);
            var back=Named("Return").GetComponent<Button>();
            var switcher=back.GetComponent<SceneSwitcher>()??back.gameObject.AddComponent<SceneSwitcher>();
            UnityEventTools.AddPersistentListener(back.onClick,switcher.ReturnToMobileApp);
            var buttonParent=controller.startButton.transform.parent;
            Button CalibrationButton(string name,string label,Vector2 position)
            {
                var clone=UnityEngine.Object.Instantiate(controller.startButton,buttonParent);
                clone.name=name;clone.onClick=new Button.ButtonClickedEvent();
                Layout((RectTransform)clone.transform,position,new Vector2(330,48));
                var text=clone.GetComponentInChildren<TMP_Text>(true);text.text=label;text.fontSize=25;
                return clone;
            }
            controller.calibrationButton=CalibrationButton("Supine calibration","躺姿校准",new Vector2(400,0));
            controller.calibrationLabel=controller.calibrationButton.GetComponentInChildren<TMP_Text>(true);
            var template=Named("Description").GetComponent<TMP_Text>();
            // This standalone game reports repetitions, not phone-authored set counts.
            var countCaption=new GameObject("Repetition caption",typeof(RectTransform),typeof(Image));
            countCaption.transform.SetParent(Named("Times"),false);
            countCaption.transform.SetAsFirstSibling();
            var repetitionValue=(TMP_Text)new SerializedObject(menu).FindProperty("timesText").objectReferenceValue;
            controller.repetitionsText=repetitionValue;
            Layout(repetitionValue.rectTransform,new Vector2(0,7),new Vector2(100,36));
            repetitionValue.fontSize=30;repetitionValue.alignment=TextAlignmentOptions.Center;repetitionValue.margin=Vector4.zero;
            Layout((RectTransform)countCaption.transform,new Vector2(0,-18),new Vector2(100,22));
            countCaption.GetComponent<Image>().color=Color.white;countCaption.GetComponent<Image>().raycastTarget=false;
            var repetitionsLabel=UnityEngine.Object.Instantiate(template,countCaption.transform);
            Layout(repetitionsLabel.rectTransform,Vector2.zero,new Vector2(100,22));
            repetitionsLabel.fontSize=12;repetitionsLabel.color=new Color(.54f,.63f,.72f);
            repetitionsLabel.text="轨道次数";

            controller.calibrationStatus=UnityEngine.Object.Instantiate(template,buttonParent);
            controller.calibrationStatus.name="Calibration status";
            Layout(controller.calibrationStatus.rectTransform,new Vector2(400,-163),new Vector2(600,70));
            controller.calibrationStatus.fontSize=23;
            controller.calibrationStatus.text="请连接动作传感器，再进行躺姿校准";
            controller.calibrationStatus.enableWordWrapping=true;
            controller.calibrationStatus.raycastTarget=false;
            controller.calibrationStatus.gameObject.SetActive(true);
            controller.calibrationButton.gameObject.SetActive(true);
            // Match the established BirdDog/DeadBug menu's left logo/title and right controls.
            Layout((RectTransform)Named("HipThrustTitlePanel"),new Vector2(-400,0),new Vector2(610,130));
            Layout(Named("Title").GetComponent<TMP_Text>().rectTransform,new Vector2(0,30),new Vector2(600,60));
            Named("Title").GetComponent<TMP_Text>().fontSize=38;
            Layout(Named("Description").GetComponent<TMP_Text>().rectTransform,new Vector2(0,-18),new Vector2(600,40));
            var coach=controller.gameObject.AddComponent<BridgeWaveCoachPreview>();controller.coachPreview=coach;coach.controller=controller;
            coach.animator=previewRoot.GetComponentInChildren<Animator>(true);
            coach.animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Movements/Recorded20261018/HipThrust_Recorded.controller");
            coach.animator.enabled=true;coach.animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            coach.previewCamera=previewRoot.GetComponentInChildren<Camera>(true);
            coach.previewCamera.enabled=true;coach.previewCamera.cullingMask=1<<31;
            coach.previewCamera.clearFlags=CameraClearFlags.SolidColor;coach.previewCamera.backgroundColor=Color.clear;
            coach.previewCamera.allowHDR=false;
            var framing=coach.previewCamera.GetComponent<HipThrustPreviewFraming>()??coach.previewCamera.gameObject.AddComponent<HipThrustPreviewFraming>();
            framing.enabled=true;framing.avatar=coach.animator;framing.previewCamera=coach.previewCamera;framing.fitAfterLatePose=true;framing.framingPadding=.35f;
            var panel=Panel(canvas.transform,"Recorded coach",new Rect(586,6,240,88),new Color(.055f,.055f,.16f,.85f));
            coach.trainingPanel=panel;
            panel.GetComponent<Image>().raycastTarget=false;
            coach.gameImage=new GameObject("Coach",typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();
            coach.gameImage.transform.SetParent(panel.transform,false);coach.gameImage.raycastTarget=false;
            Place(coach.gameImage.rectTransform,new Rect(10,20,220,64));
            coach.gamePhase=Text(panel.transform,"Coach phase","教练 · 放平准备",new Rect(6,2,228,18),11,TextAnchor.MiddleCenter);
            countdown.transform.SetParent(canvas.transform,false);
            countdown.name="CountDown";controller.countdownRoot=countdown;
            Place((RectTransform)countdown.transform,new Rect(362,145,120,90));
            controller.countdownText=countdown.GetComponent<TMP_Text>();controller.countdownText.enabled=true;
            controller.countdownText.fontSize=60;controller.countdownText.raycastTarget=false;
            countdown.SetActive(false);
        }
        static void Set(UnityEngine.Object target,string name,UnityEngine.Object value)
        {
            var serialized=new SerializedObject(target);serialized.FindProperty(name).objectReferenceValue=value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Layout(RectTransform rect,Vector2 position,Vector2 size)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;
            rect.anchoredPosition=position;rect.sizeDelta=size;rect.localScale=Vector3.one;
        }
        static float BakeHeightSpan(Animator patient,BridgeWaveTrack track)
        {
            var previous=patient.runtimeAnimatorController;
            patient.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Movements/Recorded20261018/HipThrust_Recorded.controller");
            patient.enabled=true;patient.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            patient.Rebind();patient.Update(0);
            var spans=new System.Collections.Generic.List<float>();
            float HeightAt(int frame)
            {
                patient.Play("hipthrust_routine",0,frame/track.source.sourceClip.frameRate/track.source.sourceClip.length);
                patient.Update(0);
                if(!BridgeWaveHeightInput.TryHeight(patient,out float height))throw new InvalidOperationException("Cannot sample patient reference height");
                return height;
            }
            try
            {
                foreach(var cycle in track.source.cycles)
                {
                    float low=HeightAt(cycle.liftStartFrame),high=low;
                    for(int frame=cycle.holdStartFrame;frame<=cycle.lowerStartFrame;frame++)high=Mathf.Max(high,HeightAt(frame));
                    if(high-low>=.05f)spans.Add(high-low);
                }
                if(spans.Count!=track.source.cycles.Length)throw new InvalidOperationException("Standard hip-thrust height excursion is invalid");
                spans.Sort();float span=(spans[(spans.Count-1)/2]+spans[spans.Count/2])*.5f;
                Debug.Log($"BRIDGE_WAVE_REFERENCE_SPAN: {span:0.000000} leg lengths; four recorded lifts on patient avatar.");
                return span;
            }
            finally {patient.runtimeAnimatorController=previous;patient.Rebind();patient.Update(0);}
        }
        static BridgeWaveTrack BakeTrack()
        {
            var source=AssetDatabase.LoadAssetAtPath<HipThrustPhaseTimeline>("Assets/RingBridge/RecordedHipThrustTimeline.asset");source.Validate();
            var track=AssetDatabase.LoadAssetAtPath<BridgeWaveTrack>(TrackPath);
            if(track==null){track=ScriptableObject.CreateInstance<BridgeWaveTrack>();AssetDatabase.CreateAsset(track,TrackPath);}
            track.source=source;track.leadInSeconds=1.5f;track.restSeconds=.6f;
            track.cycles=new BridgeWaveTrack.Cycle[source.cycles.Length];
            for(int i=0;i<track.cycles.Length;i++)
            {
                var c=source.cycles[i];float fps=source.sourceClip.frameRate;
                track.cycles[i]=new BridgeWaveTrack.Cycle {liftSeconds=(c.holdStartFrame-c.liftStartFrame)/fps,holdSeconds=(c.lowerStartFrame-c.holdStartFrame)/fps,
                    lowerSeconds=(c.lowerEndFrame-c.lowerStartFrame)/fps,lift=AnimationCurve.EaseInOut(0,0,1,1),lower=AnimationCurve.EaseInOut(0,1,1,0)};
            }
            EditorUtility.SetDirty(track);return track;
        }
        static Sprite Sprite(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>("Assets/BridgeWave/Sprites/"+name+".png");
        static void Place(RectTransform rect,Rect bounds)
        {
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(bounds.x,-bounds.y);rect.sizeDelta=bounds.size;
        }
        static GameObject Panel(Transform parent,string name,Rect bounds,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
            Place((RectTransform)go.transform,bounds);go.GetComponent<Image>().color=color;return go;
        }
        static Text Text(Transform parent,string name,string content,Rect bounds,int size,TextAnchor alignment)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);Place((RectTransform)go.transform,bounds);
            var t=go.GetComponent<Text>();t.font=font;t.text=content;t.fontSize=size;t.color=Color.white;t.alignment=alignment;
            t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Overflow;t.raycastTarget=false;return t;
        }
        static Button Button(Transform parent,string name,string label,Rect bounds,out Text text)
        {
            var go=Panel(parent,name,bounds,new Color(.23f,.21f,.44f,.98f));var button=go.AddComponent<Button>();button.targetGraphic=go.GetComponent<Image>();
            text=Text(go.transform,"Label",label,new Rect(0,0,bounds.width,bounds.height),11,TextAnchor.MiddleCenter);
            var colors=button.colors;colors.disabledColor=new Color(.45f,.45f,.5f,.5f);button.colors=colors;return button;
        }
    }
}
