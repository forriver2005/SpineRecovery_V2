package com.metaspine.mobile.data

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption

private const val MOTION_PACKAGE_FILE_SUFFIX = ".motion.json"

internal fun selectLatestCachedMotionPackage(candidates: Array<out File>): File? =
    candidates
        .asSequence()
        .filter {
            it.isFile &&
                it.length() > 0L &&
                it.name.endsWith(MOTION_PACKAGE_FILE_SUFFIX)
        }
        .maxWithOrNull(compareBy<File>({ it.lastModified() }, { it.name }))

/**
 * 动作数据包（CoachMotionPackage JSON）的本地仓库。
 *
 * 这些 JSON 由服务器发布、AR 端消费：AR 端按关键帧驱动教练模型，
 * 因此新动作上线不需要重新打包 AR 应用。手机端只做三件事 ——
 * 落盘、签发 content:// URI、授权给 AR 应用读取。
 *
 * 不用 file:// URI：Android 7+ 跨应用传递会抛 FileUriExposedException。
 */
object MotionPackageStore {

    private const val CACHE_DIR_NAME = "motion_packages"
    private const val FILE_SUFFIX = MOTION_PACKAGE_FILE_SUFFIX
    private const val PROVIDER_SUFFIX = ".motionprovider"

    /** 与 AndroidManifest 里 provider 的 authorities 保持一致。 */
    private fun authority(context: Context): String =
        "${context.packageName}$PROVIDER_SUFFIX"

    private fun cacheDir(context: Context): File =
        File(context.cacheDir, CACHE_DIR_NAME).apply { mkdirs() }

    private fun fileFor(context: Context, actionId: String): File =
        File(cacheDir(context), "${actionId.sanitizedForFileName()}$FILE_SUFFIX")

    /**
     * 写入（或覆盖）某个动作的数据包。
     *
     * @param actionId 动作标识，如 "dead_bug"。同时作为文件名。
     * @param json     导出工具产出的 CoachMotionPackage JSON。
     * @return 落盘后的文件；写入失败返回 null。
     */
    fun save(context: Context, actionId: String, json: String): File? {
        if (actionId.isBlank() || json.isBlank()) {
            return null
        }
        return runCatching {
            val target = fileFor(context, actionId)
            val temporary = File.createTempFile(target.name, ".tmp", target.parentFile)
            try {
                temporary.writeText(json)
                Files.move(
                    temporary.toPath(),
                    target.toPath(),
                    StandardCopyOption.ATOMIC_MOVE,
                    StandardCopyOption.REPLACE_EXISTING
                )
                target
            } finally {
                temporary.delete()
            }
        }.getOrNull()
    }

    /** 某个动作是否已有本地缓存。 */
    fun has(context: Context, actionId: String): Boolean =
        actionId.isNotBlank() && fileFor(context, actionId).let { it.isFile && it.length() > 0 }

    /** 列出所有已缓存的 actionId。 */
    fun cachedActionIds(context: Context): List<String> =
        cacheDir(context).listFiles()
            ?.filter { it.isFile && it.name.endsWith(FILE_SUFFIX) }
            ?.map { it.name.removeSuffix(FILE_SUFFIX) }
            ?.sorted()
            ?: emptyList()

    /** 返回最近写入的有效缓存动作；修改时间相同时按文件名稳定选择。 */
    fun latestCachedActionId(context: Context): String? {
        val candidates = cacheDir(context).listFiles().orEmpty()
        val selected = selectLatestCachedMotionPackage(candidates)
        return selected?.name?.removeSuffix(FILE_SUFFIX)
    }

    /** 删除某个动作的缓存。 */
    fun remove(context: Context, actionId: String): Boolean =
        actionId.isNotBlank() && fileFor(context, actionId).delete()

    /**
     * 为某个动作签发可交给 AR 应用的 content:// URI。
     * 无缓存时返回 null —— 调用方应回退到 AR 内置动作。
     */
    fun uriFor(context: Context, actionId: String): Uri? {
        val file = fileFor(context, actionId)
        if (!file.isFile || file.length() == 0L) {
            return null
        }
        return runCatching {
            FileProvider.getUriForFile(context, authority(context), file)
        }.getOrNull()
    }

    /**
     * 把 URI 的读权限授予目标应用，并在 Intent 上打好授权标志。
     *
     * 两者都必需：AR 端的 JSON 藏在 extra 字符串里而不是 Intent 的 data/ClipData，
     * 单靠 FLAG_GRANT_READ_URI_PERMISSION 覆盖不到它，所以要显式 grantUriPermission。
     * 权限在 AR 应用进程存活期间有效，由 revoke 或系统回收结束。
     */
    fun grantReadTo(
        context: Context,
        intent: Intent,
        uri: Uri,
        targetPackage: String
    ) {
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        runCatching {
            context.grantUriPermission(
                targetPackage,
                uri,
                Intent.FLAG_GRANT_READ_URI_PERMISSION
            )
        }
    }

    /** 收回此前授予的读权限。AR 会话结束后调用。 */
    fun revokeReadFrom(context: Context, uri: Uri) {
        runCatching {
            context.revokeUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
    }

    /** 文件名只保留安全字符，避免 actionId 里的路径分隔符逃出缓存目录。 */
    private fun String.sanitizedForFileName(): String =
        map { if (it.isLetterOrDigit() || it == '_' || it == '-') it else '_' }.joinToString("")
}
