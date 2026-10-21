using System;
using UnityEngine;

namespace SpineRecovery.App
{
    public interface IMonthlyAssignmentRepository
    {
        /// <summary>
        /// Newest pending assignment for the patient, or null when none is active.
        /// Until the server link exists, the very first call seeds one default
        /// page-1 assignment; after it is completed the entry stays grey until a
        /// new assignment arrives.
        /// </summary>
        MonthlyQuestionnaireAssignment GetActive(string accountId);
        MonthlyQuestionnaireAssignment[] LoadHistory(string accountId);
        /// <summary>Upserts by assignmentId so a server push can overwrite local state idempotently.</summary>
        void Save(string accountId, MonthlyQuestionnaireAssignment assignment);
    }

    [Serializable]
    internal sealed class MonthlyAssignmentFile
    {
        public MonthlyQuestionnaireAssignment[] items = Array.Empty<MonthlyQuestionnaireAssignment>();
    }

    public sealed class JsonMonthlyAssignmentRepository : IMonthlyAssignmentRepository
    {
        /// <summary>
        /// Returns the newest pending assignment, seeding a default page-1 one
        /// only for a first-time patient with no history at all. Once that seed
        /// is completed the row greys out until a real assignment arrives, so
        /// reseeding here must be a one-shot.
        /// </summary>
        public MonthlyQuestionnaireAssignment GetActive(string accountId)
        {
            MonthlyQuestionnaireAssignment[] items = LoadFile(accountId).items ?? Array.Empty<MonthlyQuestionnaireAssignment>();
            MonthlyQuestionnaireAssignment active = FindNewestPending(items);
            if (active != null)
            {
                return active;
            }
            if (items.Length > 0)
            {
                return null;
            }

            active = new MonthlyQuestionnaireAssignment
            {
                assignmentId = "local:" + Guid.NewGuid().ToString("N"),
                patientAccount = accountId,
                issuedBy = "local-demo",
                issuedAt = DateTimeOffset.UtcNow.ToString("O"),
                modules = MonthlyQuestionnaireAssignment.ModuleJoa,
                status = MonthlyQuestionnaireAssignment.StatusPending,
                syncState = MonthlyQuestionnaireAssignment.SyncLocal,
            };
            Save(accountId, active);
            return active;
        }

        public MonthlyQuestionnaireAssignment[] LoadHistory(string accountId)
        {
            return LoadFile(accountId).items;
        }

        public void Save(string accountId, MonthlyQuestionnaireAssignment assignment)
        {
            if (string.IsNullOrWhiteSpace(accountId) || assignment == null)
            {
                Debug.LogWarning("Cannot save an empty monthly assignment.");
                return;
            }

            MonthlyAssignmentFile file = LoadFile(accountId);
            MonthlyQuestionnaireAssignment[] items = file.items ?? Array.Empty<MonthlyQuestionnaireAssignment>();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null && items[i].assignmentId == assignment.assignmentId)
                {
                    items[i] = assignment;
                    JsonFileStore.Save(FileName(accountId), new MonthlyAssignmentFile { items = items });
                    return;
                }
            }

            MonthlyQuestionnaireAssignment[] updated = new MonthlyQuestionnaireAssignment[items.Length + 1];
            Array.Copy(items, updated, items.Length);
            updated[items.Length] = assignment;
            JsonFileStore.Save(FileName(accountId), new MonthlyAssignmentFile { items = updated });
        }

        private static MonthlyQuestionnaireAssignment FindNewestPending(MonthlyQuestionnaireAssignment[] items)
        {
            MonthlyQuestionnaireAssignment newest = null;
            foreach (MonthlyQuestionnaireAssignment item in items ?? Array.Empty<MonthlyQuestionnaireAssignment>())
            {
                if (item == null || item.status != MonthlyQuestionnaireAssignment.StatusPending)
                {
                    continue;
                }
                if (newest == null || string.CompareOrdinal(item.issuedAt, newest.issuedAt) > 0)
                {
                    newest = item;
                }
            }
            return newest;
        }

        private static MonthlyAssignmentFile LoadFile(string accountId)
        {
            MonthlyAssignmentFile file;
            return JsonFileStore.TryLoad(FileName(accountId), out file) && file != null ? file
                : new MonthlyAssignmentFile();
        }

        private static string FileName(string accountId)
        {
            string safeId = accountId.Trim().Replace("/", "_").Replace("\\", "_");
            return "spine-monthly-assignments-" + safeId + ".json";
        }
    }
}
