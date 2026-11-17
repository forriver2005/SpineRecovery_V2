using UnityEngine;
using UnityEditor;
using System.Reflection;

/// <summary>
/// 验证 V2 ResultPayload 序列化输出
/// 使用方法：Unity 菜单 > SpineFlow > Test V2 Result Payload
/// </summary>
public class ResultPayloadV2Test
{
    [MenuItem("SpineFlow/Test V2 Result Payload")]
    public static void TestV2Serialization()
    {
        // 使用反射创建私有类型的实例
        var sessionType = typeof(SpineFlowTrainingSession);
        var resultPayloadType = sessionType.GetNestedType("ResultPayload", BindingFlags.NonPublic);
        var modeResultType = sessionType.GetNestedType("ModeResult", BindingFlags.NonPublic);
        var itemResultType = sessionType.GetNestedType("ItemResult", BindingFlags.NonPublic);
        var metricType = sessionType.GetNestedType("MetricPayload", BindingFlags.NonPublic);
        var warningType = sessionType.GetNestedType("WarningPayload", BindingFlags.NonPublic);

        if (resultPayloadType == null)
        {
            Debug.LogError("[V2 Test] 找不到 ResultPayload 类型");
            return;
        }

        // 创建测试数据
        var result = System.Activator.CreateInstance(resultPayloadType);
        SetField(result, "sessionId", "test-session-123");
        SetField(result, "userId", "test-user-456");
        SetField(result, "requestToken", "test-token-789");
        SetField(result, "prescriptionOrderId", "order-abc");
        SetField(result, "resourceManifestSha256", "sha256-def");
        SetField(result, "status", "COMPLETED");
        SetField(result, "endReason", "NORMAL_COMPLETION");
        SetField(result, "createdAt", System.DateTime.UtcNow.ToString("o"));
        SetField(result, "completed", true);
        SetField(result, "simulated", false);
        SetField(result, "durationSeconds", 120);
        SetField(result, "overallScore", 85);
        SetField(result, "rawOverallScore", 83);
        SetField(result, "scoreMappingVersion", "formal-core-v3");
        SetField(result, "detailStatus", "MEASURED_AR_RESULT");
        SetField(result, "dataOrigin", "ar_pose_measurement_v2");
        SetField(result, "abnormalEvents", new string[0]);

        // 创建 ModeResult
        var coachMode = System.Activator.CreateInstance(modeResultType);
        SetField(coachMode, "mode", "coach");
        SetField(coachMode, "overallScore", 80);
        SetField(coachMode, "rawOverallScore", 78);
        SetField(coachMode, "plannedRepetitions", 10);
        SetField(coachMode, "completedRepetitions", 10);
        SetField(coachMode, "durationSeconds", 60);
        SetField(coachMode, "averageScore", 80);
        SetField(coachMode, "rawAverageScore", 78);
        SetField(coachMode, "bestScore", 90);
        SetField(coachMode, "rawBestScore", 88);
        SetField(coachMode, "scoreSampleCount", 100);
        SetField(coachMode, "scoreMappingVersion", "formal-core-v3");
        SetField(coachMode, "minimumUserMotionDegrees", 45.5f);
        SetField(coachMode, "minimumCoachMotionDegrees", 50.0f);
        SetField(coachMode, "metrics", System.Activator.CreateInstance(metricType));
        SetField(coachMode, "metricsAvailable", false);
        SetField(coachMode, "warnings", System.Array.CreateInstance(warningType, 0));
        SetField(coachMode, "passed", true);
        SetField(coachMode, "cheatingDetected", false);
        SetField(coachMode, "level", "standard");

        var gameMode = System.Activator.CreateInstance(modeResultType);
        SetField(gameMode, "mode", "game");
        SetField(gameMode, "overallScore", 90);
        SetField(gameMode, "rawOverallScore", 88);
        SetField(gameMode, "plannedRepetitions", 10);
        SetField(gameMode, "completedRepetitions", 10);
        SetField(gameMode, "durationSeconds", 60);
        SetField(gameMode, "averageScore", 90);
        SetField(gameMode, "rawAverageScore", 88);
        SetField(gameMode, "bestScore", 95);
        SetField(gameMode, "rawBestScore", 93);
        SetField(gameMode, "scoreSampleCount", 100);
        SetField(gameMode, "scoreMappingVersion", "formal-core-v3");
        SetField(gameMode, "minimumUserMotionDegrees", 50.0f);
        SetField(gameMode, "minimumCoachMotionDegrees", 52.0f);
        SetField(gameMode, "metrics", System.Activator.CreateInstance(metricType));
        SetField(gameMode, "metricsAvailable", false);
        SetField(gameMode, "warnings", System.Array.CreateInstance(warningType, 0));
        SetField(gameMode, "passed", true);
        SetField(gameMode, "cheatingDetected", false);
        SetField(gameMode, "level", "standard");

        // 创建 ModeResult 数组
        var modeResults = System.Array.CreateInstance(modeResultType, 2);
        modeResults.SetValue(coachMode, 0);
        modeResults.SetValue(gameMode, 1);

        // 创建 ItemResult
        var item = System.Activator.CreateInstance(itemResultType);
        SetField(item, "itemId", "dead_bug_coach");
        SetField(item, "itemName", "Dead Bug");
        SetField(item, "arTaskType", "core_stability");
        SetField(item, "modeResults", modeResults);

        // 创建 ItemResult 数组
        var itemResults = System.Array.CreateInstance(itemResultType, 1);
        itemResults.SetValue(item, 0);
        SetField(result, "itemResults", itemResults);

        // 序列化为 JSON
        string json = JsonUtility.ToJson(result, true);

        // 输出结果
        Debug.Log("========== V2 Result Payload 测试 ==========");
        Debug.Log(json);
        Debug.Log("==========================================");

        // 验证关键字段
        bool hasProtocolVersion = json.Contains("\"protocolVersion\": 2");
        bool hasItemResults = json.Contains("\"itemResults\"");
        bool hasModeResults = json.Contains("\"modeResults\"");
        bool hasNoActionResults = !json.Contains("\"actionResults\"");
        bool hasNoGameResults = !json.Contains("\"gameResults\"");
        bool hasPrescriptionOrderId = json.Contains("\"prescriptionOrderId\"");
        bool hasResourceManifestSha256 = json.Contains("\"resourceManifestSha256\"");

        Debug.Log($"\n验证结果:");
        Debug.Log($"✓ protocolVersion = 2: {(hasProtocolVersion ? "PASS" : "FAIL")}");
        Debug.Log($"✓ 包含 itemResults: {(hasItemResults ? "PASS" : "FAIL")}");
        Debug.Log($"✓ 包含 modeResults: {(hasModeResults ? "PASS" : "FAIL")}");
        Debug.Log($"✓ 无 actionResults (V1): {(hasNoActionResults ? "PASS" : "FAIL")}");
        Debug.Log($"✓ 无 gameResults (V1): {(hasNoGameResults ? "PASS" : "FAIL")}");
        Debug.Log($"✓ 包含 prescriptionOrderId: {(hasPrescriptionOrderId ? "PASS" : "FAIL")}");
        Debug.Log($"✓ 包含 resourceManifestSha256: {(hasResourceManifestSha256 ? "PASS" : "FAIL")}");

        if (hasProtocolVersion && hasItemResults && hasModeResults &&
            hasNoActionResults && hasNoGameResults &&
            hasPrescriptionOrderId && hasResourceManifestSha256)
        {
            Debug.Log("\n<color=green>✓ V2 格式验证通过！</color>");
        }
        else
        {
            Debug.LogError("\n✗ V2 格式验证失败，请检查上述字段");
        }
    }

    private static void SetField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(obj, value);
        }
        else
        {
            Debug.LogWarning($"字段 {fieldName} 未找到");
        }
    }
}
