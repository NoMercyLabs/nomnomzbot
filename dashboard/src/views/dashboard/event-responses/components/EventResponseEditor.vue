<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The editor of one event reply. The Enabled switch saves at once; the message and the read-aloud switch are a
// draft that Save sends as the changed fields only. A refused save keeps the draft and shows the bot's reason.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useEventPresets,
  useEventResponse,
  useResetEventResponse,
  useUpdateEventResponse,
  type EventResponse,
  type EventResponseDetail,
  type EventResponsePreset,
} from '@/api/eventResponses';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import { changeOf, draftOf, nameOf, sameDraft, type EventDraft } from './eventResponseDraft';

const props = defineProps<{ row: EventResponse }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const response = useEventResponse(channelId, () =>
  props.row.eventType);
const presets = useEventPresets(channelId);
const update = useUpdateEventResponse(channelId);
const reset = useResetEventResponse(channelId);

const draft = ref<EventDraft | null>(null);
// What the form was last loaded or saved with. A response that changes under an edited form never overwrites the edits.
const baseline = ref<EventDraft | null>(null);
const failure = ref<string | null>(null);

const edited = computed<boolean>(() =>
  draft.value !== null && baseline.value !== null && !sameDraft(draft.value, baseline.value),
);
const panelState = computed<PanelState>(() => {
  if (response.isError.value)
    return 'error';

  return draft.value === null
    ? 'loading'
    : 'ready';
});

watch(
  () => response.data.value,
  (saved: EventResponseDetail | undefined): void => {
    if (saved === undefined) return;

    if (edited.value) return;

    draft.value = draftOf(saved);
    baseline.value = draftOf(saved);
  },
  { immediate: true },
);

const dirtyCount = computed<number>(() =>
  draft.value === null || response.data.value === undefined
    ? 0
    : Object.keys(changeOf(draft.value, response.data.value)).length,
);
const unsaved = computed<string>(() =>
  dirtyCount.value === 0
    ? ''
    : t('dashboard.eventResponses.editor.unsaved', { count: dirtyCount.value }, dirtyCount.value),
);
const variables = computed<string>(() => {
  const preset = (presets.data.value ?? []).find(
    (item: EventResponsePreset): boolean => item.eventType === props.row.eventType,
  );

  return (preset?.variables ?? []).map((name: string): string => `{${name}}`).join(' ');
});

async function run(work: () => Promise<void>): Promise<void> {
  failure.value = null;

  try {
    await work();
  } catch (error) {
    failure.value = errorText(error, t);
  }
}

function toggle(isEnabled: boolean): Promise<void> {
  return run(() => update.mutateAsync({ eventType: props.row.eventType, change: { isEnabled } }));
}

function save(): Promise<void> {
  if (draft.value === null || response.data.value === undefined || dirtyCount.value === 0)
    return Promise.resolve();

  const change = changeOf(draft.value, response.data.value);
  const saved = { ...draft.value };

  return run(async () => {
    await update.mutateAsync({ eventType: props.row.eventType, change });
    baseline.value = saved;
  });
}

function discard(): void {
  failure.value = null;

  if (response.data.value === undefined) return;

  draft.value = draftOf(response.data.value);
  baseline.value = draftOf(response.data.value);
}

function useDefault(): Promise<void> {
  baseline.value = null;

  return run(() => reset.mutateAsync(props.row.eventType));
}
</script>

<template>
  <PanelCard
    data-event-editor
    tier="lead"
    pad
    :title="nameOf(row.eventType)"
    :state="panelState"
    :error-text="$t('dashboard.eventResponses.editor.error')"
    @retry="response.refetch()"
  >
    <template #actions>
      <Button
        v-if="!row.followsPlatformDefault"
        type="button"
        variant="outline"
        size="sm"
        :disabled="reset.isPending.value"
        @click="useDefault"
      >
        {{ $t('dashboard.eventResponses.editor.reset') }}
      </Button>
    </template>
    <div v-if="draft" class="grid gap-5">
      <RowGroup>
        <SettingRow
          :title="$t('dashboard.eventResponses.editor.enabled')"
          :description="$t('dashboard.eventResponses.editor.enabledHelp')"
          :model-value="row.isEnabled"
          @update:model-value="toggle"
        />
      </RowGroup>

      <p v-if="row.followsPlatformDefault" class="text-sm text-muted-foreground">
        {{ $t('dashboard.eventResponses.editor.following') }}
      </p>

      <TemplateField
        v-model="draft.message"
        :label="$t('dashboard.eventResponses.editor.message')"
        context="eventResponse"
        :event-type="row.eventType"
      />
      <p class="-mt-3 text-sm text-muted-foreground">
        {{ $t('dashboard.eventResponses.editor.messageHelp') }}
        <span v-if="variables">{{ $t('dashboard.eventResponses.editor.variables', { names: variables }) }}</span>
      </p>

      <RowGroup>
        <SettingRow
          v-model="draft.speakWithTts"
          :title="$t('dashboard.eventResponses.editor.speak')"
          :description="$t('dashboard.eventResponses.editor.speakHelp')"
        />
      </RowGroup>

      <div class="flex items-center gap-3">
        <Button type="button" variant="outline" size="sm" disabled>
          {{ $t('dashboard.eventResponses.editor.test') }}
        </Button>
        <span class="text-sm text-muted-foreground">{{
          $t('dashboard.eventResponses.editor.testUnavailable')
        }}</span>
      </div>

      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
    <template #footer>
      <span data-unsaved class="mr-auto text-sm text-muted-foreground">{{ unsaved }}</span>
      <Button type="button" variant="outline" size="sm" @click="discard">{{
        $t('dashboard.eventResponses.editor.discard')
      }}</Button>
      <Button
        type="button"
        size="sm"
        :disabled="dirtyCount === 0 || update.isPending.value"
        @click="save"
      >
        {{ $t('dashboard.eventResponses.editor.save') }}
      </Button>
    </template>
  </PanelCard>
</template>
