package ir.mrshoofer.ops

import android.content.Context
import android.content.SharedPreferences

/**
 * Persistent session + multi-endpoint failover so the Ops APK keeps
 * reaching local/dev and the production VPS.
 */
class OpsSession(context: Context) {
    private val prefs: SharedPreferences =
        context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    var serverUrl: String
        get() = normalize(prefs.getString(KEY_SERVER, null) ?: BuildConfig.API_BASE)
        set(value) = prefs.edit().putString(KEY_SERVER, normalize(value)).apply()

    var lastWorkingUrl: String?
        get() = prefs.getString(KEY_WORKING, null)?.let { normalize(it) }
        set(value) {
            if (value == null) prefs.edit().remove(KEY_WORKING).apply()
            else prefs.edit().putString(KEY_WORKING, normalize(value)).apply()
        }

    var username: String
        get() = prefs.getString(KEY_USER, "") ?: ""
        set(value) = prefs.edit().putString(KEY_USER, value).apply()

    var password: String
        get() = prefs.getString(KEY_PASS, "") ?: ""
        set(value) = prefs.edit().putString(KEY_PASS, value).apply()

    var token: String?
        get() = prefs.getString(KEY_TOKEN, null)
        set(value) {
            if (value.isNullOrBlank()) prefs.edit().remove(KEY_TOKEN).apply()
            else prefs.edit().putString(KEY_TOKEN, value).apply()
        }

    var loggedIn: Boolean
        get() = prefs.getBoolean(KEY_LOGGED, false) && !token.isNullOrBlank()
        set(value) = prefs.edit().putBoolean(KEY_LOGGED, value).apply()

    fun saveLogin(server: String, user: String, pass: String, tokenValue: String) {
        prefs.edit()
            .putBoolean(KEY_LOGGED, true)
            .putString(KEY_SERVER, normalize(server))
            .putString(KEY_WORKING, normalize(server))
            .putString(KEY_USER, user)
            .putString(KEY_PASS, pass)
            .putString(KEY_TOKEN, tokenValue)
            .apply()
    }

    fun clearAuth(keepServer: Boolean = true) {
        val server = serverUrl
        prefs.edit()
            .putBoolean(KEY_LOGGED, false)
            .remove(KEY_TOKEN)
            .remove(KEY_PASS)
            .apply()
        if (keepServer) serverUrl = server
    }

    /** Ordered list of bases to try — production VPS first, then local fallbacks. */
    fun candidates(preferred: String? = null): List<String> {
        val out = LinkedHashSet<String>()
        preferred?.let { out += normalize(it) }
        // Production is primary
        out += VPS
        out += "https://www.mrshoofer.com"
        lastWorkingUrl?.let { out += it }
        out += serverUrl
        out += BuildConfig.API_BASE
        // Local / USB / LAN only as backup when VPS unreachable
        out += LOCALHOST
        out += EMULATOR
        out += "http://10.25.36.110:5055"
        return out.map { normalize(it) }.filter { it.isNotBlank() }.distinct()
    }

    companion object {
        const val PREFS = "ops"
        const val VPS = "https://mrshoofer.com"
        const val LOCALHOST = "http://127.0.0.1:5055"
        const val EMULATOR = "http://10.0.2.2:5055"
        private const val KEY_SERVER = "server_url"
        private const val KEY_WORKING = "last_working_url"
        private const val KEY_USER = "username"
        private const val KEY_PASS = "password"
        private const val KEY_TOKEN = "token"
        private const val KEY_LOGGED = "logged_in"

        fun normalize(raw: String): String =
            raw.trim().trimEnd('/').let { if (it.startsWith("http")) it else "http://$it" }
    }
}
