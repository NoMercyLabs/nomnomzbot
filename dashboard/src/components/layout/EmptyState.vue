<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one empty state of a panel. kind says why nothing is shown: none = nothing made yet,
// noMatch = a search found nothing, filtered = a filter hides every row, gated = the person may not see it.
// The action slot holds the one primary action (the "make the first" button); the icon stays neutral.
import type { Component } from 'vue';

import Inbox from '~icons/nnz/inbox';
import Locked from '~icons/nnz/locked';
import SearchMagnifyingGlass from '~icons/nnz/search-magnifying-glass';
import SettingsFader from '~icons/nnz/settings-fader';

type EmptyKind = 'none' | 'noMatch' | 'filtered' | 'gated';

const KIND_ICON = {
  none: Inbox,
  noMatch: SearchMagnifyingGlass,
  filtered: SettingsFader,
  gated: Locked,
} as const satisfies Record<EmptyKind, Component>;

const props = withDefaults(
  defineProps<{
    kind?: EmptyKind;
    title?: string;
    description?: string;
    // A component replaces the icon of the kind; false shows no icon (a plain text empty).
    icon?: Component | false;
    // Inline in a flat list: left aligned, no padding, no icon.
    bare?: boolean;
  }>(),
  {
    kind: 'none',
    title: undefined,
    description: undefined,
    icon: undefined,
    bare: false,
  },
);
</script>

<template>
  <div
    data-slot="panel-empty"
    :data-empty-kind="kind"
    class="text-sm text-muted-foreground"
    :class="bare ? '' : 'flex h-full flex-col items-center justify-center gap-3 p-4 text-center'"
  >
    <component
      :is="props.icon ?? KIND_ICON[kind]"
      v-if="!bare && props.icon !== false"
      class="size-8 text-muted-foreground/70"
      aria-hidden="true"
    />
    <div v-if="title || description" class="flex max-w-prose flex-col gap-1">
      <p v-if="title" class="text-sm font-medium text-foreground">{{ title }}</p>
      <p v-if="description">{{ description }}</p>
    </div>
    <slot />
    <div v-if="$slots.action" data-slot="panel-empty-action" class="flex flex-wrap justify-center gap-2">
      <slot name="action" />
    </div>
  </div>
</template>
