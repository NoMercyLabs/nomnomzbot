<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The written rules of the layout system, each beside a live example. Every claim names the file it describes.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import EditorFooter from '@/components/layout/EditorFooter.vue';
import FilterBar from '@/components/layout/FilterBar.vue';
import type { FilterOption } from '@/components/layout/filterOption';
import ListPanel from '@/components/layout/ListPanel.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowEditDelete from '@/components/layout/RowEditDelete.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Switch } from '@/components/ui/switch';
import type { PanelState } from '@/composables/usePanelState';
import { merge } from '@/lib/array';
import ChevronDown from '~icons/nnz/chevron-down';

const { t } = useI18n();

const TIERS = ['lead', 'default', 'quiet'] as const;

type DemoRow = {
  id: string;
  kind: 'command' | 'timer';
  enabled: boolean;
  created: 'week' | 'older';
};

const DEMO_ROWS = [
  {
    id: 'so',
    kind: 'command',
    enabled: true,
    created: 'week',
  },
  {
    id: 'hug',
    kind: 'command',
    enabled: false,
    created: 'older',
  },
  {
    id: 'lurk',
    kind: 'command',
    enabled: true,
    created: 'older',
  },
  {
    id: 'hydrate',
    kind: 'timer',
    enabled: true,
    created: 'week',
  },
  {
    id: 'socials',
    kind: 'timer',
    enabled: false,
    created: 'older',
  },
] as const satisfies readonly DemoRow[];

const kindOptions = computed<FilterOption[]>(() =>
  ['all', 'command', 'timer'].map((key) => ({
    value: key,
    label: t(`dashboard.catalog.guide.filters.kind.${key}`),
  })));
const createdOptions = computed<FilterOption[]>(() =>
  ['any', 'week'].map((key) => ({
    value: key,
    label: t(`dashboard.catalog.guide.filters.created.${key}`),
  })));

const kind = ref('all');
const search = ref('');
const advancedOpen = ref(false);
const onlyEnabled = ref(false);
const created = ref('any');

const advancedCount = computed<number>(() => merge(
  onlyEnabled.value
    ? ['enabled']
    : [],
  created.value === 'any'
    ? []
    : ['created'],
).length);

const visible = computed<DemoRow[]>(() =>
  DEMO_ROWS.filter((row) =>
    (kind.value === 'all' || row.kind === kind.value) &&
    (!onlyEnabled.value || row.enabled) &&
    (created.value === 'any' || row.created === created.value) &&
    row.id.includes(search.value.trim().toLowerCase())));

const listState = computed<PanelState>(() =>
  visible.value.length > 0
    ? 'ready'
    : 'empty');

const edited = ref('');
const deleted = ref('');
</script>

<template>
  <section data-catalog="guide-tiers" class="grid gap-4">
    <h2 class="text-base font-semibold">{{ t('dashboard.catalog.guide.tiers.title') }}</h2>
    <p class="text-sm text-muted-foreground">{{ t('dashboard.catalog.guide.tiers.source') }}</p>
    <div class="grid grid-cols-3 gap-4 max-md:grid-cols-1">
      <PanelCard
        v-for="tier in TIERS"
        :key="tier"
        :tier="tier"
        :title="t(`dashboard.catalog.guide.tiers.${tier}.title`)"
        pad
      >
        <p class="text-sm">{{ t(`dashboard.catalog.guide.tiers.${tier}.rule`) }}</p>
      </PanelCard>
    </div>
  </section>

  <section data-catalog="guide-actions" class="grid gap-4">
    <h2 class="text-base font-semibold">{{ t('dashboard.catalog.guide.actions.title') }}</h2>
    <p class="text-sm text-muted-foreground">{{ t('dashboard.catalog.guide.actions.source') }}</p>
    <ul class="grid gap-1 text-sm">
      <li v-for="rule in ['group', 'rows', 'header', 'footer']" :key="rule">
        {{ t(`dashboard.catalog.guide.actions.rule.${rule}`) }}
      </li>
    </ul>
    <div class="grid grid-cols-2 gap-4 max-md:grid-cols-1">
      <PanelCard :title="t('dashboard.catalog.guide.actions.headerExample')" pad>
        <div class="flex flex-wrap gap-2">
          <Button variant="outline">{{ t('dashboard.catalog.outline') }}</Button>
          <Button>{{ t('dashboard.catalog.primary') }}</Button>
        </div>
      </PanelCard>
      <PanelCard :title="t('dashboard.catalog.guide.actions.rowExample')">
        <RowGroup>
          <SummaryRow
            :title="t('dashboard.catalog.rows.both')"
            :detail="t('dashboard.catalog.guide.actions.rowResult', { edited, deleted })"
          >
            <template #trailing>
              <RowEditDelete
                :edit-label="t('dashboard.catalog.rows.edit')"
                :delete-label="t('dashboard.catalog.rows.delete')"
                @edit="edited = t('dashboard.catalog.rows.edit')"
                @delete="deleted = t('dashboard.catalog.rows.delete')"
              />
            </template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
    </div>
    <PanelCard :title="t('dashboard.catalog.guide.actions.footerExample')" pad>
      <EditorFooter
        :discard-label="t('dashboard.catalog.editors.discard')"
        :save-label="t('dashboard.catalog.editors.save')"
        :unsaved="t('dashboard.catalog.guide.actions.footerNote')"
      />
    </PanelCard>
  </section>

  <section data-catalog="guide-filters" class="grid gap-4">
    <h2 class="text-base font-semibold">{{ t('dashboard.catalog.guide.filters.title') }}</h2>
    <p class="text-sm text-muted-foreground">{{ t('dashboard.catalog.guide.filters.source') }}</p>
    <ol class="grid gap-1 text-sm">
      <li v-for="step in ['bar', 'search', 'advanced']" :key="step">
        {{ t(`dashboard.catalog.guide.filters.step.${step}`) }}
      </li>
    </ol>
    <div data-catalog-filter-demo class="grid gap-3">
      <!-- 1. The compact filters sit in PageHeader#bar: one control, always visible. -->
      <div data-catalog-filter-bar class="flex flex-wrap items-center gap-3 rounded-[12px] border border-border px-3 py-1.5">
        <FilterBar v-model="kind" :label="t('dashboard.catalog.guide.filters.kind.label')" :options="kindOptions" />
      </div>
      <!-- 2. The search sits in the list header; 3. the advanced filters fold behind one trigger that counts them. -->
      <ListPanel
        v-model="search"
        :title="t('dashboard.catalog.guide.filters.list')"
        :placeholder="t('dashboard.catalog.search')"
        :state="listState"
        :empty-text="t('dashboard.catalog.guide.filters.noMatch')"
      >
        <template #actions>
          <Collapsible v-model:open="advancedOpen">
            <CollapsibleTrigger as-child>
              <Button variant="outline" size="sm" data-catalog-advanced-trigger>
                {{ t('dashboard.catalog.guide.filters.advanced') }}
                <Badge v-if="advancedCount > 0" variant="secondary">{{ advancedCount }}</Badge>
                <ChevronDown aria-hidden="true" />
              </Button>
            </CollapsibleTrigger>
          </Collapsible>
        </template>
        <Collapsible v-model:open="advancedOpen">
          <CollapsibleContent data-catalog-advanced class="mb-2 flex flex-wrap items-center gap-4 rounded-[12px] border border-border p-3">
            <label class="flex items-center gap-2 text-sm">
              <Switch v-model="onlyEnabled" :aria-label="t('dashboard.catalog.guide.filters.enabled')" />
              {{ t('dashboard.catalog.guide.filters.enabled') }}
            </label>
            <FilterBar v-model="created" :label="t('dashboard.catalog.guide.filters.created.label')" :options="createdOptions" />
          </CollapsibleContent>
        </Collapsible>
        <RowGroup>
          <SummaryRow v-for="row in visible" :key="row.id" :title="`!${row.id}`" :detail="t(`dashboard.catalog.guide.filters.kind.${row.kind}`)" />
        </RowGroup>
      </ListPanel>
    </div>
  </section>
</template>
