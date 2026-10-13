<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { Languages } from '@lucide/vue'
import type { AcceptableValue } from 'reka-ui'
import { useI18n } from 'vue-i18n'

import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { locales, setLocale, type Locale } from '@/i18n'

const { locale } = useI18n()

// Each language is named in its own language, so a reader finds theirs without reading the current one.
function ownName(tag: Locale): string {
  return new Intl.DisplayNames([tag], { type: 'language' }).of(tag) ?? tag
}

function choose(value: AcceptableValue): void {
  if (typeof value === 'string' && (locales as readonly string[]).includes(value)) void setLocale(value as Locale)
}
</script>

<template>
  <Select :model-value="locale" @update:model-value="choose">
    <SelectTrigger :aria-label="$t('common.language')">
      <Languages aria-hidden="true" />
      <SelectValue />
    </SelectTrigger>
    <!-- Below the button, so the open list never covers the picker or the header action beside it. -->
    <SelectContent position="popper" align="end">
      <!-- The group carries the list padding in this shadcn style; options straight in the list touch its edge. -->
      <SelectGroup>
        <SelectItem v-for="tag in locales" :key="tag" :value="tag" :lang="tag">{{ ownName(tag) }}</SelectItem>
      </SelectGroup>
    </SelectContent>
  </Select>
</template>
