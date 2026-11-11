package com.metaspine.mobile.data

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Mutex

/** 每次前台检查最多三次尝试；成功（含无更新）后本进程不再检查。 */
internal class ConfigUpdateRetryRunner(
    private val nowMillis: () -> Long = { System.nanoTime() / 1_000_000 },
    private val pause: suspend (Long) -> Unit = { delay(it) }
) {
    private val mutex = Mutex()
    private var completed = false
    private var nextAttemptAt: Long? = null

    suspend fun <T> run(attempt: suspend () -> Result<T?>): T? {
        // 同时进入的界面不重复请求；前后台或账号变化取消后可重新进入。
        if (!mutex.tryLock()) return null
        try {
            if (completed) return null
            for (index in 0..2) {
                currentCoroutineContext().ensureActive()
                val remaining = nextAttemptAt?.let { it - nowMillis() } ?: 0L
                if (remaining > 0) pause(remaining)
                currentCoroutineContext().ensureActive()
                val result = try {
                    attempt()
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (error: Exception) {
                    Result.failure(error)
                }
                currentCoroutineContext().ensureActive()
                val error = result.exceptionOrNull()
                if (error is CancellationException) throw error
                if (result.isSuccess) {
                    completed = true
                    return result.getOrNull()
                }
                // 前两次失败退避；本轮耗尽后，下一次前台触发至少间隔一分钟。
                nextAttemptAt = nowMillis() + when (index) {
                    0 -> 5_000L
                    1 -> 15_000L
                    else -> 60_000L
                }
            }
            return null
        } finally {
            mutex.unlock()
        }
    }
}
