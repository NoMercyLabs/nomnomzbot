// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

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
                devServer = KotlinWebpackConfig.DevServer(port = 8085)
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
            // Typed shared REST client (frontend.md §3) — one HttpClient configured in
            // commonMain; the engine is the only per-target piece (jvmMain/wasmJsMain).
            implementation(libs.ktor.client.core)
            implementation(libs.ktor.client.content.negotiation)
            implementation(libs.ktor.serialization.kotlinx.json)
            // WebSocket transport for the SignalR hub client (DashboardHubClient).
            implementation(libs.ktor.client.websockets)
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

// Desktop packaging + run entry point (jvmMain/Main.kt).
compose.desktop {
    application {
        mainClass = "bot.nomnomz.dashboard.MainKt"

        nativeDistributions {
            targetFormats(TargetFormat.Dmg, TargetFormat.Msi, TargetFormat.Deb)
            packageName = "NomNomzBot"
            packageVersion = "1.0.0"

            // Bundle the FULL JDK module set into the packaged runtime. jpackage otherwise ships only the
            // jlink-detected modules, which drops anything loaded reflectively / via com.sun.* — e.g.
            // `jdk.httpserver` (the OAuth loopback's HttpServer, used by the bot/integration connect) and the
            // TLS crypto providers the Twitch HTTPS calls need. Trimming those crashes the bundled app at
            // runtime ("com/sun/net/httpserver/HttpExchange") even though `gradlew run` (full JDK) works.
            includeAllModules = true
        }
    }
}
