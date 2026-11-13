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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';

import StoredCredentialRow, { type CredentialField } from '@/components/layout/StoredCredentialRow.vue';
import { i18n } from '@/i18n';

let mounted: VueWrapper | null = null;

const fields: CredentialField[] = [
  {
    name: 'clientId',
    label: 'Client ID',
    value: 'abc',
    placeholder: 'Your client id',
  },
  {
    name: 'clientSecret',
    label: 'Client secret',
    secret: true,
    placeholder: 'A secret is stored',
  },
];

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function openForm(): Promise<void> {
  mounted = mount(StoredCredentialRow, {
    props: {
      title: 'Spotify app',
      editLabel: 'Edit',
      dialogTitle: 'Edit the app',
      saveLabel: 'Save',
      removeLabel: 'Remove',
      fields,
      open: true,
    },
    attachTo: document.body,
    global: { plugins: [i18n] },
  });

  await flushPromises();
}

function fieldBlocks(): HTMLElement[] {
  return Array.from(document.body.querySelectorAll<HTMLElement>('[data-slot="field"]'));
}

describe('the stored credential row form', () => {
  it('renders each credential field as a Field block', async () => {
    await openForm();

    const names = fieldBlocks().map((block) => block.querySelector('input')?.getAttribute('name'));

    expect(names).toEqual(['clientId', 'clientSecret']);
  });

  it('links every label to its own input', async () => {
    await openForm();

    for (const block of fieldBlocks()) {
      const label = block.querySelector<HTMLLabelElement>('[data-slot="field-label"]')!;
      const input = block.querySelector<HTMLInputElement>('input')!;

      expect(input.id).not.toBe('');
      expect(label.getAttribute('for')).toBe(input.id);
    }

    const labels = fieldBlocks().map((block) => block.querySelector('label')!.textContent!.trim());

    expect(labels).toEqual(['Client ID', 'Client secret']);
  });

  it('keeps the secret masked, autocomplete off and the placeholders', async () => {
    await openForm();

    const inputs = Array.from(document.body.querySelectorAll<HTMLInputElement>('form input'));

    expect(inputs.map((input) => input.type)).toEqual(['text', 'password']);
    expect(inputs.map((input) => input.autocomplete)).toEqual(['off', 'new-password']);
    expect(inputs.map((input) => input.placeholder)).toEqual(['Your client id', 'A secret is stored']);
    expect(inputs[0]!.value).toBe('abc');
    expect(inputs[1]!.value).toBe('');
  });
});
