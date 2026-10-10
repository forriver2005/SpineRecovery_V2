package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant

data class RoutineOption(val value: String, val label: String)
data class RoutineQuestion(
    val id: String, val type: String, val prompt: String, val required: Boolean,
    val options: List<RoutineOption> = emptyList(), val min: Int = 0, val max: Int = 0,
    val step: Int = 1, val minSelections: Int = 0, val maxSelections: Int = 1, val maxLength: Int = 500
)
data class RoutineSchema(val title: String, val questions: List<RoutineQuestion>)
data class RoutineTask(
    val taskId: Long, val code: String, val version: Int,
    val name: String, val contentStatus: String, val availableAt: String, val dueAt: String?,
    val deliveredAt: String?, val schema: RoutineSchema?, val raw: String
)
data class RoutineOpenList(val serverTime: String, val items: List<RoutineTask>)

/** The wire language is data-only: unknown fields/types, coercions and remote content fail closed. */
object RoutineQuestionnaireParser {
    private val identifier = Regex("[a-z][a-z0-9_]{0,63}")
    private val forbiddenText = Regex("[\\u0000-\\u001f\\u007f]|</?[a-z][^>]*>|\\b(?:https?://|ftp://|javascript:|data:text/html)|//[^\\s/]+\\.[^\\s/]+", RegexOption.IGNORE_CASE)

    internal fun exact(o: JSONObject, keys: Set<String>) {
        require(o.keys().asSequence().toSet() == keys) { "问卷字段不符合合同" }
    }
    internal fun text(o: JSONObject, key: String, limit: Int): String {
        val value = o.get(key)
        require(value is String && value.isNotEmpty() && value.length <= limit && !forbiddenText.containsMatchIn(value)) { "问卷文本不合法" }
        return value
    }
    internal fun integer(o: JSONObject, key: String, min: Long = 1, max: Long = 9007199254740991): Long {
        val value = o.get(key)
        require((value is Int || value is Long) && (value as Number).toLong() in min..max) { "问卷整数不合法" }
        return (value as Number).toLong()
    }
    internal fun timestamp(o: JSONObject, key: String): String {
        val value = o.get(key)
        require(value is String && value.length <= 40 && value.endsWith("Z")) { "问卷时间不合法" }
        Instant.parse(value)
        return value
    }
    private fun id(o: JSONObject, key: String): String = text(o, key, 64).also { require(identifier.matches(it)) }
    private fun array(value: JSONArray, min: Int, max: Int): List<JSONObject> {
        require(value.length() in min..max)
        return (0 until value.length()).map { value.getJSONObject(it) }
    }
    fun open(data: JSONObject): RoutineOpenList {
        exact(data, setOf("serverTime", "items"))
        val tasks = array(data.getJSONArray("items"), 0, 100).map { task(it, false) }
        require(tasks.map { it.taskId }.distinct().size == tasks.size)
        return RoutineOpenList(timestamp(data, "serverTime"), tasks)
    }
    fun detail(data: JSONObject, expectedId: Long): RoutineTask {
        exact(data, setOf("task"))
        return task(data.getJSONObject("task"), true).also { require(it.taskId == expectedId) }
    }
    fun task(o: JSONObject, withSchema: Boolean): RoutineTask {
        exact(o, setOf("taskId", "template", "availableAt", "dueAt", "status", "deliveredAt"))
        require(o.get("status") == "available")
        val template = o.getJSONObject("template")
        exact(template, setOf("code", "version", "name", "contentStatus") + if (withSchema) setOf("schema") else emptySet())
        val content = text(template, "contentStatus", 20)
        require(content in setOf("ready", "placeholder"))
        val available = timestamp(o, "availableAt")
        val due = if (o.isNull("dueAt")) null else timestamp(o, "dueAt")
        require(due == null || Instant.parse(due) >= Instant.parse(available))
        return RoutineTask(integer(o, "taskId"),
            id(template, "code"), integer(template, "version", max = Int.MAX_VALUE.toLong()).toInt(),
            text(template, "name", 100), content, available, due,
            if (o.isNull("deliveredAt")) null else timestamp(o, "deliveredAt"),
            if (withSchema) schema(template.getJSONObject("schema")) else null, o.toString())
    }
    fun schema(o: JSONObject): RoutineSchema {
        require(o.toString().toByteArray(Charsets.UTF_8).size <= 65536)
        exact(o, setOf("schemaVersion", "title", "questions"))
        integer(o, "schemaVersion", 1, 1)
        val questions = array(o.getJSONArray("questions"), 1, 100).map { q ->
            val type = text(q, "type", 30)
            val extra = when (type) {
                "single_choice" -> setOf("options")
                "multi_choice" -> setOf("options", "minSelections", "maxSelections")
                "scale" -> setOf("min", "max", "step")
                "boolean" -> emptySet()
                "short_text" -> setOf("maxLength")
                else -> error("不支持的问卷题型")
            }
            exact(q, setOf("id", "type", "prompt", "required") + extra)
            require(q.get("required") is Boolean)
            var result = RoutineQuestion(id(q, "id"), type, text(q, "prompt", 500), q.getBoolean("required"))
            if (type in setOf("single_choice", "multi_choice")) {
                val options = array(q.getJSONArray("options"), 2, 20).map {
                    exact(it, setOf("value", "label")); RoutineOption(id(it, "value"), text(it, "label", 200))
                }
                require(options.map { it.value }.distinct().size == options.size)
                result = result.copy(options = options)
                if (type == "multi_choice") {
                    val min = integer(q, "minSelections", 0, options.size.toLong()).toInt()
                    val max = integer(q, "maxSelections", 1, options.size.toLong()).toInt()
                    require(min <= max)
                    result = result.copy(minSelections = min, maxSelections = max)
                }
            }
            if (type == "scale") {
                val min = integer(q, "min", -10000, 10000).toInt()
                val max = integer(q, "max", -10000, 10000).toInt()
                val step = integer(q, "step", 1, 10000).toInt()
                require(min < max && (max - min) % step == 0)
                result = result.copy(min = min, max = max, step = step)
            }
            if (type == "short_text") result = result.copy(maxLength = integer(q, "maxLength", 1, 500).toInt())
            result
        }
        require(questions.map { it.id }.distinct().size == questions.size)
        return RoutineSchema(text(o, "title", 100), questions)
    }
    fun validateAnswers(schema: RoutineSchema, answers: JSONObject): Boolean = runCatching {
        require(answers.toString().toByteArray(Charsets.UTF_8).size <= 65536)
        require(answers.keys().asSequence().all { key -> schema.questions.any { it.id == key } })
        for (q in schema.questions) {
            if (!answers.has(q.id)) { require(!q.required); continue }
            val value = answers.get(q.id)
            when (q.type) {
                "single_choice" -> require(value is String && q.options.any { it.value == value })
                "multi_choice" -> {
                    require(value is JSONArray)
                    val selected = (0 until value.length()).map { value.get(it) }
                    require(selected.size in q.minSelections..q.maxSelections && selected.distinct().size == selected.size)
                    require(selected.all { v -> v is String && q.options.any { it.value == v } })
                }
                "scale" -> { val n = integer(answers, q.id, q.min.toLong(), q.max.toLong()); require((n - q.min) % q.step == 0L) }
                "boolean" -> require(value is Boolean)
                "short_text" -> require(value is String && value.codePointCount(0, value.length) <= q.maxLength && !Regex("[\\u0000-\\u001f\\u007f]").containsMatchIn(value))
            }
        }
    }.isSuccess
}
