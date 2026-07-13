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
import java.awt.Dimension
import java.awt.Font
import java.awt.Frame
import javax.swing.JButton
import javax.swing.JComponent
import javax.swing.JDialog
import javax.swing.JPanel
import javax.swing.JScrollPane
import javax.swing.JTextArea
import javax.swing.KeyStroke
import javax.swing.SwingUtilities
import javax.swing.WindowConstants
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

// Desktop custom-code editor — a native modal Swing dialog with a monospace text area and Save / Cancel.
// The dialog is built and shown on the AWT event dispatch thread; the modal setVisible(true) pumps its own
// event loop and returns only once the operator closes it, so the IO coroutine simply blocks on
// invokeAndWait until then. (The web build gets the richer CodeMirror overlay; desktop uses the platform's
// native text editing.)
actual class CustomCodeEditor : CustomCodeEditorIO {
    actual override suspend fun edit(
        title: String,
        initialCode: String,
        language: String,
    ): String? =
        withContext(Dispatchers.IO) {
            // A one-slot holder the EDT writes and this thread reads once the modal returns. Null means the
            // edit was cancelled (or the window was closed); a non-null value — empty string included — is a save.
            val result: Array<String?> = arrayOfNulls(1)

            SwingUtilities.invokeAndWait {
                val dialog = JDialog(null as Frame?, "Edit code — $title", true)
                dialog.defaultCloseOperation = WindowConstants.DISPOSE_ON_CLOSE

                val area =
                    JTextArea(initialCode).apply {
                        font = Font(Font.MONOSPACED, Font.PLAIN, 13)
                        tabSize = 2
                        lineWrap = false
                    }

                val save = JButton("Save")
                val cancel = JButton("Cancel")
                save.addActionListener {
                    result[0] = area.text
                    dialog.dispose()
                }
                cancel.addActionListener {
                    result[0] = null
                    dialog.dispose()
                }

                // Esc cancels; Ctrl+S saves — parity with the web editor's shortcuts.
                dialog.rootPane.registerKeyboardAction(
                    { result[0] = null; dialog.dispose() },
                    KeyStroke.getKeyStroke("ESCAPE"),
                    JComponent.WHEN_IN_FOCUSED_WINDOW,
                )
                dialog.rootPane.registerKeyboardAction(
                    { result[0] = area.text; dialog.dispose() },
                    KeyStroke.getKeyStroke("control S"),
                    JComponent.WHEN_IN_FOCUSED_WINDOW,
                )

                val buttons = JPanel().apply {
                    add(cancel)
                    add(save)
                }

                dialog.layout = BorderLayout()
                dialog.add(JScrollPane(area), BorderLayout.CENTER)
                dialog.add(buttons, BorderLayout.SOUTH)
                dialog.preferredSize = Dimension(900, 640)
                dialog.pack()
                dialog.setLocationRelativeTo(null)
                area.requestFocusInWindow()
                // Modal: pumps the EDT until the dialog is disposed, then returns here.
                dialog.isVisible = true
            }

            result[0]
        }
}
