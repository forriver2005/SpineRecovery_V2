package com.metaspine.mobile.data

import org.json.JSONObject
import java.security.SecureRandom
import java.util.UUID

/** 激活预览：只够患者判断「是不是本人」，不含完整号码、patientId 与处方（设计 §8.5）。 */
data class ActivationPreview(
    val displayName: String,
    val idCardMasked: String,
    val doctorUsername: String,
    val expiresAt: String,
    val phoneMasked: String? = null
)

/** 核销成功后绑定到本地账号的脱敏身份（设计 §5.3 `BoundPatientIdentity`）。 */
data class BoundPatientIdentity(
    val displayName: String,
    val idCardMasked: String,
    val doctorUsername: String
)

data class ActivationRedeemResult(
    val patientId: String,
    val identity: BoundPatientIdentity,
    val prescription: AdmissionPrescriptionItem,
    val prescriptionV2: AdmissionPrescriptionV2
)

/** V2 首方/处方的冻结资源身份（合同 §8.2），下载与结果回传都必须保持这些身份。 */
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
)

/**
 * 阶段 C：患者端激活协议（预览 + 核销）。
 *
 * 当前测试版本：激活凭据直接使用医生建档手机号，所有构建一致，不发送短信验证码。
 * 核销接口不要求 PAT：凭据本身即授权；正式 PAT 由手机端自己生成并在请求前落盘，
 * 所以响应丢失后重试不会产生第二个 PAT（设计 §4.3）。
 */
object PatientActivationClient {
    const val PREVIEW_PATH = "/api/patient/activations/preview"
    const val REDEEM_PATH = "/api/patient/activations"


    /** 与服务端一致：11 位中国大陆手机号；输入时允许空格或连字符。 */
    private val NORMALIZED_CODE_PATTERN = Regex("^1[3-9][0-9]{9}$")
    private val SEPARATORS = Regex("[\\s\\-]")

    /** 规范化用户输入：去除手机号展示分隔符。 */
    fun normalizeInput(raw: String): String = raw.trim().replace(SEPARATORS, "")

    fun formatForDisplay(normalized: String): String = normalizeInput(normalized)

    fun isComplete(raw: String): Boolean = NORMALIZED_CODE_PATTERN.matches(normalizeInput(raw))

    fun newRequestId(): String = UUID.randomUUID().toString()

    /** 候选 PAT：`pat_` + 64 位小写十六进制（服务端 `validateActivationRedeemBody` 要求）。 */
    fun newProposedToken(): String {
        val bytes = ByteArray(32).also(SecureRandom()::nextBytes)
        return "pat_" + bytes.joinToString("") { "%02x".format(it) }
    }

    fun buildPreviewBody(activationCode: String, installationId: String): JSONObject = JSONObject()
        .put("activationCode", normalizeInput(activationCode))
        .put("installationId", installationId)

    /** 核销请求体：字段名与大小写必须与服务端白名单完全一致（多一个字段即 400）。 */
    fun buildRedeemBody(
        requestId: String,
        activationCode: String,
        proposedToken: String,
        installationId: String,
        accountId: String
    ): JSONObject = JSONObject()
        .put("requestId", requestId)
        .put("activationCode", normalizeInput(activationCode))
        .put("proposedToken", proposedToken)
        .put("installationId", installationId)
        .put("accountId", accountId.trim().lowercase())

    suspend fun preview(activationCode: String, installationId: String): Result<ActivationPreview> =
        request("POST", PREVIEW_PATH, buildPreviewBody(activationCode, installationId))
            .mapCatching(::parsePreview)

    suspend fun redeem(body: JSONObject): Result<ActivationRedeemResult> =
        request("POST", REDEEM_PATH, body).mapCatching(::parseRedeem)

    internal fun parsePreview(data: JSONObject): ActivationPreview = ActivationPreview(
        displayName = data.getString("displayName"),
        idCardMasked = data.getString("idCardMasked"),
        doctorUsername = data.getJSONObject("doctor").getString("username"),
        expiresAt = data.optString("expiresAt"),
        phoneMasked = data.opt("phoneMasked") as? String
    )

    internal fun parseRedeem(data: JSONObject): ActivationRedeemResult {
        val identity = data.getJSONObject("identity")
        val prescriptionV2 = parsePrescriptionV2(data.getJSONObject("prescription"))
        val first = prescriptionV2.items.first()
        val doctorUsername = data.optJSONObject("doctor")?.optString("username").orEmpty()
        return ActivationRedeemResult(
            patientId = data.getString("patientId"),
            identity = BoundPatientIdentity(
                displayName = identity.getString("displayName"),
                idCardMasked = identity.getString("idCardMasked"),
                doctorUsername = doctorUsername
            ),
            prescription = AdmissionPrescriptionItem(
                actionId = first.actionId,
                actionName = first.actionName,
                sets = first.sets,
                reps = first.reps,
                holdSeconds = first.holdSeconds
            ),
            prescriptionV2 = prescriptionV2
        )
    }

    /** 完整 V2 首方：1–10 项有序、每项带冻结资源身份；不再限制单项。 */
    internal fun parsePrescriptionV2(json: JSONObject): AdmissionPrescriptionV2 {
        val items = json.getJSONArray("items")
        require(items.length() in 1..10) { "激活凭据首张处方动作数量异常: ${items.length()}" }
        val list = (0 until items.length()).map { i ->
            val item = items.getJSONObject(i)
            val asset = item.getJSONObject("asset")
            val modes = asset.getJSONArray("supportedModes")
            AdmissionPrescriptionItemV2(
                itemId = item.getLong("itemId"),
                position = item.getInt("position"),
                actionId = item.getString("actionId"),
                actionName = item.getString("actionName"),
                sets = item.getInt("sets"),
                reps = item.getInt("reps"),
                holdSeconds = item.getInt("holdSeconds"),
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
        return AdmissionPrescriptionV2(
            orderId = json.getLong("orderId"),
            prescriptionContractVersion = json.getInt("prescriptionContractVersion"),
            resourceManifestSha256 = json.getString("resourceManifestSha256"),
            items = list
        )
    }

    /**
     * 失败文案（设计 §8.8）。
     *
     * 不存在/过期/撤销/已被他人使用在服务端统一为 422，这里也保持一致文案，
     * 避免手机端把「被别人用了」和「已过期」区分开而给出可探测的提示。
     */
    fun failureMessage(error: Throwable): String = when {
        error is ApiHttpException && error.httpStatus == 400 ->
            "请输入医生建档时填写的11位手机号"
        error is ApiHttpException && error.httpStatus == 426 ->
            "请升级患者端，使用医生建档的手机号激活"
        error is ApiHttpException && error.httpStatus == 409 ->
            "当前账号绑定信息冲突，请联系医生核对"
        error is ApiHttpException && error.httpStatus == 422 ->
            "手机号未匹配有效激活资格，请联系医生核对或重新签发"
        error is ApiHttpException && error.httpStatus == 429 -> "尝试次数过多，请稍后再试"
        error is ApiHttpException && error.httpStatus >= 500 ->
            error.message ?: "服务器暂时不可用，请稍后重试"
        error is ApiHttpException -> error.message ?: "激活失败，请稍后重试"
        else -> "无法连接服务器，请检查网络后重试"
    }

    private suspend fun request(
        method: String,
        path: String,
        body: JSONObject? = null
    ): Result<JSONObject> = patientRequestResult {
        val response = PatientHttpTransport.shared.request(path, method, body = body?.toString())
        response.requireSuccess("激活请求失败，HTTP "+response.status)
        val json = JSONObject(response.text)
        check(json.optInt("code", -1) == 0) { json.optString("message", "服务器返回错误") }
        json.getJSONObject("data")
    }
}
