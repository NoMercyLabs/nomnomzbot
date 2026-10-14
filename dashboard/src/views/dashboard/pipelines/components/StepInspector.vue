<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The picked step in detail (5-workbench.html:344-379): Settings edits every field the bot's catalog gives the
// action, or the block's own settings; In plain words says what the step does from the same facts. Each edit,
// Duplicate and Remove change the draft; a role that cannot change pipelines sees them locked.
import FileCopy from '~icons/nnz/file-copy';
import Trash from '~icons/nnz/trash';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineActionDescriptor, PipelineActionField, PipelineStep } from '@/api/pipelines';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import { Button } from '@/components/ui/button';
import { FieldGroup } from '@/components/ui/field';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useLocalizedText } from '@/composables/useLocalizedText';

import type { StepPatch } from './stepEdits';
import StepField from './StepField.vue';
import { actionRows, blockRows, type FieldRow } from './stepFields';
import { conditionSentence, stepSentence, stepTitle } from './stepWords';

const props = defineProps<{
  step: PipelineStep;
  number: string;
  descriptor: PipelineActionDescriptor | undefined;
  readOnly: boolean;
}>();

const emit = defineEmits<{
  duplicate: [];
  remove: [];
  patch: [patch: StepPatch];
}>();

const { t, te } = useI18n();
const text = useLocalizedText();
const page = 'dashboard.pipelines.workbench';

const tab = ref('settings');

watch(() => props.step.id, () => {
  tab.value = 'settings';
});

const title = computed(() => stepTitle(props.step, { t, te }));

const fields = computed(() =>
  props.step.block_kind === null
    ? actionRows(props.step, props.descriptor, {
        t,
        te,
        text,
      })
    : blockRows(props.step, { t, te }));

const keepGoing = computed<FieldRow>(() =>
  ({
    name: 'continue_on_error',
    label: t(`${page}.inspector.keepGoing`),
    kind: 'boolean',
    control: 'switch',
    value: props.step.continue_on_error,
    help: t(`${page}.inspector.keepGoingHelp`),
    choices: [],
    required: false,
    templated: false,
    target: 'step',
  }));

function change(row: FieldRow, value: unknown): void {
  emit('patch', row.target === 'step'
    ? { continue_on_error: value === true }
    : { [row.target]: { [row.name]: value } });
}

const declares = computed(() =>
  (props.descriptor?.fields ?? []).some((field: PipelineActionField) =>
    field.declaresVariable));

const condition = computed(() =>
  props.step.condition
    ? conditionSentence(props.step.condition, { t, te })
    : t(`${page}.inspector.always`));

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
      <TabsContent value="settings" class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1 pt-2 pb-1">
        <ReadOnlyScope :read-only="readOnly" :reason="$t('dashboard.pipelines.noWrite')">
          <FieldGroup data-inspector-fields class="gap-4">
            <StepField
              v-for="row in fields"
              :key="`${step.id}:${row.name}`"
              :row="row"
              @change="change(row, $event)"
            />
            <p v-if="fields.length === 0" class="text-sm text-muted-foreground">
              {{ $t('dashboard.pipelines.workbench.inspector.noFields') }}
            </p>
            <StepField :key="`${step.id}:keep-going`" :row="keepGoing" @change="change(keepGoing, $event)" />
          </FieldGroup>
        </ReadOnlyScope>
        <KeyValueList
          :items="[{ label: $t('dashboard.pipelines.workbench.inspector.condition'), value: condition }]"
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
    <ReadOnlyScope
      :read-only="readOnly"
      :reason="$t('dashboard.pipelines.noWrite')"
      class="flex flex-wrap gap-2 border-t pt-3"
    >
      <Button data-step-duplicate
              type="button"
              variant="outline"
              size="sm"
              @click="$emit('duplicate')"
      >
        <FileCopy aria-hidden="true" />
        {{ $t('dashboard.pipelines.workbench.inspector.duplicate') }}
      </Button>
      <Button
        data-step-remove
        type="button"
        variant="destructive"
        size="sm"
        @click="$emit('remove')"
      >
        <Trash aria-hidden="true" />
        {{ $t('dashboard.pipelines.workbench.inspector.remove') }}
      </Button>
    </ReadOnlyScope>
  </section>
</template>
