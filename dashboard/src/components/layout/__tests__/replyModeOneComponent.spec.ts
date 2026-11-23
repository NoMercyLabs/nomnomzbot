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

import { describe, expect, it } from 'vitest';

// Every responder reacts through the one shared ReplyModeField (owner 2026-11-23: "Make this 1 component used on
// all sides"): same options everywhere, so no surface passes its own kinds or builds its own reply switch.
const sources = import.meta.glob<string>('/src/**/*.vue', {
  query: '?raw',
  import: 'default',
  eager: true,
});

const shared = '/src/components/layout/ReplyModeField.vue';
const others = Object.entries(sources).filter(([path]) => path !== shared);

describe('the reaction section is one component', () => {
  it('finds the responders that use it', () => {
    const users = others.filter(([, source]) => source.includes('<ReplyModeField'));

    expect(users).toHaveLength(8);
  });

  it('lets no responder pass its own kinds list to it', () => {
    const offenders = others
      .filter(([, source]) => /<ReplyModeField[^>]*:kinds=/s.test(source))
      .map(([path]) => path);

    expect(offenders).toEqual([]);
  });

  it('lets no other file build its own reply-mode switch', () => {
    const offenders = others
      .filter(([, source]) => /common\.replyMode\.(simple|full)/.test(source))
      .map(([path]) => path);

    expect(offenders).toEqual([]);
  });
});
