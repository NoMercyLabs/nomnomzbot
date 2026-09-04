// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Display-language override hook. The Compose/Wasm app publishes the operator's forced UI language to
// window.__customLocale; this makes navigator.languages return it so Compose resources resolve against the
// chosen language. When unset (System default) the browser's own locale is used.
//
// External rather than inline: the dashboard's Content-Security-Policy allows script only from 'self' with no
// 'unsafe-inline' and no nonce, so as an inline block this was silently blocked on every load and the locale
// override never applied.
(function () {
    var base = Object.getOwnPropertyDescriptor(Navigator.prototype, "languages");

    Object.defineProperty(
        Navigator.prototype,
        "languages",
        Object.assign({}, base, {
            get: function () {
                if (window.__customLocale) {
                    return [window.__customLocale];
                }
                return base.get.apply(this);
            }
        })
    );
})();
