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

// The platform-agnostic core of the multi-file project editor's file/tab bookkeeping (S-CODE-EDITOR-d). Both the
// desktop (Swing) and web (Monaco) editors keep ONE widget's worth of "current text" on screen while a whole
// `path -> content` project sits behind it; switching the active tab must write the outgoing file's edits back into
// the map BEFORE loading the incoming file's content, or the outgoing edits are silently lost. This class holds
// that map + the active pointer and is the single place that rule lives, so it can be unit-tested directly instead
// of only through a Swing dialog or a DOM overlay.
//
// The entry file is pinned: it always exists in [files] and can never be renamed or deleted (both editors refuse
// those operations on it), mirroring the [ProjectEditorIO] contract's "never the entryPath file" guarantee.
class ProjectFilesEditorState(initialFiles: Map<String, String>, val entryPath: String) {
    private val filesByPath: LinkedHashMap<String, String> = LinkedHashMap(initialFiles)

    init {
        if (!filesByPath.containsKey(entryPath)) filesByPath[entryPath] = ""
    }

    /** The currently active/open file path. Starts on the entry file. */
    var active: String = entryPath
        private set

    /** All known file paths, sorted (the order both file lists render in). */
    val paths: List<String> get() = filesByPath.keys.sorted()

    /** The content of [path], or an empty string if it does not exist. */
    fun content(path: String): String = filesByPath[path] ?: ""

    /** The active file's current content. */
    fun activeContent(): String = content(active)

    /** Write [content] into the active file. Call before [select] to preserve the outgoing edit. */
    fun edit(content: String) {
        filesByPath[active] = content
    }

    /**
     * Switch the active file to [path]. [outgoingContent] — the editor widget's current text — is flushed into the
     * OLD active file first, so it is preserved when the caller later reads it back via [content] or [snapshot].
     * A no-op if [path] is not a known file.
     */
    fun select(path: String, outgoingContent: String) {
        if (!filesByPath.containsKey(path)) return
        filesByPath[active] = outgoingContent
        active = path
    }

    /** Add a new empty file at [path] and make it active. No-op if [path] is blank or already exists. */
    fun addFile(path: String, outgoingContent: String): Boolean {
        val trimmed: String = path.trim().trim('/')
        if (trimmed.isEmpty() || filesByPath.containsKey(trimmed)) return false
        filesByPath[active] = outgoingContent
        filesByPath[trimmed] = ""
        active = trimmed
        return true
    }

    /** Rename the active file to [newPath]. Refused for the entry file, a blank name, or an existing path. */
    fun renameActive(newPath: String, outgoingContent: String): Boolean {
        val trimmed: String = newPath.trim().trim('/')
        if (active == entryPath) return false
        if (trimmed.isEmpty() || trimmed == active || filesByPath.containsKey(trimmed)) return false
        filesByPath[active] = outgoingContent
        filesByPath[trimmed] = filesByPath.remove(active) ?: ""
        active = trimmed
        return true
    }

    /** Delete the active file, falling back to the entry file. Refused for the entry file itself. */
    fun deleteActive(): Boolean {
        if (active == entryPath) return false
        filesByPath.remove(active)
        active = entryPath
        return true
    }

    /** The full, current `path -> content` map — the entire project as it stands right now. */
    fun snapshot(outgoingContent: String): Map<String, String> {
        filesByPath[active] = outgoingContent
        return filesByPath.toMap()
    }
}
