package com.metaspine.mobile.data

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import org.json.JSONArray
import org.json.JSONObject

data class StoredUserProfile(
    val heightCm: Int,
    val weightKg: Int,
    val gender: UserGender,
    val ageYears: Int = 0
)

/**
 * Small local database for durable user records.
 *
 * The complete user JSON keeps the existing model backward compatible, while
 * profile, questionnaire and tag tables make the requested information
 * explicit and independently queryable.
 */
class SpineFlowUserDatabase(
    context: Context,
    databaseName: String = DEFAULT_DATABASE_NAME
) : SQLiteOpenHelper(context.applicationContext, databaseName, null, DATABASE_VERSION) {

    override fun onConfigure(db: SQLiteDatabase) {
        super.onConfigure(db)
        db.setForeignKeyConstraintsEnabled(true)
    }

    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL(
            """
            CREATE TABLE $TABLE_USERS (
                $COLUMN_ACCOUNT_ID TEXT PRIMARY KEY NOT NULL,
                $COLUMN_LIFECYCLE TEXT NOT NULL,
                $COLUMN_USER_JSON TEXT NOT NULL,
                $COLUMN_UPDATED_AT INTEGER NOT NULL
            )
            """.trimIndent()
        )
        db.execSQL(
            """
            CREATE TABLE $TABLE_PROFILES (
                $COLUMN_ACCOUNT_ID TEXT PRIMARY KEY NOT NULL,
                $COLUMN_HEIGHT_CM INTEGER NOT NULL,
                $COLUMN_WEIGHT_KG INTEGER NOT NULL,
                $COLUMN_GENDER TEXT NOT NULL DEFAULT 'unspecified',
                $COLUMN_AGE_YEARS INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY($COLUMN_ACCOUNT_ID) REFERENCES $TABLE_USERS($COLUMN_ACCOUNT_ID)
                    ON DELETE CASCADE
            )
            """.trimIndent()
        )
        db.execSQL(
            """
            CREATE TABLE $TABLE_QUESTIONNAIRES (
                $COLUMN_ACCOUNT_ID TEXT NOT NULL,
                $COLUMN_QUESTIONNAIRE_TYPE TEXT NOT NULL,
                $COLUMN_PAYLOAD_JSON TEXT NOT NULL,
                $COLUMN_UPDATED_AT INTEGER NOT NULL,
                PRIMARY KEY($COLUMN_ACCOUNT_ID, $COLUMN_QUESTIONNAIRE_TYPE),
                FOREIGN KEY($COLUMN_ACCOUNT_ID) REFERENCES $TABLE_USERS($COLUMN_ACCOUNT_ID)
                    ON DELETE CASCADE
            )
            """.trimIndent()
        )
        db.execSQL(
            """
            CREATE TABLE $TABLE_TAGS (
                $COLUMN_ACCOUNT_ID TEXT NOT NULL,
                $COLUMN_POSITION INTEGER NOT NULL,
                $COLUMN_TAG TEXT NOT NULL,
                PRIMARY KEY($COLUMN_ACCOUNT_ID, $COLUMN_POSITION),
                FOREIGN KEY($COLUMN_ACCOUNT_ID) REFERENCES $TABLE_USERS($COLUMN_ACCOUNT_ID)
                    ON DELETE CASCADE
            )
            """.trimIndent()
        )
        createMetadataTable(db)
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        if (oldVersion < 2) {
            db.execSQL(
                "ALTER TABLE $TABLE_PROFILES ADD COLUMN $COLUMN_GENDER " +
                    "TEXT NOT NULL DEFAULT 'unspecified'"
            )
        }
        if (oldVersion < 3) {
            createMetadataTable(db)
        }
        if (oldVersion < 4) {
            db.execSQL(
                "ALTER TABLE $TABLE_PROFILES ADD COLUMN $COLUMN_AGE_YEARS " +
                    "INTEGER NOT NULL DEFAULT 0"
            )
        }
    }

    private fun createMetadataTable(db: SQLiteDatabase) {
        db.execSQL(
            """
            CREATE TABLE IF NOT EXISTS $TABLE_METADATA (
                $COLUMN_METADATA_KEY TEXT PRIMARY KEY NOT NULL,
                $COLUMN_METADATA_VALUE TEXT NOT NULL
            )
            """.trimIndent()
        )
    }

    fun saveUserSnapshot(user: DemoUserData, userJson: String) {
        val accountId = user.accountId.trim().lowercase()
        val now = System.currentTimeMillis()
        writableDatabase.runInTransaction {
            insertWithOnConflict(
                TABLE_USERS,
                null,
                ContentValues().apply {
                    put(COLUMN_ACCOUNT_ID, accountId)
                    put(COLUMN_LIFECYCLE, user.lifecycle.name)
                    put(COLUMN_USER_JSON, userJson)
                    put(COLUMN_UPDATED_AT, now)
                },
                SQLiteDatabase.CONFLICT_REPLACE
            )

            delete(TABLE_PROFILES, "$COLUMN_ACCOUNT_ID = ?", arrayOf(accountId))
            user.profile?.let { profile ->
                insertOrThrow(
                    TABLE_PROFILES,
                    null,
                    ContentValues().apply {
                        put(COLUMN_ACCOUNT_ID, accountId)
                        put(COLUMN_HEIGHT_CM, profile.heightCm)
                        put(COLUMN_WEIGHT_KG, profile.weightKg)
                        put(COLUMN_GENDER, profile.gender.wireValue)
                        put(COLUMN_AGE_YEARS, profile.ageYears)
                    }
                )
            }

            delete(TABLE_QUESTIONNAIRES, "$COLUMN_ACCOUNT_ID = ?", arrayOf(accountId))
            user.questionnaireSnapshots().forEach { (type, payload) ->
                insertOrThrow(
                    TABLE_QUESTIONNAIRES,
                    null,
                    ContentValues().apply {
                        put(COLUMN_ACCOUNT_ID, accountId)
                        put(COLUMN_QUESTIONNAIRE_TYPE, type)
                        put(COLUMN_PAYLOAD_JSON, payload)
                        put(COLUMN_UPDATED_AT, now)
                    }
                )
            }

            delete(TABLE_TAGS, "$COLUMN_ACCOUNT_ID = ?", arrayOf(accountId))
            user.userTags.forEachIndexed { index, tag ->
                insertOrThrow(
                    TABLE_TAGS,
                    null,
                    ContentValues().apply {
                        put(COLUMN_ACCOUNT_ID, accountId)
                        put(COLUMN_POSITION, index)
                        put(COLUMN_TAG, tag)
                    }
                )
            }
        }
    }

    fun loadUserJson(accountId: String): String? {
        return readableDatabase.query(
            TABLE_USERS,
            arrayOf(COLUMN_USER_JSON),
            "$COLUMN_ACCOUNT_ID = ?",
            arrayOf(accountId.trim().lowercase()),
            null,
            null,
            null,
            "1"
        ).use { cursor ->
            if (cursor.moveToFirst()) cursor.getString(0) else null
        }
    }

    fun loadProfile(accountId: String): StoredUserProfile? {
        return readableDatabase.query(
            TABLE_PROFILES,
            arrayOf(COLUMN_HEIGHT_CM, COLUMN_WEIGHT_KG, COLUMN_GENDER, COLUMN_AGE_YEARS),
            "$COLUMN_ACCOUNT_ID = ?",
            arrayOf(accountId.trim().lowercase()),
            null,
            null,
            null,
            "1"
        ).use { cursor ->
            if (cursor.moveToFirst()) {
                StoredUserProfile(
                    heightCm = cursor.getInt(0),
                    weightKg = cursor.getInt(1),
                    gender = UserGender.fromWireValue(cursor.getString(2)),
                    ageYears = cursor.getInt(3)
                )
            } else {
                null
            }
        }
    }

    fun loadQuestionnaireSnapshots(accountId: String): Map<String, String> {
        return readableDatabase.query(
            TABLE_QUESTIONNAIRES,
            arrayOf(COLUMN_QUESTIONNAIRE_TYPE, COLUMN_PAYLOAD_JSON),
            "$COLUMN_ACCOUNT_ID = ?",
            arrayOf(accountId.trim().lowercase()),
            null,
            null,
            COLUMN_QUESTIONNAIRE_TYPE
        ).use { cursor ->
            buildMap {
                while (cursor.moveToNext()) put(cursor.getString(0), cursor.getString(1))
            }
        }
    }

    fun loadUserTags(accountId: String): List<String> {
        return readableDatabase.query(
            TABLE_TAGS,
            arrayOf(COLUMN_TAG),
            "$COLUMN_ACCOUNT_ID = ?",
            arrayOf(accountId.trim().lowercase()),
            null,
            null,
            COLUMN_POSITION
        ).use { cursor ->
            buildList {
                while (cursor.moveToNext()) add(cursor.getString(0))
            }
        }
    }

    fun deleteUsers(accountIds: Collection<String>) {
        val normalizedIds = accountIds.map { it.trim().lowercase() }.distinct()
        if (normalizedIds.isEmpty()) return
        val placeholders = normalizedIds.joinToString(",") { "?" }
        writableDatabase.delete(
            TABLE_USERS,
            "$COLUMN_ACCOUNT_ID IN ($placeholders)",
            normalizedIds.toTypedArray()
        )
    }

    fun loadMetadata(key: String): String? {
        return readableDatabase.query(
            TABLE_METADATA,
            arrayOf(COLUMN_METADATA_VALUE),
            "$COLUMN_METADATA_KEY = ?",
            arrayOf(key),
            null,
            null,
            null,
            "1"
        ).use { cursor ->
            if (cursor.moveToFirst()) cursor.getString(0) else null
        }
    }

    fun saveMetadata(key: String, value: String) {
        writableDatabase.insertWithOnConflict(
            TABLE_METADATA,
            null,
            ContentValues().apply {
                put(COLUMN_METADATA_KEY, key)
                put(COLUMN_METADATA_VALUE, value)
            },
            SQLiteDatabase.CONFLICT_REPLACE
        )
    }

    private fun DemoUserData.questionnaireSnapshots(): Map<String, String> = buildMap {
        admissionIntake?.let { intake ->
            put(
                QUESTIONNAIRE_ADMISSION_INTAKE,
                JSONObject()
                    .put("situation", intake.situation)
                    .put("symptom", intake.symptom)
                    .put("goals", JSONArray(intake.goals))
                    .toString()
            )
        }
        admissionSurvey?.let { survey ->
            put(
                QUESTIONNAIRE_ADMISSION_RESULT,
                JSONObject()
                    .put("painLevel", survey.painLevel)
                    .put("requiresClinicalReview", survey.requiresClinicalReview)
                    .toString()
            )
        }
        latestPreTrainingCheck?.let { check ->
            put(
                QUESTIONNAIRE_PRE_TRAINING,
                JSONObject()
                    .put("prePainLevel", check.prePainLevel)
                    .put("symptomChange", check.symptomChange)
                    .put("painLocationChanged", check.painLocationChanged)
                    .put("allowedToTrain", check.allowedToTrain)
                    .toString()
            )
        }
        if (latestPainPoints.isNotEmpty()) {
            put(
                QUESTIONNAIRE_PAIN_POINTS,
                JSONArray().also { array ->
                    latestPainPoints.forEach { point ->
                        array.put(
                            JSONObject()
                                .put("bodyPart", point.bodyPart)
                                .put("bodyPartLabel", point.bodyPartLabel)
                                .put("view", point.view)
                                .put("painLevel", point.painLevel)
                        )
                    }
                }.toString()
            )
        }
        val feedback = history.mapNotNull { record ->
            record.feedback?.let {
                JSONObject()
                    .put("sessionId", record.sessionId)
                    .put("completedAt", record.completedAt)
                    .put("feeling", it.feeling)
                    .put("difficulty", it.difficulty)
            }
        }
        if (feedback.isNotEmpty()) {
            put(QUESTIONNAIRE_POST_TRAINING, JSONArray(feedback).toString())
        }
        mobileAdmissionAssessment?.let { assessment ->
            put(
                QUESTIONNAIRE_MOBILE_ADMISSION,
                JSONObject()
                    .put("completedAt", assessment.completedAt)
                    .put("flexion", assessment.flexion.toQuestionnaireJson())
                    .put("extension", assessment.extension.toQuestionnaireJson())
                    .toString()
            )
        }
    }

    private fun AdmissionMovementResult.toQuestionnaireJson(): JSONObject = JSONObject()
        .put("movementType", movementType.wireValue)
        .put("completionCount", completionCount)
        .put("painResponse", painResponse.wireValue)
        .put("firstPainPhase", firstPainPhase?.wireValue ?: "")
        .put("painLevel", painLevel)

    private inline fun <T> SQLiteDatabase.runInTransaction(block: SQLiteDatabase.() -> T): T {
        beginTransaction()
        return try {
            val result = block()
            setTransactionSuccessful()
            result
        } finally {
            endTransaction()
        }
    }

    companion object {
        const val DEFAULT_DATABASE_NAME = "spineflow_users.db"

        const val QUESTIONNAIRE_ADMISSION_INTAKE = "admission_intake"
        const val QUESTIONNAIRE_ADMISSION_RESULT = "admission_result"
        const val QUESTIONNAIRE_PRE_TRAINING = "pre_training"
        const val QUESTIONNAIRE_PAIN_POINTS = "pain_points"
        const val QUESTIONNAIRE_POST_TRAINING = "post_training"
        const val QUESTIONNAIRE_MOBILE_ADMISSION = "mobile_admission"

        private const val DATABASE_VERSION = 4
        private const val TABLE_USERS = "user_snapshots"
        private const val TABLE_PROFILES = "user_profiles"
        private const val TABLE_QUESTIONNAIRES = "questionnaire_snapshots"
        private const val TABLE_TAGS = "user_tags"
        private const val TABLE_METADATA = "app_metadata"

        private const val COLUMN_ACCOUNT_ID = "account_id"
        private const val COLUMN_LIFECYCLE = "lifecycle"
        private const val COLUMN_USER_JSON = "user_json"
        private const val COLUMN_UPDATED_AT = "updated_at"
        private const val COLUMN_HEIGHT_CM = "height_cm"
        private const val COLUMN_WEIGHT_KG = "weight_kg"
        private const val COLUMN_GENDER = "gender"
        private const val COLUMN_AGE_YEARS = "age_years"
        private const val COLUMN_QUESTIONNAIRE_TYPE = "questionnaire_type"
        private const val COLUMN_PAYLOAD_JSON = "payload_json"
        private const val COLUMN_POSITION = "position"
        private const val COLUMN_TAG = "tag"
        private const val COLUMN_METADATA_KEY = "metadata_key"
        private const val COLUMN_METADATA_VALUE = "metadata_value"
    }
}
