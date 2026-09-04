// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Boot + recovery guard. Loaded BEFORE composeApp.js so window.__nnzAppReady exists when the app first
// renders, and so uncaught crashes during load are caught. The page must never sit frozen: either the app
// renders (overlay hidden), or the operator is offered a reload.
//
// External rather than inline: the dashboard's Content-Security-Policy allows script only from 'self' with no
// 'unsafe-inline' and no nonce, so as an inline block this whole guard was blocked on every load — meaning
// __nnzAppReady never existed to hide the overlay, and a stalled load showed a frozen page instead of the
// recovery screen it was written to guarantee. The Reload button's handler is bound here for the same
// reason: an inline onclick attribute is inline script too, and was equally dead.
(function () {
    var ready = false;

    function el(id) {
        return document.getElementById(id);
    }

    // The Compose app calls this once it has rendered a frame — tear the overlay down.
    window.__nnzAppReady = function () {
        ready = true;
        var boot = el("nnz-boot");
        if (boot) boot.style.display = "none";
    };

    function showRecovery(title, sub) {
        var boot = el("nnz-boot");
        if (!boot) return;
        boot.style.display = "flex";
        var spinner = el("nnz-boot-spinner");
        if (spinner) spinner.style.display = "none";
        el("nnz-boot-title").textContent = title;
        el("nnz-boot-sub").textContent = sub;
        el("nnz-boot-reload").style.display = "";
    }

    function bindReload() {
        var button = el("nnz-boot-reload");
        if (button) {
            button.addEventListener("click", function () {
                location.reload();
            });
        }
    }

    // The button lives further down the document than this script, so wait for the parse to finish.
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", bindReload);
    } else {
        bindReload();
    }

    // Load stalled — the app never signaled ready. Almost always the API is briefly unreachable
    // (restart / 502); it clears on its own, and a reload retries. Never leave a blank screen.
    setTimeout(function () {
        if (ready) return;
        showRecovery(
            "Still loading…",
            "The server may be restarting. This clears on its own — or reload to retry."
        );
        var spinner = el("nnz-boot-spinner");
        if (spinner) spinner.style.display = "";
        el("nnz-boot-title").textContent = "Still loading…";
    }, 12000);

    // Any uncaught crash (e.g. a resource bundle that 502'd mid-load) shows a recovery screen with a reload
    // rather than a frozen page. Only genuine uncaught errors reach window 'error'; benign console warnings
    // (WebGL info, rAF timing) do not. Unhandled rejections only count during boot, to avoid covering a
    // working dashboard over a stray late rejection.
    window.addEventListener("error", function () {
        showRecovery("The dashboard hit a snag", "A reload usually fixes it.");
    });
    window.addEventListener("unhandledrejection", function () {
        if (!ready) showRecovery("The dashboard hit a snag", "A reload usually fixes it.");
    });
})();
