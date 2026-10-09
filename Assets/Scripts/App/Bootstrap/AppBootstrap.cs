using UnityEngine;

namespace SpineRecovery.App
{
    public sealed class AppBootstrap : MonoBehaviour
    {
        public static AppBootstrap Instance { get; private set; }
        public AppState State { get; private set; }
        public AppRouter Router { get; private set; }
        public IUserRepository Users { get; private set; }
        public IAdminConfigRepository AdminConfigs { get; private set; }
        public TrainingSessionService Training { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            AppLogService.Ensure();
            State = new AppState();
            JsonUserRepository users = new JsonUserRepository();
            users.EnsureDemoAccounts();
            Users = users;
            AdminConfigs = new JsonAdminConfigRepository();
            Router = GetComponent<AppRouter>() ?? gameObject.AddComponent<AppRouter>();
            Training = GetComponent<TrainingSessionService>() ?? gameObject.AddComponent<TrainingSessionService>();
        }
    }
}
