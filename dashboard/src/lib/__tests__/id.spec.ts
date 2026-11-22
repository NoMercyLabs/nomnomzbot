// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';

import { newId } from '@/lib/id';

const uuidV4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const browserCrypto = globalThis.crypto;

describe('newId', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('gives the browser id on a secure page', () => {
    vi.stubGlobal('crypto', {
      randomUUID: () =>
        '11111111-1111-4111-8111-111111111111',
    });

    expect(newId()).toBe('11111111-1111-4111-8111-111111111111');
  });

  // A dashboard opened over plain http on a home network has no crypto.randomUUID.
  it('still gives a version 4 id on a page that is not secure', () => {
    vi.stubGlobal('crypto', {
      getRandomValues: (bytes: Uint8Array) =>
        bytes.fill(0xff),
    });

    expect(newId()).toBe('ffffffff-ffff-4fff-bfff-ffffffffffff');
  });

  it('gives a different id each time on a page that is not secure', () => {
    vi.stubGlobal('crypto', {
      getRandomValues: (bytes: Uint8Array<ArrayBuffer>) =>
        browserCrypto.getRandomValues(bytes),
    });

    const first = newId();
    const second = newId();

    expect(first).toMatch(uuidV4);
    expect(second).toMatch(uuidV4);
    expect(first).not.toBe(second);
  });
});
