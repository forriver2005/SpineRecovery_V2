using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeWave.EditorTools
{
    /// <summary>
    /// Menu: Tools > Bridge Wave > Build Prefab + Demo Scene
    /// 1. Sets every texture in Assets/BridgeWave/Sprites to Sprite, 200 PPU (2x art, 1 design px = 0.01 unit).
    /// 2. Assembles background, height guides, the procedural track, patient orb, counter and HUD.
    /// 3. Saves Prefabs/BridgeWave.prefab and Scenes/BridgeWave_Demo.unity (driven by SimulatedPatient).
    /// </summary>
    public static class BridgeWaveBuilder
    {
        const string Root = "Assets/BridgeWave";
        const string SpriteDir = Root + "/Sprites";
        const string FontDir = Root + "/Fonts";
        const string PrefabPath = Root + "/Prefabs/BridgeWave.prefab";
        const string ScenePath = Root + "/Scenes/BridgeWave_Demo.unity";
        const string MaterialPath = Root + "/Materials/Line.mat";

        [MenuItem("Tools/Bridge Wave/Build Prefab + Demo Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ConfigureSprites();

            GameObject root = new GameObject("BridgeWave");
            BuildBackground(root.transform);

            GameObject trackGo = new GameObject("Track (procedural)");
            trackGo.transform.SetParent(root.transform, false);
            BridgeWaveGame game = trackGo.AddComponent<BridgeWaveGame>();
            trackGo.AddComponent<SimulatedPatient>();
            game.lineMaterial = LineMaterial();
            game.starSprite = S("bw_item_star");
            game.noteSprite = S("bw_item_note");

            BuildOrb(root.transform, game);
            BuildHud(root.transform, game);

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
            EditorUtility.DisplayDialog("Bridge Wave", "Built:\n" + PrefabPath + "\n" + ScenePath +
                "\n\nPress Play: SimulatedPatient follows the curve.\nFor real use, disable SimulatedPatient and write BridgeWaveGame.height from the sensor.", "OK");
        }

        // ------------------------------------------------------------------ import settings

        public static void ConfigureSprites()
        {
            string[] paths = Directory.GetFiles(SpriteDir, "*.png", SearchOption.TopDirectoryOnly)
                .Select(p => p.Replace("\\", "/")).ToArray();
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i];
                    EditorUtility.DisplayProgressBar("Bridge Wave", "Importing " + Path.GetFileName(path), (float)i / Mathf.Max(1, paths.Length));
                    TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti == null) continue;
                    bool ok = ti.textureType == TextureImporterType.Sprite
                              && ti.textureShape == TextureImporterShape.Texture2D
                              && Mathf.Approximately(ti.spritePixelsPerUnit, 200f)
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

        // ------------------------------------------------------------------ background

        public static void BuildBackground(Transform parent)
        {
            Transform bg = Group("Background", parent);
            Place(bg, "bw_bg");
            for (int i = 0; i < WaveLayout.Sparkles.Length; i++)
            {
                Twinkle tw = Place(bg, "bw_sparkle_" + i).gameObject.AddComponent<Twinkle>();
                tw.period = WaveLayout.Sparkles[i].period;
                tw.phase = WaveLayout.Sparkles[i].phase;
            }
            Place(bg, "bw_star_glow").gameObject.AddComponent<Twinkle>().period = 3f;
            MeteorLoop meteor = Place(bg, "bw_meteor").gameObject.AddComponent<MeteorLoop>();
            meteor.headToSpriteCenterPx = new Vector2(WaveLayout.MeteorCenterOffsetX, WaveLayout.MeteorCenterOffsetY);
            Hidden(meteor.GetComponent<SpriteRenderer>());
            Place(bg, "bw_lanterns").gameObject.AddComponent<FloatBob>();
            Place(bg, "bw_guides");
        }

        // ------------------------------------------------------------------ orb

        public static void BuildOrb(Transform parent, BridgeWaveGame game)
        {
            GameObject orb = new GameObject("PatientOrb (moves with height)");
            orb.transform.SetParent(parent, false);
            orb.transform.localPosition = DesignSpace.ToWorld(game.orbX, game.bottomY);
            PlaceLocal(orb.transform, "bw_orb_glow");
            game.haloLift = Hidden(PlaceLocal(orb.transform, "bw_halo_lift"));
            game.haloHold = Hidden(PlaceLocal(orb.transform, "bw_halo_hold"));
            game.haloLower = Hidden(PlaceLocal(orb.transform, "bw_halo_lower"));
            PlaceLocal(orb.transform, "bw_orb");
            game.burstGold = Hidden(PlaceLocal(orb.transform, "bw_burst_gold"));
            game.burstLilac = Hidden(PlaceLocal(orb.transform, "bw_burst_lilac"));
            game.orb = orb.transform;
        }

        // ------------------------------------------------------------------ HUD

        public static void BuildHud(Transform parent, BridgeWaveGame game)
        {
            Transform s = Group("HUD_Sprites", parent);
            Place(s, "bw_hud_pause");
            game.captionLift = Hidden(Place(s, "bw_cap_lift"));
            game.captionHold = Hidden(Place(s, "bw_cap_hold"));
            game.captionLower = Hidden(Place(s, "bw_cap_lower"));

            GameObject counter = new GameObject("Counter (reps)");
            counter.transform.SetParent(s, false);
            counter.transform.localPosition = DesignSpace.ToWorld(422f, 34f);
            SpriteRenderer bg = PlaceAt(counter.transform, "bw_counter_bg", "bw_counter_bg", DesignSpace.CenterX, DesignSpace.CenterY);
            bg.transform.localPosition = DesignSpace.ToWorld(WaveLayout.Layers["bw_counter_bg"].x, WaveLayout.Layers["bw_counter_bg"].y) - DesignSpace.ToWorld(422f, 34f);
            GameObject ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(parent, false);
            LineRenderer ring = ringGo.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.widthMultiplier = 0.035f;
            ring.numCapVertices = 4;
            ring.startColor = ring.endColor = Hex("#FFD66B");
            ring.sortingOrder = 51;
            ring.sharedMaterial = LineMaterial();
            game.counterRing = ring;
            game.counterRoot = counter.transform;

            GameObject canvasGo = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(844f, 390f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            Font fredoka = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "/Fredoka-SemiBold.ttf");
            game.counterText = AddText(canvasGo.transform, "Count", "2", fredoka, 17, Color.white, 422f, 24f, 40f, 22f, TextAnchor.UpperCenter);
            AddText(canvasGo.transform, "CountTotal", "/ 8", fredoka, 10, Hex("#D8DCFF"), 452f, 31f, 30f, 14f, TextAnchor.UpperLeft);
            AddText(canvasGo.transform, "Score", "0,524,180", fredoka, 24, Color.white, 826f, 10f, 260f, 28f, TextAnchor.UpperRight);
            AddText(canvasGo.transform, "Combo", "128 COMBO", fredoka, 11, Hex("#D8DCFF"), 826f, 36f, 160f, 16f, TextAnchor.UpperRight);
        }

        // ------------------------------------------------------------------ helpers

        static Sprite S(string name)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + "/" + name + ".png");
            if (s == null) Debug.LogError("[BridgeWave] Missing sprite: " + name);
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
            LayerInfo info = WaveLayout.Layers[sprite];
            return PlaceAt(parent, sprite, sprite, info.x, info.y);
        }

        /// <summary>For sprites exported centred on the origin (orb parts): place at the parent's origin.</summary>
        static SpriteRenderer PlaceLocal(Transform parent, string sprite)
        {
            SpriteRenderer r = PlaceAt(parent, sprite, sprite, DesignSpace.CenterX, DesignSpace.CenterY);
            r.transform.localPosition = Vector3.zero;
            return r;
        }

        static SpriteRenderer PlaceAt(Transform parent, string name, string sprite, float x, float y)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = DesignSpace.ToWorld(x, y);
            SpriteRenderer r = g.AddComponent<SpriteRenderer>();
            r.sprite = S(sprite);
            LayerInfo info;
            r.sortingOrder = WaveLayout.Layers.TryGetValue(sprite, out info) ? info.order : 0;
            return r;
        }

        static SpriteRenderer Hidden(SpriteRenderer r)
        {
            r.color = new Color(1f, 1f, 1f, 0f);
            return r;
        }

        public static Material LineMaterial()
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

        /// <summary>UI text: 'x' is the anchor edge given by the alignment (left / centre / right), 'top' is the top edge.</summary>
        static Text AddText(Transform parent, string name, string text, Font font, int size, Color color, float x, float top, float w, float h, TextAnchor align)
        {
            GameObject g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)g.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            float px = align == TextAnchor.UpperRight ? 1f : (align == TextAnchor.UpperCenter ? 0.5f : 0f);
            rt.pivot = new Vector2(px, 0.5f);
            rt.anchoredPosition = new Vector2(x - DesignSpace.CenterX, -(top + h * 0.5f - DesignSpace.CenterY));
            rt.sizeDelta = new Vector2(w, h);
            Text t = g.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }
    }
}
