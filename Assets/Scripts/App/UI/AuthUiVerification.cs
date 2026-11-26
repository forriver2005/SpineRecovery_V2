#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpineRecovery.App
{
    public static class AuthUiVerification
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static int checks;
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch editor required");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string output = Path.GetFullPath("../tmp/auth-ui-20261126/previews");
            Directory.CreateDirectory(output);
            var host = new GameObject("AuthFixture"); host.SetActive(false);
            var shell = host.AddComponent<AppShellUi>();
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = QuestionnaireUi.PageBackground;
            canvas.worldCamera = camera;
            try
            {
                foreach (string screen in new[] { "Login", "Register", "Activation" })
                {
                    var panel = (GameObject)typeof(AppShellUi).GetMethod("Create" + screen + "Panel", Flags).Invoke(shell, new object[] { canvas.transform });
                    if (screen == "Login")
                    {
                        var cover = panel.GetComponentsInChildren<Image>().First(i => i.name == "Raster:AppUi/LoginCover");
                        Check(cover.sprite != null, "v6.2 login artwork loaded");
                        Check(panel.GetComponentsInChildren<Button>().Any(b => b.GetComponentInChildren<Text>().text == "立即注册"), "Register link present");
                        Check(panel.GetComponentsInChildren<InputField>().Length == 2, "Account and password inputs present");
                        panel.SetActive(false);
                        continue;
                    }
                    foreach (var size in new[] { new Vector2Int(390, 844), new Vector2Int(320, 568), new Vector2Int(396, 930) })
                    {
                        foreach (bool populated in new[] { false, true })
                        {
                            foreach (var input in panel.GetComponentsInChildren<InputField>())
                            {
                                input.text = populated ? (screen == "Activation" ? "13800138000" : "demo.account1234567890@example.com") : "";
                                Check(input.placeholder is Text && input.textComponent.fontSize >= 48, "Live input glyph resolution");
                                Check(input.textComponent.rectTransform.rect.width * input.textComponent.rectTransform.localScale.x <= 270.1f, "Input text respects padding");
                            }
                            var layout = panel.GetComponentInChildren<AuthMessageLayout>();
                            layout.Message.text = populated ? "请确认档案信息：测试用户，手机号 138****8000。确认是本人后再次点击绑定。若信息有误，请联系医生核对档案后重试。" : "";
                            var rt = new RenderTexture(size.x * 3, size.y * 3, 24);
                            camera.targetTexture = rt; camera.aspect = (float)size.x / size.y; camera.orthographicSize = size.y / 2f;
                            ((RectTransform)canvas.transform).sizeDelta = size;
                            Canvas.ForceUpdateCanvases();
                            var root = panel.GetComponentInChildren<QuestionnaireDesignRoot>();
                            typeof(QuestionnaireDesignRoot).GetMethod("Apply", Flags).Invoke(root, null);
                            Canvas.ForceUpdateCanvases(); layout.Apply(); Canvas.ForceUpdateCanvases();
                            var scroll = panel.GetComponentInChildren<ScrollRect>();
                            scroll.verticalNormalizedPosition = 1;
                            Canvas.ForceUpdateCanvases();
                            float messageHeight = string.IsNullOrEmpty(layout.Message.text) ? 0 : layout.Message.preferredHeight / 3f;
                            Check(-layout.Primary.anchoredPosition.y >= layout.MessageTop + messageHeight + 12, "Feedback does not overlap primary action");
                            Check(-layout.Secondary.anchoredPosition.y >= -layout.Primary.anchoredPosition.y + 60, "Buttons have separate touch areas");
                            Check(Mathf.Abs(root.GetComponent<RectTransform>().rect.width - 390) < 1, "Responsive width");
                            Capture(camera, rt, Path.Combine(output, screen.ToLowerInvariant() + "-" + size.x + "x" + size.y + (populated ? "-feedback" : "-empty") + ".png"));
                            if (size.x == 320 && populated)
                            {
                                scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
                                Capture(camera, rt, Path.Combine(output, screen.ToLowerInvariant() + "-small-bottom.png"));
                                var corners = new Vector3[4]; layout.Secondary.GetWorldCorners(corners);
                                Check(corners.All(p => p.y >= -size.y / 2f - 1 && p.y <= size.y / 2f + 1), "Small-screen back action reachable by scrolling");
                            }
                            camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt);
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(panel);
                }
                File.WriteAllText(Path.Combine(output, "checks.txt"), "AUTH_UI_VERIFICATION_PASS checks=" + checks);
                Debug.Log("AUTH_UI_VERIFICATION_PASS checks=" + checks);
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas.gameObject); UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(host); }
        }
        private static void Capture(Camera camera, RenderTexture rt, string filename)
        {
            foreach (var graphic in UnityEngine.Object.FindObjectsOfType<Graphic>()) graphic.SetAllDirty();
            Canvas.ForceUpdateCanvases(); camera.Render();
            var old = RenderTexture.active; RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
            File.WriteAllBytes(filename, image.EncodeToPNG());
            RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(image);
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    }
}
#endif
