using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    /// <summary>
    /// Building blocks for the redesigned questionnaire screens. Everything is
    /// laid out in "design units": the 390pt-wide mobile mockups exported from
    /// Figma. <see cref="QuestionnaireDesignRoot"/> maps those units onto the
    /// canvas, so coordinates here can be read straight off the mockups.
    /// </summary>
    internal static class QuestionnaireUi
    {
        public const float DesignWidth = 390f;
        public const float HeaderHeight = 56f;

        /// <summary>Pain at or above this score stops the training day / session.</summary>
        public const int PainStopThreshold = 3;

        // Text is rasterised this many times larger and scaled back down.
        // The design root is scaled up to the canvas, and Unity UI does not
        // account for parent scale when it builds glyphs, so without this
        // every label would be a blurry upscaled bitmap.
        private const float TextOversample = 3f;
        private const float RoundedTextureRadius = 24f;

        public static readonly Color PageBackground = new Color(0.961f, 0.973f, 1f);
        public static readonly Color HeaderBackground = new Color(0.976f, 0.984f, 1f);
        public static readonly Color Accent = new Color(0.290f, 0.525f, 0.851f);
        public static readonly Color Primary = new Color(0.22f, 0.74f, 0.97f);
        public static readonly Color Ink = new Color(0.10f, 0.12f, 0.18f);
        public static readonly Color Muted = new Color(0.42f, 0.46f, 0.54f);
        public static readonly Color OptionBackground = new Color(0.937f, 0.957f, 0.992f);
        public static readonly Color SecondaryButton = new Color(0.906f, 0.941f, 0.988f);
        public static readonly Color Divider = new Color(0.90f, 0.92f, 0.96f);
        public static readonly Color DangerFill = new Color(1f, 0.937f, 0.933f);
        public static readonly Color DangerBorder = new Color(0.957f, 0.710f, 0.710f);
        public static readonly Color DangerText = new Color(0.69f, 0.10f, 0.10f);
        public static readonly Color BadgeFill = new Color(1f, 0.941f, 0.878f);
        public static readonly Color BadgeText = new Color(0.55f, 0.29f, 0f);

        private static Sprite solidSprite;
        private static readonly Dictionary<int, Sprite> ringSprites = new Dictionary<int, Sprite>();

        // ---- captions shared by both questionnaires -------------------------

        public static string IntensityCaption(int value)
        {
            if (value <= 0) return "休息";
            if (value == 1) return "非常非常轻松";
            if (value == 2) return "轻松";
            if (value == 3) return "适中";
            if (value == 4) return "有点吃力";
            if (value <= 6) return "吃力";
            if (value <= 9) return "非常吃力";
            return "最大";
        }

        public static string PainCaption(int value, bool shortForm)
        {
            if (value <= 0) return "无痛";
            if (value <= 3) return "轻度疼痛";
            if (value <= 6) return "中度疼痛";
            if (value <= 9) return "重度疼痛";
            return shortForm ? "最剧烈疼痛" : "能想象的最剧烈疼痛";
        }

        // ---- sprites ---------------------------------------------------------

        /// <summary>
        /// 9-sliced rounded rectangle (thickness 0) or hollow ring. The corner
        /// radius is fixed in texels; <see cref="Rounded"/> / <see cref="Ring"/>
        /// rescale it to the requested design-unit radius.
        /// </summary>
        private static Sprite CreateSprite(float thicknessTexels)
        {
            const int size = 64;
            const float r = RoundedTextureRadius;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float cx = Mathf.Clamp(px, r, size - r);
                    float cy = Mathf.Clamp(py, r, size - r);
                    float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float alpha = Mathf.Clamp01(r - dist + 0.5f);
                    if (thicknessTexels > 0f) alpha *= Mathf.Clamp01(dist - (r - thicknessTexels) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        private static Sprite SolidSprite()
        {
            if (solidSprite == null) solidSprite = CreateSprite(0f);
            return solidSprite;
        }

        private static Sprite RingSprite(float thicknessTexels)
        {
            int key = Mathf.RoundToInt(thicknessTexels * 10f);
            if (!ringSprites.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                sprite = CreateSprite(thicknessTexels);
                ringSprites[key] = sprite;
            }
            return sprite;
        }

        // ---- primitives ------------------------------------------------------

        private static Image NewImage(Transform parent, string name, Sprite sprite, Color color, float multiplier)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = multiplier;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Image Rounded(Transform parent, string name, Color color, float radius)
        {
            return NewImage(parent, name, SolidSprite(), color, RoundedTextureRadius / Mathf.Max(radius, 0.5f));
        }

        public static Image Ring(Transform parent, string name, Color color, float radius, float thickness)
        {
            float safeRadius = Mathf.Max(radius, 0.5f);
            float texels = Mathf.Clamp(RoundedTextureRadius * thickness / safeRadius, 1f, RoundedTextureRadius - 1f);
            return NewImage(parent, name, RingSprite(texels), color, RoundedTextureRadius / safeRadius);
        }

        /// <summary>Top-left anchored placement in the parent's design units.</summary>
        public static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static Image Box(Transform parent, string name, Color color, float radius,
            float x, float y, float width, float height)
        {
            Image image = Rounded(parent, name, color, radius);
            Place(image.rectTransform, x, y, width, height);
            return image;
        }

        public static Text Label(Transform parent, string value, float size, Color color, TextAnchor anchor,
            FontStyle style, float x, float y, float width, float height)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            Place(rect, x, y, width * TextOversample, height * TextOversample);
            rect.localScale = new Vector3(1f / TextOversample, 1f / TextOversample, 1f);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = Mathf.RoundToInt(size * TextOversample);
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.text = value;
            text.lineSpacing = 1.1f;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        public static Button MakeButton(Transform parent, string name, Color fill, float radius,
            float x, float y, float width, float height, UnityAction onClick)
        {
            Image image = Box(parent, name, fill, radius, x, y, width, height);
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.84f, 0.88f, 0.95f, 1f);
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        public static Button PillButton(Transform parent, string caption, float x, float y, float width, float height,
            bool primary, UnityAction onClick)
        {
            Button button = MakeButton(parent, caption, primary ? Primary : SecondaryButton, height * 0.5f,
                x, y, width, height, onClick);
            Label(button.transform, caption, 17, primary ? Color.white : Accent, TextAnchor.MiddleCenter,
                FontStyle.Bold, 0f, 0f, width, height);
            return button;
        }

        public static void SetChoice(Button button, bool selected, Color selectedFill, Color idleFill,
            Color selectedText, Color idleText)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image;
            if (image != null) image.color = selected ? selectedFill : idleFill;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = selected ? selectedText : idleText;
        }

        // ---- screen scaffolding ---------------------------------------------

        /// <summary>
        /// Creates a full-canvas panel plus the design-space root every child
        /// is placed in. <paramref name="onEnabled"/> runs each time the router
        /// shows the panel, which is where a screen resets its state.
        /// </summary>
        public static RectTransform CreateScreen(Transform canvasParent, string name, Action onEnabled,
            out GameObject panel)
        {
            panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(QuestionnaireScreenRelay));
            panel.transform.SetParent(canvasParent, false);
            Stretch((RectTransform)panel.transform, 0f, 0f, 0f, 0f);
            Image background = panel.GetComponent<Image>();
            background.color = PageBackground;
            background.raycastTarget = false;
            panel.GetComponent<QuestionnaireScreenRelay>().Enabled = onEnabled;

            GameObject rootObject = new GameObject("DesignRoot", typeof(RectTransform), typeof(QuestionnaireDesignRoot));
            rootObject.transform.SetParent(panel.transform, false);
            RectTransform root = (RectTransform)rootObject.transform;
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            return root;
        }

        public static void Header(Transform root, string title, UnityAction onBack)
        {
            Image bar = Box(root, "Header", HeaderBackground, 0.5f, 0f, 0f, DesignWidth, HeaderHeight);
            bar.raycastTarget = true;
            Box(bar.transform, "HeaderLine", Divider, 0.5f, 0f, HeaderHeight - 1f, DesignWidth, 1f);
            Label(bar.transform, title, 17, Accent, TextAnchor.MiddleCenter, FontStyle.Bold, 56f, 0f, 278f, HeaderHeight);
            if (onBack == null) return;

            Button back = MakeButton(bar.transform, "Back", Color.clear, 0.5f, 6f, 6f, 44f, 44f, onBack);
            Color arrow = new Color(0.22f, 0.25f, 0.31f);
            Box(back.transform, "Shaft", arrow, 1f, 14f, 21f, 17f, 2f);
            for (int i = 0; i < 2; i++)
            {
                Image arm = Box(back.transform, i == 0 ? "ArmUp" : "ArmDown", arrow, 1f, 14f, 22f, 10f, 2f);
                arm.rectTransform.pivot = new Vector2(0f, 0.5f);
                arm.rectTransform.anchoredPosition = new Vector2(14f, -22f);
                arm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
            }
        }

        public static RectTransform ScrollArea(Transform root, float top, float bottom, float contentHeight)
        {
            GameObject go = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            go.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)go.transform;
            Stretch(rect, 0f, top, 0f, bottom);
            Image hit = go.GetComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            GameObject contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(go.transform, false);
            RectTransform content = (RectTransform)contentObject.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, contentHeight);

            ScrollRect scroll = go.GetComponent<ScrollRect>();
            scroll.viewport = rect;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.inertia = true;
            scroll.scrollSensitivity = 30f;
            return content;
        }

        public static RectTransform Footer(Transform root, float height)
        {
            GameObject go = new GameObject("Footer", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
            Image image = go.GetComponent<Image>();
            image.color = PageBackground;
            image.raycastTarget = true;
            return rect;
        }

        public static RectTransform Card(Transform parent, float x, float y, float width, float height)
        {
            Box(parent, "CardShadow", new Color(0.30f, 0.45f, 0.80f, 0.08f), 20f, x - 1f, y + 1f, width + 2f, height + 4f);
            return Box(parent, "Card", Color.white, 18f, x, y, width, height).rectTransform;
        }

        public static void WarningBox(Transform parent, string message, float x, float y, float width, float height)
        {
            Image border = Box(parent, "Warning", DangerBorder, 14f, x, y, width, height);
            Box(border.transform, "Fill", DangerFill, 13f, 1f, 1f, width - 2f, height - 2f);

            GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(WarningTriangleGraphic));
            icon.transform.SetParent(border.transform, false);
            Place((RectTransform)icon.transform, 16f, height * 0.5f - 9f, 20f, 18f);
            WarningTriangleGraphic triangle = icon.GetComponent<WarningTriangleGraphic>();
            triangle.color = DangerText;
            triangle.raycastTarget = false;
            Label(border.transform, "!", 11, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold,
                16f, height * 0.5f - 6f, 20f, 16f);

            Label(border.transform, message, 14.5f, DangerText, TextAnchor.MiddleLeft, FontStyle.Normal,
                46f, 0f, width - 60f, height);
        }

        /// <summary>0–10 slider with a round white thumb, as in the pre-training mockup.</summary>
        public static Slider PainSlider(Transform parent, float x, float y, float width, UnityAction<float> onChanged)
        {
            const float thumb = 28f;
            const float height = 36f;
            const float trackHeight = 5f;
            float trackY = (height - trackHeight) * 0.5f;

            GameObject go = new GameObject("PainSlider", typeof(RectTransform), typeof(Image), typeof(Slider));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, x, y, width, height);
            Image hit = go.GetComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            Box(go.transform, "Track", new Color(0.88f, 0.90f, 0.93f), trackHeight * 0.5f,
                thumb * 0.5f, trackY, width - thumb, trackHeight);

            GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            Place((RectTransform)fillArea.transform, thumb * 0.5f, trackY, width - thumb, trackHeight);
            Image fill = Rounded(fillArea.transform, "Fill", Accent, trackHeight * 0.5f);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            GameObject slideArea = new GameObject("HandleSlideArea", typeof(RectTransform));
            slideArea.transform.SetParent(go.transform, false);
            Place((RectTransform)slideArea.transform, thumb * 0.5f, 0f, width - thumb, height);
            Image handle = Rounded(slideArea.transform, "Handle", Accent, thumb * 0.5f);
            RectTransform handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(thumb, thumb);
            handleRect.anchoredPosition = Vector2.zero;
            Box(handle.transform, "HandleInner", Color.white, 10f, 4f, 4f, thumb - 8f, thumb - 8f);

            Slider slider = go.GetComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.targetGraphic = handle;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 10f;
            slider.wholeNumbers = true;
            slider.value = 0f;
            if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
            return slider;
        }
    }

    /// <summary>Runs a callback every time the router activates the panel.</summary>
    internal sealed class QuestionnaireScreenRelay : MonoBehaviour
    {
        public Action Enabled;

        private void OnEnable()
        {
            if (Enabled != null) Enabled();
        }
    }

    /// <summary>
    /// Fits a 390-unit-wide design space to the parent panel: the width is
    /// scaled to the canvas width and the height follows the screen aspect, so
    /// header and footer stay pinned on any phone ratio.
    /// </summary>
    internal sealed class QuestionnaireDesignRoot : MonoBehaviour
    {
        private Vector2 lastParentSize;

        private void OnEnable()
        {
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            RectTransform parent = transform.parent as RectTransform;
            if (parent == null) return;
            Vector2 size = parent.rect.size;
            if (size.x < 1f || size == lastParentSize) return;
            lastParentSize = size;
            float scale = size.x / QuestionnaireUi.DesignWidth;
            RectTransform rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(QuestionnaireUi.DesignWidth, size.y / scale);
            rect.localScale = new Vector3(scale, scale, 1f);
        }
    }

    internal sealed class WarningTriangleGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = GetPixelAdjustedRect();
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = new Vector2(rect.center.x, rect.yMax);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector2(rect.xMax, rect.yMin);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector2(rect.xMin, rect.yMin);
            vertexHelper.AddVert(vertex);
            vertexHelper.AddTriangle(0, 1, 2);
        }
    }

    /// <summary>The answers collected by the pre-training questionnaire.</summary>
    internal sealed class PreTrainingAnswers
    {
        public int Pain;
        public string[] Regions = Array.Empty<string>();
        public string Leg = "没有";

        /// <summary>Region names plus, when the leg hurts too, a "腿部：左腿" style entry.</summary>
        public string[] ToPainParts()
        {
            List<string> parts = new List<string>(Regions ?? Array.Empty<string>());
            if (!string.IsNullOrEmpty(Leg) && Leg != "没有") parts.Add("腿部：" + Leg);
            return parts.ToArray();
        }
    }

    /// <summary>Pre-training questionnaire ("准备好了吗？": body map, leg symptoms, 0–10 pain).</summary>
    internal sealed class PreTrainingScreen
    {
        private sealed class RegionShape
        {
            public int Region;
            public Image Fill;
            public Image Ring;
        }

        private const float FooterHeight = 100f;
        private const float ContentHeight = 1230f;
        // The body map was measured off a 557px-wide preview of the mockup;
        // this converts those px into design units.
        private const float MapScale = 0.7f;
        private const float MapCenterX = 278f;
        private const float MapTop = 325f;

        private static readonly string[] RegionNames =
            { "脊柱正中", "脊柱两旁", "腰椎旁点状", "腰部两侧", "腰背外侧", "臀部上方" };
        private static readonly string[] LegNames = { "没有", "左腿", "右腿", "两条腿" };

        // region index, centre x, centre y, width, height (preview px)
        private static readonly float[][] ShapeSpecs =
        {
            new float[] { 4, 191f, 520.5f, 78f, 301f }, new float[] { 4, 365f, 520.5f, 78f, 301f },
            new float[] { 0, 278f, 553f, 28f, 280f },
            new float[] { 1, 241f, 517.5f, 36f, 351f }, new float[] { 1, 315f, 517.5f, 36f, 351f },
            new float[] { 3, 198f, 618f, 46f, 110f }, new float[] { 3, 358f, 618f, 46f, 110f },
            new float[] { 2, 241f, 618f, 30f, 30f }, new float[] { 2, 315f, 618f, 30f, 30f },
            new float[] { 5, 227f, 707f, 59f, 40f }, new float[] { 5, 330f, 707f, 59f, 40f },
        };

        // region index, centre x, centre y of the number printed on the map
        private static readonly float[][] NumberSpecs =
        {
            new float[] { 0, 278f, 562f },
            new float[] { 1, 241f, 519f }, new float[] { 1, 315f, 519f },
            new float[] { 2, 241f, 618f }, new float[] { 2, 315f, 618f },
            new float[] { 3, 198f, 618f }, new float[] { 3, 358f, 618f },
            new float[] { 4, 191f, 512f }, new float[] { 4, 365f, 512f },
            new float[] { 5, 227f, 707f }, new float[] { 5, 330f, 707f },
        };

        private static readonly Color IdleRing = new Color(0.58f, 0.67f, 0.82f, 0.95f);
        private static readonly Color SelectedRing = new Color(0.25f, 0.45f, 0.76f, 1f);
        private static readonly Color SelectedFill = new Color(0.77f, 0.85f, 0.96f, 1f);

        private readonly bool[] regionSelected = new bool[6];
        private readonly Button[] regionButtons = new Button[6];
        private readonly Button[] legButtons = new Button[4];
        private readonly Text[] tickLabels = new Text[11];
        private readonly List<RegionShape> shapes = new List<RegionShape>();

        private Action<PreTrainingAnswers> onSubmit;
        private Func<string> startCaption;
        private RectTransform contentRect;
        private Slider slider;
        private Text painValue;
        private Text painCaption;
        private Text message;
        private Text startLabel;
        private int legIndex;

        public GameObject Build(Transform canvasParent, UnityAction onBack, Action<PreTrainingAnswers> submit,
            Func<string> startButtonCaption)
        {
            onSubmit = submit;
            startCaption = startButtonCaption;

            RectTransform root = QuestionnaireUi.CreateScreen(canvasParent, "PreTrainingPanel", Reset, out GameObject panel);
            contentRect = QuestionnaireUi.ScrollArea(root, QuestionnaireUi.HeaderHeight, FooterHeight, ContentHeight);
            BuildContent(contentRect);

            RectTransform footer = QuestionnaireUi.Footer(root, FooterHeight);
            message = QuestionnaireUi.Label(footer, "", 12.5f, QuestionnaireUi.DangerText, TextAnchor.MiddleCenter,
                FontStyle.Normal, 20f, 4f, 350f, 16f);
            Button start = QuestionnaireUi.PillButton(footer, "开始训练", 20f, 24f, 350f, 52f, true, HandleStart);
            startLabel = start.GetComponentInChildren<Text>();

            QuestionnaireUi.Header(root, "腰椎康复系统", onBack);
            Reset();
            return panel;
        }

        /// <summary>Clears every answer; called each time the panel is shown.</summary>
        public void Reset()
        {
            for (int i = 0; i < regionSelected.Length; i++) regionSelected[i] = false;
            legIndex = 0;
            if (slider != null) slider.SetValueWithoutNotify(0f);
            if (message != null) message.text = "";
            if (startLabel != null)
            {
                string caption = startCaption != null ? startCaption() : null;
                startLabel.text = string.IsNullOrEmpty(caption) ? "开始训练" : caption;
            }
            if (contentRect != null) contentRect.anchoredPosition = Vector2.zero;
            RefreshRegions();
            RefreshLeg();
            RefreshPain(0);
        }

        private void BuildContent(RectTransform content)
        {
            QuestionnaireUi.Label(content, "准备好了吗？", 25, QuestionnaireUi.Accent, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 22f, 350f, 36f);
            QuestionnaireUi.Label(content, "开始前确认今天的状态", 14, QuestionnaireUi.Muted, TextAnchor.MiddleLeft,
                FontStyle.Normal, 20f, 62f, 350f, 20f);

            RectTransform card = QuestionnaireUi.Card(content, 20f, 107f, 350f, 965f);
            QuestionnaireUi.Label(card, "现在腰哪里疼？有多疼？", 16, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 22f, 310f, 24f);

            GameObject mapObject = new GameObject("BodyMap", typeof(RectTransform));
            mapObject.transform.SetParent(card, false);
            RectTransform map = QuestionnaireUi.Place((RectTransform)mapObject.transform, 20f, 64f, 309f, 299f);
            BuildBodyMap(map);

            for (int i = 0; i < RegionNames.Length; i++)
            {
                int region = i;
                float x = 20f + (i % 2) * 158.5f;
                float y = 381f + (i / 2) * 52f;
                Button button = QuestionnaireUi.MakeButton(card, "Region" + (i + 1), QuestionnaireUi.OptionBackground,
                    14f, x, y, 150.5f, 43f, () => ToggleRegion(region));
                QuestionnaireUi.Label(button.transform, (i + 1) + " " + RegionNames[i], 14, QuestionnaireUi.Ink,
                    TextAnchor.MiddleLeft, FontStyle.Bold, 14f, 0f, 130f, 43f);
                regionButtons[i] = button;
            }
            QuestionnaireUi.Label(card, "可多选 · 背面视角 · 左右两侧编号相同的算同一个部位", 12, QuestionnaireUi.Muted,
                TextAnchor.MiddleLeft, FontStyle.Normal, 20f, 538f, 310f, 16f);

            QuestionnaireUi.Label(card, "腿也疼或发麻吗？", 15, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 569f, 310f, 22f);
            QuestionnaireUi.Box(card, "LegBackground", new Color(0.93f, 0.95f, 0.99f), 15f, 20f, 601f, 309f, 46f);
            float segment = (309f - 8f) / LegNames.Length;
            for (int i = 0; i < LegNames.Length; i++)
            {
                int leg = i;
                Button button = QuestionnaireUi.MakeButton(card, "Leg" + i, Color.clear, 12f,
                    24f + i * segment, 605f, segment, 38f, () => SelectLeg(leg));
                QuestionnaireUi.Label(button.transform, LegNames[i], 14.5f, QuestionnaireUi.Ink,
                    TextAnchor.MiddleCenter, FontStyle.Bold, 0f, 0f, segment, 38f);
                legButtons[i] = button;
            }

            QuestionnaireUi.Box(card, "Divider", QuestionnaireUi.Divider, 0.5f, 20f, 665f, 309f, 1f);
            painValue = QuestionnaireUi.Label(card, "0", 42, QuestionnaireUi.Accent, TextAnchor.MiddleCenter,
                FontStyle.Bold, 20f, 679f, 309f, 50f);
            painCaption = QuestionnaireUi.Label(card, "无痛", 14, QuestionnaireUi.Muted, TextAnchor.MiddleCenter,
                FontStyle.Normal, 20f, 729f, 309f, 20f);

            slider = QuestionnaireUi.PainSlider(card, 20f, 767f, 309f, value => RefreshPain(Mathf.RoundToInt(value)));
            for (int i = 0; i < tickLabels.Length; i++)
            {
                float centre = 20f + 14f + i * (309f - 28f) / 10f;
                tickLabels[i] = QuestionnaireUi.Label(card, i.ToString(), 13, QuestionnaireUi.Muted,
                    TextAnchor.MiddleCenter, FontStyle.Normal, centre - 12f, 815f, 24f, 18f);
            }
            QuestionnaireUi.Label(card, "0 无痛", 12.5f, QuestionnaireUi.Muted, TextAnchor.MiddleLeft,
                FontStyle.Normal, 20f, 848f, 150f, 16f);
            QuestionnaireUi.Label(card, "10 能想象的最剧烈疼痛", 12.5f, QuestionnaireUi.Muted, TextAnchor.MiddleRight,
                FontStyle.Normal, 179f, 848f, 150f, 16f);

            Image legend = QuestionnaireUi.Box(card, "Legend", QuestionnaireUi.OptionBackground, 14f, 20f, 882f, 309f, 60f);
            QuestionnaireUi.Label(legend.transform,
                "0 无痛 • 1–3 轻度疼痛 • 4–6 中度疼痛 • 7–9 重度疼痛 • 10 能想象的最剧烈疼痛", 12.5f,
                QuestionnaireUi.Muted, TextAnchor.MiddleLeft, FontStyle.Normal, 14f, 0f, 281f, 60f);

            QuestionnaireUi.WarningBox(content,
                "疼痛达到 " + QuestionnaireUi.PainStopThreshold + " 分及以上时，今天不进行训练。如果出现大小便异常、会阴部发麻或腿突然没力，请立即就医。",
                20f, 1093f, 350f, 97f);
        }

        private void BuildBodyMap(RectTransform map)
        {
            float half = map.sizeDelta.x * 0.5f;
            QuestionnaireUi.Box(map, "Torso", new Color(0.925f, 0.949f, 0.992f), 16f, half - 97f, 0f, 194f, 299f);
            Image torsoOutline = QuestionnaireUi.Ring(map, "TorsoOutline", new Color(0.40f, 0.55f, 0.80f, 0.9f), 16f, 1.4f);
            QuestionnaireUi.Place(torsoOutline.rectTransform, half - 97f, 0f, 194f, 299f);
            QuestionnaireUi.Box(map, "CenterGuide", new Color(0.62f, 0.70f, 0.83f, 0.9f), 0.5f, half - 0.5f, 0f, 1f, 299f);

            QuestionnaireUi.Label(map, "左", 14, QuestionnaireUi.Muted, TextAnchor.MiddleCenter, FontStyle.Normal,
                0f, 10f, 24f, 20f);
            QuestionnaireUi.Label(map, "右", 14, QuestionnaireUi.Muted, TextAnchor.MiddleCenter, FontStyle.Normal,
                285f, 10f, 24f, 20f);

            foreach (float[] spec in ShapeSpecs)
            {
                float width = spec[3] * MapScale;
                float height = spec[4] * MapScale;
                float x = half + (spec[1] - MapCenterX) * MapScale - width * 0.5f;
                float y = (spec[2] - MapTop) * MapScale - height * 0.5f;
                float radius = Mathf.Min(width, height) * 0.5f;
                RegionShape shape = new RegionShape
                {
                    Region = (int)spec[0],
                    Fill = QuestionnaireUi.Box(map, "RegionFill", Color.clear, radius, x, y, width, height),
                };
                shape.Ring = QuestionnaireUi.Ring(map, "RegionRing", IdleRing, radius, 1.4f);
                QuestionnaireUi.Place(shape.Ring.rectTransform, x, y, width, height);
                shapes.Add(shape);
            }

            foreach (float[] spec in NumberSpecs)
            {
                float x = half + (spec[1] - MapCenterX) * MapScale - 10f;
                float y = (spec[2] - MapTop) * MapScale - 9f;
                QuestionnaireUi.Label(map, ((int)spec[0] + 1).ToString(), 12, new Color(0.28f, 0.40f, 0.66f),
                    TextAnchor.MiddleCenter, FontStyle.Bold, x, y, 20f, 18f);
            }
        }

        private void ToggleRegion(int region)
        {
            regionSelected[region] = !regionSelected[region];
            if (message != null) message.text = "";
            RefreshRegions();
        }

        private void SelectLeg(int index)
        {
            legIndex = index;
            RefreshLeg();
        }

        private void RefreshRegions()
        {
            for (int i = 0; i < regionButtons.Length; i++)
            {
                QuestionnaireUi.SetChoice(regionButtons[i], regionSelected[i], QuestionnaireUi.Accent,
                    QuestionnaireUi.OptionBackground, Color.white, QuestionnaireUi.Ink);
            }
            foreach (RegionShape shape in shapes)
            {
                bool selected = regionSelected[shape.Region];
                shape.Fill.color = selected ? SelectedFill : Color.clear;
                shape.Ring.color = selected ? SelectedRing : IdleRing;
            }
        }

        private void RefreshLeg()
        {
            for (int i = 0; i < legButtons.Length; i++)
            {
                QuestionnaireUi.SetChoice(legButtons[i], i == legIndex, Color.white, Color.clear,
                    QuestionnaireUi.Accent, new Color(0.30f, 0.34f, 0.42f));
            }
        }

        private void RefreshPain(int value)
        {
            if (painValue != null) painValue.text = value.ToString();
            if (painCaption != null) painCaption.text = QuestionnaireUi.PainCaption(value, false);
            for (int i = 0; i < tickLabels.Length; i++)
            {
                if (tickLabels[i] == null) continue;
                bool current = i == value;
                tickLabels[i].color = current ? QuestionnaireUi.Accent : QuestionnaireUi.Muted;
                tickLabels[i].fontStyle = current ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private void HandleStart()
        {
            int pain = Mathf.RoundToInt(slider.value);
            List<string> regions = new List<string>();
            for (int i = 0; i < regionSelected.Length; i++)
            {
                if (regionSelected[i]) regions.Add(RegionNames[i]);
            }
            if (pain > 0 && regions.Count == 0)
            {
                message.text = "请先选择疼痛部位。";
                return;
            }
            message.text = "";
            if (onSubmit != null)
            {
                onSubmit(new PreTrainingAnswers { Pain = pain, Regions = regions.ToArray(), Leg = LegNames[legIndex] });
            }
        }
    }

    /// <summary>
    /// Post-action questionnaire ("[动作名称] 完成"): exertion 0–10 and pain 0–10.
    /// Shown once the AR scene hands control back to the app shell.
    /// </summary>
    internal sealed class PostActionScreen
    {
        private const float FooterHeight = 110f;
        private const float ContentHeight = 806f;

        private readonly Button[] intensityButtons = new Button[11];
        private readonly Button[] painButtons = new Button[11];

        private Action<int, int> onSubmit;
        private RectTransform contentRect;
        private Text chipLabel;
        private Text titleLabel;
        private Text intensityValue;
        private Text painValue;
        private Text notice;
        private Text nextLabel;
        private int intensity = -1;
        private int pain = -1;

        public GameObject Build(Transform canvasParent, UnityAction onBack, Action<int, int> submit)
        {
            onSubmit = submit;
            RectTransform root = QuestionnaireUi.CreateScreen(canvasParent, "PostTrainingPanel", null, out GameObject panel);
            contentRect = QuestionnaireUi.ScrollArea(root, QuestionnaireUi.HeaderHeight, FooterHeight, ContentHeight);
            BuildContent(contentRect);

            RectTransform footer = QuestionnaireUi.Footer(root, FooterHeight);
            notice = QuestionnaireUi.Label(footer, "", 13, QuestionnaireUi.Muted, TextAnchor.MiddleCenter,
                FontStyle.Normal, 20f, 8f, 350f, 20f);
            Button next = QuestionnaireUi.PillButton(footer, "完成训练", 20f, 38f, 350f, 52f, true, HandleNext);
            nextLabel = next.GetComponentInChildren<Text>();

            QuestionnaireUi.Header(root, "腰椎康复系统", onBack);
            return panel;
        }

        /// <summary>Starts a fresh questionnaire for action <paramref name="actionNumber"/> of <paramref name="actionCount"/>.</summary>
        public void Show(int actionNumber, int actionCount, string actionName)
        {
            intensity = -1;
            pain = -1;
            chipLabel.text = "动作 " + actionNumber + "/" + actionCount;
            titleLabel.text = actionName + " 完成";
            nextLabel.text = actionNumber < actionCount ? "下一个动作" : "完成训练";
            contentRect.anchoredPosition = Vector2.zero;
            RefreshNotice();
            Refresh();
        }

        private void BuildContent(RectTransform content)
        {
            Image chip = QuestionnaireUi.Box(content, "ActionChip", new Color(0.89f, 0.93f, 0.99f), 13f, 20f, 25f, 84f, 27f);
            chipLabel = QuestionnaireUi.Label(chip.transform, "", 13, QuestionnaireUi.Accent, TextAnchor.MiddleCenter,
                FontStyle.Bold, 0f, 0f, 84f, 27f);
            titleLabel = QuestionnaireUi.Label(content, "", 26, QuestionnaireUi.Accent, TextAnchor.MiddleLeft,
                FontStyle.Bold, 20f, 58f, 350f, 36f);
            QuestionnaireUi.Label(content, "回答两个小问题，再进入下一个动作", 14, QuestionnaireUi.Muted,
                TextAnchor.MiddleLeft, FontStyle.Normal, 20f, 100f, 350f, 20f);

            BuildQuestionCard(content, 143f, "运动强度", "刚才这个动作，你感觉强度如何？", "0 休息", "10 最大",
                "0 休息 • 1 非常非常轻松 • 2 轻松 • 3 适中 • 4 有点吃力 • 5 吃力 • 7 非常吃力 • 10 最大",
                intensityButtons, value => { intensity = value; Refresh(); RefreshNotice(); }, out intensityValue);
            BuildQuestionCard(content, 477f, "疼痛", "请用 0–10 表示现在腰椎的疼痛程度", "0 无痛", "10 能想象的最剧烈疼痛",
                "0 无痛 • 1–3 轻度疼痛 • 4–6 中度疼痛 • 7–9 重度疼痛 • 10 能想象的最剧烈疼痛",
                painButtons, value => { pain = value; Refresh(); RefreshNotice(); }, out painValue);
        }

        private static void BuildQuestionCard(RectTransform content, float y, string chip, string question,
            string lowLabel, string highLabel, string legend, Button[] buttons, Action<int> onPick, out Text valueLabel)
        {
            RectTransform card = QuestionnaireUi.Card(content, 20f, y, 350f, 313f);
            Image chipBox = QuestionnaireUi.Box(card, "Chip", new Color(0.89f, 0.93f, 0.99f), 11f, 20f, 20f, 64f, 22f);
            QuestionnaireUi.Label(chipBox.transform, chip, 12, QuestionnaireUi.Accent, TextAnchor.MiddleCenter,
                FontStyle.Bold, 0f, 0f, 64f, 22f);
            valueLabel = QuestionnaireUi.Label(card, "", 15, QuestionnaireUi.Muted, TextAnchor.MiddleRight,
                FontStyle.Bold, 130f, 19f, 200f, 24f);
            QuestionnaireUi.Label(card, question, 16, QuestionnaireUi.Ink, TextAnchor.MiddleLeft, FontStyle.Bold,
                20f, 49f, 310f, 24f);

            for (int i = 0; i < buttons.Length; i++)
            {
                int value = i;
                float x = 20f + (i % 6) * 53f;
                float rowY = 87f + (i / 6) * 54f;
                Button button = QuestionnaireUi.MakeButton(card, "Score" + i, QuestionnaireUi.OptionBackground, 12f,
                    x, rowY, 45f, 45f, () => onPick(value));
                QuestionnaireUi.Label(button.transform, i.ToString(), 17, QuestionnaireUi.Ink, TextAnchor.MiddleCenter,
                    FontStyle.Bold, 0f, 0f, 45f, 45f);
                buttons[i] = button;
            }

            QuestionnaireUi.Label(card, lowLabel, 12.5f, QuestionnaireUi.Muted, TextAnchor.MiddleLeft,
                FontStyle.Normal, 20f, 200f, 150f, 16f);
            QuestionnaireUi.Label(card, highLabel, 12.5f, QuestionnaireUi.Muted, TextAnchor.MiddleRight,
                FontStyle.Normal, 180f, 200f, 150f, 16f);
            Image legendBox = QuestionnaireUi.Box(card, "Legend", QuestionnaireUi.OptionBackground, 14f, 20f, 224f, 310f, 61f);
            QuestionnaireUi.Label(legendBox.transform, legend, 12.5f, QuestionnaireUi.Muted, TextAnchor.MiddleLeft,
                FontStyle.Normal, 14f, 0f, 282f, 61f);
        }

        private void Refresh()
        {
            for (int i = 0; i < intensityButtons.Length; i++)
            {
                QuestionnaireUi.SetChoice(intensityButtons[i], i == intensity, QuestionnaireUi.Accent,
                    QuestionnaireUi.OptionBackground, Color.white, QuestionnaireUi.Ink);
                QuestionnaireUi.SetChoice(painButtons[i], i == pain, QuestionnaireUi.Accent,
                    QuestionnaireUi.OptionBackground, Color.white, QuestionnaireUi.Ink);
            }
            SetValue(intensityValue, intensity, QuestionnaireUi.IntensityCaption(intensity));
            SetValue(painValue, pain, QuestionnaireUi.PainCaption(pain, true));
        }

        private static void SetValue(Text label, int value, string caption)
        {
            if (value < 0)
            {
                label.text = "未选择";
                label.color = QuestionnaireUi.Muted;
                return;
            }
            label.text = value + " · " + caption;
            label.color = QuestionnaireUi.Accent;
        }

        private void RefreshNotice()
        {
            notice.text = "疼痛达到 " + QuestionnaireUi.PainStopThreshold + " 分及以上时，本次训练会暂停";
            notice.color = QuestionnaireUi.Muted;
        }

        private void HandleNext()
        {
            if (intensity < 0 || pain < 0)
            {
                notice.text = "请先回答上面两个问题。";
                notice.color = QuestionnaireUi.DangerText;
                return;
            }
            if (onSubmit != null) onSubmit(intensity, pain);
        }
    }

    /// <summary>
    /// "Stop" screens that share one layout: a headline, a record card, the
    /// emergency warning and two pinned buttons. Used for 今天先不练了 (pain too
    /// high before training) and 这次先练到这里 (pain too high after an action).
    /// </summary>
    internal sealed class NoticeScreen
    {
        internal enum RowStyle
        {
            Muted,
            Strong,
            Badge,
        }

        internal struct Row
        {
            public string Label;
            public RowStyle Style;

            public Row(string label, RowStyle style)
            {
                Label = label;
                Style = style;
            }
        }

        private const float FooterHeight = 148f;
        private const float CardTop = 159f;

        private Text subtitle;
        private Text notice;
        private Text[] values;
        private RectTransform contentRect;

        public GameObject Build(Transform canvasParent, string name, string title, string recordTitle, Row[] rows,
            string primaryCaption, string secondaryCaption, UnityAction onBack, UnityAction onPrimary,
            UnityAction onSecondary)
        {
            float cardHeight = 55f * rows.Length + 49.5f;
            float warningTop = CardTop + cardHeight + 20f;
            float contentHeight = warningTop + 72f + 12f + 40f + 16f;

            RectTransform root = QuestionnaireUi.CreateScreen(canvasParent, name, null, out GameObject panel);
            contentRect = QuestionnaireUi.ScrollArea(root, QuestionnaireUi.HeaderHeight, FooterHeight, contentHeight);
            QuestionnaireUi.Label(contentRect, title, 24, QuestionnaireUi.Ink, TextAnchor.MiddleCenter, FontStyle.Bold,
                20f, 48f, 350f, 32f);
            subtitle = QuestionnaireUi.Label(contentRect, "", 14, QuestionnaireUi.Muted, TextAnchor.MiddleCenter,
                FontStyle.Normal, 20f, 92f, 350f, 48f);

            RectTransform card = QuestionnaireUi.Card(contentRect, 20f, CardTop, 350f, cardHeight);
            QuestionnaireUi.Label(card, recordTitle, 16, QuestionnaireUi.Ink, TextAnchor.MiddleLeft, FontStyle.Bold,
                20f, 18f, 310f, 28f);
            values = new Text[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                float centre = 72.5f + 55f * i;
                QuestionnaireUi.Label(card, rows[i].Label, 16, QuestionnaireUi.Ink, TextAnchor.MiddleLeft,
                    FontStyle.Normal, 20f, centre - 15f, 130f, 30f);
                switch (rows[i].Style)
                {
                    case RowStyle.Badge:
                        Image badge = QuestionnaireUi.Box(card, "Badge", QuestionnaireUi.BadgeFill, 15f,
                            258f, centre - 15f, 72f, 30f);
                        values[i] = QuestionnaireUi.Label(badge.transform, "", 16, QuestionnaireUi.BadgeText,
                            TextAnchor.MiddleCenter, FontStyle.Bold, 0f, 0f, 72f, 30f);
                        break;
                    case RowStyle.Strong:
                        values[i] = QuestionnaireUi.Label(card, "", 16, QuestionnaireUi.Ink, TextAnchor.MiddleRight,
                            FontStyle.Bold, 150f, centre - 25f, 180f, 50f);
                        break;
                    default:
                        values[i] = QuestionnaireUi.Label(card, "", 15, QuestionnaireUi.Muted, TextAnchor.MiddleRight,
                            FontStyle.Normal, 150f, centre - 25f, 180f, 50f);
                        break;
                }
                if (i < rows.Length - 1)
                {
                    QuestionnaireUi.Box(card, "Divider", QuestionnaireUi.Divider, 0.5f, 20f, centre + 27.5f, 310f, 1f);
                }
            }

            QuestionnaireUi.WarningBox(contentRect, "如果出现大小便异常、会阴部发麻或腿突然没力，请立即就医。",
                20f, warningTop, 350f, 72f);
            notice = QuestionnaireUi.Label(contentRect, "", 13, QuestionnaireUi.Muted, TextAnchor.MiddleCenter,
                FontStyle.Normal, 20f, warningTop + 84f, 350f, 40f);

            RectTransform footer = QuestionnaireUi.Footer(root, FooterHeight);
            QuestionnaireUi.PillButton(footer, primaryCaption, 20f, 12f, 350f, 52f, true, onPrimary);
            QuestionnaireUi.PillButton(footer, secondaryCaption, 20f, 76f, 350f, 52f, false, onSecondary);
            QuestionnaireUi.Header(root, "腰椎康复系统", onBack);
            return panel;
        }

        /// <summary>Fills in the subtitle and one value per row; also clears any earlier notice.</summary>
        public void Show(string subtitleText, string[] rowValues)
        {
            subtitle.text = subtitleText;
            notice.text = "";
            for (int i = 0; i < values.Length; i++)
            {
                values[i].text = rowValues != null && i < rowValues.Length ? rowValues[i] : "";
            }
            contentRect.anchoredPosition = Vector2.zero;
        }

        public void SetNotice(string text)
        {
            notice.text = text;
        }
    }
}
