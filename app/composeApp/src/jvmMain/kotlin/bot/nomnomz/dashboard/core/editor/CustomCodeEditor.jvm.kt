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
import javax.swing.JButton
import javax.swing.JComponent
import javax.swing.JDialog
import javax.swing.JLabel
import javax.swing.JPanel
import javax.swing.JScrollPane
import javax.swing.JTextArea
import javax.swing.KeyStroke
import javax.swing.SwingUtilities
import javax.swing.WindowConstants
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.withContext

// Desktop custom-code editor — a NON-modal Swing dialog with a monospace text area, a "Save & Compile" button,
// and a result label. Unlike a plain modal "return on save" dialog, this one stays open across compiles: the
// coroutine builds the dialog on the AWT event dispatch thread, then drives a loop that receives each save as a
// [EditorSignal.Compile] over a channel, awaits the caller's suspend compile, and posts the result back to the
// label on the EDT. A "Close" (button, window-X, or Esc) sends [EditorSignal.Close] and the loop returns.
// (The web build gets the richer CodeMirror overlay; desktop uses the platform's native text editing.)
actual class CustomCodeEditor : CustomCodeEditorIO {
    actual override suspend fun editAndCompile(
        title: String,
        initialCode: String,
        language: String,
        compile: suspend (String) -> CompileFeedback,
    ) =
        withContext(Dispatchers.IO) {
            // Buttons on the EDT publish signals here; this coroutine consumes them. Unlimited so a trySend from
            // the EDT never blocks the UI thread.
            val signals: Channel<EditorSignal> = Channel(Channel.UNLIMITED)
            // The dialog handle, written on the EDT and only ever touched again from the EDT (via invokeLater).
            val ui: Array<EditorDialog?> = arrayOfNulls(1)

            SwingUtilities.invokeLater { ui[0] = buildEditorDialog(title, initialCode, language, signals) }

            try {
                while (true) {
                    when (val signal: EditorSignal = signals.receive()) {
                        is EditorSignal.Compile -> {
                            val feedback: CompileFeedback = compile(signal.source)
                            SwingUtilities.invokeLater { ui[0]?.showResult(feedback) }
                        }
                        EditorSignal.Close -> break
                    }
                }
            } finally {
                SwingUtilities.invokeLater { ui[0]?.dispose() }
            }
        }
}

// What a button/keystroke asks the driving coroutine to do.
private sealed interface EditorSignal {
    /** "Save & Compile" pressed with the editor's current [source]. */
    class Compile(val source: String) : EditorSignal

    /** The editor was closed (Close button, window-X, or Esc). */
    data object Close : EditorSignal
}

// Owns the live Swing widgets so the driving coroutine can flip the button state and paint results — all EDT-only.
private class EditorDialog(
    private val dialog: JDialog,
    private val saveButton: JButton,
    private val resultLabel: JLabel,
) {
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

    companion object {
        const val SAVE_LABEL: String = "Save & Compile"
        const val COMPILING_LABEL: String = "Compiling…"
        val OK_COLOR: Color = Color(0x2E, 0xA0, 0x43)
        val ERROR_COLOR: Color = Color(0xD1, 0x3B, 0x3B)
    }
}

// Builds and shows the non-modal editor dialog on the EDT, wiring its buttons/keystrokes to publish
// [EditorSignal]s. [language] is accepted for signature parity with the web editor (the Swing text area has no
// syntax highlighting to configure) and is surfaced only in the window title.
private fun buildEditorDialog(
    title: String,
    initialCode: String,
    language: String,
    signals: Channel<EditorSignal>,
): EditorDialog {
    val heading: String =
        if (language.isBlank()) "Edit code — $title" else "Edit code — $title (${language.uppercase()})"
    val dialog = JDialog(null as Frame?, heading, false)
    dialog.defaultCloseOperation = WindowConstants.DISPOSE_ON_CLOSE

    val area =
        JTextArea(initialCode).apply {
            font = Font(Font.MONOSPACED, Font.PLAIN, 13)
            tabSize = 2
            lineWrap = false
        }

    val resultLabel = JLabel(" ")
    val saveButton = JButton(EditorDialog.SAVE_LABEL)
    val closeButton = JButton("Close")
    val handle = EditorDialog(dialog, saveButton, resultLabel)

    // Save & Compile: flip to the compiling state and publish the current source; the coroutine paints the result.
    val requestCompile: () -> Unit = {
        handle.markCompiling()
        signals.trySend(EditorSignal.Compile(area.text))
    }
    saveButton.addActionListener { requestCompile() }
    closeButton.addActionListener { signals.trySend(EditorSignal.Close) }
    dialog.addWindowListener(
        object : WindowAdapter() {
            override fun windowClosed(event: WindowEvent?) {
                signals.trySend(EditorSignal.Close)
            }
        }
    )

    // Esc closes; Ctrl+S saves & compiles — parity with the web editor's shortcuts.
    dialog.rootPane.registerKeyboardAction(
        { signals.trySend(EditorSignal.Close) },
        KeyStroke.getKeyStroke("ESCAPE"),
        JComponent.WHEN_IN_FOCUSED_WINDOW,
    )
    dialog.rootPane.registerKeyboardAction(
        { requestCompile() },
        KeyStroke.getKeyStroke("control S"),
        JComponent.WHEN_IN_FOCUSED_WINDOW,
    )

    val buttons =
        JPanel().apply {
            add(resultLabel)
            add(closeButton)
            add(saveButton)
        }

    dialog.layout = BorderLayout()
    dialog.add(JScrollPane(area), BorderLayout.CENTER)
    dialog.add(buttons, BorderLayout.SOUTH)
    dialog.preferredSize = Dimension(900, 640)
    dialog.pack()
    dialog.setLocationRelativeTo(null)
    dialog.isVisible = true
    area.requestFocusInWindow()
    return handle
}
