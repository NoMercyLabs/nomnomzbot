<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// A new sound clip: the audio file, the name it keeps, the name a person sees and how loud it plays. The chat word,
// the cooldown and who may play it are set afterwards with Edit.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useUploadSoundClip } from '@/api/soundClips';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import { addProblem, blankAddForm, maxVolume, type AddForm } from './soundClipForm';

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const upload = useUploadSoundClip(channelId);

const form = ref<AddForm>(blankAddForm());
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  form.value = blankAddForm();
  error.value = undefined;
});

function pickFile(event: Event): void {
  form.value.file = (event.target as HTMLInputElement).files?.[0] ?? null;
}

async function submit(): Promise<void> {
  const problem = addProblem(form.value);

  error.value = problem === undefined
    ? undefined
    : t(`dashboard.sounds.dialog.problem.${problem}`, { max: maxVolume });

  if (error.value !== undefined || form.value.file === null) return;

  try {
    await upload.mutateAsync({
      file: form.value.file,
      name: form.value.name.trim(),
      displayName: form.value.displayName.trim(),
      defaultVolume: form.value.defaultVolume,
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
    :title="$t('dashboard.sounds.dialog.newTitle')"
    :description="$t('dashboard.sounds.dialog.newDescription')"
    :action="$t('dashboard.sounds.dialog.create')"
    :pending="upload.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel for="sound-file">{{ $t("dashboard.sounds.dialog.file") }}</FieldLabel>
        <FieldInfo>{{ $t("dashboard.sounds.dialog.fileHelp") }}</FieldInfo>
      </div>
      <Input id="sound-file"
             type="file"
             accept="audio/*"
             data-sound-file
             @change="pickFile"
      />
    </Field>
    <div class="grid grid-cols-2 gap-3">
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="sound-name">{{ $t("dashboard.sounds.dialog.name") }}</FieldLabel>
          <FieldInfo>{{ $t("dashboard.sounds.dialog.nameHelp") }}</FieldInfo>
        </div>
        <Input id="sound-name" v-model="form.name" data-sound-name autocomplete="off" />
      </Field>
      <Field>
        <FieldLabel for="sound-display-name">{{ $t("dashboard.sounds.dialog.displayName") }}</FieldLabel>
        <Input id="sound-display-name"
               v-model="form.displayName"
               data-sound-display-name
               autocomplete="off"
        />
      </Field>
    </div>
    <Field>
      <FieldLabel for="sound-volume">{{ $t("dashboard.sounds.dialog.volume") }}</FieldLabel>
      <Input id="sound-volume"
             v-model.number="form.defaultVolume"
             data-sound-volume
             type="number"
             min="0"
             :max="maxVolume"
             step="1"
      />
    </Field>
  </FormDialog>
</template>
