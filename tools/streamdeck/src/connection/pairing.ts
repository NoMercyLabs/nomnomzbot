// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import http, { type Server } from "node:http";
import { setPairingState } from "./tokenStore.js";
import { automationClient } from "./automationClient.js";

/** Loopback candidate ports the dashboard's browser JS probes (stream-deck.md D7). */
export const CANDIDATE_PORTS = [61325, 61326, 61327, 61328, 61329];

interface HandoffBody {
  code: string;
  backendUrl: string;
}

interface PairResponse {
  success: boolean;
  data?: { backendUrl: string; token: string; scopes: string[]; tokenExpiresAt: string };
  message?: string;
}

/**
 * The plugin-side half of D7's automatic handoff: a local HTTP listener the dashboard's browser JS
 * posts the pairing code to. Redeems it itself against the real backend and stores the result — zero
 * typing on the golden (same-machine) path. Manual code entry (D2) remains the property-inspector
 * fallback for the cases this can't reach (remote device, different machine).
 */
export function startPairingListener(onPaired: () => void): Server | null {
  const server = http.createServer((req, res) => {
    if (req.method !== "POST" || req.url !== "/nomnomz/pair-handoff") {
      res.writeHead(404).end();
      return;
    }
    const origin = req.headers.origin;
    if (origin) {
      res.setHeader("Access-Control-Allow-Origin", origin);
      res.setHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
      res.setHeader("Access-Control-Allow-Headers", "Content-Type");
    }

    let body = "";
    req.on("data", (chunk: Buffer) => (body += chunk.toString()));
    req.on("end", () => {
      void handleHandoff(body)
        .then((ok) => {
          res.writeHead(ok ? 200 : 400, { "Content-Type": "application/json" });
          res.end(JSON.stringify({ success: ok }));
          if (ok) onPaired();
        })
        .catch(() => {
          res.writeHead(500).end();
        });
    });
  });

  for (const port of CANDIDATE_PORTS) {
    try {
      server.listen(port, "127.0.0.1");
      return server;
    } catch {
      continue;
    }
  }
  return null;
}

async function handleHandoff(rawBody: string): Promise<boolean> {
  let parsed: HandoffBody;
  try {
    parsed = JSON.parse(rawBody) as HandoffBody;
  } catch {
    return false;
  }
  if (!parsed.code || !parsed.backendUrl) return false;

  const response = await fetch(`${parsed.backendUrl}/automation/v1/pair`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code: parsed.code, device: { kind: "streamdeck", name: null } }),
  });
  const result = (await response.json().catch(() => null)) as PairResponse | null;
  if (!response.ok || !result?.success || !result.data) return false;

  await setPairingState({
    backendUrl: result.data.backendUrl,
    token: result.data.token,
    tokenExpiresAt: result.data.tokenExpiresAt,
    deviceKind: "streamdeck",
  });
  await automationClient.connectStream();
  return true;
}

/** Manual-code fallback (D2/D7) — used by a not-yet-paired action's property inspector. */
export async function redeemCodeManually(backendUrl: string, code: string): Promise<boolean> {
  return handleHandoff(JSON.stringify({ code, backendUrl }));
}
