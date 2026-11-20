using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TaikoNight
{
    // The card follows the artwork camera's projected slot, including letterboxing and resizing.
    // Only its RawImage displays the coach, just like the RingSkinC recorded coach window.
    public sealed class TaikoNightCoachCard : MonoBehaviour
    {
        static readonly Color[] Colors = { new Color(1f,.49f,.67f), new Color(1f,.84f,.39f), new Color(.73f,.61f,1f) };
        readonly Image[] badges = new Image[3];
        readonly TMP_Text[] badgeLabels = new TMP_Text[3];
        Camera artCamera, previewCamera;
        RenderTexture texture;
        Transform slot;
        RectTransform panel, fill, countdown;
        TMP_Text phase, timing, number;
        Canvas canvas;
        TMP_FontAsset font;
        string[] names;

        public void Initialize(Canvas hud, TaikoNightChart chart, Camera art, Animator avatar, TMP_FontAsset labelFont, string[] labels, Camera existingPreview = null)
        {
            canvas = hud;
            artCamera = art;
            font = labelFont;
            names = labels;
            slot = chart.transform.Find("CoachSlot/tn_coach_card");
            chart.transform.Find("CoachSlot/tn_coach_placeholder").gameObject.SetActive(false);
            chart.transform.Find("HUD").gameObject.SetActive(false); // static score and combo on the demo prefab
            chart.transform.Find("HUD_Sprites").gameObject.SetActive(false); // the game provides a real pause button
            Texture image;
            if (existingPreview != null) image = existingPreview.targetTexture;
            else
            {
                const int previewLayer = 28;
                foreach (var mesh in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    mesh.gameObject.layer = previewLayer;
                    mesh.updateWhenOffscreen = true;
                }
                var host = new GameObject("Recorded action preview camera");
                host.transform.SetParent(transform, false);
                previewCamera = host.AddComponent<Camera>();
                previewCamera.transform.rotation = Quaternion.identity;
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = Color.clear;
                previewCamera.cullingMask = 1 << previewLayer;
                previewCamera.orthographic = true;
                previewCamera.nearClipPlane = .01f;
                previewCamera.farClipPlane = 30f;
                previewCamera.allowHDR = false;
                texture = new RenderTexture(640, 400, 24, RenderTextureFormat.ARGB32) { name = "Taiko coach preview", antiAliasing = 2 };
                texture.Create();
                previewCamera.targetTexture = texture;
                var framing = host.AddComponent<HipThrustPreviewFraming>();
                framing.avatar = avatar;
                framing.previewCamera = previewCamera;
                framing.fitAfterLatePose = true;
                framing.framingPadding = .5f;
                image = texture;
            }
            panel = Box(hud.transform, "教练示范区", Color.clear, 0, 0, 164, 164);
            Label(panel, "教练示范", 0, 62, 145, 18, 13);
            var preview = new GameObject("教练模型", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            preview.transform.SetParent(panel, false);
            preview.texture = image;
            preview.raycastTarget = false;
            float aspect = (float)image.width / image.height;
            Layout(preview.rectTransform, 0, 12, Mathf.Min(148, 83 * aspect), Mathf.Min(83, 148 / aspect));
            for (int i = 0; i < names.Length; i++)
            {
                var badge = Box(panel, names[i], Colors[i], 48*(i-1), -39, 45, 15);
                badges[i] = badge.GetComponent<Image>();
                badgeLabels[i] = Label(badge, names[i], 0, 0, 45, 15, 10);
            }
            var track = Box(panel, "阶段进度", new Color(.20f,.21f,.36f), 0, -52, 140, 3);
            fill = Box(track, "进度", Color.white, 0, 0, 0, 0);
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            phase = Label(panel, "准备动作", 0, -68, 158, 19, 13);
            timing = Label(panel, "", 0, -94, 175, 18, 11);
            countdown = Box(hud.transform, "开场倒计时", new Color(.055f,.055f,.16f,.96f), 0, 0, 190, 115);
            Label(countdown, "准备开始", 0, 33, 175, 24, 16);
            number = Label(countdown, "", 0, -12, 175, 74, 57);
            Show(false, null, -1, 0, 0, false);
        }

        public void Show(bool visible, string count, int stage, float progress, float seconds, bool paused)
        {
            artCamera.orthographicSize = Mathf.Max(1.95f, 4.22f / artCamera.aspect);
            panel.gameObject.SetActive(visible);
            countdown.gameObject.SetActive(visible && !string.IsNullOrEmpty(count));
            if (previewCamera != null) previewCamera.enabled = visible;
            if (!visible) return;
            float scale = artCamera.pixelHeight * DesignSpace.PxToUnit / (2 * artCamera.orthographicSize * canvas.scaleFactor);
            Vector2 screen = artCamera.WorldToScreenPoint(slot.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, screen, null, out Vector2 position);
            panel.anchoredPosition = position;
            panel.localScale = countdown.localScale = Vector3.one * scale;
            number.text = count ?? "";
            phase.text = (count != null ? "准备开始" : stage < 0 ? "准备动作" : names[stage]) + (paused ? " · 已暂停" : "");
            phase.color = stage < 0 ? Color.white : Colors[stage];
            timing.text = paused ? "继续后跟随教练完成动作" : stage < 0 ? "跟随教练完成动作" : $"剩余 {seconds:0.0} 秒";
            for (int i = 0; i < badges.Length; i++)
            {
                badges[i].color = i == stage ? Colors[i] : new Color(.20f,.21f,.36f);
                badgeLabels[i].color = i == stage ? new Color(.08f,.07f,.18f) : Color.white;
            }
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
            fill.GetComponent<Image>().color = phase.color;
        }

        static void Layout(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = new Vector2(x,y); rect.sizeDelta = new Vector2(width,height);
        }
        static RectTransform Box(Transform parent, string name, Color color, float x, float y, float w, float h)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent,false); image.raycastTarget = false; image.color = color;
            Layout(image.rectTransform,x,y,w,h); return image.rectTransform;
        }
        TMP_Text Label(Transform parent, string value, float x, float y, float w, float h, float size)
        {
            var label = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            label.transform.SetParent(parent,false); label.font = font; label.text = value; label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            Layout(label.rectTransform,x,y,w,h); return label;
        }
        void OnDestroy()
        {
            if (texture == null) return;
            if (previewCamera != null) previewCamera.targetTexture = null;
            texture.Release(); Destroy(texture);
        }
    }
}
