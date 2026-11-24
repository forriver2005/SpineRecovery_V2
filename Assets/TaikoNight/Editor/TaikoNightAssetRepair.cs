using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

namespace TaikoNight.EditorTools
{
    /// <summary>
    /// Repairs imported Taiko Night assets whose prefab serialized before the
    /// sprite import settings were applied. The repair is intentionally scoped
    /// to the newly added Taiko Night assets and practice scenes.
    /// </summary>
    public static class TaikoNightAssetRepair
    {
        private const string PrefabPath = "Assets/TaikoNight/Prefabs/TaikoNight.prefab";
        private const string SpriteDir = "Assets/TaikoNight/Sprites";
        private const string ChineseFontPath = "Assets/Fonts/SourceHanSans/SourceHanSansSC-Heavy SDF.asset";

        [MenuItem("Tools/Taiko Night/Repair Imported Art And Practice Scenes")]
        public static void RepairAll()
        {
            RepairPrefab();
            RepairScene("Assets/Scenes/HipThrust_TaikoNight_Dynamic.unity", "动态臀桥 · 星轨敲鼓");
            RepairScene("Assets/Scenes/CrunchPractice_TaikoNight.unity", "动态卷腹 · 星轨敲鼓");
            RepairScene("Assets/Scenes/LyingCatPractice_TaikoNight.unity", "动态卧位猫 · 星轨敲鼓");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Taiko Night/Repair Taiko Night Prefab Art")]
        public static void RepairPrefab()
        {
            EnsureSpriteImportSettings();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new System.IO.FileNotFoundException(PrefabPath);

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject instance = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (SpriteRenderer renderer in instance.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    Sprite sprite = LoadSprite(renderer.gameObject.name);
                    if (sprite != null) renderer.sprite = sprite;
                }

                foreach (SpriteMask mask in instance.GetComponentsInChildren<SpriteMask>(true))
                {
                    Sprite sprite = LoadSprite("tn_lane_mask");
                    if (sprite != null) mask.sprite = sprite;
                }

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
            }

            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("Repaired Taiko Night prefab sprite references.");
        }

        private static void RepairScene(string path, string title)
        {
            if (!System.IO.File.Exists(path)) return;
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            TaikoNightActionRuntime runtime = Object.FindObjectOfType<TaikoNightActionRuntime>(true);
            if (runtime != null)
            {
                runtime.font = LoadChineseFont();
                runtime.title = title;
                EditorUtility.SetDirty(runtime);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static TMP_FontAsset LoadChineseFont()
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ChineseFontPath);
        }

        private static Sprite LoadSprite(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            string assetName = objectName;
            if (objectName.StartsWith("Stud")) assetName = "tn_stud";
            else if (objectName.StartsWith("HoldBar")) assetName = "tn_hold_bar";
            else if (objectName.StartsWith("Lift")) assetName = "tn_note_up";
            else if (objectName.StartsWith("Lower")) assetName = "tn_note_down";
            string path = SpriteDir + "/" + assetName + ".png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                sprite = asset as Sprite;
                if (sprite != null) return sprite;
            }

            Debug.LogWarning("[TaikoNight] Sprite asset is unavailable: " + path);
            return null;
        }

        private static void EnsureSpriteImportSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteDir });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 200f;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
    }
}
