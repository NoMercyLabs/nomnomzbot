// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Boot + recovery guard. Loaded BEFORE composeApp.js so window.__nnzAppAlive exists when the app first
// renders, and so uncaught crashes during load are caught. The page must never sit frozen: either the app
// renders (overlay hidden), or the operator is offered a reload.
//
// External rather than inline: the dashboard's Content-Security-Policy allows script only from 'self' with no
// 'unsafe-inline' and no nonce, so as an inline block this whole guard was blocked on every load — meaning
// __nnzAppAlive never existed to hide the overlay, and a stalled load showed a frozen page instead of the
// recovery screen it was written to guarantee. The Reload button's handler is bound here for the same
// reason: an inline onclick attribute is inline script too, and was equally dead.
(function () {
    var ready = false;
    var lastBeat = 0;
    var visibleSince = Date.now();
    var STALE_AFTER_MS = 8000;

    // Looks in the shadow root too: the overlay lives there once [mountVisible] has moved it.
    function el(id) {
        var root = document.body && document.body.shadowRoot;
        return (root && root.getElementById(id)) || document.getElementById(id);
    }

    // The Compose app calls this every second once it has rendered a frame. The first call tears the overlay
    // down; the rest prove the app is still running.
    window.__nnzAppAlive = function () {
        lastBeat = Date.now();
        if (ready) return;
        ready = true;
        var boot = el("nnz-boot");
        if (boot) boot.style.display = "none";
    };

    // Compose renders into a shadow root on <body>, and a shadow host lays out none of its light-DOM children.
    // So once the app has started, the overlay is invisible where index.html put it: move it, and a copy of
    // its styles (document styles do not reach into a shadow root), into the shadow root.
    function mountVisible(boot) {
        var root = document.body && document.body.shadowRoot;
        if (!root || boot.parentNode === root) return;
        var style = document.getElementById("nnz-boot-style");
        if (style) root.appendChild(style.cloneNode(true));
        root.appendChild(boot);
    }

    function showRecovery(title, sub) {
        var boot = el("nnz-boot");
        if (!boot) return;
        mountVisible(boot);
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
    // The app rendered, then stopped beating: an uncaught error in a coroutine killed the recomposer. That error
    // is only logged to the console and never reaches window 'error', so without this the dashboard sits frozen
    // on its last frame. A hidden tab throttles timers, so the clock only runs while the page is visible.
    document.addEventListener("visibilitychange", function () {
        if (document.visibilityState === "visible") visibleSince = Date.now();
    });
    setInterval(function () {
        if (!ready || document.visibilityState !== "visible") return;
        var now = Date.now();
        if (now - visibleSince < STALE_AFTER_MS) return;
        if (now - lastBeat > STALE_AFTER_MS) {
            showRecovery("The dashboard stopped responding", "Reload to pick up where you left off.");
        }
    }, 2000);

    window.addEventListener("error", function () {
        showRecovery("The dashboard hit a snag", "A reload usually fixes it.");
    });
    window.addEventListener("unhandledrejection", function () {
        if (!ready) showRecovery("The dashboard hit a snag", "A reload usually fixes it.");
    });
})();
