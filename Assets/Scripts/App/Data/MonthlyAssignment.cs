using System;

namespace SpineRecovery.App
{
    /// <summary>
    /// A doctor-issued monthly questionnaire assignment. The questionnaire is
    /// modular: <see cref="modules"/> is a bitmask of the pages the patient
    /// must fill this round — 1 = page 1 (JOA self assessment), 2 = page 2
    /// (SF-MPQ), 3 = both. Stored per patient locally and synced later; the
    /// demo seeds a page-1-only assignment because there is no server yet.
    /// </summary>
    [Serializable]
    public sealed class MonthlyQuestionnaireAssignment
    {
        public const int ModuleJoa = 1;
        public const int ModuleSfMpq = 2;

        public const string StatusPending = "pending";
        public const string StatusCompleted = "completed";

        public const string SyncLocal = "local";
        public const string SyncPendingUpload = "pendingUpload";
        public const string SyncSynced = "synced";

        /// <summary>Server id once synced; "local:&lt;guid&gt;" for demo-issued entries.</summary>
        public string assignmentId;
        public string patientAccount;
        /// <summary>Issuing doctor account; "local-demo" until the doctor client exists.</summary>
        public string issuedBy;
        /// <summary>ISO 8601 issue time; used to pick the newest pending assignment.</summary>
        public string issuedAt;
        /// <summary>Bit mask of assigned pages: 1, 2 or 3 (1 + 2).</summary>
        public int modules;
        public string status = StatusPending;
        public string completedAt;
        /// <summary>True once the patient opened the entry; clears the 「新」 prompt.</summary>
        public bool opened;
        /// <summary>local / pendingUpload / synced — feeds the future offline upload queue.</summary>
        public string syncState = SyncLocal;

        public bool Includes(int module)
        {
            return (modules & module) != 0;
        }
    }
}
