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
} from '@/api/eventResponses';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import EditorFooter from '@/components/layout/EditorFooter.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Badge } from '@/components/ui/badge';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { boundTarget } from '@/lib/bindingTarget';
import { errorText } from '@/lib/errors';

import ReplyModeField from '@/components/layout/ReplyModeField.vue';

import DefaultLineList from './DefaultLineList.vue';
import { changeOf, draftOf, sameDraft, type EventDraft } from './eventResponseDraft';
import { useEventName } from './useEventName';

const props = defineProps<{ row: EventResponse }>();

const { t } = useI18n();
const eventName = useEventName();
const { channelId } = useCurrentChannel();
const response = useEventResponse(channelId, () => props.row.eventType);
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

    picked.value = false;
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
const toneLines = computed<string[]>(() => response.data.value?.toneLines ?? []);
// The bot posts one of the default lines until the channel has its own text; a picked line becomes that text.
const picked = ref(false);
const showDefaults = computed<boolean>(() => toneLines.value.length > 0 && !picked.value);
const fromDefault = computed<boolean>(() =>
  showDefaults.value || (props.row.followsPlatformDefault && !picked.value),
);
const linesHelp = computed<string>(() => {
  if (showDefaults.value)
    return t('dashboard.eventResponses.editor.pickHelp');

  return picked.value
    ? t('dashboard.eventResponses.editor.replaces', { count: toneLines.value.length }, toneLines.value.length)
    : t('dashboard.eventResponses.editor.ownHelp');
});
const variables = computed<string>(() => {
  const preset = (presets.data.value ?? []).find(
    (item): boolean => item.eventType === props.row.eventType,
  );
  const names = (preset?.variables ?? []).map((name): string => `{${name}}`).join(' ');

  return names === ''
    ? ''
    : t('dashboard.eventResponses.editor.variables', { names });
});
const resetOpen = ref(false);
const resetLoses = computed<string[]>(() => {
  const own = response.data.value?.message ?? '';
  const bound = boundTarget(response.data.value?.target, response.data.value?.pipelineId);
  const lines: string[] = [];

  if (own !== '') {
    lines.push(t('dashboard.eventResponses.editor.resetLoses', { line: own }));
  }

  if (bound !== null) {
    lines.push(t(`dashboard.eventResponses.editor.resetLosesBinding.${bound.kind}`, { action: bound.actionType ?? '' }));
  }

  return lines;
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
  picked.value = false;

  if (response.data.value === undefined) return;

  draft.value = draftOf(response.data.value);
  baseline.value = draftOf(response.data.value);
}

function pick(line: string): void {
  if (draft.value === null) return;

  draft.value.message = line;
  picked.value = true;
}

async function useDefault(): Promise<void> {
  baseline.value = null;
  picked.value = false;
  await run(() => reset.mutateAsync(props.row.eventType));
  resetOpen.value = false;
}
</script>

<template>
  <PanelCard
    data-event-editor
    tier="lead"
    pad
    :title="eventName(row.eventType)"
    :state="panelState"
    :error-text="$t('dashboard.eventResponses.editor.error')"
    @retry="response.refetch()"
  >
    <template #actions>
      <LabeledSwitch
        :label="$t('dashboard.eventResponses.editor.enabled')"
        :help="$t('dashboard.eventResponses.editor.enabledHelp')"
        :model-value="row.isEnabled"
        @update:model-value="toggle"
      />
    </template>
    <div v-if="draft" class="grid gap-5">
      <ReplyModeField
        v-model:mode="draft.mode"
        v-model:target="draft.target"
        :kinds="['command', 'pipeline', 'action']"
        :new-pipeline-name="eventName(row.eventType)"
      >
        <section class="grid gap-2" data-reply-lines>
          <div class="flex items-center gap-2">
            <h3 class="text-sm font-medium">{{ $t('dashboard.eventResponses.editor.lines') }}</h3>
            <Badge data-reply-source variant="secondary">{{
              $t(fromDefault ? 'dashboard.eventResponses.editor.defaultBadge' : 'dashboard.eventResponses.editor.ownBadge')
            }}</Badge>
          </div>
          <p class="text-sm text-muted-foreground">{{ linesHelp }}</p>
          <DefaultLineList v-if="showDefaults" :lines="toneLines" @pick="pick" />
          <TemplateField
            v-else
            v-model="draft.message"
            :label="$t('dashboard.eventResponses.editor.message')"
            :help="variables || undefined"
            :panel="false"
            context="eventResponse"
            :maxlength="500"
            :event-type="row.eventType"
          />
        </section>
        <!-- Speech belongs to a text reply; a pipeline speaks through its own step. -->
        <LabeledSwitch
          v-model="draft.speakWithTts"
          :label="$t('dashboard.eventResponses.editor.speak')"
          :help="$t('dashboard.eventResponses.editor.speakHelp')"
        />
      </ReplyModeField>

      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
    <template #footer>
      <!-- A fixed-set item cannot be deleted: Reset takes the place of Delete at the left. -->
      <EditorFooter
        :reset-label="row.followsPlatformDefault ? undefined : $t('dashboard.eventResponses.editor.reset')"
        :resetting="reset.isPending.value"
        :discard-label="$t('dashboard.eventResponses.editor.discard')"
        :save-label="$t('dashboard.eventResponses.editor.save')"
        :unsaved="unsaved"
        :can-discard="dirtyCount > 0 || picked"
        :can-save="dirtyCount > 0"
        :saving="update.isPending.value"
        @reset="resetOpen = true"
        @discard="discard"
        @save="save"
      />
    </template>
  </PanelCard>
  <ConfirmDialog
    v-model:open="resetOpen"
    :title="$t('dashboard.eventResponses.editor.resetTitle')"
    :description="$t('dashboard.eventResponses.editor.resetDescription')"
    :consequences="resetLoses"
    :action="$t('dashboard.eventResponses.editor.reset')"
    :pending="reset.isPending.value"
    @confirm="useDefault"
  />
</template>
