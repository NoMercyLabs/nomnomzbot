// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
