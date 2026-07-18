// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

@file:OptIn(ExperimentalWasmJsInterop::class)

package bot.nomnomz.dashboard.core.io

import kotlin.js.ExperimentalWasmJsInterop

// Web clipboard write via the legacy execCommand path. Unlike navigator.clipboard.writeText (undefined in a
// non-secure http context, i.e. the self-host LAN dashboard), a hidden, selected <textarea> + execCommand('copy')
// works over http and returns a synchronous boolean — the honest success signal the DS copy controls need.
// A user gesture is always present here (the call originates from an onClick), which execCommand requires.
actual fun copyToClipboard(text: String): Boolean = execCommandCopy(text)

private fun execCommandCopy(text: String): Boolean =
    js(
        """{
            try {
                var ta = document.createElement('textarea');
                ta.value = text;
                ta.setAttribute('readonly', '');
                ta.style.position = 'fixed';
                ta.style.top = '-9999px';
                ta.style.left = '-9999px';
                ta.style.opacity = '0';
                document.body.appendChild(ta);
                var selection = document.getSelection();
                var savedRange = (selection && selection.rangeCount > 0) ? selection.getRangeAt(0) : null;
                ta.focus();
                ta.select();
                var ok = document.execCommand('copy');
                document.body.removeChild(ta);
                if (savedRange && selection) { selection.removeAllRanges(); selection.addRange(savedRange); }
                return ok;
            } catch (e) {
                return false;
            }
        }"""
    )
