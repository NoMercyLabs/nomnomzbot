// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Mock } from 'vitest';

// A settings spec mocks the HTTP client with one answer per path. The page gates read the access
// query (/roles/effective/me), so this wraps a mock's current answers and adds that one: the keys
// returned are the ones `held()` lists at the time of the call, so a test can change them.
export const accessPath = '/api/v{version}/channels/{channelId}/roles/effective/me';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

type Reader = (path: string, init: unknown) => Promise<Answer>;

export function answerHeldKeys(client: Mock<Reader>, held: () => readonly string[]): void {
  const inner = client.getMockImplementation() as Reader;

  client.mockImplementation(async (path, init) =>
    path === accessPath
      ? {
          data: { data: { heldActionKeys: [...held()] } },
          response: { ok: true, status: 200 },
        }
      : inner(path, init));
}
