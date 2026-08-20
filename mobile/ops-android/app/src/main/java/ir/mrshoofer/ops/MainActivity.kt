package ir.mrshoofer.ops

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class LoginActivity : Activity() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private lateinit var session: OpsSession

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        session = OpsSession(this)
        if (session.loggedIn) {
            startActivity(Intent(this, MonitorActivity::class.java))
            finish()
            return
        }
        setContentView(R.layout.activity_login)

        findViewById<EditText>(R.id.serverUrl).setText(OpsSession.VPS)
        findViewById<EditText>(R.id.username).setText(session.username)
        if (session.password.isNotBlank()) {
            findViewById<EditText>(R.id.password).setText(session.password)
        }
        val loginBtn = findViewById<Button>(R.id.loginBtn)
        val err = findViewById<TextView>(R.id.errorText)

        loginBtn.setOnClickListener {
            val preferredRaw = findViewById<EditText>(R.id.serverUrl).text.toString().trim()
            val preferred = preferredRaw.takeIf { it.isNotBlank() }?.let { OpsSession.normalize(it) }
            val user = findViewById<EditText>(R.id.username).text.toString().trim()
            val pass = findViewById<EditText>(R.id.password).text.toString()
            if (user.isBlank() || pass.isBlank()) {
                err.visibility = View.VISIBLE
                err.text = "نام کاربری و رمز را وارد کنید"
                return@setOnClickListener
            }
            loginBtn.isEnabled = false
            loginBtn.text = "در حال اتصال…"
            err.visibility = View.GONE
            val api = OpsApi(session)
            scope.launch {
                val result = withContext(Dispatchers.IO) {
                    api.login(user, pass, preferred)
                }
                loginBtn.isEnabled = true
                loginBtn.text = "ورود"
                result.onSuccess {
                    startActivity(Intent(this@LoginActivity, MonitorActivity::class.java))
                    finish()
                }.onFailure {
                    err.visibility = View.VISIBLE
                    err.text = it.message ?: "خطا در ورود"
                }
            }
        }
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }
}

class MonitorActivity : Activity() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var session: OpsSession
    private lateinit var api: OpsApi
    private lateinit var componentsContainer: LinearLayout
    private lateinit var statusPill: TextView
    private lateinit var heroDot: View
    private lateinit var heroText: TextView
    private lateinit var uptimeText: TextView
    private lateinit var lastCheckText: TextView
    private lateinit var endpointText: TextView
    private lateinit var messageText: TextView
    private lateinit var restartBtn: Button
    private lateinit var refreshBtn: Button
    private var restarting = false

    private val autoRefresh = object : Runnable {
        override fun run() {
            if (!restarting) refresh(silent = true)
            handler.postDelayed(this, 20_000)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_monitor)
        session = OpsSession(this)
        if (!session.loggedIn) {
            startActivity(Intent(this, LoginActivity::class.java))
            finish()
            return
        }
        api = OpsApi(session)

        componentsContainer = findViewById(R.id.componentsContainer)
        statusPill = findViewById(R.id.statusPill)
        heroDot = findViewById(R.id.heroDot)
        heroText = findViewById(R.id.heroText)
        uptimeText = findViewById(R.id.uptimeText)
        lastCheckText = findViewById(R.id.lastCheckText)
        endpointText = findViewById(R.id.endpointText)
        messageText = findViewById(R.id.messageText)
        restartBtn = findViewById(R.id.restartBtn)
        refreshBtn = findViewById(R.id.refreshBtn)
        updateEndpointLabel()

        refreshBtn.setOnClickListener { refresh() }
        restartBtn.setOnClickListener { confirmRestart() }
        findViewById<Button>(R.id.logoutBtn).setOnClickListener {
            api.clearSession()
            startActivity(Intent(this, LoginActivity::class.java))
            finish()
        }
        // Re-discover path on open
        scope.launch {
            withContext(Dispatchers.IO) { api.discover() }
            updateEndpointLabel()
            refresh()
        }
    }

    private fun updateEndpointLabel() {
        val url = api.baseUrl
        val label = when {
            url.contains("mrshoofer.com") -> "VPS · $url"
            url.contains("127.0.0.1") || url.contains("10.0.2.2") -> "USB/لوکال · $url"
            else -> "شبکه · $url"
        }
        endpointText.text = "مسیر اتصال: $label"
    }

    private fun confirmRestart() {
        AlertDialog.Builder(this)
            .setTitle("راه‌اندازی مجدد وب‌اپ")
            .setMessage("فقط سرویس اپلیکیشن مسترشوفر ری‌استارت می‌شود — نه کل ماشین. ادامه؟")
            .setPositiveButton("بله، ری‌استارت اپ") { _, _ -> doRestart() }
            .setNegativeButton("انصراف", null)
            .show()
    }

    private fun doRestart() {
        restarting = true
        setBusy(true, "در حال ری‌استارت وب‌اپ…")
        showMessage("دستور ری‌استارت وب‌اپ ارسال شد…")
        scope.launch {
            val result = withContext(Dispatchers.IO) { api.restart() }
            result.onSuccess { msg ->
                showMessage(msg)
                pollUntilHealthy()
            }.onFailure {
                showMessage(it.message ?: "در حال بررسی مجدد…")
                pollUntilHealthy()
            }
        }
    }

    private suspend fun pollUntilHealthy() {
        heroText.text = "در حال بالا آمدن…"
        statusPill.text = "…"
        for (i in 1..45) {
            delay(2000)
            withContext(Dispatchers.IO) { api.discover() }
            updateEndpointLabel()
            val result = withContext(Dispatchers.IO) { api.status() }
            if (result.isSuccess) {
                render(result.getOrThrow())
                showMessage("وب‌اپ دوباره فعال شد")
                restarting = false
                setBusy(false)
                return
            }
            val msg = result.exceptionOrNull()?.message.orEmpty()
            if (msg.contains("نشست") && session.password.isBlank()) {
                restarting = false
                setBusy(false)
                goLogin()
                return
            }
            showMessage("منتظر وب‌اپ… ($i)")
        }
        restarting = false
        setBusy(false)
        showMessage("وب‌اپ هنوز پاسخ نمی‌دهد — بعداً بروزرسانی کنید")
        Toast.makeText(this, "وب‌اپ هنوز بالا نیامده", Toast.LENGTH_LONG).show()
    }

    private fun setBusy(busy: Boolean, restartLabel: String = "راه‌اندازی مجدد وب‌اپ") {
        refreshBtn.isEnabled = !busy
        restartBtn.isEnabled = !busy
        restartBtn.text = if (busy) restartLabel else "راه‌اندازی مجدد وب‌اپ"
    }

    private fun goLogin() {
        api.clearSession()
        startActivity(Intent(this, LoginActivity::class.java))
        finish()
    }

    override fun onResume() {
        super.onResume()
        handler.postDelayed(autoRefresh, 20_000)
    }

    override fun onPause() {
        handler.removeCallbacks(autoRefresh)
        super.onPause()
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }

    private fun refresh(silent: Boolean = false) {
        if (!silent) {
            refreshBtn.isEnabled = false
            refreshBtn.text = "در حال بروزرسانی…"
        }
        scope.launch {
            if (!silent) {
                withContext(Dispatchers.IO) { api.discover() }
                updateEndpointLabel()
            }
            val result = withContext(Dispatchers.IO) { api.status() }
            if (!silent) {
                refreshBtn.isEnabled = true
                refreshBtn.text = "بروزرسانی وضعیت"
            }
            result.onSuccess {
                updateEndpointLabel()
                render(it)
            }.onFailure {
                if (it.message?.contains("نشست") == true && session.password.isBlank()) {
                    goLogin()
                } else if (!silent) {
                    heroText.text = "قطع ارتباط"
                    statusPill.text = "OFF"
                    statusPill.setTextColor(resources.getColor(R.color.ink, null))
                    statusPill.setBackgroundResource(R.drawable.bg_pill_down)
                    heroDot.setBackgroundResource(R.drawable.bg_dot_down)
                    Toast.makeText(this@MonitorActivity, it.message, Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    private fun render(status: OpsStatus) {
        val up = status.isHealthy
        heroText.text = if (up) "فعال" else "قطع"
        statusPill.text = if (up) "UP" else "DOWN"
        if (up) {
            statusPill.setTextColor(resources.getColor(R.color.ink_inverse, null))
            statusPill.setBackgroundResource(R.drawable.bg_pill_up)
            heroDot.setBackgroundResource(R.drawable.bg_dot_up)
        } else {
            statusPill.setTextColor(resources.getColor(R.color.ink, null))
            statusPill.setBackgroundResource(R.drawable.bg_pill_down)
            heroDot.setBackgroundResource(R.drawable.bg_dot_down)
        }
        uptimeText.text = "آپتایم ۲۴ساعت: ${status.uptimePercent24h}%"
        lastCheckText.text = "آخرین بررسی: ${ShamsiDate.formatIso(status.checkedAt)}"

        componentsContainer.removeAllViews()
        status.components.forEach { c ->
            val row = layoutInflater.inflate(R.layout.item_component, componentsContainer, false)
            row.findViewById<TextView>(R.id.componentName).text = c.label
            row.findViewById<TextView>(R.id.componentDetail).text = buildString {
                append(if (c.isHealthy) "سالم" else "ناپایدار")
                c.details?.let { append(" · "); append(it) }
                c.responseMs?.let { append(" · "); append("${it}ms") }
            }
            row.findViewById<View>(R.id.componentDot)
                .setBackgroundResource(if (c.isHealthy) R.drawable.bg_dot_up else R.drawable.bg_dot_down)
            componentsContainer.addView(row)
        }
    }

    private fun showMessage(msg: String) {
        messageText.visibility = View.VISIBLE
        messageText.text = msg
    }
}
