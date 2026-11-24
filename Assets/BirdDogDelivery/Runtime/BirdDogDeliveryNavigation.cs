using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BirdDogDelivery
{
    /// <summary>Add an entry at runtime; never resave or modify an original scene.</summary>
    public sealed class BirdDogDeliveryNavigation : MonoBehaviour
    {
        Canvas display;
        GameWorkoutScene entry;
        GameTestMenu menu;
        bool loading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
        }
        static void SceneLoaded(Scene scene,LoadSceneMode mode)
        {
            if (scene.name != BirdDogDeliveryGame.OriginalSceneName) return;
            var root = new GameObject("Bird dog delivery entry"); SceneManager.MoveGameObjectToScene(root,scene);
            root.AddComponent<BirdDogDeliveryNavigation>();
        }
        void Update()
        {
            if (entry == null) entry = FindObjectOfType<GameWorkoutScene>();
            if (entry == null) return;
            if (display == null)
            {
                display = BirdDogUI.Canvas("Bird dog new scoring entry",1510,new Vector2(1920,1080));
                var scaler = display.GetComponent<CanvasScaler>(); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1;
                BirdDogUI.Button(display.transform,entry.font,"鸟狗\n新评分场景",new Vector2(790,315),new Vector2(250,100),Open,26);
            }
            if (menu == null) menu = FindObjectOfType<GameTestMenu>();
            display.gameObject.SetActive(!loading && (menu != null ? menu.Panel != null && menu.Panel.activeInHierarchy : !entry.IsPlaying || entry.coach.IsActionPaused));
        }
        public void Open()
        {
            if (loading) return; loading = true;
            entry.StopTraining(); Time.timeScale = 1; AudioListener.pause = false;
            SceneManager.LoadSceneAsync(BirdDogDeliveryGame.SceneName);
        }
        void OnDestroy() { if (display != null) Destroy(display.gameObject); }
    }
}
