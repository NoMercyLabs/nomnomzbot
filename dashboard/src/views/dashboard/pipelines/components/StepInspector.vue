<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The picked step in detail (5-workbench.html:344-379): Settings lists every field the bot's catalog gives the
// action, with its saved value; In plain words says what the step does from the same facts.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineActionDescriptor, PipelineActionField, PipelineStep } from '@/api/pipelines';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useLocalizedText } from '@/composables/useLocalizedText';
import { humanize } from '@/lib/text';

import { conditionSentence, fieldHelp, stepSentence, stepTitle, valueText } from './stepWords';

const props = defineProps<{
  step: PipelineStep;
  number: string;
  descriptor: PipelineActionDescriptor | undefined;
}>();

const { t, te } = useI18n();
const text = useLocalizedText();
const page = 'dashboard.pipelines.workbench';

const tab = ref('settings');

watch(() => props.step, () => {
  tab.value = 'settings';
});

const title = computed(() => stepTitle(props.step, { t, te }));

// One field stacked as name, value and help: the help runs the full panel width instead of wrapping in a
// value column.
type FieldRow = {
  name: string;
  label: string;
  value: string | null;
  help: string;
};

const fields = computed<FieldRow[]>(() => {
  if (props.step.block_kind !== null) {
    return Object.entries(props.step.block_config ?? {}).map(([key, value]) => ({
      name: key,
      label: humanize(key),
      value: valueText(value),
      help: '',
    }));
  }

  return (props.descriptor?.fields ?? []).map((field: PipelineActionField) => ({
    name: field.name,
    label: humanize(field.name),
    value: valueText(props.step.action[field.name]),
    help: fieldHelp(props.step.action.type, field, text),
  }));
});

const declares = computed(() =>
  (props.descriptor?.fields ?? []).some((field: PipelineActionField) =>
    field.declaresVariable));

const condition = computed(() =>
  props.step.condition
    ? conditionSentence(props.step.condition, { t, te })
    : t(`${page}.inspector.always`));

const onError = computed(() =>
  props.step.continue_on_error
    ? t(`${page}.inspector.goesOn`)
    : t(`${page}.inspector.stops`));

const plainOnError = computed(() =>
  props.step.continue_on_error
    ? t(`${page}.inspector.plainGoesOn`)
    : t(`${page}.inspector.plainStops`));

const sentence = computed(() => stepSentence(props.descriptor, text));
</script>

<template>
  <section data-step-inspector class="flex min-h-0 flex-col gap-3 rounded-xl border bg-card p-4">
    <header class="flex flex-col gap-0.5">
      <span class="text-xs text-muted-foreground">
        {{ $t('dashboard.pipelines.workbench.inspector.number', { number }) }}
      </span>
      <h2 class="font-semibold">{{ title }}</h2>
    </header>
    <Tabs v-model="tab" class="flex min-h-0 flex-1 flex-col">
      <TabsList class="w-full">
        <TabsTrigger value="settings" data-inspector-tab="settings">
          {{ $t('dashboard.pipelines.workbench.inspector.settings') }}
        </TabsTrigger>
        <TabsTrigger value="plain" data-inspector-tab="plain">
          {{ $t('dashboard.pipelines.workbench.inspector.plain') }}
        </TabsTrigger>
      </TabsList>
      <TabsContent value="settings" class="flex flex-col gap-4 pt-2">
        <dl v-if="fields.length > 0" data-inspector-fields class="m-0 flex flex-col gap-3 text-sm">
          <div v-for="field in fields" :key="field.name" data-inspector-field class="flex flex-col gap-0.5">
            <dt class="font-medium">{{ field.label }}</dt>
            <dd class="m-0 wrap-break-word" :class="field.value === null && 'text-muted-foreground'">
              {{ field.value ?? $t('dashboard.pipelines.workbench.inspector.notSet') }}
            </dd>
            <dd v-if="field.help" class="m-0 text-xs text-muted-foreground">{{ field.help }}</dd>
          </div>
        </dl>
        <p v-else class="text-sm text-muted-foreground">
          {{ $t('dashboard.pipelines.workbench.inspector.noFields') }}
        </p>
        <KeyValueList
          :items="[
            { label: $t('dashboard.pipelines.workbench.inspector.condition'), value: condition },
            { label: $t('dashboard.pipelines.workbench.inspector.onError'), value: onError },
          ]"
        />
      </TabsContent>
      <TabsContent value="plain" data-inspector-plain class="flex flex-col gap-2 pt-2 text-sm">
        <p v-if="sentence">{{ sentence }}</p>
        <p>{{ condition }}</p>
        <p>{{ plainOnError }}</p>
        <p v-if="declares" class="text-muted-foreground">
          {{ $t('dashboard.pipelines.workbench.inspector.declares') }}
        </p>
      </TabsContent>
    </Tabs>
  </section>
</template>
