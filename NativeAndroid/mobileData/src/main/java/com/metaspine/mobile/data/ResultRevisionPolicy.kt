package com.metaspine.mobile.data

import org.json.JSONObject

/**
 * P5.3 / P5.6：修订号与冻结身份的纯逻辑（不依赖 Android，可直接单测）。
 *
 * 规则：
 * - 同一份内容（摘要相同）重试 → 原样返回已有记录，修订号不变；
 * - 内容变化 → 修订号必须严格大于已发出的最大修订号（可比 1→2→7 跳跃）；
 * - 冻结身份（requestToken / createdAt / 处方号 / 清单摘要）不可改写；
 * - 依赖阻塞只记录在本地，重发时仍指向原练前事件。
 */
internal object ResultRevisionPolicy {

    fun next(
        scope: String,
        result: PrescriptionResultV2,
        frozen: FrozenResult,
        existing: ResultRecord?,
    ): ResultRecord {
        if (existing != null) {
            if (existing.scope != scope || existing.sessionId != result.sessionId) {
                throw RevisionConflict("结果不属于原冻结会话与绑定")
            }
            ensureSameIdentity(existing, result)
            if (existing.blockedBy != null &&
                result.preTrainingEventId != existing.blockedBy
            ) {
                throw RevisionConflict("练前事件依赖无法改写: 仍应等待 ${existing.blockedBy}")
            }
            if (existing.contentSha256 == frozen.contentSha256) return existing
            if (result.resultRevision <= existing.resultRevision) {
                throw RevisionConflict(
                    "修订号必须严格递增: 已有 ${existing.resultRevision}，收到 ${result.resultRevision}"
                )
            }
            val previous = try { PrescriptionResultV2.parse(JSONObject(existing.body)) }
                catch (_: Exception) { throw RevisionConflict("原冻结结果不可读取，不能覆盖") }
            if (previous.completed) throw RevisionConflict("已完成会话不能产生新修订")
            if (result.durationSeconds < previous.durationSeconds) throw RevisionConflict("会话时长不能减少")
            if (result.itemResults.size != previous.itemResults.size) throw RevisionConflict("处方条目不能改变")
            previous.itemResults.zip(result.itemResults).forEach { (oldItem, newItem) ->
                if (oldItem.itemId != newItem.itemId || oldItem.position != newItem.position ||
                    oldItem.actionId != newItem.actionId || oldItem.actionVersionId != newItem.actionVersionId ||
                    oldItem.variantKey != newItem.variantKey || oldItem.sha256 != newItem.sha256) {
                    throw RevisionConflict("冻结处方资源身份不能改变")
                }
                oldItem.modeResults.zip(newItem.modeResults).forEach { (oldMode, newMode) ->
                    val sameScore = if (oldMode.score == null || newMode.score == null) oldMode.score == newMode.score
                        else oldMode.score.toDouble() == newMode.score.toDouble()
                    if (oldMode.status == ResultSessionStatus.COMPLETED &&
                        (oldMode.copy(score = null) != newMode.copy(score = null) || !sameScore)) {
                        throw RevisionConflict("已完成模式的测量事实不能改变")
                    }
                    if ((oldMode.status != ResultSessionStatus.NOT_STARTED && newMode.status == ResultSessionStatus.NOT_STARTED) ||
                        newMode.completedReps < oldMode.completedReps || newMode.durationSeconds < oldMode.durationSeconds) {
                        throw RevisionConflict("模式状态、次数或时长不能回退")
                    }
                }
            }
        }
        return ResultRecord(
            scope = scope,
            sessionId = result.sessionId,
            requestToken = result.requestToken,
            createdAt = result.createdAt,
            prescriptionOrderId = result.prescriptionOrderId,
            resourceManifestSha256 = result.resourceManifestSha256,
            resultRevision = result.resultRevision,
            source = result.source,
            contentSha256 = frozen.contentSha256,
            body = frozen.body,
            canonicalUtf8 = frozen.canonicalUtf8,
            itemIds = result.itemResults.map { it.itemId },
            modeKeys = result.itemResults.flatMap { item ->
                item.modeResults.map { "${item.itemId}:${it.mode.wireValue}" }
            },
            preTrainingEventId = result.preTrainingEventId,
            blockedBy = existing?.blockedBy,
            ackState = AckState.Pending,
            acknowledgedRevision = existing?.acknowledgedRevision,
        )
    }

    fun ensureSameIdentity(existing: ResultRecord, result: PrescriptionResultV2) {
        if (existing.requestToken != result.requestToken) {
            throw RevisionConflict("同一 sessionId 的 requestToken 不可改写")
        }
        if (existing.createdAt != result.createdAt) {
            throw RevisionConflict("同一 sessionId 的 createdAt 不可改写")
        }
        if (existing.prescriptionOrderId != result.prescriptionOrderId) {
            throw RevisionConflict("同一 sessionId 的处方编号不可改写")
        }
        if (existing.resourceManifestSha256 != result.resourceManifestSha256) {
            throw RevisionConflict("同一 sessionId 的处方清单摘要不可改写")
        }
        if (existing.preTrainingEventId != result.preTrainingEventId) {
            throw RevisionConflict("同一 sessionId 的练前事件不能替换或删除")
        }
    }
}
