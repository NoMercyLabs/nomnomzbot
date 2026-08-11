// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, it, expect, vi, afterEach } from "vitest";
import { initDevicePairing, pollDevicePairing } from "../src/connection/deviceFlow.js";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("initDevicePairing", () => {
  it("returns the parsed device-init payload on success", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(200, {
        status: "ok",
        data: {
          deviceCode: "dc-123",
          userCode: "ABCD2345",
          verificationUri: "http://localhost:5080/api/v1/automation/pair/device/approve?code=ABCD2345",
          expiresAt: "2026-08-11T00:10:00Z",
          pollIntervalSeconds: 3,
        },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const result = await initDevicePairing("http://localhost:5080");

    expect(result).not.toBeNull();
    expect(result!.deviceCode).toBe("dc-123");
    expect(result!.userCode).toBe("ABCD2345");
    expect(result!.pollIntervalMs).toBe(3000);
    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:5080/automation/v1/pair/device/init",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("strips a trailing slash from the host so the request path never doubles up", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(200, {
        status: "ok",
        data: {
          deviceCode: "dc-123",
          userCode: "ABCD2345",
          verificationUri: "https://dev.nomnomz.bot/api/v1/automation/pair/device/approve?code=ABCD2345",
          expiresAt: "2026-08-11T00:10:00Z",
          pollIntervalSeconds: 3,
        },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await initDevicePairing("https://dev.nomnomz.bot/");

    expect(fetchMock).toHaveBeenCalledWith(
      "https://dev.nomnomz.bot/automation/v1/pair/device/init",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("returns null when the backend is unreachable", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockRejectedValue(new Error("ECONNREFUSED")),
    );

    const result = await initDevicePairing("http://localhost:5080");

    expect(result).toBeNull();
  });

  it("returns null when the backend answers status:error", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(429, { status: "error" })));

    const result = await initDevicePairing("http://localhost:5080");

    expect(result).toBeNull();
  });
});

describe("pollDevicePairing", () => {
  it("reports pending without ever exposing a token", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse(200, { status: "ok", data: { status: "pending" } })),
    );

    const result = await pollDevicePairing("http://localhost:5080", "dc-123");

    expect(result).toBe("pending");
  });

  it("returns the pairing state once approved", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse(200, {
          status: "ok",
          data: {
            status: "approved",
            backendUrl: "http://localhost:5080",
            token: "nnzb_ak_real",
            scopes: ["invoke", "events", "read"],
            tokenExpiresAt: "2026-09-10T00:00:00Z",
          },
        }),
      ),
    );

    const result = await pollDevicePairing("http://localhost:5080", "dc-123");

    expect(result).not.toBe("pending");
    expect(result).not.toBeNull();
    expect((result as { token: string }).token).toBe("nnzb_ak_real");
  });

  it("returns null on an expired or unknown device code", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse(404, { status: "error", message: "NOT_FOUND" })),
    );

    const result = await pollDevicePairing("http://localhost:5080", "gone");

    expect(result).toBeNull();
  });
});
