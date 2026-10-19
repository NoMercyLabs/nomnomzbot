<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The pronunciation lexicon: when a phrase appears in a message, the bot speaks the replacement instead.
// The form on top adds a rule, or saves the rule picked with the pencil.
import { computed, reactive, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import Edit from '~icons/nnz/edit';
import Trash from '~icons/nnz/trash';

import { useTtsConfig } from '@/api/tts';
import {
  useDeleteLexiconEntry,
  useSaveLexiconEntry,
  useTtsDefaults,
  useTtsLexicon,
  type TtsLexiconEntry,
} from '@/api/ttsRules';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import PlaySampleButton from './PlaySampleButton.vue';

const matchKinds = ['word', 'exact'] as const;
const emptyDraft = {
  phrase: '',
  replacement: '',
  matchKind: 'word',
};

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const config = useTtsConfig(channelId);
const defaults = useTtsDefaults(channelId);
const lexicon = useTtsLexicon(channelId);
const saveEntry = useSaveLexiconEntry(channelId);
const removeEntry = useDeleteLexiconEntry(channelId);

const editing = ref<string | null>(null);
const pendingRemoval = ref<TtsLexiconEntry | null>(null);
const draft = reactive({ ...emptyDraft });

const entries = computed(() => lexicon.data.value ?? []);

// The channel voice, or the platform default it follows: the voice the bot really speaks a rule in.
const heardVoice = computed<string | null>(
  () => config.data.value?.defaultVoiceId || defaults.data.value?.defaultVoiceId || null,
);
const canSave = computed(
  () => draft.phrase.trim() !== '' && draft.replacement.trim() !== '' && !saveEntry.isPending.value,
);

const state = computed<PanelState>(() => {
  if (lexicon.isError.value)
    return 'error';

  return lexicon.isPending.value
    ? 'loading'
    : 'ready';
});

function reset(): void {
  editing.value = null;
  Object.assign(draft, emptyDraft);
}

function edit(entry: TtsLexiconEntry): void {
  editing.value = entry.id;

  Object.assign(draft, {
    phrase: entry.phrase,
    replacement: entry.replacement,
    matchKind: entry.matchKind,
  });
}

function submit(): void {
  saveEntry.mutate(
    {
      entryId: editing.value,
      draft: {
        phrase: draft.phrase.trim(),
        replacement: draft.replacement.trim(),
        matchKind: draft.matchKind,
      },
    },
    {
      onSuccess: reset,
      onError: () => toast.error(t('dashboard.tts.lexicon.saveFailed')),
    },
  );
}

function remove(entry: TtsLexiconEntry): void {
  removeEntry.mutate(entry.id, {
    onSuccess: () => {
      pendingRemoval.value = null;

      if (editing.value === entry.id) {
        reset();
      }
    },
    onError: () => toast.error(t('dashboard.tts.lexicon.deleteFailed')),
  });
}
</script>

<template>
  <PanelCard
    data-view="tts-lexicon"
    :title="$t('dashboard.tts.lexicon.title')"
    :description="$t('dashboard.tts.lexicon.description')"
    :state="state"
    :error-text="$t('dashboard.tts.lexicon.loadFailed')"
    @retry="lexicon.refetch()"
  >
    <form
      v-if="can('tts:config:write')"
      class="flex flex-wrap items-end gap-2 border-b border-border p-3"
      @submit.prevent="submit"
    >
      <label class="flex min-w-32 flex-1 flex-col gap-1 text-sm font-medium">
        {{ $t("dashboard.tts.lexicon.phrase") }}
        <Input v-model="draft.phrase" maxlength="100" />
      </label>
      <label class="flex min-w-32 flex-1 flex-col gap-1 text-sm font-medium">
        {{ $t("dashboard.tts.lexicon.replacement") }}
        <Input v-model="draft.replacement" maxlength="200" />
      </label>
      <div class="flex flex-col gap-1 text-sm font-medium">
        {{ $t("dashboard.tts.lexicon.matchKind") }}
        <Select v-model="draft.matchKind">
          <SelectTrigger class="w-52" :aria-label="$t('dashboard.tts.lexicon.matchKind')">
            <SelectValue />
          </SelectTrigger>
          <SelectContent position="popper" align="end">
            <SelectItem v-for="kind in matchKinds" :key="kind" :value="kind">
              {{ $t(`dashboard.tts.lexicon.kind.${kind}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <Button v-if="editing" type="button" variant="ghost" @click="reset">
        {{ $t("dashboard.tts.lexicon.cancel") }}
      </Button>
      <Button type="submit" :disabled="!canSave">
        {{ $t(editing ? "dashboard.tts.lexicon.save" : "dashboard.tts.lexicon.add") }}
      </Button>
    </form>
    <p v-if="entries.length === 0" class="p-3 text-sm text-muted-foreground">
      {{ $t("dashboard.tts.lexicon.empty") }}
    </p>
    <ul v-else class="divide-y divide-border">
      <li
        v-for="entry in entries"
        :key="entry.id"
        class="flex items-center gap-2 px-3 py-2"
        data-lexicon-row
      >
        <span class="min-w-0 flex flex-1 text-sm gap-1 items-center">
          <span class="font-medium">{{ entry.phrase }}</span>
          <span class="text-muted-foreground">
            {{ $t("dashboard.tts.lexicon.saysAs") }}
          </span>
          <span>{{ entry.replacement }}</span>
          <span class="block text-xs text-muted-foreground leading-none">
            {{ $t(`dashboard.tts.lexicon.kind.${entry.matchKind}`) }}
          </span>
        </span>
        <PlaySampleButton v-if="heardVoice" compact :voice-id="heardVoice" :text="entry.phrase" />
        <Button
          v-if="can('tts:config:write')"
          variant="ghost"
          size="icon"
          :aria-label="$t('dashboard.tts.lexicon.edit', { phrase: entry.phrase })"
          @click="edit(entry)"
        >
          <Edit class="size-5" />
        </Button>
        <Button
          v-if="can('tts:config:write')"
          variant="ghost"
          size="icon"
          :aria-label="$t('dashboard.tts.lexicon.remove', { phrase: entry.phrase })"
          @click="pendingRemoval = entry"
        >
          <Trash class="size-5" />
        </Button>
      </li>
    </ul>
  </PanelCard>
  <ConfirmDialog
    :open="pendingRemoval !== null"
    :title="$t('dashboard.tts.lexicon.confirmTitle')"
    :description="
      $t('dashboard.tts.lexicon.confirmDescription', { phrase: pendingRemoval?.phrase ?? '' })
    "
    :action="$t('dashboard.tts.lexicon.confirmAction')"
    :pending="removeEntry.isPending.value"
    @update:open="pendingRemoval = null"
    @confirm="pendingRemoval && remove(pendingRemoval)"
  />
</template>
