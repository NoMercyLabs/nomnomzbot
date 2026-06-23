// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

@file:Suppress("UnstableApiUsage")

pluginManagement {
    repositories {
        google {
            content {
                includeGroupByRegex("com\\.android.*")
                includeGroupByRegex("com\\.google.*")
                includeGroupByRegex("androidx.*")
            }
        }
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    // PREFER_PROJECT, not FAIL_ON_PROJECT_REPOS: building the Wasm browser distribution makes the Kotlin
    // toolchain register the Node.js distribution server (nodejs.org/dist) as a project repository to download
    // its bundled Node/webpack toolchain. Gradle 9 rejects that under both FAIL_ON_PROJECT_REPOS and
    // PREFER_SETTINGS ("repository ... was added by unknown code"). PREFER_PROJECT tolerates the trusted
    // Kotlin-plugin toolchain repos so the web (wasmJs) target can be bundled and served by the bot; the real
    // dependency repos are still the settings-declared google + mavenCentral below. (Backend supply-chain is
    // unaffected — server/ is a separate .NET build with no Gradle.)
    repositoriesMode.set(RepositoriesMode.PREFER_PROJECT)
    repositories {
        google {
            content {
                includeGroupByRegex("com\\.android.*")
                includeGroupByRegex("com\\.google.*")
                includeGroupByRegex("androidx.*")
            }
        }
        mavenCentral()
    }
}

rootProject.name = "NomNomzBot"

// One Compose module holds all targets (frontend.md §1 / frontend-structure.md F1).
// A separate :shared module is added only on Rule-of-Three.
include(":composeApp")
