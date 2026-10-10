package com.metaspine.mobile

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID
import java.util.zip.ZipInputStream

/** Optional user-selected backup import. It never reads another installed app's private directory. */
class LegacyArchiveImportActivity : Activity() {
    private lateinit var accountId: String
    private var temporaryRoot: File? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        accountId = intent.getStringExtra("accountId").orEmpty()
        if (savedInstanceState == null) startActivityForResult(
            Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*")
                .addCategory(Intent.CATEGORY_OPENABLE)
                .putExtra(Intent.EXTRA_MIME_TYPES, arrayOf("application/json", "application/zip", "application/octet-stream")), 1)
    }

    @Deprecated("Android Activity result callback")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        val uri = data?.data
        if (requestCode != 1 || resultCode != RESULT_OK || uri == null) { finish(); return }
        AlertDialog.Builder(this).setTitle("确认导入目标")
            .setMessage("备份中的回放将归入当前账号 $accountId，请确认备份只包含本人的记录。已有患者绑定和档案会保留。")
            .setPositiveButton("导入") { _, _ -> performImport(uri) }
            .setNegativeButton("取消") { _, _ -> finish() }.setOnCancelListener { finish() }.show()
    }

    private fun performImport(uri: Uri) {
        Thread({
            val result = runCatching { importSelected(uri) }
            temporaryRoot?.deleteRecursively()
            runOnUiThread {
                val message = result.getOrElse { "导入未完成：${it.message}。部分条目可能已导入，原备份保留；重试会跳过已有记录。" }
                AlertDialog.Builder(this).setTitle("备份导入").setMessage(message)
                    .setPositiveButton("返回 App") { _, _ -> finish() }.setOnCancelListener { finish() }.show()
            }
        }, "MetaSpine Archive Import").start()
    }

    private fun importSelected(uri: Uri): String {
        val root = File(cacheDir, "unified_import_${UUID.randomUUID()}").apply { mkdirs(); temporaryRoot = this }
        val source = File(root, "selected")
        contentResolver.openInputStream(uri).use { input ->
            requireNotNull(input)
            source.outputStream().use { output ->
                val buffer = ByteArray(65536)
                var size = 0L
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    size += count
                    require(size <= 512L * 1024 * 1024) { "备份超过512 MiB" }
                    output.write(buffer, 0, count)
                }
            }
        }
        var userFile: File? = null
        val isZip = source.inputStream().use { it.read() == 0x50 && it.read() == 0x4b }
        val unpacked = File(root, "data").apply { mkdirs() }
        if (isZip) {
            var size = 0L
            ZipInputStream(source.inputStream()).use { zip ->
                while (true) {
                    val entry = zip.nextEntry ?: break
                    val target = File(unpacked, entry.name).canonicalFile
                    require(target.path.startsWith(unpacked.canonicalPath + File.separator)) { "备份路径不合法" }
                    if (entry.isDirectory) { target.mkdirs(); continue }
                    target.parentFile?.mkdirs()
                    target.outputStream().use { output ->
                        val buffer = ByteArray(65536)
                        while (true) {
                            val count = zip.read(buffer)
                            if (count < 0) break
                            size += count
                            require(size <= 1024L * 1024 * 1024) { "备份解压后超过1 GiB" }
                            output.write(buffer, 0, count)
                        }
                    }
                    if (target.name.startsWith("spine-user-") && target.extension == "json" && target.length() <= 8 * 1024 * 1024 && runCatching { JSONObject(target.readText()).optString("accountId").trim().lowercase() == accountId }.getOrDefault(false)) userFile = target
                }
            }
        } else userFile = source

        var records = 0
        userFile?.let { require(it.length() <= 8 * 1024 * 1024) { "用户JSON超过8 MiB，请分批导入" }; records = UnifiedMobileBridge.importLegacyUser(accountId, it.readText()) }
        val replayIds = mutableListOf<String>()
        if (isZip) {
            val replayRoot = File(requireNotNull(getExternalFilesDir(null)), "Replays").apply { mkdirs() }
            unpacked.walkTopDown().filter { it.isFile && it.name == "manifest.json" }.forEach { manifestFile ->
                val manifest = JSONObject(manifestFile.readText())
                require(manifest.optInt("formatVersion") == 3 && manifest.optBoolean("completed")) { "仅支持已完成的V3回放" }
                val id = manifest.getString("sessionId")
                require(id.matches(Regex("[A-Za-z0-9_-]{1,200}"))) { "回放编号不合法" }
                val directory = manifestFile.parentFile!!
                val payloadName = manifest.getString("payloadFile")
                require(payloadName == "poses.bin" && File(directory, "commit.ok").isFile) { "回放未完整提交" }
                val payload = File(directory, payloadName)
                require(payload.isFile) { "回放数据缺失" }
                val digest = java.security.MessageDigest.getInstance("SHA-256")
                payload.inputStream().use { input ->
                    val buffer = ByteArray(65536)
                    while (true) { val n = input.read(buffer); if (n < 0) break; digest.update(buffer, 0, n) }
                }
                val hash = digest.digest().joinToString("") { "%02x".format(it) }
                require(hash.equals(manifest.getString("payloadSha256"), true)) { "回放校验失败" }
                val target = File(replayRoot, id)
                if (!target.exists()) {
                    val staging = File(replayRoot, "import_${UUID.randomUUID()}.tmp").apply { mkdirs() }
                    for (name in listOf("manifest.json", payloadName, "commit.ok")) File(directory, name).copyTo(File(staging, name))
                    check(staging.renameTo(target)) { "回放发布失败" }
                    replayIds += id
                    UnifiedMobileBridge.addImportedReplays(accountId, JSONArray(listOf(id)).toString())
                }
            }
            UnifiedMobileBridge.addImportedReplays(accountId, JSONArray(replayIds).toString())
        }
        require(records > 0 || replayIds.isNotEmpty()) { "没有可导入的新记录；现有数据未覆盖" }
        // This is a uniquely-created cache directory, never the user's selected source.
        root.deleteRecursively()
        return "已导入 $records 条历史/疼痛记录和 ${replayIds.size} 个回放。现有患者绑定、档案和同编号记录已保留。"
    }
}
