<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The editor of one voice trigger. The Enabled switch saves at once; every other field is a draft that
// Save sends as the changed fields only. A refused save keeps the draft and shows the bot's reason.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useUpdateVoiceTrigger, type VoiceTrigger } from '@/api/voiceTriggers';
import EditorFooter from '@/components/layout/EditorFooter.vue';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useTime } from '@/composables/useTime';
import { errorText } from '@/lib/errors';

import StickerSelect from './StickerSelect.vue';
import { changeOf, draftOf, type VoiceTriggerDraft } from './voiceTriggerDraft';

const props = defineProps<{ row: VoiceTrigger }>();
const emit = defineEmits<{ remove: [row: VoiceTrigger] }>();

const { t } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const update = useUpdateVoiceTrigger(channelId);

const draft = ref<VoiceTriggerDraft>(draftOf(props.row));
const failure = ref<string | null>(null);
const confirmOpen = ref(false);

const dirtyCount = computed<number>(() =>
  Object.keys(changeOf(draft.value, props.row)).length);
const unsaved = computed<string>(() =>
  dirtyCount.value === 0
    ? ''
    : t('dashboard.voiceTriggers.editor.unsaved', { count: dirtyCount.value }, dirtyCount.value),
);

// The live numbers: they move on their own as the word is heard.
const facts = computed(() => [
  { label: t('dashboard.voiceTriggers.editor.count'), value: String(props.row.currentCount) },
  { label: t('dashboard.voiceTriggers.editor.startedAt'), value: String(props.row.startingCount) },
  {
    label: t('dashboard.voiceTriggers.editor.lastHeard'),
    value: props.row.lastFiredAt
      ? time.dateTime(props.row.lastFiredAt)
      : t('dashboard.voiceTriggers.editor.never'),
  },
]);

function setCooldown(value: string | number): void {
  draft.value.cooldownSeconds = Number(value);
}

async function save(): Promise<void> {
  if (dirtyCount.value === 0) return;

  failure.value = null;

  try {
    await update.mutateAsync({ triggerId: props.row.id, change: changeOf(draft.value, props.row) });
  } catch (error) {
    failure.value = errorText(error, t);
  }
}

function toggle(): void {
  update.mutate({ triggerId: props.row.id, change: { isEnabled: !props.row.isEnabled } });
}

function discard(): void {
  failure.value = null;
  draft.value = draftOf(props.row);
}

function remove(): void {
  confirmOpen.value = false;
  emit('remove', props.row);
}
</script>

<template>
  <PanelCard data-voice-editor tier="lead" pad :title="row.word">
    <template #actions>
      <LabeledSwitch
        :label="$t('dashboard.voiceTriggers.editor.enabled')"
        :help="$t('dashboard.voiceTriggers.editor.enabledHelp')"
        :model-value="row.isEnabled"
        @update:model-value="toggle"
      />
    </template>
    <div class="grid gap-5">
      <div class="flex items-center gap-4">
        <img
          v-if="row.stickerImageUrl"
          data-voice-sticker
          :src="row.stickerImageUrl"
          :alt="$t('dashboard.voiceTriggers.editor.stickerAlt', { word: row.word })"
          class="size-16 flex-none rounded-[8px] object-contain"
        />
        <KeyValueList :items="facts" class="min-w-0 flex-1" />
      </div>

      <div class="grid gap-4 md:grid-cols-[2fr_1fr]">
        <Field>
          <FieldLabel for="voice-word">{{ $t('dashboard.voiceTriggers.editor.word') }}</FieldLabel>
          <Input
            id="voice-word"
            v-model="draft.word"
            autocomplete="off"
            maxlength="200"
            :aria-label="$t('dashboard.voiceTriggers.editor.word')"
          />
        </Field>
        <Field>
          <div class="flex items-center gap-1.5">
            <FieldLabel for="voice-cooldown">{{ $t('dashboard.voiceTriggers.editor.cooldown') }}</FieldLabel>
            <FieldInfo>{{ $t('dashboard.voiceTriggers.editor.cooldownHelp') }}</FieldInfo>
          </div>
          <Input
            id="voice-cooldown"
            type="number"
            min="0"
            max="3600"
            step="1"
            :aria-label="$t('dashboard.voiceTriggers.editor.cooldown')"
            :model-value="draft.cooldownSeconds"
            @update:model-value="setCooldown"
          />
        </Field>
      </div>

      <StickerSelect v-model="draft.stickerAssetId" />

      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
    <template #footer>
      <EditorFooter
        v-model:confirm-open="confirmOpen"
        :delete-label="$t('dashboard.voiceTriggers.editor.delete')"
        :discard-label="$t('dashboard.voiceTriggers.editor.discard')"
        :save-label="$t('dashboard.voiceTriggers.editor.save')"
        :unsaved="unsaved"
        :can-save="dirtyCount > 0"
        :saving="update.isPending.value"
        :confirm-title="$t('dashboard.voiceTriggers.editor.confirmTitle', { name: row.word })"
        :confirm-description="
          $t('dashboard.voiceTriggers.editor.confirmBody', { count: row.currentCount })
        "
        :confirm-action="$t('dashboard.voiceTriggers.editor.confirmAction')"
        @ask-delete="confirmOpen = true"
        @confirm-delete="remove"
        @discard="discard"
        @save="save"
      />
    </template>
  </PanelCard>
</template>
