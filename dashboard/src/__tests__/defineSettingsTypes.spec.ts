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

import { describe, expect, expectTypeOf, it } from 'vitest';

// nnz.defineSettings({...}) declares an overlay's settings form from its own code and returns the injected
// settings typed from that form: a misspelled key or a wrong value type fails vue-tsc, not the stream.
describe('NnzOverlaySdk.defineSettings typing', () => {
  const sdk = {
    defineSettings: () => ({}) as never,
  } as unknown as NnzOverlaySdk;

  it('types each declared key from its field type', () => {
    const settings = sdk.defineSettings({
      fields: [
        {
          key: 'fontSize',
          type: 'number',
          default: 24,
        },
        {
          key: 'title',
          type: 'text',
          default: 'Hi',
        },
        {
          key: 'accent',
          type: 'color',
          default: '#ff0066',
        },
        {
          key: 'loud',
          type: 'bool',
          default: false,
        },
        {
          key: 'mode',
          type: 'select',
          default: 'dark',
          options: [{ value: 'dark' }, { value: 'light' }],
        },
        {
          key: 'tags',
          type: 'list',
          default: [],
        },
      ],
    });

    expectTypeOf(settings.fontSize).toEqualTypeOf<number>();
    expectTypeOf(settings.title).toEqualTypeOf<string>();
    expectTypeOf(settings.accent).toEqualTypeOf<string>();
    expectTypeOf(settings.loud).toEqualTypeOf<boolean>();
    expectTypeOf(settings.mode).toEqualTypeOf<'dark' | 'light'>();
    expectTypeOf(settings.tags).toEqualTypeOf<string[]>();
    expect(settings).toBeDefined();
  });

  it('refuses a key the form does not declare', () => {
    const settings = sdk.defineSettings({
      fields: [{
        key: 'fontSize',
        type: 'number',
        default: 24,
      }],
    });

    // @ts-expect-error fontsize is not declared (the key is fontSize)
    expect(settings.fontsize).toBeUndefined();
  });
});
