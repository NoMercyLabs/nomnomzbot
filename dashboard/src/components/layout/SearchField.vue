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
import { useTemplateRef, type HTMLAttributes } from 'vue';
import SearchMagnifyingGlass from '~icons/nnz/search-magnifying-glass';

import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group';
import { Kbd } from '@/components/ui/kbd';
import { useShortcut } from '@/composables/useShortcut';
import { cn } from '@/lib/utils';

// A list search fills its row; only a page that puts it inline (a header bar) passes its own width.
const props = defineProps<{ placeholder: string; class?: HTMLAttributes['class'] }>();

const query = defineModel<string>({ default: '' });
const input = useTemplateRef<{ $el: HTMLInputElement }>('input');
const shortcut = { key: '/', label: 'common.shortcuts.search' } as const;

useShortcut(shortcut, () => {
  input.value?.$el.focus();
});
</script>

<template>
  <InputGroup data-slot="search-field" :class="cn('w-full', props.class)">
    <InputGroupAddon>
      <SearchMagnifyingGlass />
    </InputGroupAddon>
    <InputGroupInput
      ref="input"
      v-model="query"
      type="search"
      :placeholder="placeholder"
      :aria-label="placeholder"
      @keydown.escape="input?.$el.blur()"
    />
    <InputGroupAddon align="inline-end">
      <!-- A quiet key cap: an outline, not a filled block, curved to the field around it
        (16px minus the 8px inset). The addon rounds key caps itself, so this radius must win. -->
      <Kbd class="rounded-[calc(var(--radius-md)-8px)]! border border-border bg-transparent">
        {{ shortcut.key }}
      </Kbd>
    </InputGroupAddon>
  </InputGroup>
</template>
