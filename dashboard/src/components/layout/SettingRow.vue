<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A setting: title and help on the left, a Switch on the right. The whole row is one label,
// so a press anywhere on it toggles the switch. Radius 8 fits a RowGroup (12 - 4).
import { useId } from 'vue'

import { Switch } from '@/components/ui/switch'

withDefaults(
  defineProps<{
    title: string
    description?: string
    disabled?: boolean
  }>(),
  {
    description: undefined,
    disabled: false,
  },
)

const checked = defineModel<boolean>({ default: false })
const titleId = useId()
</script>

<template>
  <label
    data-slot="setting-row"
    class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2"
    :class="disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer hover:bg-foreground/5'"
  >
    <span class="min-w-0 flex-1">
      <span :id="titleId" class="block text-sm font-medium wrap-break-word">{{ title }}</span>
      <span v-if="description" class="block text-sm text-muted-foreground">{{ description }}</span>
    </span>
    <Switch v-model="checked" :disabled="disabled" :aria-labelledby="titleId" />
  </label>
</template>
