<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Facts as a label and a value (C11): a value with no source shows a dash and, when given, the reason.
// The value slot lets a row show a StatusBadge or a link instead of plain text.
export interface KeyValueItem {
  label: string
  value?: string | number | null
  reason?: string
}

defineProps<{ items: readonly KeyValueItem[] }>()

const missing: string = '-'
</script>

<template>
  <dl data-slot="key-value-list" class="grid grid-cols-[minmax(6rem,9rem)_minmax(0,1fr)] gap-x-4 gap-y-2 text-sm">
    <template v-for="item in items" :key="item.label">
      <dt class="text-muted-foreground">{{ item.label }}</dt>
      <dd class="m-0 min-w-0 wrap-break-word">
        <slot name="value" :item="item">
          <template v-if="item.value === undefined || item.value === null || item.value === ''">
            {{ missing }}
            <span v-if="item.reason" class="text-muted-foreground">{{ item.reason }}</span>
          </template>
          <template v-else>{{ item.value }}</template>
        </slot>
      </dd>
    </template>
  </dl>
</template>
