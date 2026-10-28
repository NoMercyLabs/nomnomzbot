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

import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it } from 'vitest';

import { useSaveErrorsStore } from '../saveErrors';

const title = 'frame.inbox.saveFailedTitle';
const message = 'frame.inbox.saveFailedMessage';
const turnOff = { key: 'turnOffWidget', params: { id: 'w1' } };

describe('the save error inbox', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('folds the same failure on the same page into one entry with a count', () => {
    const store = useSaveErrorsStore();

    for (let attempt = 0; attempt < 4; attempt++) {
      store.report(title, message, turnOff, '/app/overlays');
    }

    expect(store.items).toHaveLength(1);

    expect(store.items[0]).toMatchObject({
      count: 4,
      what: turnOff,
      where: '/app/overlays',
    });
  });

  it('keeps a different change, or the same change on another page, as its own entry', () => {
    const store = useSaveErrorsStore();

    store.report(title, message, turnOff, '/app/overlays');
    store.report(title, message, { key: 'turnOffWidget', params: { id: 'w2' } }, '/app/overlays');
    store.report(title, message, turnOff, '/app/obs');

    expect(store.items.map((item) => item.count)).toEqual([1, 1, 1]);
  });

  it('removes one entry when it is dismissed and leaves the others', () => {
    const store = useSaveErrorsStore();

    store.report(title, message, turnOff, '/app/overlays');
    store.report(title, message, turnOff, '/app/obs');
    store.dismiss(store.items[0]!.id);

    expect(store.items.map((item) => item.where)).toEqual(['/app/obs']);
  });
});
