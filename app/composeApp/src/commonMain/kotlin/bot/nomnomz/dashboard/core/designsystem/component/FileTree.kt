// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.icon.ChevronDownGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.ChevronRightGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CodeGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.FileGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.FolderGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.FolderOpenGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography

// The glyph box + the per-depth indent step. Fixed affordance sizes, not design-token spacing.
private val RowGlyphSize: Dp = 16.dp
private val IndentStep: Dp = 16.dp

/**
 * A node in a project file tree — either a [Folder] holding children or a [FileLeaf] addressed by its full
 * project-relative [path]. Built from a flat path set by [buildFileTree]; the caller-facing [FileTree] renders it.
 */
sealed interface FileTreeNode {
    val name: String

    data class Folder(override val name: String, val path: String, val children: List<FileTreeNode>) :
        FileTreeNode

    data class FileLeaf(override val name: String, val path: String) : FileTreeNode
}

/**
 * Turn a flat set of project-relative paths (`index.vue`, `components/Bar.vue`, `lib/util.ts`) into a nested
 * [FileTreeNode] forest. Folders sort before files, each group alphabetically — the stable order a file tree
 * shows. Backslashes are normalised to `/` so a path is split consistently regardless of who authored it.
 */
fun buildFileTree(paths: Collection<String>): List<FileTreeNode> {
    // A mutable staging node while we walk every path segment; converted to the immutable model at the end.
    class Stage(val name: String, val path: String) {
        val folders: LinkedHashMap<String, Stage> = LinkedHashMap()
        val files: LinkedHashMap<String, String> = LinkedHashMap()
    }

    val root = Stage(name = "", path = "")
    for (raw in paths) {
        val normalized: String = raw.replace('\\', '/').trim('/')
        if (normalized.isEmpty()) continue
        val segments: List<String> = normalized.split('/')
        var cursor: Stage = root
        var prefix = ""
        for ((index, segment) in segments.withIndex()) {
            prefix = if (prefix.isEmpty()) segment else "$prefix/$segment"
            if (index == segments.lastIndex) {
                cursor.files[segment] = prefix
            } else {
                cursor = cursor.folders.getOrPut(segment) { Stage(segment, prefix) }
            }
        }
    }

    fun convert(stage: Stage): List<FileTreeNode> {
        val folders: List<FileTreeNode> =
            stage.folders.values
                .sortedBy { it.name.lowercase() }
                .map { FileTreeNode.Folder(it.name, it.path, convert(it)) }
        val files: List<FileTreeNode> =
            stage.files.entries
                .sortedBy { it.key.lowercase() }
                .map { FileTreeNode.FileLeaf(it.key, it.value) }
        return folders + files
    }

    return convert(root)
}

/**
 * A file-tree pattern ported to the design system (frontend-design-system.md §4, catalogue row — `FileTree`):
 * the recursive `src/` navigator the code editor shows. [paths] is the project's flat path set; the tree renders
 * folders (disclosable, default-expanded) and files (selectable). The selected [selectedPath] row is raised with
 * [Tokens.accent]; clicking a file calls [onSelect]. Indent, colours, and type all come from the tokens/spacing —
 * no raw hex or dp beyond the fixed glyph/indent affordance sizes.
 */
@Composable
fun FileTree(
    paths: Collection<String>,
    selectedPath: String?,
    onSelect: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val nodes: List<FileTreeNode> = remember(paths) { buildFileTree(paths) }
    // Folder path → expanded. Absent means expanded (folders default open so the tree reads at a glance).
    val collapsed = remember(paths) { mutableStateMapOf<String, Boolean>() }

    val visible: List<VisibleRow> = flattenVisible(nodes, collapsed)

    LazyColumn(modifier = modifier) {
        items(visible, key = { it.path.ifEmpty { it.name } }) { row ->
            FileTreeRow(
                row = row,
                selected = row.path == selectedPath && !row.isFolder,
                onClick = {
                    if (row.isFolder) {
                        collapsed[row.path] = !(collapsed[row.path] ?: false)
                    } else {
                        onSelect(row.path)
                    }
                },
            )
        }
    }
}

// One rendered line of the tree — a folder or a file, with its nesting [depth] and (for folders) [expanded].
private data class VisibleRow(
    val name: String,
    val path: String,
    val depth: Int,
    val isFolder: Boolean,
    val expanded: Boolean,
)

// Walk the forest in display order, emitting only rows whose ancestor folders are all expanded.
private fun flattenVisible(
    nodes: List<FileTreeNode>,
    collapsed: Map<String, Boolean>,
    depth: Int = 0,
): List<VisibleRow> {
    val rows: MutableList<VisibleRow> = mutableListOf()
    for (node in nodes) {
        when (node) {
            is FileTreeNode.Folder -> {
                val expanded: Boolean = !(collapsed[node.path] ?: false)
                rows += VisibleRow(node.name, node.path, depth, isFolder = true, expanded = expanded)
                if (expanded) rows += flattenVisible(node.children, collapsed, depth + 1)
            }
            is FileTreeNode.FileLeaf ->
                rows += VisibleRow(node.name, node.path, depth, isFolder = false, expanded = false)
        }
    }
    return rows
}

@Composable
private fun FileTreeRow(row: VisibleRow, selected: Boolean, onClick: () -> Unit) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    val rowColor: Color = if (selected) tokens.accent else Color.Transparent
    val contentColor: Color =
        when {
            selected -> tokens.accentForeground
            row.isFolder -> tokens.foreground
            else -> tokens.mutedForeground
        }

    Row(
        modifier =
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(tokens.radius.sm))
                .background(rowColor)
                .clickable(onClick = onClick)
                .pointerHoverIcon(PointerIcon.Hand)
                .padding(
                    start = spacing.s2 + IndentStep * row.depth,
                    end = spacing.s2,
                    top = spacing.s1_5,
                    bottom = spacing.s1_5,
                ),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s1_5),
    ) {
        if (row.isFolder) {
            Icon(
                imageVector = if (row.expanded) ChevronDownGlyph else ChevronRightGlyph,
                contentDescription = null,
                tint = contentColor,
                modifier = Modifier.size(RowGlyphSize),
            )
            Icon(
                imageVector = if (row.expanded) FolderOpenGlyph else FolderGlyph,
                contentDescription = null,
                tint = contentColor,
                modifier = Modifier.size(RowGlyphSize),
            )
        } else {
            // A file row aligns its icon under the folder icon (past the missing chevron column).
            Spacer(modifier = Modifier.width(RowGlyphSize))
            Icon(
                imageVector = if (row.name.isCodeFile()) CodeGlyph else FileGlyph,
                contentDescription = null,
                tint = contentColor,
                modifier = Modifier.size(RowGlyphSize),
            )
        }
        Text(
            text = row.name,
            style = typography.sm,
            color = contentColor,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

// A source file (script/markup) shows the code glyph; assets/other files show the plain file glyph.
private fun String.isCodeFile(): Boolean {
    val ext: String = substringAfterLast('.', "").lowercase()
    return ext in CODE_EXTENSIONS
}

private val CODE_EXTENSIONS: Set<String> =
    setOf("ts", "tsx", "js", "jsx", "vue", "svelte", "html", "css", "scss", "json")
