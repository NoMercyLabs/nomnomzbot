<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One dialog adds a pronunciation rule (entry null) or edits one (entry given).
// It closes when the save works. A refusal keeps it open with the reason inside, so the typed text survives.
import { computed, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { ApiError } from '@/api/errors';
import { useSaveLexiconEntry, type TtsLexiconEntry } from '@/api/ttsRules';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

const props = defineProps<{ entry: TtsLexiconEntry | null }>();

const open = defineModel<boolean>('open', { required: true });

const matchKinds = ['word', 'exact'] as const;
const duplicateStatus = 409;

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const saveEntry = useSaveLexiconEntry(channelId);

const draft = reactive({
  phrase: '',
  replacement: '',
  matchKind: 'word',
});
const failure = ref<string | undefined>(undefined);

const canSave = computed(() => draft.phrase.trim() !== '' && draft.replacement.trim() !== '');

// Every opening starts from the rule being edited, or from an empty rule, and without the last refusal.
watch(open, (isOpen) => {
  if (!isOpen) return;

  failure.value = undefined;

  Object.assign(draft, {
    phrase: props.entry?.phrase ?? '',
    replacement: props.entry?.replacement ?? '',
    matchKind: props.entry?.matchKind ?? 'word',
  });
});

function reason(error: Error): string {
  const duplicate = error instanceof ApiError && error.status === duplicateStatus;

  return duplicate
    ? t('dashboard.tts.lexicon.duplicate')
    : errorText(error, t);
}

function submit(): void {
  if (!canSave.value) return;

  saveEntry.mutate(
    {
      entryId: props.entry?.id ?? null,
      draft: {
        phrase: draft.phrase.trim(),
        replacement: draft.replacement.trim(),
        matchKind: draft.matchKind,
      },
    },
    {
      onSuccess: () => {
        open.value = false;
      },
      onError: (error) => {
        failure.value = reason(error);
        toast.error(t('dashboard.tts.lexicon.saveFailed'));
      },
    },
  );
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-lexicon-dialog
    :title="$t(entry ? 'dashboard.tts.lexicon.editTitle' : 'dashboard.tts.lexicon.addTitle')"
    :description="$t('dashboard.tts.lexicon.dialogDescription')"
    :action="$t(entry ? 'dashboard.tts.lexicon.save' : 'dashboard.tts.lexicon.add')"
    :pending="saveEntry.isPending.value"
    :disabled="!canSave"
    :error="failure"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="lexicon-phrase">{{ $t('dashboard.tts.lexicon.phrase') }}</FieldLabel>
      <Input id="lexicon-phrase" v-model="draft.phrase" maxlength="100" autocomplete="off" />
    </Field>
    <Field>
      <FieldLabel for="lexicon-replacement">
        {{ $t('dashboard.tts.lexicon.replacement') }}
      </FieldLabel>
      <Input
        id="lexicon-replacement"
        v-model="draft.replacement"
        maxlength="200"
        autocomplete="off"
      />
    </Field>
    <Field>
      <FieldLabel for="lexicon-match-kind">{{ $t('dashboard.tts.lexicon.matchKind') }}</FieldLabel>
      <Select v-model="draft.matchKind">
        <SelectTrigger id="lexicon-match-kind" class="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent position="popper">
          <SelectItem v-for="kind in matchKinds" :key="kind" :value="kind">
            {{ $t(`dashboard.tts.lexicon.kind.${kind}`) }}
          </SelectItem>
        </SelectContent>
      </Select>
    </Field>
  </FormDialog>
</template>
