// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
//
// Exits 0 when the local API is listening on 5080, 1 otherwise. Run as a child process by
// webpack.config.d/proxy.js, which needs the answer synchronously to pick a proxy target.

const net = require("net");

const socket = net.connect(5080, "127.0.0.1");
socket.setTimeout(600);
socket.on("connect", () => {
    socket.destroy();
    process.exit(0);
});
socket.on("error", () => process.exit(1));
socket.on("timeout", () => {
    socket.destroy();
    process.exit(1);
});
