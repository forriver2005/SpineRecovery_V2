package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject

/**
 * V2 处方的完整冻结模型（技术文档 §8）。
 *
 * A02 首方与 D01 下行处方映射到同一模型：首方 `items[].id` 映射为 [AdmissionPrescriptionItemV2.itemId]，
 * 两种响应其余字段一致。本模型只承载传输事实，不包含剂量改写或执行算法。
 */
data class AdmissionPrescriptionAsset(
    val actionVersionId: Long,
    val variantKey: String,
    val formatVersion: Int,
    val runtimeContractVersion: Int,
    val doseProfile: String,
    val supportedModes: List<String>,
    val fileSize: Long,
    val sha256: String,
    val downloadPath: String
)

data class AdmissionPrescriptionItemV2(
    val itemId: Long,
    val position: Int,
    val actionId: String,
    val actionName: String,
    val sets: Int,
    val reps: Int,
    val holdSeconds: Int,
    val asset: AdmissionPrescriptionAsset
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

    /**
     * 固定清单摘要（服务器 `lib/prescription-v2-contract.js` 的 `manifestBytes`）。
     *
     * 有序数组 `[2, orderId, [[itemId, position, actionId, actionVersionId, variantKey,
     * formatVersion, runtimeContractVersion, doseProfile, supportedModes, fileSize, sha256,
     * sets, reps, holdSeconds], ...]]` 的紧凑 JSON UTF-8 摘要。
     *
     * 不是整个 D01 响应 hash，也不是结果摘要；回执 `directive_saved`/`assets_ready` 必须用它。
     */
    fun computeManifestSha256(): String {
        val rows = JSONArray(items.map { item ->
            val asset = item.asset
            JSONArray(listOf(
                item.itemId, item.position, item.actionId, asset.actionVersionId, asset.variantKey,
                asset.formatVersion, asset.runtimeContractVersion, asset.doseProfile,
                JSONArray(asset.supportedModes), asset.fileSize, asset.sha256,
                item.sets, item.reps, item.holdSeconds
            ))
        })
        val manifest = JSONArray(listOf(CONTRACT_VERSION, orderId, rows))
        return ResultDigest.sha256Hex(CanonicalJson.canonicalize(manifest))
    }

    companion object {
        const val CONTRACT_VERSION = 2
        const val MIN_ITEMS = 1
        const val MAX_ITEMS = 10

        /**
         * 解析 V2 处方。外层响应的 directiveVersion 由调用方单独校验；
         * 此处只负责处方自身版本与条目完整性，任何缺项都直接失败，不截项、不补默认动作。
         */
        fun fromJson(json: JSONObject, itemIdKey: String = "itemId"): AdmissionPrescriptionV2 {
            val version = json.optInt("prescriptionContractVersion", -1)
            require(version == CONTRACT_VERSION) { "处方合同版本不支持: $version（需要 2）" }
            val items = json.getJSONArray("items")
            require(items.length() in MIN_ITEMS..MAX_ITEMS) { "处方动作数量异常: ${items.length()}" }
            val parsed = (0 until items.length()).map { index ->
                parseItem(items.getJSONObject(index), itemIdKey)
            }
            require(parsed.map { it.position } == parsed.indices.toList()) { "处方条目顺序与 position 不一致" }
            return AdmissionPrescriptionV2(
                orderId = json.getLong("orderId"),
                prescriptionContractVersion = version,
                resourceManifestSha256 = json.getString("resourceManifestSha256"),
                items = parsed
            )
        }
    }
}

internal fun AdmissionPrescriptionItemV2.toJson(): JSONObject = JSONObject()
    .put("itemId", itemId)
    .put("position", position)
    .put("actionId", actionId)
    .put("actionName", actionName)
    .put("sets", sets)
    .put("reps", reps)
    .put("holdSeconds", holdSeconds)
    .put("asset", asset.toJson())

private fun parseItem(json: JSONObject, itemIdKey: String): AdmissionPrescriptionItemV2 {
    val asset = json.getJSONObject("asset")
    val modes = asset.getJSONArray("supportedModes")
    return AdmissionPrescriptionItemV2(
        itemId = json.getLong(itemIdKey),
        position = json.getInt("position"),
        actionId = json.getString("actionId"),
        actionName = json.getString("actionName"),
        sets = json.getInt("sets"),
        reps = json.getInt("reps"),
        holdSeconds = json.getInt("holdSeconds"),
        asset = AdmissionPrescriptionAsset(
            actionVersionId = asset.getLong("actionVersionId"),
            variantKey = asset.getString("variantKey"),
            formatVersion = asset.getInt("formatVersion"),
            runtimeContractVersion = asset.getInt("runtimeContractVersion"),
            doseProfile = asset.getString("doseProfile"),
            supportedModes = (0 until modes.length()).map { modes.getString(it) },
            fileSize = asset.getLong("fileSize"),
            sha256 = asset.getString("sha256"),
            downloadPath = asset.getString("downloadPath")
        )
    )
}

internal fun AdmissionPrescriptionAsset.toJson(): JSONObject = JSONObject()
    .put("actionVersionId", actionVersionId)
    .put("variantKey", variantKey)
    .put("formatVersion", formatVersion)
    .put("runtimeContractVersion", runtimeContractVersion)
    .put("doseProfile", doseProfile)
    .put("supportedModes", JSONArray(supportedModes))
    .put("fileSize", fileSize)
    .put("sha256", sha256)
    .put("downloadPath", downloadPath)
