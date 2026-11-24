package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject

/** Each supported prescription has a shipped coach and game scene. */
internal enum class BuiltInExercise(val key: String, val legacyNumber: String, val coach: String, val game: String, vararg val names: String) {
    DEAD_BUG("dead_bug", "7", "DeadBugPractice", "DeadBug_RingSkinC_Recorded", "死虫", "死虫式"),
    BIRD_DOG("bird_dog", "8", "BirdDogPractice", "BirdDog_RingSkinC_Recorded", "鸟狗", "鸟狗式"),
    HIP_THRUST("hip_thrust", "4", "HipThrustPractice", "HipThrust_RingBridge_NewScoring", "臀桥"),
    SIDE_BRIDGE("side_bridge", "5", "SideBridgePractice", "SideBridge_RingBridge_Dynamic", "侧桥");

    companion object {
        fun forKey(key: String) = entries.singleOrNull { it.key == key }
        // Older recordings reused numbers. Require the matching number and display name.
        fun forLegacy(id: String, name: String): BuiltInExercise? =
            forKey(id) ?: entries.singleOrNull { it.legacyNumber == id && name in it.names }
    }
}

/** Frozen release metadata for matching receipts and results; training uses only built-in scenes. */
data class AdmissionPrescriptionAsset(
    val actionVersionId: Long,
    val variantKey: String,
    val formatVersion: Int,
    val runtimeContractVersion: Int,
    val doseProfile: String,
    val supportedModes: List<String>,
    val fileSize: Long,
    val sha256: String
)

data class AdmissionPrescriptionItemV2(
    val itemId: Long,
    val position: Int,
    val action: String,
    val actionName: String,
    val sets: Int,
    val reps: Int,
    val holdSeconds: Int,
    val legacyId: String? = null,
    val asset: AdmissionPrescriptionAsset? = null,
    val level: String? = null
)

data class AdmissionPrescriptionV2(
    val orderId: Long,
    val prescriptionContractVersion: Int,
    val resourceManifestSha256: String,
    val items: List<AdmissionPrescriptionItemV2>
) {
    fun toJson(): JSONObject = JSONObject()
        .put("orderId", orderId)
        .put("prescriptionContractVersion", prescriptionContractVersion)
        .put("resourceManifestSha256", resourceManifestSha256)
        .put("items", JSONArray(items.map { it.toJson() }))

    fun computeManifestSha256(): String {
        val rows = JSONArray(items.map { item ->
            val a = item.asset
            if (a == null) JSONArray(listOf(item.itemId, item.position, item.action, item.sets, item.reps, item.holdSeconds)).apply { item.level?.let { put(it) } }
            else JSONArray(listOf(item.itemId, item.position, item.legacyId ?: item.action, a.actionVersionId, a.variantKey,
                a.formatVersion, a.runtimeContractVersion, a.doseProfile, JSONArray(a.supportedModes), a.fileSize,
                a.sha256, item.sets, item.reps, item.holdSeconds)).apply { item.level?.let { put(it) } }
        })
        val manifest = JSONArray(listOf(CONTRACT_VERSION, orderId, rows))
        return ResultDigest.sha256Hex(CanonicalJson.canonicalize(manifest))
    }

    companion object {
        const val CONTRACT_VERSION = 2
        const val MIN_ITEMS = 1
        const val MAX_ITEMS = 10

        fun fromJson(json: JSONObject, itemIdKey: String = "itemId"): AdmissionPrescriptionV2 {
            val version = json.getInt("prescriptionContractVersion")
            require(version == CONTRACT_VERSION) { "处方版本不受支持" }
            val items = json.getJSONArray("items")
            require(items.length() in MIN_ITEMS..MAX_ITEMS) { "处方动作数量异常" }
            val parsed = (0 until items.length()).map { parseItem(items.getJSONObject(it), itemIdKey) }
            require(parsed.map { it.position } == parsed.indices.toList() &&
                parsed.all { it.itemId > 0 && it.sets in 1..20 && it.reps in 1..100 && it.holdSeconds in 0..600 &&
                    (it.level == null || it.level in listOf("simple", "standard", "hard")) } &&
                parsed.distinctBy { it.itemId }.size == parsed.size &&
                parsed.all { (it.asset == null) == (parsed.first().asset == null) }) { "处方条目或剂量异常" }
            return AdmissionPrescriptionV2(json.getLong("orderId"), version, json.getString("resourceManifestSha256"), parsed)
        }

        fun fromActivationJson(json: JSONObject) = fromJson(json, "id")
        fun fromDirectiveJson(json: JSONObject) = fromJson(json)
    }
}

private fun parseItem(json: JSONObject, itemIdKey: String): AdmissionPrescriptionItemV2 {
    val legacy = !json.has("action")
    val id = if (legacy) json.getString("actionId") else null
    val name = json.optString("actionName")
    val key = if (id == null) json.getString("action") else BuiltInExercise.forLegacy(id, name)?.key ?: id
    val asset = if (legacy || json.has("asset")) {
        val a = json.getJSONObject("asset")
        val modes = a.getJSONArray("supportedModes")
        AdmissionPrescriptionAsset(a.getLong("actionVersionId"), a.getString("variantKey"), a.getInt("formatVersion"),
            a.getInt("runtimeContractVersion"), a.getString("doseProfile"), (0 until modes.length()).map { modes.getString(it) },
            a.getLong("fileSize"), a.getString("sha256"))
    } else null
    return AdmissionPrescriptionItemV2(json.getLong(itemIdKey), json.getInt("position"), key, name.ifBlank { key },
        json.getInt("sets"), json.getInt("reps"), json.getInt("holdSeconds"), id, asset,
        if (json.has("level")) json.getString("level").also { require(it in listOf("simple", "standard", "hard")) { "处方动作难度无效" } } else null)
}

private fun AdmissionPrescriptionItemV2.toJson(): JSONObject = JSONObject()
    .put("itemId", itemId).put("position", position).put("actionName", actionName)
    .put("sets", sets).put("reps", reps).put("holdSeconds", holdSeconds)
    .apply {
        level?.let { put("level", it) }
        if (legacyId == null) put("action", action) else put("actionId", legacyId)
        if (asset != null) {
            put("asset", JSONObject().put("actionVersionId", asset.actionVersionId).put("variantKey", asset.variantKey)
                .put("formatVersion", asset.formatVersion).put("runtimeContractVersion", asset.runtimeContractVersion)
                .put("doseProfile", asset.doseProfile).put("supportedModes", JSONArray(asset.supportedModes))
                .put("fileSize", asset.fileSize).put("sha256", asset.sha256))
        }
    }
