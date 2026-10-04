// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.resources

import java.io.File
import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue
import kotlin.test.fail

// The page-level "Rotate token" button rotates the CHANNEL-wide token. Widget addresses carry each
// widget's own token, so they keep working. The dialog must name what really stops working: the voice
// listener link, calendar subscription links and old addresses that still use the channel-wide token.
class ChannelTokenRotateCopyTest {

    @Test
    fun english_dialog_names_what_stops_and_what_keeps_working() {
        val message: String = readStrings("values").getValue("widgets_rotate_token_message")
        assertFalse(message.startsWith("Every overlay browser-source URL"), message)
        assertTrue(message.contains("voice listener", ignoreCase = true), message)
        assertTrue(message.contains("calendar", ignoreCase = true), message)
        assertTrue(message.contains("own token", ignoreCase = true), message)
    }

    @Test
    fun dutch_dialog_names_what_stops_and_what_keeps_working() {
        val message: String = readStrings("values-nl").getValue("widgets_rotate_token_message")
        assertFalse(message.startsWith("Alle overlay-browserbron"), message)
        assertTrue(message.contains("spraaklistener", ignoreCase = true), message)
        assertTrue(message.contains("agenda", ignoreCase = true), message)
        assertTrue(message.contains("eigen token", ignoreCase = true), message)
    }

    @Test
    fun the_button_says_channel_token_in_both_languages() {
        assertTrue(readStrings("values").getValue("widgets_rotate_token_action").contains("channel", true))
        assertTrue(readStrings("values-nl").getValue("widgets_rotate_token_action").contains("kanaal", true))
    }

    private fun readStrings(dir: String): Map<String, String> {
        val file = File(resourcesRoot(), "$dir/strings.xml")
        if (!file.isFile) fail("Missing $dir/strings.xml")
        return Regex("<string name=\"([^\"]+)\">(.*?)</string>", RegexOption.DOT_MATCHES_ALL)
            .findAll(file.readText())
            .associate { it.groupValues[1] to it.groupValues[2] }
    }

    private fun resourcesRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, "app/composeApp/src/commonMain/composeResources")
            if (candidate.isDirectory) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate composeResources from ${System.getProperty("user.dir")}")
    }
}
