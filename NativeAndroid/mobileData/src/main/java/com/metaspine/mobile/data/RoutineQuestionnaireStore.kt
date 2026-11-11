package com.metaspine.mobile.data

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper

/** Independent schema: never migrates or writes the pre-training questionnaires database. */
class RoutineQuestionnaireStore(context: Context, name: String = "routine_questionnaires.db") :
    SQLiteOpenHelper(context.applicationContext, name, null, 1), RoutineQuestionnaireStorage {
    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL("CREATE TABLE bindings (account_id TEXT PRIMARY KEY, patient_id TEXT NOT NULL, fingerprint TEXT NOT NULL, rejected INTEGER NOT NULL DEFAULT 0)")
        db.execSQL("CREATE TABLE tasks (account_id TEXT NOT NULL, patient_id TEXT NOT NULL, task_id INTEGER NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(account_id, patient_id, task_id))")
    }
    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) = error("不支持的问卷缓存升级")
    @Synchronized fun clearAccount(accountId: String) {
        transaction { db ->
            db.delete("tasks", "account_id = ?", arrayOf(accountId))
            db.delete("bindings", "account_id = ?", arrayOf(accountId))
        }
    }
    private fun <T> transaction(block: (SQLiteDatabase) -> T): T {
        val db = writableDatabase
        db.beginTransaction()
        try { val result = block(db); db.setTransactionSuccessful(); return result }
        finally { db.endTransaction() }
    }
    @Synchronized override fun bind(scope: RoutineScope) {
        transaction { db ->
            if (!matches(scope)) {
                db.delete("tasks", "account_id = ?", arrayOf(scope.accountId))
                db.insertOrThrow("bindings", null, ContentValues().apply {
                    // The old binding is replaced only inside the same transaction as the purge.
                    db.delete("bindings", "account_id = ?", arrayOf(scope.accountId))
                    put("account_id", scope.accountId); put("patient_id", scope.patientId); put("fingerprint", scope.fingerprint)
                })
            }
        }
    }
    @Synchronized override fun matches(scope: RoutineScope): Boolean = readableDatabase.rawQuery(
        "SELECT 1 FROM bindings WHERE account_id=? AND patient_id=? AND fingerprint=?", args(scope)
    ).use { it.moveToFirst() }
    @Synchronized override fun rejected(scope: RoutineScope): Boolean = readableDatabase.rawQuery(
        "SELECT rejected FROM bindings WHERE account_id=? AND patient_id=? AND fingerprint=?", args(scope)
    ).use { !it.moveToFirst() || it.getInt(0) == 1 }
    @Synchronized override fun reject(scope: RoutineScope) {
        writableDatabase.update("bindings", ContentValues().apply { put("rejected", 1) },
            "account_id=? AND patient_id=? AND fingerprint=?", args(scope))
    }
    @Synchronized override fun read(scope: RoutineScope): List<LocalRoutineTask> {
        if (!matches(scope)) return emptyList()
        return readableDatabase.rawQuery("SELECT task_id,payload FROM tasks WHERE account_id=? AND patient_id=? ORDER BY task_id DESC",
            arrayOf(scope.accountId, scope.patientId)).use { cursor ->
            buildList {
                while (cursor.moveToNext()) {
                    val id = cursor.getLong(0)
                    val row = LocalRoutineTask.decode(SecureLocalData.open(aad(scope, id), cursor.getString(1)))
                    check(row.task.taskId == id)
                    add(row)
                }
            }
        }
    }
    @Synchronized override fun put(scope: RoutineScope, rows: List<LocalRoutineTask>): Boolean = transaction { db ->
        if (!matches(scope) || rejected(scope)) return@transaction false
        rows.forEach { row ->
            db.insertWithOnConflict("tasks", null, ContentValues().apply {
                put("account_id", scope.accountId); put("patient_id", scope.patientId); put("task_id", row.task.taskId)
                put("payload", SecureLocalData.seal(aad(scope, row.task.taskId), row.encode()))
            }, SQLiteDatabase.CONFLICT_REPLACE).also { check(it != -1L) { "问卷缓存未能保存" } }
        }
        true
    }
    private fun args(scope: RoutineScope) = arrayOf(scope.accountId, scope.patientId, scope.fingerprint)
    private fun aad(scope: RoutineScope, id: Long) = "routine:${scope.accountId}:${scope.patientId}:$id:${scope.fingerprint}"
}
