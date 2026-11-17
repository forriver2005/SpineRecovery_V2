using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpineRecovery.App
{
    [Serializable]
    public sealed class NativeRequest
    {
        public string accountId;
        public string password;
        public string code;
        public string planToken;
        public bool consumerConfirmed;
        public long taskId;
        public string questionId;
        public string answerType;
        public int numberValue;
        public bool boolValue;
        public string textValue;
        public string answersJson;
        public string[] values = Array.Empty<string>();
    }

    [Serializable]
    public sealed class NativeQuestionnaireOption
    {
        public string value;
        public string label;
    }

    [Serializable]
    public sealed class NativeQuestionnaireQuestion
    {
        public string id;
        public string type;
        public bool required;
        public bool answerSet;
        public string textValue;
        public NativeQuestionnaireOption[] options = Array.Empty<NativeQuestionnaireOption>();
    }

    [Serializable]
    public sealed class NativeQuestionnaireTask
    {
        public long taskId;
        public string templateCode;
        public string availableAt;
        public string completedAt;
        public string availability;
        public bool pendingSubmission;
        public string submissionStatus;
        public string problem;
        public int templateVersion;
        public string schemaJson;
        public string answersJson;
        public string requestId;
        public NativeQuestionnaireQuestion[] questions = Array.Empty<NativeQuestionnaireQuestion>();
    }

    [Serializable]
    public sealed class NativeQuestionnaireTasks
    {
        public NativeQuestionnaireTask[] items = Array.Empty<NativeQuestionnaireTask>();
    }

    [Serializable]
    public sealed class NativeReply
    {
        public string requestId;
        public bool ok;
        public string dataJson;
        public string error;
    }

    public sealed class NativeMobileService : MonoBehaviour
    {
        private const string BridgeClass = "com.metaspine.mobile.UnifiedMobileBridge";
        private readonly Dictionary<string, Action<NativeReply>> pending = new Dictionary<string, Action<NativeReply>>();
        public static NativeMobileService Instance { get; private set; }

        public static bool Available
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        private void Awake()
        {
            Instance = this;
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var bridge = new AndroidJavaClass(BridgeClass))
                bridge.CallStatic("initialize", activity);
#endif
        }

        public static T Call<T>(string method, params object[] args)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(BridgeClass)) return bridge.CallStatic<T>(method, args);
#else
            throw new InvalidOperationException("Native mobile data requires an Android player.");
#endif
        }

        public static void Call(string method, params object[] args)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(BridgeClass)) bridge.CallStatic(method, args);
#endif
        }

        public void Request(string command, NativeRequest request, Action<NativeReply> callback)
        {
            if (!Available)
            {
                callback(new NativeReply { error = "该业务需要在 Android App 中连接服务器。" });
                return;
            }
            string id = Guid.NewGuid().ToString("N");
            pending[id] = callback;
            try { Call("dispatch", id, command, JsonUtility.ToJson(request), gameObject.name); }
            catch (Exception error)
            {
                pending.Remove(id);
                callback(new NativeReply { requestId = id, error = error.Message });
            }
        }

        // UnitySendMessage dispatches on the Unity main thread.
        [UnityEngine.Scripting.Preserve]
        public void OnNativeReply(string json)
        {
            var reply = JsonUtility.FromJson<NativeReply>(json);
            if (reply == null || !pending.TryGetValue(reply.requestId, out var callback)) return;
            pending.Remove(reply.requestId);
            callback(reply);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused && Available) Call("foreground");
        }
    }

    public sealed class AndroidUserRepository : IUserRepository
    {
        public bool TryAuthenticate(string accountId, string password, out UserSession session)
        {
            session = null;
            if (!NativeMobileService.Call<bool>("authenticate", accountId, password)) return false;
            session = Load(accountId);
            return true;
        }

        public bool TryAuthenticateAdministrator(string accountId, string password, out UserSession session)
        {
            session = null;
            return false;
        }

        public AccountRegistrationResult TryRegister(string accountId, string password, out UserSession session)
        {
            var result = (AccountRegistrationResult)NativeMobileService.Call<int>("register", accountId, password);
            session = result == AccountRegistrationResult.Success ? Load(accountId) : null;
            return result;
        }

        public UserSession Load(string accountId) => JsonUtility.FromJson<UserSession>(NativeMobileService.Call<string>("load", accountId));

        public void Save(UserSession session)
        {
            NativeMobileService.Call("save", JsonUtility.ToJson(session));
            JsonUtility.FromJsonOverwrite(NativeMobileService.Call<string>("load", session.accountId), session);
        }

        public void AppendPainRecord(UserSession session, PainHistoryRecord record)
        {
            var rows = new List<PainHistoryRecord>(session.painHistory ?? Array.Empty<PainHistoryRecord>());
            int index = rows.FindIndex(row => row.id == record.id);
            if (index >= 0) rows[index] = record; else rows.Add(record);
            session.painHistory = rows.ToArray();
            Save(session);
        }

        public void AppendTrainingRecord(UserSession session, TrainingHistoryRecord record)
        {
            var rows = new List<TrainingHistoryRecord>(session.trainingHistory ?? Array.Empty<TrainingHistoryRecord>());
            int index = rows.FindIndex(row => row.sessionId == record.sessionId);
            if (index >= 0) rows[index] = record; else rows.Add(record);
            session.trainingHistory = rows.ToArray();
            Save(session);
        }

        public void AppendMonthlyRecord(UserSession session, MonthlyQuestionnaireRecord record)
        {
            var rows = new List<MonthlyQuestionnaireRecord>(session.monthlyHistory ?? Array.Empty<MonthlyQuestionnaireRecord>());
            int index = rows.FindIndex(row => row.id == record.id);
            if (index >= 0) rows[index] = record; else rows.Add(record);
            session.monthlyHistory = rows.ToArray();
            Save(session);
        }
    }
}
