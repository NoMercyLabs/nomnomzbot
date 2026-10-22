<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The pronunciation lexicon: when a phrase appears in a message, the bot speaks the replacement instead.
// A dialog adds a rule (the Add rule button) or edits one (the pencil of its row).
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import Add from '~icons/nnz/add';
import Edit from '~icons/nnz/edit';
import Trash from '~icons/nnz/trash';

import { useTtsConfig } from '@/api/tts';
import {
  useDeleteLexiconEntry,
  useTtsDefaults,
  useTtsLexicon,
  type TtsLexiconEntry,
} from '@/api/ttsRules';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import LexiconRuleDialog from './LexiconRuleDialog.vue';
import PlaySampleButton from './PlaySampleButton.vue';

// The one column grid of the header and every rule row: phrase, replacement, match, actions.
// Under md the cells stack and the header hides.
const columns = 'md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_13rem_11rem]';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const config = useTtsConfig(channelId);
const defaults = useTtsDefaults(channelId);
const lexicon = useTtsLexicon(channelId);
const removeEntry = useDeleteLexiconEntry(channelId);

const dialogOpen = ref(false);
const editing = ref<TtsLexiconEntry | null>(null);
const pendingRemoval = ref<TtsLexiconEntry | null>(null);

const entries = computed(() => lexicon.data.value ?? []);

// The channel voice, or the platform default it follows: the voice the bot really speaks a rule in.
const heardVoice = computed<string | null>(
  () => config.data.value?.defaultVoiceId || defaults.data.value?.defaultVoiceId || null,
);
// The header names the columns of the rules; with no rule on screen it has nothing to name.
const showHeader = computed(() => entries.value.length > 0);

const state = computed<PanelState>(() => {
  if (lexicon.isError.value)
    return 'error';

  return lexicon.isPending.value
    ? 'loading'
    : 'ready';
});

function openDialog(entry: TtsLexiconEntry | null): void {
  editing.value = entry;
  dialogOpen.value = true;
}

function remove(entry: TtsLexiconEntry): void {
  removeEntry.mutate(entry.id, {
    onSuccess: () => {
      pendingRemoval.value = null;
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
    <template v-if="can('tts:config:write')" #actions>
      <Button data-lexicon-add @click="openDialog(null)">
        <Add class="size-5" aria-hidden="true" />
        {{ $t("dashboard.tts.lexicon.add") }}
      </Button>
    </template>
    <div
      v-if="showHeader"
      data-lexicon-header
      :class="columns"
      class="hidden items-center gap-2 border-b border-border px-2 py-2 text-xs font-medium text-muted-foreground md:grid"
    >
      <span>{{ $t("dashboard.tts.lexicon.phrase") }}</span>
      <span>{{ $t("dashboard.tts.lexicon.replacement") }}</span>
      <span>{{ $t("dashboard.tts.lexicon.matchKind") }}</span>
      <span />
    </div>
    <p v-if="entries.length === 0" class="p-3 text-sm text-muted-foreground">
      {{ $t("dashboard.tts.lexicon.empty") }}
    </p>
    <ul v-else class="divide-y divide-border">
      <li
        v-for="entry in entries"
        :key="entry.id"
        :class="columns"
        class="grid items-center gap-2 px-2 py-2 text-sm"
        data-lexicon-row
      >
        <span data-lexicon-phrase class="min-w-0 font-medium wrap-break-word">
          {{ entry.phrase }}
        </span>
        <span data-lexicon-replacement class="min-w-0 wrap-break-word">
          {{ entry.replacement }}
        </span>
        <span data-lexicon-kind class="min-w-0 text-muted-foreground">
          {{ $t(`dashboard.tts.lexicon.kind.${entry.matchKind}`) }}
        </span>
        <div class="flex items-center justify-end gap-2">
          <PlaySampleButton v-if="heardVoice" compact :voice-id="heardVoice" :text="entry.phrase" />
          <Button
            v-if="can('tts:config:write')"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.tts.lexicon.edit', { phrase: entry.phrase })"
            @click="openDialog(entry)"
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
        </div>
      </li>
    </ul>
  </PanelCard>
  <LexiconRuleDialog v-model:open="dialogOpen" :entry="editing" />
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
