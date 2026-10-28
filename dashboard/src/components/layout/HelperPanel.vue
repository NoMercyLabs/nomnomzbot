<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The full helper list of one place (spec row Textarea + Command), grouped by the first part of the key.
// Every group is collapsed until a query matches it; only open groups render rows, and a big group shows a
// bounded slice. Pressing a helper inserts "{key}" at the caret its TemplateField remembered; the press does
// not steal focus.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import ChevronDown from '~icons/nnz/chevron-down';

import { useTemplateHelpers, type TemplateContext, type TemplateHelper } from '@/api/templates';
import { Badge } from '@/components/ui/badge';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Input } from '@/components/ui/input';
import { splitMatches } from '@/lib/text';

import { useHelperText } from './useHelperText';
import { braced, groupHelpers, groupRowLimit, searchHelpers } from './templatePlaceholders';

const props = withDefaults(
  defineProps<{
    target: { insert: (text: string) => void } | null;
    context: TemplateContext;
    eventType?: string;
  }>(),
  { eventType: undefined },
);

const { t } = useI18n();
const { describe, groupLabel } = useHelperText();
const helpers = useTemplateHelpers(
  () => props.context,
  () => props.eventType,
);
const query = ref('');
const needle = computed(() => query.value.trim());
const searching = computed(() => needle.value !== '');
const opened = ref<string[]>([]);
const unlimited = ref<string[]>([]);
const shown = computed<readonly TemplateHelper[]>(() =>
  searchHelpers(helpers.data.value ?? [], needle.value, describe),
);
const groups = computed(() => groupHelpers(shown.value));

function isOpen(group: string): boolean {
  return searching.value || opened.value.includes(group);
}

function toggle(group: string, open: boolean): void {
  opened.value = open
    ? [...opened.value, group]
    : opened.value.filter((name) => name !== group);
}

function rowsOf(group: string, rows: TemplateHelper[]): TemplateHelper[] {
  return unlimited.value.includes(group)
    ? rows
    : rows.slice(0, groupRowLimit);
}

function showAll(group: string): void {
  unlimited.value = [...unlimited.value, group];
}

function countText(count: number): string {
  return t('template.panel.count', { count }, count);
}
</script>

<template>
  <div data-slot="helper-panel" class="flex min-h-0 flex-col gap-2">
    <Input
      v-model="query"
      type="search"
      :aria-label="t('template.panel.search')"
      :placeholder="t('template.panel.search')"
    />
    <div class="flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto">
      <p v-if="groups.length === 0" class="px-1 text-sm text-muted-foreground">
        {{ t('template.panel.empty') }}
      </p>
      <Collapsible
        v-for="entry in groups"
        :key="entry.group"
        :open="isOpen(entry.group)"
        :data-helper-group="entry.group"
        class="flex shrink-0 flex-col gap-1"
        @update:open="toggle(entry.group, $event)"
      >
        <CollapsibleTrigger
          data-helper-group-trigger
          class="group flex min-h-9 w-full cursor-pointer items-center justify-between gap-2 rounded-[8px] px-2 text-left text-sm font-medium hover:bg-foreground/5"
        >
          <span class="truncate">{{ groupLabel(entry.group) }}</span>
          <span class="flex shrink-0 items-center gap-2">
            <Badge
              variant="secondary"
              data-helper-count
              :aria-label="countText(entry.helpers.length)"
            >
              {{ entry.helpers.length }}
            </Badge>
            <ChevronDown
              class="text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
              aria-hidden="true"
            />
          </span>
        </CollapsibleTrigger>
        <CollapsibleContent class="flex flex-col gap-1">
          <!-- A row of several lines, as SummaryRow: a plain button, since the frame gives Button one fixed height. -->
          <button
            v-for="helper in rowsOf(entry.group, entry.helpers)"
            :key="helper.key"
            type="button"
            :data-helper-row="helper.key"
            class="flex min-h-11 cursor-pointer flex-col items-start gap-0.5 rounded-[8px] px-2 py-1.5 text-left hover:bg-foreground/5"
            @mousedown.prevent
            @click="target?.insert(braced(helper.key))"
          >
            <span data-helper-key class="font-mono text-sm font-medium">
              <template
                v-for="(part, index) in splitMatches(braced(helper.key), needle)"
                :key="index"
              >
                <mark v-if="part.match" class="rounded-[2px] bg-muted text-foreground">{{
                  part.text
                }}</mark>
                <template v-else>{{ part.text }}</template>
              </template>
            </span>
            <span v-if="describe(helper)" class="text-xs text-muted-foreground">
              <template
                v-for="(part, index) in splitMatches(describe(helper), needle)"
                :key="index"
              >
                <mark v-if="part.match" class="rounded-[2px] bg-muted text-foreground">{{
                  part.text
                }}</mark>
                <template v-else>{{ part.text }}</template>
              </template>
            </span>
            <span v-if="helper.sample" class="text-xs text-muted-foreground">
              {{ t('template.panel.sample', { sample: helper.sample }) }}
            </span>
          </button>
          <button
            v-if="rowsOf(entry.group, entry.helpers).length < entry.helpers.length"
            type="button"
            data-helper-more
            class="min-h-9 cursor-pointer rounded-[8px] px-2 text-left text-sm text-muted-foreground hover:bg-foreground/5"
            @mousedown.prevent
            @click="showAll(entry.group)"
          >
            {{
              t('template.panel.more', {
                count: entry.helpers.length - rowsOf(entry.group, entry.helpers).length,
              })
            }}
          </button>
        </CollapsibleContent>
      </Collapsible>
    </div>
  </div>
</template>
