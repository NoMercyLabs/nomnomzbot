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

import { mount } from '@vue/test-utils';
import { expect, it } from 'vitest';

import { FieldInfo } from '@/components/ui/field';
import { Select } from '@/components/ui/select';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import TtsSegmentsField from '../components/TtsSegmentsField.vue';
import type { FieldRow } from '../components/stepFields';
import { readTtsBlocks, setTtsBlockField } from '../components/ttsSegments';

it('keeps the spoken block and its earlier settings while editing style and reading', () => {
  const source = JSON.stringify([{
    text: '42',
    voice: 'en-US-AriaNeural',
    rate: '+5%',
  }]);
  const styled = setTtsBlockField(readTtsBlocks(source)!, 0, 'style', 'cheerful');
  const reading = setTtsBlockField(readTtsBlocks(styled)!, 0, 'sayAs', 'cardinal');

  expect(JSON.parse(reading)).toEqual([{
    text: '42',
    voice: 'en-US-AriaNeural',
    rate: '+5%',
    style: 'cheerful',
    sayAs: 'cardinal',
  }]);
});

it('offers both controls and writes a changed style into the block JSON', async () => {
  i18n.global.mergeLocaleMessage('en', en);
  const row: FieldRow = {
    name: 'segments',
    label: 'Segments',
    kind: 'text',
    control: 'tts_segments',
    value: '[{"text":"42","style":"cheerful","sayAs":"cardinal"}]',
    help: 'Several voices in one clip',
    choices: [],
    required: false,
    templated: true,
    target: 'action',
  };
  const wrapper = mount(TtsSegmentsField, { props: { row }, global: { plugins: [i18n] } });

  expect(wrapper.findAllComponents(FieldInfo)).toHaveLength(3);
  expect(wrapper.text()).toContain('Speaking style');
  expect(wrapper.text()).toContain('Read as');
  const selects = wrapper.findAllComponents(Select);

  expect(selects).toHaveLength(2);
  selects[0]!.vm.$emit('update:modelValue', 'sad');
  await wrapper.vm.$nextTick();

  const emitted = wrapper.emitted('change');

  expect(JSON.parse(String(emitted?.[0]?.[0]))).toEqual([{
    text: '42',
    style: 'sad',
    sayAs: 'cardinal',
  }]);

  wrapper.unmount();
});
