<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The editor of one timer. The Enabled switch saves at once; every other field is a draft that Save
// sends as the changed fields only. A refused save keeps the draft and shows the bot's reason.
import Trash2 from '~icons/nnz/trash';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useToggleTimer, type Timer as TimerRow } from '@/api/liveOps';
import { useTimer, useUpdateTimer, type Timer } from '@/api/timers';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import { changeOf, draftOf, sameDraft, type TimerDraft } from './timerDraft';

const props = defineProps<{ row: TimerRow }>();
const emit = defineEmits<{ remove: [row: TimerRow] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const timer = useTimer(channelId, () =>
  props.row.id);
const update = useUpdateTimer(channelId);
const toggle = useToggleTimer(channelId);

const draft = ref<TimerDraft | null>(null);
// What the form was last loaded or saved with. A timer that changes under an edited form never overwrites the edits.
const baseline = ref<TimerDraft | null>(null);
const failure = ref<string | null>(null);
const confirmOpen = ref(false);

const edited = computed<boolean>(() =>
  draft.value !== null && baseline.value !== null && !sameDraft(draft.value, baseline.value),
);
const panelState = computed<PanelState>(() => {
  if (timer.isError.value)
    return 'error';

  return draft.value === null
    ? 'loading'
    : 'ready';
});

watch(
  () => timer.data.value,
  (saved: Timer | undefined): void => {
    if (saved === undefined) return;

    if (edited.value) return;

    draft.value = draftOf(saved);
    baseline.value = draftOf(saved);
  },
  { immediate: true },
);

const dirtyCount = computed<number>(() =>
  draft.value === null || timer.data.value === undefined
    ? 0
    : Object.keys(changeOf(draft.value, timer.data.value)).length,
);
const unsaved = computed<string>(() =>
  dirtyCount.value === 0
    ? ''
    : t('dashboard.timers.editor.unsaved', { count: dirtyCount.value }, dirtyCount.value),
);

function setNumber(key: 'intervalMinutes' | 'minChatActivity', value: string | number): void {
  if (draft.value !== null) {
    draft.value[key] = Number(value);
  }
}

async function save(): Promise<void> {
  if (draft.value === null || timer.data.value === undefined || dirtyCount.value === 0) return;

  failure.value = null;

  try {
    await update.mutateAsync({
      timerId: props.row.id,
      change: changeOf(draft.value, timer.data.value),
    });

    baseline.value = { ...draft.value };
  } catch (error) {
    failure.value = errorText(error, t);
  }
}

function discard(): void {
  failure.value = null;

  if (timer.data.value === undefined) return;

  draft.value = draftOf(timer.data.value);
  baseline.value = draftOf(timer.data.value);
}

function remove(): void {
  confirmOpen.value = false;
  emit('remove', props.row);
}
</script>

<template>
  <PanelCard
    data-timer-editor
    tier="lead"
    pad
    :title="row.name"
    :state="panelState"
    :error-text="$t('dashboard.timers.editor.error')"
    @retry="timer.refetch()"
  >
    <template #actions>
      <Button type="button" variant="outline" size="sm" @click="confirmOpen = true">
        <Trash2 aria-hidden="true" />
        {{ $t('dashboard.timers.editor.delete') }}
      </Button>
    </template>
    <div v-if="draft" class="grid gap-5">
      <RowGroup>
        <SettingRow
          :title="$t('dashboard.timers.editor.enabled')"
          :description="$t('dashboard.timers.editor.enabledHelp')"
          :model-value="row.isEnabled"
          @update:model-value="toggle.mutate(row.id)"
        />
      </RowGroup>

      <Field>
        <FieldLabel for="timer-name">{{ $t('dashboard.timers.editor.name') }}</FieldLabel>
        <Input
          id="timer-name"
          v-model="draft.name"
          autocomplete="off"
          :aria-label="$t('dashboard.timers.editor.name')"
        />
      </Field>

      <TemplateField
        id="timer-messages"
        v-model="draft.messages"
        context="timer"
        :rows="4"
        :label="$t('dashboard.timers.editor.messages')"
        :help="$t('dashboard.timers.editor.messagesHelp')"
      />

      <Field>
        <FieldLabel for="timer-interval">{{ $t('dashboard.timers.editor.interval') }}</FieldLabel>
        <Input
          id="timer-interval"
          type="number"
          min="1"
          step="1"
          :aria-label="$t('dashboard.timers.editor.interval')"
          :model-value="draft.intervalMinutes"
          @update:model-value="setNumber('intervalMinutes', $event)"
        />
      </Field>

      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="timer-activity">{{ $t('dashboard.timers.editor.activity') }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.timers.editor.activityHelp') }}</FieldInfo>
        </div>
        <Input
          id="timer-activity"
          type="number"
          min="0"
          step="1"
          :aria-label="$t('dashboard.timers.editor.activity')"
          :model-value="draft.minChatActivity"
          @update:model-value="setNumber('minChatActivity', $event)"
        />
      </Field>

      <RowGroup>
        <SettingRow
          v-model="draft.fireOnce"
          :title="$t('dashboard.timers.editor.once')"
          :description="$t('dashboard.timers.editor.onceHelp')"
        />
        <SettingRow
          v-model="draft.speakWithTts"
          :title="$t('dashboard.timers.editor.speak')"
          :description="$t('dashboard.timers.editor.speakHelp')"
        />
      </RowGroup>

      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
    <template #footer>
      <span data-unsaved class="mr-auto text-sm text-muted-foreground">{{ unsaved }}</span>
      <Button type="button" variant="outline" size="sm" @click="discard">{{
        $t('dashboard.timers.editor.discard')
      }}</Button>
      <Button
        type="button"
        size="sm"
        :disabled="dirtyCount === 0 || update.isPending.value"
        @click="save"
      >
        {{ $t('dashboard.timers.editor.save') }}
      </Button>
    </template>
    <ConfirmDialog
      v-model:open="confirmOpen"
      :title="$t('dashboard.timers.editor.confirmTitle', { name: row.name })"
      :description="$t('dashboard.timers.editor.confirmBody')"
      :action="$t('dashboard.timers.editor.confirmAction')"
      @confirm="remove"
    />
  </PanelCard>
</template>
