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

// Every admin page source, as text.
const pages: Record<string, string> = import.meta.glob('../Admin*View.vue', {
  eager: true,
  query: '?raw',
  import: 'default',
});

// On a phone the frame styles panels through the page body's data-page-body mark: flat and edge to edge. An admin
// page that draws its own PageBody without the mark would show rounded inset panels, a second style on one console.
describe('the phone panel style of the admin pages', () => {
  it('reads every admin page', () => {
    expect(Object.keys(pages).length).toBeGreaterThanOrEqual(10);
  });

  it('gives every PageBody the data-page-body mark, so all admin pages share one phone style', () => {
    const unmarked = Object.entries(pages)
      .filter(([, source]) =>
        /<PageBody(?![^>]*data-page-body)[^>]*>/.test(source))
      .map(([path]) => path);

    expect(unmarked).toEqual([]);
  });
});
