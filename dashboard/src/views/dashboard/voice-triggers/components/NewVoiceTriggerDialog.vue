<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A new voice trigger: the word, the count to start from, the cooldown and the sticker. The Enabled switch
// of the editor turns it off or on later; a new trigger starts on.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreateVoiceTrigger } from '@/api/voiceTriggers';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import StickerSelect from './StickerSelect.vue';
import { noSticker } from './voiceTriggerDraft';

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateVoiceTrigger(channelId);

const word = ref('');
const startingCount = ref(0);
const cooldownSeconds = ref(5);
const sticker = ref(noSticker);
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  word.value = '';
  startingCount.value = 0;
  cooldownSeconds.value = 5;
  sticker.value = noSticker;
  error.value = undefined;
});

async function submit(): Promise<void> {
  if (word.value.trim() === '') {
    error.value = t('dashboard.voiceTriggers.dialog.wordRequired');

    return;
  }

  error.value = undefined;

  try {
    await create.mutateAsync({
      word: word.value.trim(),
      startingCount: startingCount.value,
      cooldownSeconds: cooldownSeconds.value,
      ...(sticker.value === noSticker
        ? {}
        : { stickerAssetId: sticker.value }),
    });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="$t('dashboard.voiceTriggers.dialog.title')"
    :description="$t('dashboard.voiceTriggers.dialog.description')"
    :action="$t('dashboard.voiceTriggers.dialog.create')"
    :pending="create.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="new-voice-word">{{ $t('dashboard.voiceTriggers.editor.word') }}</FieldLabel>
      <Input
        id="new-voice-word"
        v-model="word"
        autocomplete="off"
        maxlength="200"
        :aria-label="$t('dashboard.voiceTriggers.editor.word')"
      />
    </Field>
    <div class="grid gap-4 md:grid-cols-2">
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="new-voice-start">{{ $t('dashboard.voiceTriggers.dialog.startingCount') }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.voiceTriggers.dialog.startingCountHelp') }}</FieldInfo>
        </div>
        <Input
          id="new-voice-start"
          v-model.number="startingCount"
          type="number"
          min="0"
          step="1"
          :aria-label="$t('dashboard.voiceTriggers.dialog.startingCount')"
        />
      </Field>
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="new-voice-cooldown">{{ $t('dashboard.voiceTriggers.editor.cooldown') }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.voiceTriggers.editor.cooldownHelp') }}</FieldInfo>
        </div>
        <Input
          id="new-voice-cooldown"
          v-model.number="cooldownSeconds"
          type="number"
          min="0"
          max="3600"
          step="1"
          :aria-label="$t('dashboard.voiceTriggers.editor.cooldown')"
        />
      </Field>
    </div>
    <StickerSelect v-model="sticker" />
  </FormDialog>
</template>
