package com.metaspine.mobile.data

import android.content.Context
import org.json.JSONObject
import java.io.File
import java.util.UUID

/**
 * 已发布固定资源的本地身份：医院/服务 + 订单 + 条目 + 动作版本 + variant + 原始字节摘要。
 *
 * 只记录已经过长度与 SHA-256 校验的文件；半文件、错误摘要和旧 scope 的产物不会出现在这里。
 */
data class PrescriptionAssetRecord(
    val origin: String,
    val serviceId: String,
    val hospitalKey: String,
    val orderId: Long,
    val itemId: Long,
    val actionVersionId: Long,
    val variantKey: String,
    val sha256: String,
    val byteCount: Long,
    val relativePath: String
) {
    internal fun toJson(): JSONObject = JSONObject()
        .put("origin", origin)
        .put("serviceId", serviceId)
        .put("hospitalKey", hospitalKey)
        .put("orderId", orderId)
        .put("itemId", itemId)
        .put("actionVersionId", actionVersionId)
        .put("variantKey", variantKey)
        .put("sha256", sha256)
        .put("byteCount", byteCount)
        .put("relativePath", relativePath)

    companion object {
        internal fun fromJson(json: JSONObject): PrescriptionAssetRecord = PrescriptionAssetRecord(
            origin = json.getString("origin"),
            serviceId = json.getString("serviceId"),
            hospitalKey = json.getString("hospitalKey"),
            orderId = json.getLong("orderId"),
            itemId = json.getLong("itemId"),
            actionVersionId = json.getLong("actionVersionId"),
            variantKey = json.getString("variantKey"),
            sha256 = json.getString("sha256"),
            byteCount = json.getLong("byteCount"),
            relativePath = json.getString("relativePath")
        )
    }
}

/**
 * P4 §4.1 缓存：按固定身份存放已验证文件，发布用“同目录临时文件 + rename”，
 * 读取前重新核对长度与摘要，任一不符即删除并报告缺失。
 */
class PrescriptionAssetStore(
    private val root: File,
    private val scope: BindingScope,
    private val elapsedMillis: () -> Long = { android.os.SystemClock.elapsedRealtime() }
) : PrescriptionAssetStorage {
    private val indexFile = File(root, "index.json")

    /** 下载目标路径：不含来源与 PAT，只用于本地定位。 */
    internal fun destinationFor(
        orderId: Long,
        itemId: Long,
        actionVersionId: Long,
        sha256: String,
        variantKey: String = "single"
    ): File {
        val sourceKey = ResultDigest.sha256Hex(listOf(scope.origin, scope.serviceId, scope.hospitalKey).joinToString("|"))
        val directory = File(root, "assets/$sourceKey")
        return File(directory, "o${orderId}_i${itemId}_v${actionVersionId}_${variantKey}_${sha256}.bin")
    }

    /** 临时文件必须与目标同目录，renameTo 才能保持原子性。 */
    internal fun stagingFileFor(destination: File): File =
        File(destination.parentFile, ".${destination.name}.${UUID.randomUUID()}.part")

    fun publish(
        orderId: Long,
        itemId: Long,
        actionVersionId: Long,
        variantKey: String,
        sha256: String,
        temp: File
    ): File {
        val destination = destinationFor(orderId, itemId, actionVersionId, sha256, variantKey)
        check(destination.parentFile?.isDirectory == true || destination.parentFile?.mkdirs() == true) {
            "资源缓存目录无法创建"
        }
        if (destination.exists() && !destination.delete()) {
            throw PrescriptionAssetException("旧缓存无法替换")
        }
        if (!temp.renameTo(destination)) {
            // 跨卷等情况：复制到同目录临时文件后再 rename，仍不暴露半文件。
            val staged = stagingFileFor(destination)
            temp.copyTo(staged, overwrite = true)
            if (!staged.renameTo(destination)) {
                staged.delete()
                throw PrescriptionAssetException("资源无法原子发布")
            }
            temp.delete()
        }
        val record = PrescriptionAssetRecord(
            origin = scope.origin,
            serviceId = scope.serviceId,
            hospitalKey = scope.hospitalKey,
            orderId = orderId,
            itemId = itemId,
            actionVersionId = actionVersionId,
            variantKey = variantKey,
            sha256 = sha256,
            byteCount = destination.length(),
            relativePath = destination.relativeTo(root).invariantSeparatorsPath
        )
        writeRecord(record)
        return destination
    }

    /** 读取已发布文件；长度或摘要不符时删除并返回 null（绝不返回“看起来像”的缓存）。 */
    fun verifiedFile(record: PrescriptionAssetRecord): File? {
        if (record.origin != scope.origin || record.serviceId != scope.serviceId ||
            record.hospitalKey != scope.hospitalKey) return null
        val file = File(root, record.relativePath)
        if (!file.isFile || file.length() != record.byteCount) {
            file.delete()
            removeRecord(record)
            return null
        }
        if (PrescriptionAssetClient.sha256Hex(file) != record.sha256) {
            file.delete()
            removeRecord(record)
            return null
        }
        return file
    }

    fun recordFor(orderId: Long, itemId: Long, sha256: String? = null): PrescriptionAssetRecord? =
        readIndex().firstOrNull {
            it.orderId == orderId && it.itemId == itemId &&
                it.origin == scope.origin && it.serviceId == scope.serviceId &&
                it.hospitalKey == scope.hospitalKey && (sha256 == null || it.sha256 == sha256)
        }

    /**
     * 已有且仍与清单摘要一致的文件；摘要不一致视为无缓存，强制重新下载。
     * 这里不返回“上一次的版本”——处方冻结的是具体字节，不是动作名。
     */
    override fun existing(orderId: Long, itemId: Long, sha256: String): File? {
        val record = recordFor(orderId, itemId, sha256) ?: return null
        return verifiedFile(record)
    }

    override suspend fun download(
        patientToken: String,
        order: DownloadedOrder,
        item: AdmissionPrescriptionItemV2,
        expectedLease: TrainingResourceLease
    ): PrescriptionAssetClient.VerifiedAsset {
        val asset = item.asset
        return PrescriptionAssetClient.download(
            patientToken = patientToken,
            downloadPath = asset.downloadPath,
            expectedSha256 = asset.sha256,
            expectedFileSize = asset.fileSize,
            origin = { scope.origin },
            expectedLease = expectedLease,
            expectedActionId = item.actionId,
            expectedVersionId = asset.actionVersionId,
            expectedVariantKey = asset.variantKey,
            elapsedMillis = elapsedMillis,
            publish = { temp ->
                publish(order.orderId, item.itemId, asset.actionVersionId, asset.variantKey,
                    asset.sha256, temp)
            }
        )
    }

    fun readIndex(): List<PrescriptionAssetRecord> = runCatching {
        if (!indexFile.isFile) return emptyList<PrescriptionAssetRecord>()
        val array = JSONObject(indexFile.readText(Charsets.UTF_8)).getJSONArray("records")
        (0 until array.length()).mapNotNull { index ->
            runCatching { PrescriptionAssetRecord.fromJson(array.getJSONObject(index)) }.getOrNull()
        }
    }.getOrDefault(emptyList())

    private fun writeRecord(record: PrescriptionAssetRecord) {
        val records = readIndex().filterNot {
            it.origin == record.origin && it.serviceId == record.serviceId && it.hospitalKey == record.hospitalKey &&
                it.orderId == record.orderId && it.itemId == record.itemId &&
                it.actionVersionId == record.actionVersionId && it.variantKey == record.variantKey && it.sha256 == record.sha256
        } + record
        persist(records)
    }

    private fun removeRecord(record: PrescriptionAssetRecord) {
        persist(readIndex().filterNot {
            it == record
        })
    }

    private fun persist(records: List<PrescriptionAssetRecord>) {
        val directory = indexFile.parentFile
        check(directory?.isDirectory == true || directory?.mkdirs() == true) { "资源缓存目录无法创建" }
        val temp = File(directory, ".index.${UUID.randomUUID()}.part")
        temp.writeText(JSONObject().put("records",
            org.json.JSONArray(records.map { it.toJson() })).toString(), Charsets.UTF_8)
        if (indexFile.exists()) indexFile.delete()
        if (!temp.renameTo(indexFile)) {
            temp.delete()
            throw PrescriptionAssetException("资源索引无法保存")
        }
    }

    companion object {
        /** FileProvider 的 `<files-path name="prescription_assets" path="prescription_assets/" />`。 */
        internal const val PROVIDER_AUTHORITY_SUFFIX = ".motionprovider"

        fun forContext(context: Context, scope: BindingScope): PrescriptionAssetStore =
            PrescriptionAssetStore(File(context.filesDir, LOCATION), scope)

        /** 只允许把本缓存根目录内的文件交给 AR；越界路径直接失败，不签发 URI。 */
        fun contentUriFor(context: Context, file: File): android.net.Uri? = runCatching {
            val root = File(context.filesDir, LOCATION).canonicalFile
            val canonical = file.canonicalFile
            require(canonical.path.startsWith(root.path + File.separator)) { "资源不在处方缓存目录内" }
            androidx.core.content.FileProvider.getUriForFile(
                context, context.packageName + PROVIDER_AUTHORITY_SUFFIX, canonical)
        }.getOrNull()

        /** URI 必须能通过实际 ContentResolver 打开；消费者仍需读取并验证资源合同。 */
        fun readableContentUriFor(context: Context, file: File): android.net.Uri? = runCatching {
            val uri = requireNotNull(contentUriFor(context, file))
            requireNotNull(context.contentResolver.openInputStream(uri)).use { input ->
                require(input.read() >= 0) { "处方文件为空或无法读取" }
            }
            uri
        }.getOrNull()

        private const val LOCATION = "prescription_assets"
    }
}
