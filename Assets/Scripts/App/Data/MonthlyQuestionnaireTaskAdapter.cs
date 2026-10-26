using System;
using System.Text;

namespace SpineRecovery.App
{
    /// <summary>Maps the two frozen server templates to the existing monthly forms.</summary>
    internal static class MonthlyQuestionnaireTaskAdapter
    {
        private static readonly string[] JoaIds =
        {
            "back_pain", "leg_pain", "walking",
            "activity_limits_0", "activity_limits_1", "activity_limits_2", "activity_limits_3",
            "activity_limits_4", "activity_limits_5", "activity_limits_6", "urination"
        };

        private static readonly string[] SfMpqIds =
        {
            "sfmpq_sensory_0", "sfmpq_sensory_1", "sfmpq_sensory_2", "sfmpq_sensory_3",
            "sfmpq_sensory_4", "sfmpq_sensory_5", "sfmpq_sensory_6", "sfmpq_sensory_7",
            "sfmpq_sensory_8", "sfmpq_sensory_9", "sfmpq_sensory_10",
            "sfmpq_affective_0", "sfmpq_affective_1", "sfmpq_affective_2", "sfmpq_affective_3"
        };

        internal static int Module(NativeQuestionnaireTask task)
        {
            if (task == null || task.templateVersion != 1) return 0;
            if (task.templateCode == "joa_self_assessment") return MonthlyQuestionnaireAssignment.ModuleJoa;
            if (task.templateCode == "sf_mpq") return MonthlyQuestionnaireAssignment.ModuleSfMpq;
            return 0;
        }

        internal static bool Supports(NativeQuestionnaireTask task)
        {
            int module = Module(task);
            if (module == 0 || task.questions == null) return false;
            string[] ids = Ids(module);
            if (task.questions.Length != ids.Length) return false;
            for (int i = 0; i < ids.Length; i++)
            {
                NativeQuestionnaireQuestion question = Find(task, ids[i]);
                int count = OptionCount(module, i);
                if (question == null || question.type != "single_choice" ||
                    question.options == null || question.options.Length != count) return false;
                for (int option = 0; option < count; option++)
                {
                    if (question.options[option] == null ||
                        question.options[option].value != "option_" + option) return false;
                }
            }
            return true;
        }

        internal static MonthlyQuestionnaireRecord Restore(NativeQuestionnaireTask task)
        {
            RequireSupported(task);
            int module = Module(task);
            var record = new MonthlyQuestionnaireRecord
            {
                modules = module,
                backPain = -1,
                legPain = -1,
                walking = -1,
                urination = -1,
                activityLimits = new[] { -1, -1, -1, -1, -1, -1, -1 },
                sfmpqSensory = new int[11],
                sfmpqAffective = new int[4]
            };
            string[] ids = Ids(module);
            for (int i = 0; i < ids.Length; i++)
            {
                NativeQuestionnaireQuestion question = Find(task, ids[i]);
                if (!question.answerSet) continue;
                for (int option = 0; option < question.options.Length; option++)
                {
                    if (question.textValue != question.options[option].value) continue;
                    SetChoice(record, module, i, option);
                    break;
                }
            }
            return record;
        }

        internal static string AnswersJson(NativeQuestionnaireTask task, MonthlyQuestionnaireRecord record)
        {
            RequireSupported(task);
            int module = Module(task);
            string[] ids = Ids(module);
            var json = new StringBuilder("{");
            for (int i = 0; i < ids.Length; i++)
            {
                int choice = Choice(record, module, i);
                if (choice < 0) continue;
                if (choice >= OptionCount(module, i)) throw new ArgumentOutOfRangeException(ids[i]);
                if (json.Length > 1) json.Append(',');
                json.Append('"').Append(ids[i]).Append("\":\"option_").Append(choice).Append('"');
            }
            return json.Append('}').ToString();
        }

        private static string[] Ids(int module)
        {
            return module == MonthlyQuestionnaireAssignment.ModuleJoa ? JoaIds : SfMpqIds;
        }

        private static int OptionCount(int module, int index)
        {
            return module == MonthlyQuestionnaireAssignment.ModuleJoa && index >= 3 ? 3 : 4;
        }

        private static NativeQuestionnaireQuestion Find(NativeQuestionnaireTask task, string id)
        {
            return Array.Find(task.questions, question => question != null && question.id == id);
        }

        private static int Choice(MonthlyQuestionnaireRecord record, int module, int index)
        {
            if (module == MonthlyQuestionnaireAssignment.ModuleSfMpq)
                return index < 11 ? record.sfmpqSensory[index] : record.sfmpqAffective[index - 11];
            switch (index)
            {
                case 0: return record.backPain;
                case 1: return record.legPain;
                case 2: return record.walking;
                case 10: return record.urination;
                default: return record.activityLimits[index - 3];
            }
        }

        private static void SetChoice(MonthlyQuestionnaireRecord record, int module, int index, int choice)
        {
            if (module == MonthlyQuestionnaireAssignment.ModuleSfMpq)
            {
                if (index < 11) record.sfmpqSensory[index] = choice;
                else record.sfmpqAffective[index - 11] = choice;
                return;
            }
            switch (index)
            {
                case 0: record.backPain = choice; break;
                case 1: record.legPain = choice; break;
                case 2: record.walking = choice; break;
                case 10: record.urination = choice; break;
                default: record.activityLimits[index - 3] = choice; break;
            }
        }

        private static void RequireSupported(NativeQuestionnaireTask task)
        {
            if (!Supports(task)) throw new ArgumentException("Unsupported monthly questionnaire schema", nameof(task));
        }
    }
}
