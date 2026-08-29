package ir.mrshoofer.ops

import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.util.concurrent.TimeUnit

data class ComponentStatus(
    val name: String,
    val label: String,
    val isHealthy: Boolean,
    val details: String?,
    val responseMs: Int?
)

data class OpsStatus(
    val isHealthy: Boolean,
    val checkedAt: String?,
    val uptimePercent24h: Double,
    val components: List<ComponentStatus>
)

/**
 * Resilient Ops API client: endpoint failover + auto re-login so access
 * to local/dev or VPS is not lost when the network path changes.
 */
class OpsApi(private val session: OpsSession) {
    @Volatile
    var baseUrl: String = session.lastWorkingUrl ?: session.serverUrl
        private set

    private val client = OkHttpClient.Builder()
        .connectTimeout(8, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .retryOnConnectionFailure(true)
        .build()

    private val json = "application/json; charset=utf-8".toMediaType()

    fun ping(url: String = baseUrl): Result<Unit> = runCatching {
        val req = Request.Builder().url("${OpsSession.normalize(url)}/health").get().build()
        client.newCall(req).execute().use { resp ->
            if (!resp.isSuccessful) error("پاسخ نداد (${resp.code})")
        }
    }.recoverCatching { e -> throw Exception(friendlyNetError(e, url)) }

    /** Find first reachable endpoint among candidates. */
    fun discover(preferred: String? = null): Result<String> = runCatching {
        val list = session.candidates(preferred)
        var lastErr: String? = null
        for (url in list) {
            val r = ping(url)
            if (r.isSuccess) {
                adopt(url)
                return@runCatching url
            }
            lastErr = r.exceptionOrNull()?.message
        }
        error(lastErr ?: "هیچ endpoint در دسترس نیست")
    }

    fun login(username: String, password: String, preferredUrl: String? = null): Result<String> =
        runCatching {
            val urls = if (preferredUrl != null) {
                listOf(OpsSession.normalize(preferredUrl)) + session.candidates(preferredUrl)
            } else {
                session.candidates()
            }.distinct()

            var lastErr: String? = null
            for (url in urls) {
                if (ping(url).isFailure) continue
                val result = loginAt(url, username, password)
                if (result.isSuccess) {
                    val token = result.getOrThrow()
                    adopt(url)
                    session.saveLogin(url, username, password, token)
                    return@runCatching token
                }
                lastErr = result.exceptionOrNull()?.message
                // Wrong password — don't try every host with same bad creds forever
                if (lastErr?.contains("رمز") == true || lastErr?.contains("ادمین") == true)
                    error(lastErr!!)
            }
            error(lastErr ?: "ورود ناموفق — endpoint پیدا نشد")
        }

    fun status(): Result<OpsStatus> = withFailover("status") { url, token ->
        statusAt(url, token)
    }

    fun restart(): Result<String> = withFailover("restart") { url, token ->
        restartAt(url, token)
    }

    /**
     * Soft restart via the web app; if the app is hard-down, fall back to the
     * always-on Ops Agent (/ops-agent/restart) which runs independently on the VPS.
     */
    fun restartOrStart(): Result<String> {
        val primary = restart()
        if (primary.isSuccess) return primary

        ensureToken()
        val token = session.token
        if (token.isNullOrBlank())
            return Result.failure(Exception("نشست منقضی شده — دوباره وارد شوید"))

        var lastErr: Throwable? = primary.exceptionOrNull()
        for (base in OpsSession.agentBases()) {
            val agent = restartViaAgent(base, token)
            if (agent.isSuccess) return agent
            lastErr = agent.exceptionOrNull() ?: lastErr
        }
        return Result.failure(
            lastErr ?: Exception("راه‌اندازی ناموفق — نه وب‌اپ و نه Ops Agent پاسخ دادند")
        )
    }

    /** True if the always-on agent is reachable (main app may still be down). */
    fun agentReachable(): Boolean =
        OpsSession.agentBases().any { pingAgent(it).isSuccess }

    fun pingAgent(base: String = OpsSession.VPS): Result<Unit> = runCatching {
        val url = "${OpsSession.normalize(base)}/ops-agent/health"
        val req = Request.Builder().url(url).get().build()
        client.newCall(req).execute().use { resp ->
            if (!resp.isSuccessful) error("agent ${resp.code}")
        }
    }

    fun clearSession() {
        session.clearAuth(keepServer = true)
    }

    private fun adopt(url: String) {
        val n = OpsSession.normalize(url)
        baseUrl = n
        session.lastWorkingUrl = n
        session.serverUrl = n
    }

    private fun <T> withFailover(op: String, call: (url: String, token: String) -> Result<T>): Result<T> {
        ensureToken()
        var token = session.token
        if (token.isNullOrBlank()) return Result.failure(Exception("نشست منقضی شده — دوباره وارد شوید"))

        val urls = session.candidates(baseUrl)
        var lastErr: Throwable? = null

        for (url in urls) {
            var result = call(url, token!!)
            if (result.isSuccess) {
                adopt(url)
                return result
            }
            val msg = result.exceptionOrNull()?.message.orEmpty()
            if (msg.contains("نشست") || msg.contains("401")) {
                // Re-login then retry same host
                val refreshed = silentRelogin(url)
                if (refreshed.isSuccess) {
                    token = session.token
                    result = call(url, token!!)
                    if (result.isSuccess) {
                        adopt(url)
                        return result
                    }
                }
                lastErr = result.exceptionOrNull() ?: refreshed.exceptionOrNull()
                continue
            }
            lastErr = result.exceptionOrNull()
        }
        return Result.failure(lastErr ?: Exception("ارتباط با $op برقرار نشد"))
    }

    private fun ensureToken() {
        if (!session.token.isNullOrBlank()) return
        silentRelogin(baseUrl)
    }

    private fun silentRelogin(url: String): Result<String> {
        val user = session.username
        val pass = session.password
        if (user.isBlank() || pass.isBlank())
            return Result.failure(Exception("نشست منقضی شده — دوباره وارد شوید"))
        val r = loginAt(url, user, pass)
        if (r.isSuccess) {
            session.token = r.getOrThrow()
            session.loggedIn = true
            adopt(url)
        }
        return r
    }

    private fun loginAt(url: String, username: String, password: String): Result<String> = runCatching {
        val body = JSONObject()
            .put("username", username)
            .put("password", password)
            .toString()
            .toRequestBody(json)
        val req = Request.Builder()
            .url("${OpsSession.normalize(url)}/Admin/Ops/ApiLogin")
            .post(body)
            .header("Accept", "application/json")
            .build()
        client.newCall(req).execute().use { resp ->
            val text = resp.body?.string().orEmpty()
            if (!resp.isSuccessful) {
                val msg = runCatching { JSONObject(text).optString("message") }.getOrNull()
                    ?.takeIf { it.isNotBlank() }
                    ?: "ورود ناموفق (${resp.code})"
                error(msg)
            }
            val obj = JSONObject(text)
            if (!obj.optBoolean("success", false))
                error(obj.optString("message", "ورود ناموفق"))
            obj.optString("token").takeIf { it.isNotBlank() }
                ?: error("توکن دریافت نشد")
        }
    }.recoverCatching { e ->
        if (isAuthMessage(e.message)) throw e
        throw Exception(friendlyNetError(e, url))
    }

    private fun statusAt(url: String, token: String): Result<OpsStatus> = runCatching {
        val req = Request.Builder()
            .url("${OpsSession.normalize(url)}/Admin/Ops/ApiStatus")
            .get()
            .header("Accept", "application/json")
            .header("Authorization", "Bearer $token")
            .header("X-Ops-Token", token)
            .build()
        client.newCall(req).execute().use { resp ->
            val text = resp.body?.string().orEmpty()
            if (resp.code == 401 || resp.code == 302) error("نشست منقضی شده")
            if (!resp.isSuccessful) error("خطا در وضعیت (${resp.code})")
            parseStatus(JSONObject(text))
        }
    }.recoverCatching { e ->
        if (e.message?.contains("نشست") == true) throw e
        throw Exception(friendlyNetError(e, url))
    }

    private fun restartAt(url: String, token: String): Result<String> = runCatching {
        val restartClient = client.newBuilder().readTimeout(90, TimeUnit.SECONDS).build()
        val body = JSONObject().put("confirm", "RESTART").toString().toRequestBody(json)
        val req = Request.Builder()
            .url("${OpsSession.normalize(url)}/Admin/Ops/ApiRestart")
            .post(body)
            .header("Accept", "application/json")
            .header("Authorization", "Bearer $token")
            .header("X-Ops-Token", token)
            .build()
        restartClient.newCall(req).execute().use { resp ->
            val text = resp.body?.string().orEmpty()
            val jsonObj = runCatching { JSONObject(text) }.getOrElse { JSONObject() }
            val message = jsonObj.optString("message", text)
            if (resp.code == 401) error("نشست منقضی شده")
            if (!resp.isSuccessful || !jsonObj.optBoolean("success", false))
                error(message.ifBlank { "راه‌اندازی مجدد ناموفق" })
            message
        }
    }.recoverCatching { e ->
        if (e.message?.contains("نشست") == true || e.message?.contains("راه‌اندازی") == true) throw e
        throw Exception(e.message ?: "وب‌اپ در حال ری‌استارت است…")
    }

    private fun restartViaAgent(base: String, token: String): Result<String> = runCatching {
        val restartClient = client.newBuilder().readTimeout(120, TimeUnit.SECONDS).build()
        val body = JSONObject().put("confirm", "RESTART").toString().toRequestBody(json)
        val req = Request.Builder()
            .url("${OpsSession.normalize(base)}/ops-agent/restart")
            .post(body)
            .header("Accept", "application/json")
            .header("Authorization", "Bearer $token")
            .header("X-Ops-Token", token)
            .build()
        restartClient.newCall(req).execute().use { resp ->
            val text = resp.body?.string().orEmpty()
            val jsonObj = runCatching { JSONObject(text) }.getOrElse { JSONObject() }
            val message = jsonObj.optString("message", text)
            if (resp.code == 401) error("نشست منقضی شده")
            if (!resp.isSuccessful || !jsonObj.optBoolean("success", false))
                error(message.ifBlank { "Ops Agent نتوانست وب‌اپ را بالا بیاورد" })
            message.ifBlank { "وب‌اپ از طریق Ops Agent راه‌اندازی شد" }
        }
    }.recoverCatching { e ->
        if (e.message?.contains("نشست") == true || e.message?.contains("Ops Agent") == true) throw e
        throw Exception(friendlyNetError(e, "$base/ops-agent"))
    }

    private fun isAuthMessage(msg: String?): Boolean {
        val m = msg.orEmpty()
        return m.contains("رمز") || m.contains("ادمین") || m.contains("ورود") || m.contains("توکن")
    }

    private fun friendlyNetError(e: Throwable, url: String): String = when (e) {
        is java.net.ConnectException,
        is java.net.UnknownHostException,
        is java.net.SocketTimeoutException,
        is java.net.NoRouteToHostException ->
            "قطع ارتباط با $url"
        else -> e.message ?: "خطای شبکه"
    }

    private fun parseStatus(obj: JSONObject): OpsStatus {
        val components = mutableListOf<ComponentStatus>()
        val arr = obj.optJSONArray("components")
        if (arr != null) {
            for (i in 0 until arr.length()) {
                val c = arr.getJSONObject(i)
                components += ComponentStatus(
                    name = c.optString("name"),
                    label = c.optString("label", c.optString("name")),
                    isHealthy = c.optBoolean("isHealthy", false),
                    details = c.optString("details").takeIf { it.isNotBlank() },
                    responseMs = if (c.has("responseMs") && !c.isNull("responseMs")) c.optInt("responseMs") else null
                )
            }
        }
        return OpsStatus(
            isHealthy = obj.optBoolean("isHealthy", false),
            checkedAt = obj.optString("checkedAt").takeIf { it.isNotBlank() },
            uptimePercent24h = obj.optDouble("uptimePercent24h", 0.0),
            components = components
        )
    }
}
