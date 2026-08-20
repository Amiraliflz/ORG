package ir.mrshoofer.ops

import java.util.Calendar
import java.util.TimeZone

/**
 * Gregorian ↔ Jalali (Shamsi) helpers for Ops Monitor timestamps.
 */
object ShamsiDate {
    private val tehran = TimeZone.getTimeZone("Asia/Tehran")

    fun formatIso(iso: String?): String {
        if (iso.isNullOrBlank()) return "—"
        return try {
            // Handles "2026-08-20T10:15:00Z" and "2026-08-20T10:15:00.1234567"
            val cleaned = iso.trim()
                .replace("Z", "+00:00")
            val tIndex = cleaned.indexOf('T')
            if (tIndex < 0) return iso
            val datePart = cleaned.substring(0, tIndex)
            val timePart = cleaned.substring(tIndex + 1).substringBefore('+').substringBefore('-')
            val dp = datePart.split('-').map { it.toInt() }
            val tp = timePart.split(':')
            val hour = tp.getOrNull(0)?.toIntOrNull() ?: 0
            val minute = tp.getOrNull(1)?.toIntOrNull() ?: 0
            val second = tp.getOrNull(2)?.substringBefore('.')?.toIntOrNull() ?: 0

            val cal = Calendar.getInstance(TimeZone.getTimeZone("UTC"))
            cal.set(dp[0], dp[1] - 1, dp[2], hour, minute, second)
            cal.set(Calendar.MILLISECOND, 0)
            format(cal.timeInMillis)
        } catch (_: Exception) {
            iso
        }
    }

    fun format(epochMs: Long = System.currentTimeMillis()): String {
        val cal = Calendar.getInstance(tehran)
        cal.timeInMillis = epochMs
        val gY = cal.get(Calendar.YEAR)
        val gM = cal.get(Calendar.MONTH) + 1
        val gD = cal.get(Calendar.DAY_OF_MONTH)
        val h = cal.get(Calendar.HOUR_OF_DAY)
        val mi = cal.get(Calendar.MINUTE)
        val (jy, jm, jd) = toJalali(gY, gM, gD)
        return String.format("%04d/%02d/%02d  %02d:%02d", jy, jm, jd, h, mi)
    }

    fun now(): String = format()

    /** Algorithm based on common civil Jalali conversion. */
    fun toJalali(gy: Int, gm: Int, gd: Int): Triple<Int, Int, Int> {
        val gdm = intArrayOf(0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334)
        var gy2 = gy - 1600
        var gm2 = gm - 1
        var gd2 = gd - 1
        var gDayNo = 365 * gy2 + (gy2 + 3) / 4 - (gy2 + 99) / 100 + (gy2 + 399) / 400
        gDayNo += gdm[gm2] + gd2
        if (gm2 > 1 && ((gy % 4 == 0 && gy % 100 != 0) || (gy % 400 == 0))) gDayNo++
        var jDayNo = gDayNo - 79
        val jNp = jDayNo / 12053
        jDayNo %= 12053
        var jy = 979 + 33 * jNp + 4 * (jDayNo / 1461)
        jDayNo %= 1461
        if (jDayNo >= 366) {
            jy += (jDayNo - 1) / 365
            jDayNo = (jDayNo - 1) % 365
        }
        val jm: Int
        val jd: Int
        if (jDayNo < 186) {
            jm = 1 + jDayNo / 31
            jd = 1 + jDayNo % 31
        } else {
            jm = 7 + (jDayNo - 186) / 30
            jd = 1 + (jDayNo - 186) % 30
        }
        return Triple(jy, jm, jd)
    }
}
