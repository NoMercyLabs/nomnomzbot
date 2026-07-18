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

import java.awt.BorderLayout
import java.awt.Color
import java.awt.Dimension
import java.awt.Font
import java.awt.Frame
import java.awt.event.WindowAdapter
import java.awt.event.WindowEvent
import javax.swing.DefaultListModel
import javax.swing.JButton
import javax.swing.JComponent
import javax.swing.JDialog
import javax.swing.JLabel
import javax.swing.JList
import javax.swing.JOptionPane
import javax.swing.JPanel
import javax.swing.JScrollPane
import javax.swing.JSplitPane
import javax.swing.JTextArea
import javax.swing.KeyStroke
import javax.swing.ListSelectionModel
import javax.swing.SwingUtilities
import javax.swing.WindowConstants
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.withContext

// Desktop multi-file project editor — a NON-modal Swing dialog: a file list on the left, a monospace text area on
// the right, add/rename/delete buttons, "Save & Compile", and a result label. The multi-file sibling of
// CustomCodeEditor.jvm: the dialog owns the live `path → content` map; selecting a file flushes the current text
// back into the map and loads the selected file. It stays open across compiles — the coroutine builds the dialog
// on the EDT, then drives a loop that receives each save as an [ProjectEditorSignal.Compile] (carrying the whole map),
// awaits the caller's compile, and posts the result back on the EDT. Close (button / window-X / Esc) ends the loop.
actual class ProjectEditor : ProjectEditorIO {
    // [sdkTypes] (the generated nnz.d.ts) is ignored on desktop: the Swing text area has no TypeScript language
    // service or in-browser preview to feed it to. The autocomplete + esbuild-wasm live preview it powers are a
    // web-only enhancement; the desktop editor keeps its plain compile-on-save loop unchanged.
    actual override suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    ) =
        withContext(Dispatchers.IO) {
            val signals: Channel<ProjectEditorSignal> = Channel(Channel.UNLIMITED)
            val ui: Array<ProjectEditorDialog?> = arrayOfNulls(1)

            SwingUtilities.invokeLater {
                ui[0] = buildProjectEditorDialog(title, initialFiles, entryPath, language, signals)
            }

            try {
                while (true) {
                    when (val signal: ProjectEditorSignal = signals.receive()) {
                        is ProjectEditorSignal.Compile -> {
                            val feedback: CompileFeedback = compile(signal.files)
                            SwingUtilities.invokeLater { ui[0]?.showResult(feedback) }
                        }
                        ProjectEditorSignal.Close -> break
                    }
                }
            } finally {
                SwingUtilities.invokeLater { ui[0]?.dispose() }
            }
        }
}

// What a button/keystroke asks the driving coroutine to do.
private sealed interface ProjectEditorSignal {
    /** "Save & Compile" pressed with the editor's current full [files] map. */
    class Compile(val files: Map<String, String>) : ProjectEditorSignal

    /** The editor was closed (Close button, window-X, or Esc). */
    data object Close : ProjectEditorSignal
}

// Owns the live Swing widgets + the mutable file map — all EDT-only. Selecting a file flushes the text area back
// into [files] and loads the picked file; add/rename/delete edit the map and the list model in place.
private class ProjectEditorDialog(
    private val dialog: JDialog,
    private val fileList: JList<String>,
    private val listModel: DefaultListModel<String>,
    private val area: JTextArea,
    private val saveButton: JButton,
    private val resultLabel: JLabel,
    private val files: MutableMap<String, String>,
    private val entryPath: String,
) {
    private var active: String = entryPath

    init {
        refreshList()
        fileList.selectedValue?.let { select(it) } ?: select(entryPath)
        fileList.addListSelectionListener { event ->
            if (!event.valueIsAdjusting) {
                fileList.selectedValue?.let { if (it != active) select(it) }
            }
        }
    }

    fun snapshot(): Map<String, String> {
        flush()
        return files.toMap()
    }

    fun addFile() {
        val name: String? = JOptionPane.showInputDialog(dialog, "New file path (e.g. components/Bar.vue)")
        val trimmed: String = name?.trim()?.trim('/') ?: return
        if (trimmed.isEmpty() || files.containsKey(trimmed)) return
        flush()
        files[trimmed] = ""
        refreshList()
        select(trimmed)
    }

    fun renameActive() {
        if (active == entryPath) return
        val next: String? = JOptionPane.showInputDialog(dialog, "Rename file", active)
        val trimmed: String = next?.trim()?.trim('/') ?: return
        if (trimmed.isEmpty() || trimmed == active || files.containsKey(trimmed)) return
        flush()
        files[trimmed] = files.remove(active) ?: ""
        active = trimmed
        refreshList()
        select(trimmed)
    }

    fun deleteActive() {
        if (active == entryPath) return
        files.remove(active)
        active = entryPath
        refreshList()
        select(entryPath)
    }

    fun markCompiling() {
        saveButton.isEnabled = false
        saveButton.text = COMPILING_LABEL
        resultLabel.text = " "
    }

    fun showResult(feedback: CompileFeedback) {
        resultLabel.text = feedback.message
        resultLabel.foreground = if (feedback.ok) OK_COLOR else ERROR_COLOR
        saveButton.isEnabled = true
        saveButton.text = SAVE_LABEL
    }

    fun dispose() {
        if (dialog.isDisplayable) dialog.dispose()
    }

    // Stash the text area's current content back into the map under the active path.
    private fun flush() {
        files[active] = area.text
    }

    private fun select(path: String) {
        if (path != active) flush()
        active = path
        area.text = files[path] ?: ""
        area.caretPosition = 0
        if (fileList.selectedValue != path) fileList.setSelectedValue(path, true)
    }

    private fun refreshList() {
        val sorted: List<String> = files.keys.sorted()
        listModel.clear()
        sorted.forEach { listModel.addElement(it) }
    }

    companion object {
        const val SAVE_LABEL: String = "Save & Compile"
        const val COMPILING_LABEL: String = "Compiling…"
        val OK_COLOR: Color = Color(0x2E, 0xA0, 0x43)
        val ERROR_COLOR: Color = Color(0xD1, 0x3B, 0x3B)
    }
}

// Builds and shows the non-modal multi-file editor dialog on the EDT, wiring its buttons/keystrokes to publish
// [ProjectEditorSignal]s. [language] is surfaced in the window title (the Swing text area has no highlighting to configure).
private fun buildProjectEditorDialog(
    title: String,
    initialFiles: Map<String, String>,
    entryPath: String,
    language: String,
    signals: Channel<ProjectEditorSignal>,
): ProjectEditorDialog {
    val heading: String =
        if (language.isBlank()) "Edit project — $title" else "Edit project — $title (${language.uppercase()})"
    val dialog = JDialog(null as Frame?, heading, false)
    dialog.defaultCloseOperation = WindowConstants.DISPOSE_ON_CLOSE

    val files: MutableMap<String, String> = LinkedHashMap(initialFiles)
    if (!files.containsKey(entryPath)) files[entryPath] = ""

    val listModel = DefaultListModel<String>()
    val fileList =
        JList(listModel).apply {
            selectionMode = ListSelectionModel.SINGLE_SELECTION
            font = Font(Font.MONOSPACED, Font.PLAIN, 12)
        }
    val area =
        JTextArea().apply {
            font = Font(Font.MONOSPACED, Font.PLAIN, 13)
            tabSize = 2
            lineWrap = false
        }

    val resultLabel = JLabel(" ")
    val saveButton = JButton(ProjectEditorDialog.SAVE_LABEL)
    val closeButton = JButton("Close")
    val addButton = JButton("New file")
    val renameButton = JButton("Rename")
    val deleteButton = JButton("Delete")

    val handle =
        ProjectEditorDialog(dialog, fileList, listModel, area, saveButton, resultLabel, files, entryPath)

    val requestCompile: () -> Unit = {
        handle.markCompiling()
        signals.trySend(ProjectEditorSignal.Compile(handle.snapshot()))
    }
    saveButton.addActionListener { requestCompile() }
    closeButton.addActionListener { signals.trySend(ProjectEditorSignal.Close) }
    addButton.addActionListener { handle.addFile() }
    renameButton.addActionListener { handle.renameActive() }
    deleteButton.addActionListener { handle.deleteActive() }
    dialog.addWindowListener(
        object : WindowAdapter() {
            override fun windowClosed(event: WindowEvent?) {
                signals.trySend(ProjectEditorSignal.Close)
            }
        }
    )

    dialog.rootPane.registerKeyboardAction(
        { signals.trySend(ProjectEditorSignal.Close) },
        KeyStroke.getKeyStroke("ESCAPE"),
        JComponent.WHEN_IN_FOCUSED_WINDOW,
    )
    dialog.rootPane.registerKeyboardAction(
        { requestCompile() },
        KeyStroke.getKeyStroke("control S"),
        JComponent.WHEN_IN_FOCUSED_WINDOW,
    )

    val fileButtons =
        JPanel().apply {
            add(addButton)
            add(renameButton)
            add(deleteButton)
        }
    val sidebar =
        JPanel(BorderLayout()).apply {
            add(JScrollPane(fileList), BorderLayout.CENTER)
            add(fileButtons, BorderLayout.SOUTH)
            preferredSize = Dimension(240, 640)
        }

    val split =
        JSplitPane(JSplitPane.HORIZONTAL_SPLIT, sidebar, JScrollPane(area)).apply {
            dividerLocation = 240
        }

    val bottom =
        JPanel().apply {
            add(resultLabel)
            add(closeButton)
            add(saveButton)
        }

    dialog.layout = BorderLayout()
    dialog.add(split, BorderLayout.CENTER)
    dialog.add(bottom, BorderLayout.SOUTH)
    dialog.preferredSize = Dimension(1040, 680)
    dialog.pack()
    dialog.setLocationRelativeTo(null)
    dialog.isVisible = true
    area.requestFocusInWindow()
    return handle
}
