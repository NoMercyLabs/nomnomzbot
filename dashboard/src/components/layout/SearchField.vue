<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The search box of a list. The key "/" focuses it from anywhere on the page, unless
// the person is already typing in a field.
import { useEventListener } from '@vueuse/core';
import { useTemplateRef } from 'vue';
import SearchMagnifyingGlass from '~icons/nnz/search-magnifying-glass';

import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group';
import { Kbd } from '@/components/ui/kbd';

defineProps<{ placeholder: string }>();

const query = defineModel<string>({ default: '' });
const input = useTemplateRef<{ $el: HTMLInputElement }>('input');
const shortcut: string = '/';

function isTyping(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLElement &&
    (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName))
  );
}

useEventListener(document, 'keydown', (event: KeyboardEvent): void => {
  if (
    event.key !== shortcut ||
    event.ctrlKey ||
    event.metaKey ||
    event.altKey ||
    isTyping(event.target)
  ) return;

  event.preventDefault();
  input.value?.$el.focus();
});
</script>

<template>
  <InputGroup data-slot="search-field" class="w-56">
    <InputGroupAddon>
      <SearchMagnifyingGlass />
    </InputGroupAddon>
    <InputGroupInput
      ref="input"
      v-model="query"
      type="search"
      :placeholder="placeholder"
      :aria-label="placeholder"
    />
    <InputGroupAddon align="inline-end">
      <Kbd>{{ shortcut }}</Kbd>
    </InputGroupAddon>
  </InputGroup>
</template>
