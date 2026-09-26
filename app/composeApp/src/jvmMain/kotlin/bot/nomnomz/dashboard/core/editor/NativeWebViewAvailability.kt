// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import java.lang.reflect.InvocationTargetException
import java.lang.reflect.Method
import java.util.Locale
import java.util.concurrent.TimeUnit

/** Whether the operating system's own web view can host the editor page, and if not, why. */
sealed interface NativeWebViewAvailability {
    data object Available : NativeWebViewAvailability

    /** Windows without the Microsoft Edge WebView2 Runtime. Fixed by installing the Evergreen runtime. */
    data object MissingWebView2Runtime : NativeWebViewAvailability

    /** The web view binding could not load on this system (e.g. Linux without WebKitGTK). */
    data class Unavailable(val detail: String) : NativeWebViewAvailability
}

// Checks the operating system for a usable native web view BEFORE a window is built around one, so a missing
// runtime becomes a clear, fixable message instead of a blank window or a silent crash in native code.
object NativeWebViewProbe {
    /** Microsoft's Evergreen WebView2 Runtime bootstrapper page — the documented fix for a missing runtime. */
    const val WEBVIEW2_DOWNLOAD_URL: String = "https://developer.microsoft.com/microsoft-edge/webview2/"

    // The WebView2 Runtime's product client id, per Microsoft's "Detect if a WebView2 Runtime is already
    // installed" guidance: a per-machine install registers under HKLM (WOW6432Node on 64-bit Windows), a
    // per-user install under HKCU. A usable install has a `pv` (product version) that is neither empty nor 0.0.0.0.
    private const val RUNTIME_CLIENT_ID: String = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
    private val runtimeRegistryKeys: List<String> =
        listOf(
            "HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\EdgeUpdate\\Clients\\$RUNTIME_CLIENT_ID",
            "HKLM\\SOFTWARE\\Microsoft\\EdgeUpdate\\Clients\\$RUNTIME_CLIENT_ID",
            "HKCU\\Software\\Microsoft\\EdgeUpdate\\Clients\\$RUNTIME_CLIENT_ID",
        )

    fun probe(osName: String = System.getProperty("os.name").orEmpty()): NativeWebViewAvailability {
        if (isWindows(osName)) {
            if (runtimeRegistryKeys.none(::hasRuntimeVersion)) return NativeWebViewAvailability.MissingWebView2Runtime
            // Before the binding loads: the runtime reads its user-data folder from the process environment.
            if (!WebView2UserDataFolder.ensure()) {
                return NativeWebViewAvailability.Unavailable(WebView2UserDataFolder.VARIABLE)
            }
        }
        return loadBinding()
    }

    /** True when `reg query` output for a runtime key carries a real `pv` product version. */
    fun isUsableRuntimeVersion(regQueryOutput: String): Boolean {
        val version: String =
            regQueryOutput
                .lineSequence()
                .map { line -> line.trim() }
                .firstOrNull { line -> line.startsWith("pv ") || line.startsWith("pv\t") }
                ?.split(Regex("\\s+"))
                ?.lastOrNull()
                .orEmpty()
        return version.isNotEmpty() && version != "REG_SZ" && version != "0.0.0.0"
    }

    private fun isWindows(osName: String): Boolean = osName.lowercase(Locale.ROOT).startsWith("windows")

    private fun hasRuntimeVersion(key: String): Boolean =
        runCatching {
                val process: Process =
                    ProcessBuilder("reg", "query", key, "/v", "pv").redirectErrorStream(true).start()
                val output: String = process.inputStream.bufferedReader().use { reader -> reader.readText() }
                process.waitFor(5, TimeUnit.SECONDS) && process.exitValue() == 0 && isUsableRuntimeVersion(output)
            }
            .getOrDefault(false)

    // The binding's static initializer extracts and links its native library but only LOGS a failure, so the
    // class loading fine proves nothing. Calling its cheapest native entry point does: an unlinked library
    // throws UnsatisfiedLinkError right here, the one place a missing engine or unsupported CPU is cheap to find.
    private fun loadBinding(): NativeWebViewAvailability =
        try {
            val binding: Class<*> =
                Class.forName("ca.weblite.webview.WebViewNative", true, NativeWebViewProbe::class.java.classLoader)
            val probe: Method = binding.getDeclaredMethod("webview_cred_store_available")
            probe.isAccessible = true
            probe.invoke(null)
            NativeWebViewAvailability.Available
        } catch (error: Throwable) {
            val cause: Throwable = (error as? InvocationTargetException)?.targetException ?: error
            NativeWebViewAvailability.Unavailable(cause.message ?: cause::class.java.simpleName)
        }
}
