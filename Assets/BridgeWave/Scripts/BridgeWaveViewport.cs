using UnityEngine;
using UnityEngine.UI;
namespace BridgeWave
{
    /// <summary>One world-space design canvas aligns UI and art at every aspect ratio.</summary>
    [ExecuteAlways]
    public sealed class BridgeWaveViewport : MonoBehaviour
    {
        public Camera view;
        public Canvas canvas;
        public void ConfigureCanvas()
        {
            if(canvas==null)return;
            canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=view;
            var rect=(RectTransform)canvas.transform;
            rect.sizeDelta=new Vector2(844,390);rect.pivot=new Vector2(.5f,.5f);
            rect.position=new Vector3(0,0,-5);rect.rotation=Quaternion.identity;rect.localScale=Vector3.one*.01f;
            var scaler=canvas.GetComponent<CanvasScaler>();
            if(scaler!=null){scaler.dynamicPixelsPerUnit=3;scaler.referencePixelsPerUnit=100;}
        }
        void LateUpdate()
        {
            if(view==null)return;
            float screenAspect=(float)Screen.width/Mathf.Max(1,Screen.height),design=844f/390;
            if(screenAspect>design) { float w=design/screenAspect; view.rect=new Rect((1-w)/2,0,w,1); }
            else { float h=screenAspect/design;view.rect=new Rect(0,(1-h)/2,1,h); }
            view.orthographicSize=1.95f;
            ConfigureCanvas();
        }
    }
}
