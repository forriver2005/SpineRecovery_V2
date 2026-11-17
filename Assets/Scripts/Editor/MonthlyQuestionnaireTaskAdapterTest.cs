using System;
using System.Reflection;
using SpineRecovery.App;
using UnityEditor;
using UnityEngine;

/// <summary>Batch entry: -executeMethod MonthlyQuestionnaireTaskAdapterTest.TestMapping.</summary>
public static class MonthlyQuestionnaireTaskAdapterTest
{
    private static readonly Type Adapter = typeof(NativeQuestionnaireTask).Assembly.GetType(
        "SpineRecovery.App.MonthlyQuestionnaireTaskAdapter", true);
    private static int assertions;

    [MenuItem("SpineFlow/Test Monthly Questionnaire Task Adapter")]
    public static void TestMapping()
    {
        assertions = 0;
        TestJoaMapping();
        TestSfMpqMapping();
        TestEmptyAndDraft();
        TestChoiceBoundaries();
        TestSchemaCompatibility();
        Debug.Log("[Monthly Questionnaire Adapter] PASS: " + assertions + " assertions");
    }

    private static void TestJoaMapping()
    {
        NativeQuestionnaireTask task = JoaTask();
        int[] choices = { 0, 1, 3, 0, 1, 2, 0, 1, 2, 2, 2 };
        SetAnswers(task, choices);
        Check(Call<bool>("Supports", task), "JOA v1 is supported");
        Check(Call<int>("Module", task) == MonthlyQuestionnaireAssignment.ModuleJoa, "JOA module");
        MonthlyQuestionnaireRecord record = Call<MonthlyQuestionnaireRecord>("Restore", task);
        Check(record.backPain == 0 && record.legPain == 1 && record.walking == 3 && record.urination == 2,
            "JOA symptoms preserve option indices without score conversion");
        Check(record.activityLimits.Length == 7, "JOA activity array length");
        for (int i = 0; i < 7; i++) Check(record.activityLimits[i] == choices[i + 3], "JOA activity " + i);
        string json = Call<string>("AnswersJson", task, record);
        CheckAnswers(json, task, choices);
        Check(!json.Contains("sfmpq_"), "JOA does not include SF-MPQ answers");
        Array.Reverse(task.questions);
        record = Call<MonthlyQuestionnaireRecord>("Restore", task);
        Check(record.backPain == 0 && record.urination == 2 && record.activityLimits[2] == 2,
            "JOA restores by field ID rather than question position");
    }

    private static void TestSfMpqMapping()
    {
        NativeQuestionnaireTask task = SfMpqTask();
        int[] choices = { 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2 };
        SetAnswers(task, choices);
        Check(Call<bool>("Supports", task), "SF-MPQ v1 is supported");
        Check(Call<int>("Module", task) == MonthlyQuestionnaireAssignment.ModuleSfMpq, "SF-MPQ module");
        MonthlyQuestionnaireRecord record = Call<MonthlyQuestionnaireRecord>("Restore", task);
        Check(record.sfmpqSensory.Length == 11 && record.sfmpqAffective.Length == 4, "SF-MPQ array lengths");
        for (int i = 0; i < 11; i++) Check(record.sfmpqSensory[i] == choices[i], "SF-MPQ sensory " + i);
        for (int i = 0; i < 4; i++) Check(record.sfmpqAffective[i] == choices[i + 11], "SF-MPQ affective " + i);
        // The other page can contain answers, but it must not leak into this task.
        record.backPain = 3;
        record.legPain = 2;
        record.walking = 1;
        record.urination = 2;
        string json = Call<string>("AnswersJson", task, record);
        CheckAnswers(json, task, choices);
        Check(!json.Contains("back_pain") && !json.Contains("activity_limits_") && !json.Contains("urination"),
            "SF-MPQ does not include JOA answers");
        Array.Reverse(task.questions);
        record = Call<MonthlyQuestionnaireRecord>("Restore", task);
        Check(record.sfmpqSensory[3] == 3 && record.sfmpqAffective[0] == 3,
            "SF-MPQ restores by field ID rather than question position");
    }

    private static void TestEmptyAndDraft()
    {
        NativeQuestionnaireTask joa = JoaTask();
        MonthlyQuestionnaireRecord record = Call<MonthlyQuestionnaireRecord>("Restore", joa);
        Check(record.backPain == -1 && record.legPain == -1 && record.walking == -1 && record.urination == -1,
            "Unanswered JOA symptoms remain unselected");
        foreach (int choice in record.activityLimits) Check(choice == -1, "Unanswered JOA activity remains unselected");
        Check(Call<string>("AnswersJson", joa, record) == "{}", "Negative choices are omitted");
        joa.questions[0].answerSet = true;
        joa.questions[0].textValue = "option_0";
        joa.questions[1].textValue = "option_2";
        joa.questions[10].answerSet = true;
        joa.questions[10].textValue = "option_2";
        record = Call<MonthlyQuestionnaireRecord>("Restore", joa);
        Check(record.backPain == 0 && record.legPain == -1 && record.urination == 2, "JOA draft preserves explicit zero");
        Check(Call<string>("AnswersJson", joa, record) == "{\"back_pain\":\"option_0\",\"urination\":\"option_2\"}",
            "JOA draft emits only answered fields");

        NativeQuestionnaireTask sf = SfMpqTask();
        record = Call<MonthlyQuestionnaireRecord>("Restore", sf);
        CheckAnswers(Call<string>("AnswersJson", sf, record), sf, new int[15]);
        sf.questions[0].answerSet = true;
        sf.questions[0].textValue = "option_0";
        sf.questions[14].answerSet = true;
        sf.questions[14].textValue = "option_3";
        record = Call<MonthlyQuestionnaireRecord>("Restore", sf);
        Check(record.sfmpqSensory[0] == 0 && record.sfmpqSensory[1] == 0 && record.sfmpqAffective[3] == 3,
            "SF-MPQ draft preserves zero and defaults unanswered fields to 无");
        record.sfmpqSensory[0] = -1;
        Check(!Call<string>("AnswersJson", sf, record).Contains("\"sfmpq_sensory_0\""), "Negative SF-MPQ choice is omitted");
    }

    private static void TestChoiceBoundaries()
    {
        NativeQuestionnaireTask joa = JoaTask();
        joa.questions[0].answerSet = true;
        joa.questions[0].textValue = "option_4";
        joa.questions[10].answerSet = true;
        joa.questions[10].textValue = "option_3";
        MonthlyQuestionnaireRecord record = Call<MonthlyQuestionnaireRecord>("Restore", joa);
        Check(record.backPain == -1 && record.urination == -1, "Out-of-range JOA draft is not treated as an answer");
        record.backPain = 4;
        RejectChoice(joa, record, "JOA symptoms upper bound");
        record.backPain = -1;
        record.urination = 3;
        RejectChoice(joa, record, "JOA urination upper bound");
        record.urination = -1;
        record.activityLimits[0] = 3;
        RejectChoice(joa, record, "JOA activity upper bound");

        NativeQuestionnaireTask sf = SfMpqTask();
        sf.questions[0].answerSet = true;
        sf.questions[0].textValue = "option_4";
        record = Call<MonthlyQuestionnaireRecord>("Restore", sf);
        Check(record.sfmpqSensory[0] == 0, "Out-of-range SF-MPQ draft retains the form default");
        record.sfmpqSensory[0] = 4;
        RejectChoice(sf, record, "SF-MPQ sensory upper bound");
        record.sfmpqSensory[0] = 0;
        record.sfmpqAffective[3] = 4;
        RejectChoice(sf, record, "SF-MPQ affective upper bound");
    }

    private static void TestSchemaCompatibility()
    {
        NativeQuestionnaireTask task = JoaTask();
        task.templateCode = "questionnaire_a";
        Check(!Call<bool>("Supports", task) && Call<int>("Module", task) == 0, "Legacy template is rejected");
        task = JoaTask();
        task.templateVersion = 2;
        Check(!Call<bool>("Supports", task) && Call<int>("Module", task) == 0, "Unknown version is rejected");
        task = JoaTask();
        task.questions[0].id = "backPain";
        Check(!Call<bool>("Supports", task), "Unknown field is rejected");
        task = JoaTask();
        task.questions[0].type = "scale";
        Check(!Call<bool>("Supports", task), "Different answer type is rejected");
        task = JoaTask();
        task.questions[0].options[0].value = "0";
        Check(!Call<bool>("Supports", task), "Numeric string cannot replace option_N");
        task = JoaTask();
        Array.Resize(ref task.questions[10].options, 4);
        Check(!Call<bool>("Supports", task), "Different option count is rejected");
        task = SfMpqTask();
        Array.Resize(ref task.questions, 14);
        Check(!Call<bool>("Supports", task), "Incomplete template is rejected");
    }

    private static NativeQuestionnaireTask JoaTask()
    {
        string[] ids = { "back_pain", "leg_pain", "walking", "activity_limits_0", "activity_limits_1",
            "activity_limits_2", "activity_limits_3", "activity_limits_4", "activity_limits_5", "activity_limits_6", "urination" };
        var questions = new NativeQuestionnaireQuestion[11];
        for (int i = 0; i < questions.Length; i++) questions[i] = Question(ids[i], i < 3 ? 4 : 3);
        return new NativeQuestionnaireTask { taskId = 100, templateCode = "joa_self_assessment", templateVersion = 1, questions = questions };
    }

    private static NativeQuestionnaireTask SfMpqTask()
    {
        var questions = new NativeQuestionnaireQuestion[15];
        for (int i = 0; i < 11; i++) questions[i] = Question("sfmpq_sensory_" + i, 4);
        for (int i = 0; i < 4; i++) questions[i + 11] = Question("sfmpq_affective_" + i, 4);
        return new NativeQuestionnaireTask { taskId = 101, templateCode = "sf_mpq", templateVersion = 1, questions = questions };
    }

    private static NativeQuestionnaireQuestion Question(string id, int count)
    {
        var options = new NativeQuestionnaireOption[count];
        for (int i = 0; i < count; i++) options[i] = new NativeQuestionnaireOption { value = "option_" + i, label = "选项 " + i };
        return new NativeQuestionnaireQuestion { id = id, type = "single_choice", required = true, options = options };
    }

    private static void SetAnswers(NativeQuestionnaireTask task, int[] choices)
    {
        for (int i = 0; i < choices.Length; i++)
        {
            task.questions[i].answerSet = true;
            task.questions[i].textValue = "option_" + choices[i];
        }
    }

    private static void CheckAnswers(string json, NativeQuestionnaireTask task, int[] choices)
    {
        Check(json.StartsWith("{") && json.EndsWith("}"), "Answers are a JSON object");
        Check(json.Split(':').Length - 1 == choices.Length, "Only this task's answer fields are emitted");
        for (int i = 0; i < choices.Length; i++)
            Check(json.Contains("\"" + task.questions[i].id + "\":\"option_" + choices[i] + "\""), "JSON field " + task.questions[i].id);
    }

    private static void RejectChoice(NativeQuestionnaireTask task, MonthlyQuestionnaireRecord record, string message)
    {
        try { Call<string>("AnswersJson", task, record); }
        catch (ArgumentOutOfRangeException) { Check(true, message); return; }
        throw new InvalidOperationException(message + " was not rejected");
    }

    private static T Call<T>(string method, params object[] arguments)
    {
        try { return (T)Adapter.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[Monthly Questionnaire Adapter] " + message);
        assertions++;
    }
}
