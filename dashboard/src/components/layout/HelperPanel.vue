<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The full helper list of one place (spec row Textarea + Command), grouped by the first part of the key.
// Pressing a helper inserts "{key}" at the caret its TemplateField remembered; the press does not steal focus.
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useTemplateHelpers, type TemplateContext, type TemplateHelper } from '@/api/templates'
import { Input } from '@/components/ui/input'

import { useHelperText } from './useHelperText'
import { braced, filterHelpers, groupHelpers } from './templatePlaceholders'

const props = withDefaults(
  defineProps<{
    target: { insert: (text: string) => unknown } | null
    context: TemplateContext
    eventType?: string
  }>(),
  { eventType: undefined },
)

const { t } = useI18n()
const { describe, groupLabel } = useHelperText()
const helpers = useTemplateHelpers(
  () => props.context,
  () => props.eventType,
)
const query = ref('')
const shown = computed<TemplateHelper[]>(() =>
  filterHelpers(helpers.data.value ?? [], query.value.trim()),
)
const groups = computed(() => groupHelpers(shown.value))
</script>

<template>
  <div data-slot="helper-panel" class="flex min-h-0 flex-col gap-2">
    <Input
      v-model="query"
      type="search"
      :aria-label="t('template.panel.search')"
      :placeholder="t('template.panel.search')"
    />
    <div class="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto">
      <p v-if="groups.length === 0" class="px-1 text-sm text-muted-foreground">
        {{ t('template.panel.empty') }}
      </p>
      <section v-for="entry in groups" :key="entry.group" class="flex shrink-0 flex-col gap-1">
        <h3 class="px-1 text-xs font-medium text-muted-foreground">
          {{ groupLabel(entry.group) }}
        </h3>
        <!-- A row of several lines, as SummaryRow: a plain button, since the frame gives Button one fixed height. -->
        <button
          v-for="helper in entry.helpers"
          :key="helper.key"
          type="button"
          :data-helper-row="helper.key"
          class="flex min-h-11 cursor-pointer flex-col items-start gap-0.5 rounded-[8px] px-2 py-1.5 text-left hover:bg-foreground/5"
          @mousedown.prevent
          @click="target?.insert(braced(helper.key))"
        >
          <span class="font-mono text-sm font-medium">{{ braced(helper.key) }}</span>
          <span v-if="describe(helper)" class="text-xs text-muted-foreground">{{
            describe(helper)
          }}</span>
          <span v-if="helper.sample" class="text-xs text-muted-foreground">
            {{ t('template.panel.sample', { sample: helper.sample }) }}
          </span>
        </button>
      </section>
    </div>
  </div>
</template>
