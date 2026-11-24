<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// A sound clip definition's draft with the fields of the channel's sound clip dialog, plus the short name and the
// audio file from the platform library. Save keeps the whole payload as a new version and is the one primary
// action; Edit JSON is the raw fallback.
import Cross from '~icons/nnz/cross';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAudioAssets } from '@/api/adminContent';
import { rungNames } from '@/api/permissions';
import DurationInput from '@/components/layout/DurationInput.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ensureWords } from '@/i18n';
import { maxTriggerLength, maxVolume } from '@/views/dashboard/sounds/soundClipForm';

import {
  soundClipPayloadProblem,
  soundClipWords,
  writeSoundClipPayload,
  type SoundClipPayloadForm,
} from './soundClipPayload';

const props = defineProps<{
  title: string;
  payload: SoundClipPayloadForm;
  // Keeps the payload; resolves to the message shown after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{
  close: [];
  // Hands the draft over as JSON for the raw editor.
  editJson: [payloadJson: string];
}>();

const { t } = useI18n();

// The limits the template adds to the channel's own form; their words live in the admin locale.
const templateProblems = new Set<string>([
  'nameRequired',
  'nameCharacters',
  'nameTooLong',
  'displayNameTooLong',
  'cooldownTooLong',
  'assetRequired',
]);

const draft = ref<SoundClipPayloadForm>({ ...props.payload });
const assets = useAudioAssets();

const wordsReady = ref(false);
const message = ref('');
const saving = ref(false);

void ensureWords(soundClipWords).then(() => {
  wordsReady.value = true;
});

const problem = computed(() => soundClipPayloadProblem(draft.value));
const problemText = computed(() => {
  const found = problem.value;

  if (found === null)
    return '';

  if (templateProblems.has(found))
    return t(`dashboard.admin.content.soundClipEditor.problem.${found}`);

  return t(`dashboard.sounds.dialog.problem.${found}`, { max: maxVolume, trigger: maxTriggerLength });
});

function currentJson(): string {
  return writeSoundClipPayload(draft.value);
}

async function save(): Promise<void> {
  saving.value = true;
  message.value = await props.keep(currentJson());
  saving.value = false;
}
</script>

<template>
  <Dialog :open="true" @update:open="emit('close')">
    <DialogContent
      data-soundclip-payload-editor
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl"
    >
      <div class="flex flex-none items-start justify-between gap-4 px-6 pt-6 pb-4">
        <div class="grid gap-1.5">
          <DialogTitle class="text-lg font-semibold">{{ title }}</DialogTitle>
          <DialogDescription class="text-sm text-muted-foreground">
            {{ $t("dashboard.admin.content.soundClipEditor.description") }}
          </DialogDescription>
        </div>
        <DialogClose as-child>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('common.close')"
          >
            <Cross class="size-5" aria-hidden="true" />
          </Button>
        </DialogClose>
      </div>
      <div v-if="wordsReady" class="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-6 pb-4">
        <div class="grid grid-cols-2 gap-3">
          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="soundclip-name">{{ $t("dashboard.sounds.dialog.name") }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.sounds.dialog.nameHelp") }}</FieldInfo>
            </div>
            <Input
              id="soundclip-name"
              v-model="draft.name"
              autocomplete="off"
            />
          </Field>
          <Field>
            <FieldLabel for="soundclip-display-name">{{ $t("dashboard.sounds.dialog.displayName") }}</FieldLabel>
            <Input
              id="soundclip-display-name"
              v-model="draft.displayName"
              autocomplete="off"
            />
          </Field>
        </div>

        <Field>
          <div class="flex items-center gap-1.5">
            <FieldLabel>{{ $t("dashboard.admin.content.soundClipEditor.audio") }}</FieldLabel>
            <FieldInfo>{{ $t("dashboard.admin.content.soundClipEditor.audioHelp") }}</FieldInfo>
          </div>
          <Select v-model="draft.assetId">
            <SelectTrigger :aria-label="$t('dashboard.admin.content.soundClipEditor.audio')" class="w-full">
              <SelectValue :placeholder="$t('dashboard.admin.content.soundClipEditor.audioPlaceholder')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="asset in assets.data.value ?? []"
                :key="asset.id"
                :value="asset.id"
              >
                {{ asset.displayName }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field>

        <div class="grid grid-cols-2 gap-3">
          <Field>
            <FieldLabel for="soundclip-volume">{{ $t("dashboard.sounds.dialog.volume", { max: maxVolume }) }}</FieldLabel>
            <Input
              id="soundclip-volume"
              v-model.number="draft.defaultVolume"
              type="number"
              min="0"
              :max="maxVolume"
              step="1"
            />
          </Field>
          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="soundclip-cooldown">{{ $t("dashboard.sounds.dialog.cooldown") }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.sounds.dialog.cooldownHelp") }}</FieldInfo>
            </div>
            <DurationInput id="soundclip-cooldown" v-model="draft.cooldownSeconds" />
          </Field>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <Field>
            <FieldLabel>{{ $t("dashboard.sounds.dialog.permission") }}</FieldLabel>
            <Select v-model="draft.minPermissionLevel">
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
              <FieldLabel for="soundclip-trigger">{{ $t("dashboard.sounds.dialog.trigger") }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.sounds.dialog.triggerHelp") }}</FieldInfo>
            </div>
            <Input
              id="soundclip-trigger"
              v-model="draft.triggerWord"
              autocomplete="off"
              :maxlength="maxTriggerLength"
            />
          </Field>
        </div>

        <InlineNotice v-if="problemText" data-soundclip-problem tone="error" compact>
          {{ problemText }}
        </InlineNotice>
      </div>
      <p
        v-if="message"
        data-soundclip-message
        class="flex-none px-6 pb-4 text-sm text-muted-foreground"
        role="status"
      >
        {{ message }}
      </p>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button
          data-soundclip-edit-json
          type="button"
          variant="outline"
          class="sm:mr-auto"
          @click="emit('editJson', currentJson())"
        >
          {{ $t("dashboard.admin.content.soundClipEditor.editJson") }}
        </Button>
        <Button
          data-soundclip-save
          type="button"
          :disabled="saving || problem !== null"
          @click="save"
        >
          {{ $t("dashboard.admin.content.draft.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
