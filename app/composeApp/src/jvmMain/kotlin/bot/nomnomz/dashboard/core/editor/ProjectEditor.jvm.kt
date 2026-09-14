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
import java.awt.FlowLayout
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
import javax.swing.JTabbedPane
import javax.swing.JTextArea
import javax.swing.JTextField
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
    // [sdkTypes] (the generated nnz.d.ts) and [eventSubscriptions] (the fire bar's declared-events source) are
    // both ignored on desktop: the Swing text area has no TypeScript language service, live preview, or fire bar
    // to feed them to. Those are web-only enhancements; the desktop editor keeps its plain compile-on-save loop.
    actual override suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        eventSubscriptions: List<String>,
        history: EditorHistory?,
        testRun: EditorTestRun?,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    ) =
        withContext(Dispatchers.IO) {
            val signals: Channel<ProjectEditorSignal> = Channel(Channel.UNLIMITED)
            val ui: Array<ProjectEditorDialog?> = arrayOfNulls(1)

            SwingUtilities.invokeLater {
                ui[0] = buildProjectEditorDialog(title, initialFiles, entryPath, language, history, testRun, signals)
            }

            try {
                while (true) {
                    when (val signal: ProjectEditorSignal = signals.receive()) {
                        is ProjectEditorSignal.Compile -> {
                            val feedback: CompileFeedback = compile(signal.files)
                            SwingUtilities.invokeLater { ui[0]?.showResult(feedback) }
                        }
                        is ProjectEditorSignal.HistoryLoadMore -> {
                            val outcome: EditorOutcome<EditorVersionsPage>? = history?.loadMore?.invoke()
                            SwingUtilities.invokeLater { ui[0]?.showHistory(outcome) }
                        }
                        is ProjectEditorSignal.HistoryRollback -> {
                            val outcome: EditorOutcome<EditorVersionsPage>? = history?.rollback?.invoke(signal.versionId)
                            SwingUtilities.invokeLater { ui[0]?.showHistory(outcome) }
                        }
                        is ProjectEditorSignal.HistoryDelete -> {
                            val outcome: EditorOutcome<EditorVersionsPage>? = history?.delete?.invoke(signal.versionId)
                            SwingUtilities.invokeLater { ui[0]?.showHistory(outcome) }
                        }
                        is ProjectEditorSignal.RunTest -> {
                            val outcome: EditorOutcome<EditorTestRunResult>? = testRun?.run?.invoke(signal.variables, signal.args)
                            SwingUtilities.invokeLater { ui[0]?.showTestRunResult(outcome) }
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

    /** The History tab's "Load more" was pressed. */
    data object HistoryLoadMore : ProjectEditorSignal

    /** The History tab's "Publish" was pressed on one row. */
    class HistoryRollback(val versionId: String) : ProjectEditorSignal

    /** The History tab's delete action was pressed on one row. */
    class HistoryDelete(val versionId: String) : ProjectEditorSignal

    /** The Test run tab's "Run" was pressed with its current variables/args fields. */
    class RunTest(val variables: Map<String, String>, val args: List<String>) : ProjectEditorSignal

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
    private val historyPanel: HistoryPanel?,
    private val testRunPanel: TestRunPanel?,
    initialFiles: Map<String, String>,
    entryPath: String,
) {
    // The file map + active-path bookkeeping is the shared, unit-tested core (ProjectFilesEditorState) — this
    // dialog only owns the Swing widgets and flushes the text area into it before every switch.
    private val state: ProjectFilesEditorState = ProjectFilesEditorState(initialFiles, entryPath)

    init {
        refreshList()
        fileList.selectedValue?.let { select(it) } ?: select(entryPath)
        fileList.addListSelectionListener { event ->
            if (!event.valueIsAdjusting) {
                fileList.selectedValue?.let { if (it != state.active) select(it) }
            }
        }
    }

    fun snapshot(): Map<String, String> = state.snapshot(area.text)

    fun addFile() {
        val name: String? = JOptionPane.showInputDialog(dialog, "New file path (e.g. components/Bar.vue)")
        val trimmed: String = name?.trim() ?: return
        if (!state.addFile(trimmed, area.text)) return
        refreshList()
        load(state.active)
    }

    fun renameActive() {
        val next: String? = JOptionPane.showInputDialog(dialog, "Rename file", state.active)
        val trimmed: String = next?.trim() ?: return
        if (!state.renameActive(trimmed, area.text)) return
        refreshList()
        load(state.active)
    }

    fun deleteActive() {
        if (!state.deleteActive()) return
        refreshList()
        load(state.active)
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

    /** Render one [EditorOutcome] of a History action — a [null] outcome means no [EditorHistory] was wired. */
    fun showHistory(outcome: EditorOutcome<EditorVersionsPage>?) {
        historyPanel?.render(outcome)
    }

    /** Render one [EditorOutcome] of a Test run — a [null] outcome means no [EditorTestRun] was wired. */
    fun showTestRunResult(outcome: EditorOutcome<EditorTestRunResult>?) {
        testRunPanel?.render(outcome)
    }

    // Flush the text area into the OLD active file, switch, and load the new active file's content.
    private fun select(path: String) {
        state.select(path, area.text)
        load(state.active)
    }

    // Paint [path]'s content into the text area WITHOUT touching the map (the switch already happened).
    private fun load(path: String) {
        area.text = state.content(path)
        area.caretPosition = 0
        if (fileList.selectedValue != path) fileList.setSelectedValue(path, true)
    }

    private fun refreshList() {
        listModel.clear()
        state.paths.forEach { listModel.addElement(it) }
    }

    companion object {
        const val SAVE_LABEL: String = "Save & Compile"
        const val COMPILING_LABEL: String = "Compiling…"
        val OK_COLOR: Color = Color(0x2E, 0xA0, 0x43)
        val ERROR_COLOR: Color = Color(0xD1, 0x3B, 0x3B)
    }
}

// Builds and shows the non-modal multi-file editor dialog on the EDT, wiring its buttons/keystrokes to publish
// [ProjectEditorSignal]s. [language] is surfaced in the window title (the Swing text area has no highlighting to
// configure). [history] and [testRun] add a "History" / "Test run" tab alongside the code split when non-null —
// the desktop counterpart of the web editor's History / Run & test side views (S-CODE-COLLAPSE); the tab bar IS
// this dialog's existing pattern for combining the editor with auxiliary panels (it already separated file
// management from Save & Compile as two regions of one window).
private fun buildProjectEditorDialog(
    title: String,
    initialFiles: Map<String, String>,
    entryPath: String,
    language: String,
    history: EditorHistory?,
    testRun: EditorTestRun?,
    signals: Channel<ProjectEditorSignal>,
): ProjectEditorDialog {
    val heading: String =
        if (language.isBlank()) "Edit project — $title" else "Edit project — $title (${language.uppercase()})"
    val dialog = JDialog(null as Frame?, heading, false)
    dialog.defaultCloseOperation = WindowConstants.DISPOSE_ON_CLOSE

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

    val historyPanel: HistoryPanel? =
        if (history == null) null
        else
            HistoryPanel(
                onLoadMore = { signals.trySend(ProjectEditorSignal.HistoryLoadMore) },
                onRollback = { versionId -> signals.trySend(ProjectEditorSignal.HistoryRollback(versionId)) },
                onDelete = { versionId -> signals.trySend(ProjectEditorSignal.HistoryDelete(versionId)) },
            ).apply { render(EditorOutcome.Ok(EditorVersionsPage(history.initialVersions, history.initialHasMore))) }

    val testRunPanel: TestRunPanel? =
        if (testRun == null) null
        else TestRunPanel(onRun = { variables, args -> signals.trySend(ProjectEditorSignal.RunTest(variables, args)) })

    val handle =
        ProjectEditorDialog(
            dialog, fileList, listModel, area, saveButton, resultLabel, historyPanel, testRunPanel, initialFiles, entryPath,
        )

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

    val tabs =
        JTabbedPane().apply {
            addTab("Code", split)
            historyPanel?.let { addTab("History", it.component) }
            testRunPanel?.let { addTab("Test run", it.component) }
        }

    val bottom =
        JPanel().apply {
            add(resultLabel)
            add(closeButton)
            add(saveButton)
        }

    dialog.layout = BorderLayout()
    dialog.add(tabs, BorderLayout.CENTER)
    dialog.add(bottom, BorderLayout.SOUTH)
    dialog.preferredSize = Dimension(1040, 680)
    dialog.pack()
    dialog.setLocationRelativeTo(null)
    dialog.isVisible = true
    area.requestFocusInWindow()
    return handle
}

// ── History tab ──────────────────────────────────────────────────────────────

// The desktop "History" tab: a vertical list of version rows (number, validation status, a "current" badge),
// each with Publish/Delete buttons except the currently-published row (mirrors the web editor's History view
// and the pre-collapse Compose version-history card this replaces). Rebuilt wholesale on every [render] — the
// list is short (paged, a handful of rows per page) so there is nothing to gain from incremental patching.
private class HistoryPanel(
    private val onLoadMore: () -> Unit,
    private val onRollback: (versionId: String) -> Unit,
    private val onDelete: (versionId: String) -> Unit,
) {
    private val rows: JPanel = JPanel().apply { layout = javax.swing.BoxLayout(this, javax.swing.BoxLayout.Y_AXIS) }
    private val statusLabel: JLabel = JLabel(" ")
    private val loadMoreButton: JButton = JButton("Load more").apply { isVisible = false }

    val component: JComponent =
        JPanel(BorderLayout()).apply {
            add(JScrollPane(rows), BorderLayout.CENTER)
            add(
                JPanel(FlowLayout(FlowLayout.LEFT)).apply {
                    add(statusLabel)
                    add(loadMoreButton)
                },
                BorderLayout.SOUTH,
            )
        }

    init {
        loadMoreButton.addActionListener { onLoadMore() }
    }

    fun render(outcome: EditorOutcome<EditorVersionsPage>?) {
        when (outcome) {
            is EditorOutcome.Ok -> renderPage(outcome.value)
            is EditorOutcome.Failed -> statusLabel.text = outcome.message
            null -> statusLabel.text = "History is not available for this project."
        }
    }

    private fun renderPage(page: EditorVersionsPage) {
        statusLabel.text = if (page.versions.isEmpty()) "No saved versions yet." else " "
        loadMoreButton.isVisible = page.hasMore
        rows.removeAll()
        for (version in page.versions) {
            rows.add(versionRow(version))
        }
        rows.revalidate()
        rows.repaint()
    }

    private fun versionRow(version: EditorVersionSummary): JComponent {
        val current: String = if (version.isCurrent) " (current)" else ""
        val label = JLabel("v${version.version} — ${version.validationStatus}$current")
        val row = JPanel(FlowLayout(FlowLayout.LEFT)).apply { add(label) }
        if (!version.isCurrent) {
            val publish = JButton("Publish").apply { addActionListener { onRollback(version.id) } }
            val delete = JButton("Delete").apply { addActionListener { onDelete(version.id) } }
            row.add(publish)
            row.add(delete)
        }
        return row
    }
}

// ── Test run tab ─────────────────────────────────────────────────────────────

// The desktop "Test run" tab: sample variables (`key=value` per line) + space-separated args, a Run button, and
// a read-only result area — the dry-run panel moved off the pre-collapse Compose page. Effects are captured by
// the backend sandbox, never performed.
private class TestRunPanel(onRun: (variables: Map<String, String>, args: List<String>) -> Unit) {
    private val variablesArea: JTextArea = JTextArea(6, 40).apply { font = Font(Font.MONOSPACED, Font.PLAIN, 12) }
    private val argsField: JTextField = JTextField(40)
    private val runButton: JButton = JButton("Run in sandbox")
    private val resultArea: JTextArea =
        JTextArea(12, 40).apply {
            font = Font(Font.MONOSPACED, Font.PLAIN, 12)
            isEditable = false
            lineWrap = true
        }

    val component: JComponent =
        JPanel(BorderLayout()).apply {
            val form =
                JPanel().apply {
                    layout = javax.swing.BoxLayout(this, javax.swing.BoxLayout.Y_AXIS)
                    add(JLabel("Variables (key=value per line)").apply { alignmentX = JComponent.LEFT_ALIGNMENT })
                    add(JScrollPane(variablesArea).apply { alignmentX = JComponent.LEFT_ALIGNMENT })
                    add(JLabel("Args (space-separated)").apply { alignmentX = JComponent.LEFT_ALIGNMENT })
                    add(argsField.apply { alignmentX = JComponent.LEFT_ALIGNMENT })
                    add(JPanel(FlowLayout(FlowLayout.LEFT)).apply { add(runButton) })
                }
            add(form, BorderLayout.NORTH)
            add(JScrollPane(resultArea), BorderLayout.CENTER)
        }

    init {
        runButton.addActionListener {
            runButton.isEnabled = false
            runButton.text = "Running…"
            resultArea.text = ""
            onRun(parseTestRunVariables(variablesArea.text), parseTestRunArgs(argsField.text))
        }
    }

    fun render(outcome: EditorOutcome<EditorTestRunResult>?) {
        runButton.isEnabled = true
        runButton.text = "Run in sandbox"
        resultArea.text =
            when (outcome) {
                is EditorOutcome.Ok -> formatTestRunResult(outcome.value)
                is EditorOutcome.Failed -> "Failed: ${outcome.message}"
                null -> "Test run is not available for this project."
            }
    }
}

private fun formatTestRunResult(result: EditorTestRunResult): String {
    val header: String =
        (if (result.success) "OK" else "FAILED") + " — ${result.durationMs}ms, ${result.hostCallCount} host calls"
    val error: String = result.error?.takeIf { it.isNotBlank() }?.let { "\nError: $it" } ?: ""
    val chat: String =
        if (result.chatOutput.isEmpty()) "\n\nChat output: (none)"
        else "\n\nChat output:\n" + result.chatOutput.joinToString("\n")
    val effects: String =
        if (result.effects.isEmpty()) "\n\nCaptured effects: (none)"
        else "\n\nCaptured effects:\n" + result.effects.joinToString("\n") { "${it.name}  ${it.argsPreview}" }
    return header + error + chat + effects
}

// Parses the variables text area (one `key=value` per line); blank lines and lines without `=` are skipped.
private fun parseTestRunVariables(text: String): Map<String, String> =
    text.lineSequence()
        .mapNotNull { line ->
            val trimmed: String = line.trim()
            if (trimmed.isEmpty() || !trimmed.contains('=')) return@mapNotNull null
            val key: String = trimmed.substringBefore('=').trim()
            val value: String = trimmed.substringAfter('=').trim()
            if (key.isEmpty()) null else key to value
        }
        .toMap()

// Parses the args field into positional arguments, split on any run of whitespace.
private fun parseTestRunArgs(text: String): List<String> =
    text.trim().split(Regex("\\s+")).filter { it.isNotEmpty() }
