using UnityEngine;

namespace SpineRecovery.App
{
    public enum AccountRegistrationResult
    {
        Success,
        AccountAlreadyExists,
        InvalidInput,
        StorageFailure,
    }

    public interface IUserRepository
    {
        bool TryAuthenticate(string accountId, string password, out UserSession session);
        bool TryAuthenticateAdministrator(string accountId, string password, out UserSession session);
        AccountRegistrationResult TryRegister(string accountId, string password, out UserSession session);
        void Save(UserSession session);
        UserSession Load(string accountId);
        void AppendPainRecord(UserSession session, PainHistoryRecord record);
        void AppendTrainingRecord(UserSession session, TrainingHistoryRecord record);
        void AppendMonthlyRecord(UserSession session, MonthlyQuestionnaireRecord record);
    }

    public sealed class JsonUserRepository : IUserRepository
    {
        private const string DemoPassword = "123";
        private const string AdministratorPassword = "admin";

        public void EnsureDemoAccounts()
        {
            for (int i = 1; i <= 10; i++)
            {
                EnsureDemoAccount("new" + i, UserLifecycle.AdmissionPending);
                EnsureDemoAccount("old" + i, UserLifecycle.ReturningUser);
            }
        }

        private void EnsureDemoAccount(string accountId, UserLifecycle lifecycle)
        {
            // Seed only missing accounts; never reset an existing user's saved progress.
            if (Load(accountId) != null)
            {
                return;
            }

            Save(new UserSession
            {
                accountId = accountId,
                lifecycle = lifecycle,
            });
        }

        public bool TryAuthenticate(string accountId, string password, out UserSession session)
        {
            session = null;
            string normalizedId = accountId?.Trim().ToLowerInvariant();
            if (TryGetDemoLifecycle(normalizedId, out UserLifecycle lifecycle) && password == DemoPassword)
            {
                session = Load(normalizedId) ?? new UserSession
                {
                    accountId = normalizedId,
                    lifecycle = lifecycle,
                    profile = new UserProfile { password = DemoPassword },
                };
                Save(session);
                return true;
            }

            UserSession registered = Load(normalizedId);
            if (registered == null || registered.profile == null ||
                string.IsNullOrEmpty(registered.profile.password) || registered.profile.password != password)
            {
                return false;
            }
            session = registered;
            return true;
        }

        public AccountRegistrationResult TryRegister(string accountId, string password, out UserSession session)
        {
            session = null;
            string normalizedId = accountId?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedId) || string.IsNullOrWhiteSpace(password) || normalizedId == "admin")
                return AccountRegistrationResult.InvalidInput;
            if (Load(normalizedId) != null || TryGetDemoLifecycle(normalizedId, out _))
                return AccountRegistrationResult.AccountAlreadyExists;
            session = new UserSession
            {
                accountId = normalizedId,
                lifecycle = UserLifecycle.AdmissionPending,
                profile = new UserProfile { password = password },
            };
            try
            {
                Save(session);
                return AccountRegistrationResult.Success;
            }
            catch
            {
                session = null;
                return AccountRegistrationResult.StorageFailure;
            }
        }

        public bool TryAuthenticateAdministrator(string accountId, string password, out UserSession session)
        {
            session = null;
            if (!string.Equals(accountId?.Trim(), "admin", System.StringComparison.OrdinalIgnoreCase) ||
                password != AdministratorPassword)
            {
                return false;
            }

            session = new UserSession
            {
                accountId = "admin",
                role = UserRole.Administrator,
                lifecycle = UserLifecycle.ReturningUser,
                profile = new UserProfile(),
            };
            return true;
        }

        public void Save(UserSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.accountId))
            {
                Debug.LogWarning("Cannot save an empty user session.");
                return;
            }

            JsonFileStore.Save(FileName(session.accountId), session);
        }

        public UserSession Load(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return null;
            }

            UserSession session;
            return JsonFileStore.TryLoad(FileName(accountId), out session) ? session : null;
        }

        public void AppendPainRecord(UserSession session, PainHistoryRecord record)
        {
            if (session == null || record == null) return;
            var records = session.painHistory ?? System.Array.Empty<PainHistoryRecord>();
            var updated = new PainHistoryRecord[records.Length + 1];
            System.Array.Copy(records, updated, records.Length);
            updated[records.Length] = record;
            session.painHistory = updated;
            Save(session);
        }

        public void AppendTrainingRecord(UserSession session, TrainingHistoryRecord record)
        {
            if (session == null || record == null) return;
            var records = session.trainingHistory ?? System.Array.Empty<TrainingHistoryRecord>();
            var updated = new TrainingHistoryRecord[records.Length + 1];
            System.Array.Copy(records, updated, records.Length);
            updated[records.Length] = record;
            session.trainingHistory = updated;
            Save(session);
        }

        public void AppendMonthlyRecord(UserSession session, MonthlyQuestionnaireRecord record)
        {
            if (session == null || record == null) return;
            var records = session.monthlyHistory ?? System.Array.Empty<MonthlyQuestionnaireRecord>();
            var updated = new MonthlyQuestionnaireRecord[records.Length + 1];
            System.Array.Copy(records, updated, records.Length);
            updated[records.Length] = record;
            session.monthlyHistory = updated;
            Save(session);
        }

        private static string FileName(string accountId)
        {
            string safeId = accountId.Trim().Replace("/", "_").Replace("\\", "_");
            return "spine-user-" + safeId + ".json";
        }

        private static bool TryGetDemoLifecycle(string accountId, out UserLifecycle lifecycle)
        {
            lifecycle = UserLifecycle.AdmissionPending;
            if (string.IsNullOrEmpty(accountId) || accountId.Length < 4)
            {
                return false;
            }

            string numberPart;
            if (accountId.StartsWith("new"))
            {
                lifecycle = UserLifecycle.AdmissionPending;
                numberPart = accountId.Substring(3);
            }
            else if (accountId.StartsWith("old"))
            {
                lifecycle = UserLifecycle.ReturningUser;
                numberPart = accountId.Substring(3);
            }
            else
            {
                return false;
            }

            return int.TryParse(numberPart, out int number) && number >= 1 && number <= 10;
        }
    }
}
