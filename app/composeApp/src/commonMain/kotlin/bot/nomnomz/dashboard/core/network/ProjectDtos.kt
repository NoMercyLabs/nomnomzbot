// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The multi-file project the dev-platform code editor round-trips (dev-platform.md §4.2 / §8). A widget or a
// code script is a `src/` file set (`path → content`) plus a manifest describing the entry, kind, framework, and
// declared dependencies. Both the widget project endpoints
// (`GET/PUT /channels/{channelId}/widgets/{widgetId}/project`) and the script project endpoints
// (`GET/PUT /code-scripts/{id}/project`) speak exactly this shape — identical whether the artifact is one file or
// a full tree. On PUT the server re-builds from the file set + manifest (the trust boundary, never a client
// bundle), so these two DTOs ARE the source of truth the editor edits.

/**
 * A multi-file project (backend `ProjectDto`). [files] maps each project-relative path (e.g. `index.vue`,
 * `components/Bar.vue`) to its full text content; [manifest] carries the build metadata. A `Map<String, String>`
 * serializes as a plain JSON object, matching the backend `Dictionary<string, string>`.
 */
@Serializable
data class ProjectDto(
    val files: Map<String, String> = emptyMap(),
    val manifest: ProjectManifestDto = ProjectManifestDto(),
)

/**
 * The project manifest on the wire (backend `ProjectManifestDto`) — `{ entry, kind, framework, dependencies[] }`.
 * [entry] is the path in [ProjectDto.files] the build compiles first; [kind] is `widget` or `script`; [framework]
 * ∈ `vanilla | vue | react | svelte` (widgets) or the script language; [dependencies] is the npm allow-list the
 * project declares (currently `vue`-only), null/empty when it declares none.
 */
@Serializable
data class ProjectManifestDto(
    val entry: String = "",
    val kind: String = "",
    val framework: String = "",
    val dependencies: List<String>? = null,
)
