<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The words Twitch holds back from the channel's chat. Twitch owns the list, so the page reads it live and each
// change answers with the new list. Add is the one primary action; Remove is outline, and it has no confirm
// because adding the term again undoes it.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useAddBlockedTerm, useBlockedTerms, useRemoveBlockedTerm } from '@/api/blockedTerms';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();

const terms = useBlockedTerms(channelId);
const add = useAddBlockedTerm(channelId);
const remove = useRemoveBlockedTerm(channelId);

const draft = ref('');
const state = usePanelState(terms, (list: string[]) =>
  list.length === 0);
const canEdit = computed<boolean>(() => can('moderation:blocklist:write'));
const trimmed = computed<string>(() => draft.value.trim());

function submit(): void {
  if (trimmed.value === '') return;

  add.mutate(trimmed.value, {
    onSuccess: (): void => {
      draft.value = '';
    },
    onError: (error: Error) =>
      toast.error(t('dashboard.settings.blockedTerms.addError', { reason: error.message })),
  });
}

function drop(term: string): void {
  remove.mutate(term, {
    onError: (error: Error) =>
      toast.error(t('dashboard.settings.blockedTerms.removeError', { reason: error.message })),
  });
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.blockedTerms')"
    :crumb="$t('dashboard.settings.blockedTerms.crumb')"
  />
  <PageBody data-page-body>
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.settings.blockedTerms.explain") }}</p>
    <PanelCard
      v-if="canEdit"
      tier="lead"
      :title="$t('dashboard.settings.blockedTerms.addTitle')"
    >
      <form class="flex gap-2" data-add-form @submit.prevent="submit">
        <Input
          v-model="draft"
          data-term-input
          :aria-label="$t('dashboard.settings.blockedTerms.termLabel')"
          :placeholder="$t('dashboard.settings.blockedTerms.termPlaceholder')"
        />
        <Button type="submit" data-add :disabled="trimmed === '' || add.isPending.value">
          {{ $t("dashboard.settings.blockedTerms.add") }}
        </Button>
      </form>
    </PanelCard>
    <PanelCard
      :title="$t('dashboard.settings.blockedTerms.listTitle')"
      :state="state"
      :empty-text="$t('dashboard.settings.blockedTerms.empty')"
      :error-text="$t('dashboard.settings.blockedTerms.loadError')"
      @retry="terms.refetch()"
    >
      <RowGroup>
        <SummaryRow v-for="term in terms.data.value" :key="term" :data-term="term" :title="term">
          <template v-if="canEdit" #trailing>
            <Button
              variant="outline"
              size="sm"
              :data-term-remove="term"
              :disabled="remove.isPending.value"
              @click="drop(term)"
            >
              {{ $t("dashboard.settings.blockedTerms.remove") }}
            </Button>
          </template>
        </SummaryRow>
      </RowGroup>
    </PanelCard>
  </PageBody>
</template>
