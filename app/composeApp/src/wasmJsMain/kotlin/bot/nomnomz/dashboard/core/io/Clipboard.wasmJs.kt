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
//
// CRITICAL: the Compose app renders inside `document.body.shadowRoot`. A textarea appended to the light
// `document.body` CANNOT take focus or hold a selection across the shadow boundary — `ta.focus()` leaves the
// active element on BODY and `ta.select()` yields an EMPTY selection, so execCommand returns `true` but copies
// NOTHING. Every copy button in the app silently failed for this reason. The textarea must be mounted in the
// same root as the app (the shadow root when present), where focus + selection actually take.
actual fun copyToClipboard(text: String): Boolean = execCommandCopy(text)

private fun execCommandCopy(text: String): Boolean =
    js(
        """{
            try {
                var root = document.body.shadowRoot || document.body;
                var ta = document.createElement('textarea');
                ta.value = text;
                ta.setAttribute('readonly', '');
                ta.style.position = 'fixed';
                ta.style.top = '-9999px';
                ta.style.left = '-9999px';
                ta.style.opacity = '0';
                root.appendChild(ta);
                var selection = (root.getSelection ? root.getSelection() : document.getSelection());
                var savedRange = (selection && selection.rangeCount > 0) ? selection.getRangeAt(0) : null;
                ta.focus();
                ta.select();
                ta.setSelectionRange(0, text.length);
                var ok = document.execCommand('copy');
                root.removeChild(ta);
                if (savedRange && selection) { selection.removeAllRanges(); selection.addRange(savedRange); }
                return ok;
            } catch (e) {
                return false;
            }
        }"""
    )
