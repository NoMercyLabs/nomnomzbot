<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// The settings of one saved clip: the name people see, the volume, the cooldown, who may play it from chat and the
// chat word. The audio file and the short name stay as uploaded; upload a new clip to change the sound.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { rungNames } from '@/api/permissions';
import { useSaveSoundClip, type SoundClip } from '@/api/soundClips';
import DurationInput from '@/components/layout/DurationInput.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import {
  changeFromForm,
  editFormFrom,
  editProblem,
  maxTriggerLength,
  maxVolume,
  type EditForm,
} from './soundClipForm';

const open = defineModel<boolean>('open', { required: true });
const props = defineProps<{ clip: SoundClip | null }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSaveSoundClip(channelId);

const form = ref<EditForm | null>(null);
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen || props.clip === null) return;

  form.value = editFormFrom(props.clip);
  error.value = undefined;
});

async function submit(): Promise<void> {
  if (form.value === null || props.clip === null) return;

  const problem = editProblem(form.value);

  error.value = problem === undefined
    ? undefined
    : t(`dashboard.sounds.dialog.problem.${problem}`, {
        max: maxVolume,
        trigger: maxTriggerLength,
      });

  if (error.value !== undefined) return;

  try {
    await save.mutateAsync({
      id: props.clip.id,
      change: changeFromForm(form.value, props.clip.isEnabled),
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
    v-if="form"
    v-model:open="open"
    :title="$t('dashboard.sounds.dialog.editTitle', { name: clip?.displayName ?? '' })"
    :description="$t('dashboard.sounds.dialog.editDescription')"
    :action="$t('dashboard.sounds.dialog.save')"
    :pending="save.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="sound-edit-display-name">{{ $t("dashboard.sounds.dialog.displayName") }}</FieldLabel>
      <Input id="sound-edit-display-name"
             v-model="form.displayName"
             data-sound-display-name
             autocomplete="off"
      />
    </Field>
    <div class="grid grid-cols-2 gap-3">
      <Field>
        <FieldLabel for="sound-edit-volume">{{ $t("dashboard.sounds.dialog.volume") }}</FieldLabel>
        <Input id="sound-edit-volume"
               v-model.number="form.defaultVolume"
               data-sound-volume
               type="number"
               min="0"
               :max="maxVolume"
               step="1"
        />
      </Field>
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="sound-edit-cooldown">{{ $t("dashboard.sounds.dialog.cooldown") }}</FieldLabel>
          <FieldInfo>{{ $t("dashboard.sounds.dialog.cooldownHelp") }}</FieldInfo>
        </div>
        <DurationInput id="sound-edit-cooldown"
                       v-model="form.cooldownSeconds"
                       data-sound-cooldown
        />
      </Field>
    </div>
    <div class="grid grid-cols-2 gap-3">
      <Field>
        <FieldLabel>{{ $t("dashboard.sounds.dialog.permission") }}</FieldLabel>
        <Select v-model="form.minPermissionLevel">
          <SelectTrigger :aria-label="$t('dashboard.sounds.dialog.permission')" class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="level in rungNames" :key="level" :value="level">
              {{ level }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="sound-edit-trigger">{{ $t("dashboard.sounds.dialog.trigger") }}</FieldLabel>
          <FieldInfo>{{ $t("dashboard.sounds.dialog.triggerHelp") }}</FieldInfo>
        </div>
        <Input id="sound-edit-trigger"
               v-model="form.triggerWord"
               data-sound-trigger
               autocomplete="off"
               :maxlength="maxTriggerLength"
        />
      </Field>
    </div>
  </FormDialog>
</template>
