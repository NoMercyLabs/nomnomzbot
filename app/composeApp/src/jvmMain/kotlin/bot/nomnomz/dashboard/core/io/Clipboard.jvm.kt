// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.io

import java.awt.Toolkit
import java.awt.datatransfer.StringSelection

// Desktop clipboard write via AWT. The JVM has a real system clipboard in every context, so this is a direct,
// confirmable write (unlike the web's insecure-context caveat).
actual fun copyToClipboard(text: String): Boolean =
    try {
        Toolkit.getDefaultToolkit().systemClipboard.setContents(StringSelection(text), null)
        true
    } catch (_: Throwable) {
        false
    }
