using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TaikoNight.EditorTools
{
    /// <summary>
    /// Menu: Tools > Taiko Night > Build Prefab + Demo Scene
    /// 1. Sets every texture in Assets/TaikoNight/Sprites to Sprite, 200 PPU (2x art, 1 design px = 0.01 unit).
    /// 2. Assembles background, coach slot, drum, masked note lane, judge circle, sticks and HUD.
    /// 3. Saves Prefabs/TaikoNight.prefab and Scenes/TaikoNight_Demo.unity.
    /// </summary>
    public static class TaikoNightBuilder
    {
        const string Root = "Assets/TaikoNight";
        const string SpriteDir = Root + "/Sprites";
        const string FontDir = Root + "/Fonts";
        const string PrefabPath = Root + "/Prefabs/TaikoNight.prefab";
        const string ScenePath = Root + "/Scenes/TaikoNight_Demo.unity";
        const string MaterialPath = Root + "/Materials/HoldRing.mat";
        const float Period = 660f;   // note pattern length in design px (6 s x 110 px/s)

        [MenuItem("Tools/Taiko Night/Build Prefab + Demo Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ConfigureSprites();

            GameObject root = new GameObject("TaikoNight");
            TaikoNightChart chart = root.AddComponent<TaikoNightChart>();

            BuildBackground(root.transform);
            BuildCoach(root.transform);
            BuildDrum(root.transform, chart);
            chart.strip = BuildLane(root.transform);
            BuildJudge(root.transform, chart);
            BuildSticks(root.transform, chart);
            Place(Group("Foreground", root.transform), "tn_front_cloud");
            BuildHud(root.transform);

            Directory.CreateDirectory(Root + "/Prefabs");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.95f; // 390 design px tall
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("#0F1034");
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            PrefabUtility.InstantiatePrefab(prefab);

            Directory.CreateDirectory(Root + "/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Taiko Night", "Built:\n" + PrefabPath + "\n" + ScenePath +
                "\n\nSet the Game view to a landscape phone aspect (e.g. 2532x1170) and press Play.", "OK");
        }

        // ------------------------------------------------------------------ import settings

        static void ConfigureSprites()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteDir });
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EditorUtility.DisplayProgressBar("Taiko Night", "Importing " + Path.GetFileName(path), (float)i / guids.Length);
                    TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti == null) continue;
                    bool ok = ti.textureType == TextureImporterType.Sprite
                              && Mathf.Approximately(ti.spritePixelsPerUnit, 200f)
                              && ti.textureShape == TextureImporterShape.Texture2D
                              && !ti.mipmapEnabled
                              && ti.textureCompression == TextureImporterCompression.Uncompressed;
                    if (ok) continue;
                    ti.textureType = TextureImporterType.Sprite;
                    ti.textureShape = TextureImporterShape.Texture2D;
                    ti.spriteImportMode = SpriteImportMode.Single;
                    ti.spritePixelsPerUnit = 200f;
                    ti.mipmapEnabled = false;
                    ti.alphaIsTransparency = true;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.filterMode = FilterMode.Bilinear;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.maxTextureSize = 4096;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    TextureImporterSettings s = new TextureImporterSettings();
                    ti.ReadTextureSettings(s);
                    s.spriteMeshType = SpriteMeshType.FullRect;
                    s.spriteAlignment = (int)SpriteAlignment.Center;
                    ti.SetTextureSettings(s);
                    ti.SaveAndReimport();
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ------------------------------------------------------------------ background + coach

        static void BuildBackground(Transform parent)
        {
            Transform bg = Group("Background", parent);
            Place(bg, "tn_bg");
            for (int i = 0; i < NightLayout.Sparkles.Length; i++)
            {
                Twinkle tw = Place(bg, "tn_sparkle_" + i).gameObject.AddComponent<Twinkle>();
                tw.period = NightLayout.Sparkles[i].period;
                tw.phase = NightLayout.Sparkles[i].phase;
            }
            Place(bg, "tn_star_glow").gameObject.AddComponent<Twinkle>().period = 3f;
            MeteorLoop meteor = Place(bg, "tn_meteor").gameObject.AddComponent<MeteorLoop>();
            meteor.headToSpriteCenterPx = new Vector2(NightLayout.MeteorCenterOffsetX, NightLayout.MeteorCenterOffsetY);
            Hidden(meteor.GetComponent<SpriteRenderer>());
            Place(bg, "tn_lanterns").gameObject.AddComponent<FloatBob>();
        }

        /// <summary>Coach slot: 164 px dark-glass square, usable 144x144 centred at (110, 200). Put the coach under CoachAnchor.</summary>
        static void BuildCoach(Transform parent)
        {
            Transform c = Group("CoachSlot", parent);
            Place(c, "tn_coach_card");
            Place(c, "tn_coach_placeholder");
            GameObject anchor = new GameObject("CoachAnchor (144x144 px = 1.44x1.44 units)");
            anchor.transform.SetParent(c, false);
            anchor.transform.localPosition = DesignSpace.ToWorld(110f, 200f);
        }

        // ------------------------------------------------------------------ drum

        static void BuildDrum(Transform parent, TaikoNightChart chart)
        {
            Transform d = Group("Drum", parent);
            Place(d, "tn_drum");
            Transform studs = Group("Studs", d);
            for (int i = 0; i < NightLayout.Studs.Length; i++)
            {
                Vector2 p = NightLayout.Studs[i];
                SpriteRenderer r = PlaceAt(studs, "Stud" + i, "tn_stud", p.x, p.y);
                r.sortingOrder = 21;
                Twinkle tw = r.gameObject.AddComponent<Twinkle>();
                tw.period = 2.4f;
                tw.phase = 0.3f * i;
                tw.minAlpha = 0.55f;
                tw.minScale = 1f;
                tw.maxScale = 1f;
            }
            chart.headFlash = Hidden(Place(d, "tn_head_flash"));
            chart.waveDon = Hidden(Place(d, "tn_wave_don"));
            chart.waveKa = Hidden(Place(d, "tn_wave_ka"));
        }

        // ------------------------------------------------------------------ lane

        static Transform BuildLane(Transform parent)
        {
            Transform lane = Group("Lane", parent);
            Place(lane, "tn_lane");
            GameObject maskGo = new GameObject("LaneMask (notes vanish into the judge circle)");
            maskGo.transform.SetParent(lane, false);
            LayerInfo m = NightLayout.Layers["tn_lane_mask"];
            maskGo.transform.localPosition = DesignSpace.ToWorld(m.x, m.y);
            SpriteMask mask = maskGo.AddComponent<SpriteMask>();
            mask.sprite = S("tn_lane_mask");
            mask.isCustomRangeActive = true;
            mask.backSortingOrder = 30;
            mask.frontSortingOrder = 32;

            Transform strip = Group("NoteStrip", lane);
            float dKa = 330f;  // 抬起 note -> 放下 note distance (3 s x 110 px/s)
            for (int k = -1; k <= 1; k++)
            {
                float o = Period * k;
                Masked(Offset(strip, "HoldBar" + k, "tn_hold_bar", o));
                Masked(Offset(strip, "Lift" + k, "tn_note_up", o + 110f));
                Masked(Offset(strip, "Lower" + k, "tn_note_down", o + 110f + dKa));
            }
            return strip;
        }

        // ------------------------------------------------------------------ judge circle

        static void BuildJudge(Transform parent, TaikoNightChart chart)
        {
            Transform j = Group("JudgeCircle", parent);
            chart.holdGlow = Hidden(Place(j, "tn_hold_glow"));
            Place(j, "tn_judge_ring");
            chart.judge = Place(j, "tn_judge").transform;
            chart.holdRingGlow = Ring(j, "HoldRingGlow", 0.12f, Hex("#FFD45E"), 43);
            chart.holdRing = Ring(j, "HoldRing", 0.04f, Color.white, 44);
            chart.rippleDon = Hidden(Place(j, "tn_ripple_don"));
            chart.rippleKa = Hidden(Place(j, "tn_ripple_ka"));
            chart.perfect = Hidden(Place(j, "tn_perfect"));
        }

        static LineRenderer Ring(Transform parent, string name, float width, Color col, int order)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            LineRenderer lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.widthMultiplier = width;
            lr.numCapVertices = 4;
            lr.startColor = col;
            lr.endColor = col;
            lr.sortingOrder = order;
            lr.sharedMaterial = RingMaterial();
            lr.positionCount = 0;
            return lr;
        }

        // ------------------------------------------------------------------ sticks

        static void BuildSticks(Transform parent, TaikoNightChart chart)
        {
            Transform s = Group("Sticks", parent);
            // Stick sprites are centred on the hand pivot, so rotation happens at the grip
            chart.stickLeft = Place(s, "tn_stick_left").transform;
            chart.stickRight = Place(s, "tn_stick_right").transform;
            chart.sparkDon = Hidden(Place(s, "tn_spark_don"));
            chart.sparkKa = Hidden(Place(s, "tn_spark_ka"));
        }

        // ------------------------------------------------------------------ HUD

        static void BuildHud(Transform parent)
        {
            Place(Group("HUD_Sprites", parent), "tn_hud_pause");
            GameObject canvasGo = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(844f, 390f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            Font fredoka = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "/Fredoka-SemiBold.ttf");
            AddText(canvasGo.transform, "Score", "0,524,180", fredoka, 24, Color.white, 826f, 10f, 260f, 28f);
            AddText(canvasGo.transform, "Combo", "128 COMBO", fredoka, 11, Hex("#D8DCFF"), 826f, 36f, 160f, 16f);
        }

        // ------------------------------------------------------------------ helpers

        static Sprite S(string name)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + "/" + name + ".png");
            if (s == null) Debug.LogError("[TaikoNight] Missing sprite: " + name);
            return s;
        }

        static Transform Group(string name, Transform parent)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        static SpriteRenderer Place(Transform parent, string sprite)
        {
            LayerInfo info = NightLayout.Layers[sprite];
            return PlaceAt(parent, sprite, sprite, info.x, info.y);
        }

        /// <summary>
        /// Shifts a sprite along the lane by dx design px from its exported position.
        /// Note sprites are exported sitting on the judge circle; the hold bar at its t = 0 position.
        /// </summary>
        static SpriteRenderer Offset(Transform parent, string name, string sprite, float dx)
        {
            LayerInfo info = NightLayout.Layers[sprite];
            return PlaceAt(parent, name, sprite, info.x + dx, info.y);
        }

        static SpriteRenderer PlaceAt(Transform parent, string name, string sprite, float x, float y)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = DesignSpace.ToWorld(x, y);
            SpriteRenderer r = g.AddComponent<SpriteRenderer>();
            r.sprite = S(sprite);
            LayerInfo info;
            r.sortingOrder = NightLayout.Layers.TryGetValue(sprite, out info) ? info.order : 0;
            return r;
        }

        static SpriteRenderer Hidden(SpriteRenderer r)
        {
            r.color = new Color(1f, 1f, 1f, 0f);
            return r;
        }

        static void Masked(SpriteRenderer r)
        {
            r.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        }

        static Material RingMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            Material m = new Material(shader);
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.CreateAsset(m, MaterialPath);
            return m;
        }

        static void AddText(Transform parent, string name, string text, Font font, int size, Color color, float right, float top, float w, float h)
        {
            GameObject g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)g.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(right - DesignSpace.CenterX, -(top + h * 0.5f - DesignSpace.CenterY));
            rt.sizeDelta = new Vector2(w, h);
            Text t = g.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.UpperRight;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
        }

        static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }
    }
}
