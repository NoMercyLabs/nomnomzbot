// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import java.awt.Color
import java.awt.Font
import java.awt.RenderingHints
import java.awt.image.BufferedImage
import java.io.ByteArrayOutputStream
import java.io.File
import javax.imageio.ImageIO
import org.jetbrains.compose.desktop.application.dsl.TargetFormat
import org.jetbrains.kotlin.gradle.ExperimentalWasmDsl
import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import org.jetbrains.kotlin.gradle.targets.js.webpack.KotlinWebpackConfig

plugins {
    alias(libs.plugins.kotlin.multiplatform)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.compose.multiplatform)
    alias(libs.plugins.compose.compiler)
}

// Single source of truth for the desktop distribution's version (S111c) — both the packaged
// installer's version AND the version the running app surfaces (AppVersion, read from a
// generated resource at runtime) derive from this ONE literal instead of two independent copies
// silently drifting apart.
version = "1.0.0"

kotlin {
    // The expect/actual seams (TokenVault, OAuthLauncher — frontend.md §6) use expect/actual
    // CLASSES, which are stable-in-practice but flagged Beta; opt in to silence the warning
    // (the project treats warnings as noise to keep build output clean).
    compilerOptions {
        freeCompilerArgs.add("-Xexpect-actual-classes")
    }

    // JVM desktop target.
    jvm {
        compilerOptions {
            jvmTarget.set(JvmTarget.JVM_21)
        }
    }

    // Web target — the identical full dashboard runs in the browser via wasmJs
    // (frontend.md §1). Same commonMain, no cut-down view.
    @OptIn(ExperimentalWasmDsl::class)
    wasmJs {
        browser {
            commonWebpackConfig {
                outputFileName = "composeApp.js"
                // Preserve the plugin's default dev server (its `static` dirs serve index.html from
                // processedResources) and only override the port — replacing the whole DevServer
                // object drops those static dirs, so `/` 404s and the app never loads in the browser.
                devServer = (devServer ?: KotlinWebpackConfig.DevServer()).copy(port = 5173)
            }
        }
        binaries.executable()
    }

    sourceSets {
        commonMain.dependencies {
            implementation(compose.runtime)
            implementation(compose.foundation)
            implementation(compose.material3)
            implementation(compose.ui)
            implementation(compose.components.resources)
            implementation(libs.androidx.lifecycle.runtime.compose)
            implementation(libs.androidx.lifecycle.viewmodel.compose)
            implementation(libs.kotlinx.coroutines.core)
            implementation(libs.kotlinx.serialization.json)
            // Local-time formatting of chat message timestamps (chat-client.md §0 render contract).
            implementation(libs.kotlinx.datetime)
            // Typed shared REST client (frontend.md §3) — one HttpClient configured in
            // commonMain; the engine is the only per-target piece (jvmMain/wasmJsMain).
            implementation(libs.ktor.client.core)
            implementation(libs.ktor.client.content.negotiation)
            implementation(libs.ktor.serialization.kotlinx.json)
            // WebSocket transport for the SignalR hub client (DashboardHubClient).
            implementation(libs.ktor.client.websockets)
            // Async image loading for chat emotes, badges, and cheermotes (coil3 KMP).
            implementation(libs.coil3.compose)
            implementation(libs.coil3.network.ktor3)
        }

        commonTest.dependencies {
            implementation(kotlin("test"))
            implementation(libs.kotlinx.coroutines.core)
            implementation(libs.kotlinx.coroutines.test)
        }

        jvmMain.dependencies {
            implementation(compose.desktop.currentOs)
            // Desktop main dispatcher (frontend.md §10).
            implementation(libs.kotlinx.coroutines.swing)
            // Desktop REST engine (frontend.md §2).
            implementation(libs.ktor.client.cio)
            // Desktop mDNS LAN browse of `_nomnomz._tcp` (frontend.md §6) — jvm only.
            implementation(libs.jmdns)
        }

        wasmJsMain.dependencies {
            // `kotlinx.browser` (document/window) for the wasmJs ComposeViewport mount.
            implementation(libs.kotlinx.browser)
            // Web REST engine — Fetch-backed (frontend.md §2).
            implementation(libs.ktor.client.js)
        }
    }
}

// Stamps `version` (above) into a classpath resource so the RUNNING app can read its own version
// (core/platform/AppVersion.kt) instead of a second hardcoded literal — the packaged installer and
// the app's own "About"/diagnostics surface are guaranteed to agree.
val generateAppVersionResource: TaskProvider<Task> =
    tasks.register("generateAppVersionResource") {
        val outputDir: File = layout.buildDirectory.dir("generated/appVersion").get().asFile
        val propertiesFile = File(outputDir, "app-version.properties")
        // Captured as a plain String local (not a live reference into the Gradle script/Project
        // object) so this task action serializes cleanly under the configuration cache.
        val stampedVersion: String = version.toString()
        inputs.property("version", stampedVersion)
        outputs.file(propertiesFile)
        doLast {
            outputDir.mkdirs()
            propertiesFile.writeText("version=$stampedVersion\n")
        }
    }

kotlin.sourceSets.getByName("jvmMain") {
    resources.srcDir(layout.buildDirectory.dir("generated/appVersion"))
}

tasks.matching { it.name == "jvmProcessResources" }.configureEach { dependsOn(generateAppVersionResource) }

// Generates the desktop app icon at build time (S111c — the distribution previously shipped with
// no icon at all) as a self-contained ICO/ICNS/PNG trio, drawn with java.awt so no new dependency
// or binary asset is checked into the repo. A real brand mark can replace these generated files
// later by dropping fixed .ico/.icns/.png files at the same output paths — this task only fills
// the gap of "there is no icon file to point nativeDistributions at".
val generateAppIcons: TaskProvider<Task> =
    tasks.register("generateAppIcons") {
        val outputDir: File = layout.buildDirectory.dir("generated/icons").get().asFile
        val icoFile = File(outputDir, "icon.ico")
        val icnsFile = File(outputDir, "icon.icns")
        val pngFile = File(outputDir, "icon.png")
        outputs.files(icoFile, icnsFile, pngFile)
        doLast {
            outputDir.mkdirs()

            fun drawIcon(size: Int): BufferedImage {
                val image = BufferedImage(size, size, BufferedImage.TYPE_INT_ARGB)
                val g = image.createGraphics()
                g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON)
                // NomNomzBot's dashboard accent is dynamic per-streamer at runtime; the packaged icon
                // needs one fixed color, so it uses a neutral dark base matching the shadcn dark scheme
                // background rather than any one streamer's accent.
                g.color = Color(0x18, 0x18, 0x1B)
                g.fillRoundRect(0, 0, size, size, (size * 0.22).toInt(), (size * 0.22).toInt())
                g.color = Color(0xFA, 0xFA, 0xFA)
                g.font = Font("SansSerif", Font.BOLD, (size * 0.56).toInt())
                val glyph = "N"
                val metrics = g.fontMetrics
                val textX = (size - metrics.stringWidth(glyph)) / 2
                val textY = (size - metrics.height) / 2 + metrics.ascent
                g.drawString(glyph, textX, textY)
                g.dispose()
                return image
            }

            fun pngBytes(size: Int): ByteArray {
                val out = ByteArrayOutputStream()
                ImageIO.write(drawIcon(size), "png", out)
                return out.toByteArray()
            }

            pngFile.writeBytes(pngBytes(512))

            // ICO container: 6-byte header + one ICONDIRENTRY per image, each entry's payload is a
            // plain PNG stream (supported by Windows Vista+/jpackage for any registered size).
            fun writeIco(sizes: List<Int>) {
                val images: List<ByteArray> = sizes.map { pngBytes(it) }
                val out = ByteArrayOutputStream()
                fun u16(v: Int) { out.write(v and 0xFF); out.write((v shr 8) and 0xFF) }
                fun u32(v: Int) {
                    out.write(v and 0xFF); out.write((v shr 8) and 0xFF)
                    out.write((v shr 16) and 0xFF); out.write((v shr 24) and 0xFF)
                }
                u16(0); u16(1); u16(sizes.size)
                var offset = 6 + 16 * sizes.size
                sizes.forEachIndexed { index, size ->
                    val dimensionByte = if (size >= 256) 0 else size
                    out.write(dimensionByte); out.write(dimensionByte)
                    out.write(0); out.write(0)
                    u16(1); u16(32)
                    u32(images[index].size)
                    u32(offset)
                    offset += images[index].size
                }
                images.forEach { out.write(it) }
                icoFile.writeBytes(out.toByteArray())
            }
            writeIco(listOf(16, 32, 48, 256))

            // ICNS container: 'icns' magic + total length, then one OSType+length+PNG entry per size
            // (ic07/ic08/ic09 are the PNG-compressed entry types Apple defines for 128/256/512).
            fun writeIcns(entries: List<Pair<String, Int>>) {
                val body = ByteArrayOutputStream()
                fun u32(out: ByteArrayOutputStream, v: Int) {
                    out.write((v shr 24) and 0xFF); out.write((v shr 16) and 0xFF)
                    out.write((v shr 8) and 0xFF); out.write(v and 0xFF)
                }
                entries.forEach { (osType, size) ->
                    val png: ByteArray = pngBytes(size)
                    body.write(osType.toByteArray(Charsets.US_ASCII))
                    u32(body, 8 + png.size)
                    body.write(png)
                }
                val bodyBytes: ByteArray = body.toByteArray()
                val out = ByteArrayOutputStream()
                out.write("icns".toByteArray(Charsets.US_ASCII))
                u32(out, 8 + bodyBytes.size)
                out.write(bodyBytes)
                icnsFile.writeBytes(out.toByteArray())
            }
            writeIcns(listOf("ic07" to 128, "ic08" to 256, "ic09" to 512))
        }
    }

// Desktop packaging + run entry point (jvmMain/Main.kt).
compose.desktop {
    application {
        mainClass = "bot.nomnomz.dashboard.MainKt"

        nativeDistributions {
            targetFormats(TargetFormat.Dmg, TargetFormat.Msi, TargetFormat.Deb)
            packageName = "NomNomzBot"
            // Single source of truth — the `version` set at the top of this file, not a second
            // hardcoded literal (S111c).
            packageVersion = version.toString()

            // Bundle the FULL JDK module set into the packaged runtime. jpackage otherwise ships only the
            // jlink-detected modules, which drops anything loaded reflectively / via com.sun.* — e.g.
            // `jdk.httpserver` (the OAuth loopback's HttpServer, used by the bot/integration connect) and the
            // TLS crypto providers the Twitch HTTPS calls need. Trimming those crashes the bundled app at
            // runtime ("com/sun/net/httpserver/HttpExchange") even though `gradlew run` (full JDK) works.
            includeAllModules = true

            val iconsDir: File = layout.buildDirectory.dir("generated/icons").get().asFile
            windows { iconFile.set(File(iconsDir, "icon.ico")) }
            macOS { iconFile.set(File(iconsDir, "icon.icns")) }
            linux { iconFile.set(File(iconsDir, "icon.png")) }
        }
    }
}

// The compose plugin's packaging tasks read the icon files at execution time (jpackage input),
// so they must exist by then even though nativeDistributions wires the paths at configuration time.
tasks.matching { task ->
    task.name.startsWith("package") || task.name.startsWith("createDistributable") || task.name.startsWith("run")
}.configureEach { dependsOn(generateAppIcons) }
