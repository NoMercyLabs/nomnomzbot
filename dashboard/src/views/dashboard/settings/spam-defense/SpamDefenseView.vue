<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's own spam-defense policy. The form beneath renders one control per catalog entry the bot sends
// and checks the catalog limits before Save; this page owns the Save and sends the whole record.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';
import Check from '~icons/nnz/check';

import { useSaveSpamDefensePolicy, useSpamDefensePolicy } from '@/api/spamDefensePolicy';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SpamSettingsForm, { type SpamFormState } from '@/components/spam/SpamSettingsForm.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const MANAGE = 'spam:policy:manage';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const policy = useSpamDefensePolicy(channelId);
const save = useSaveSpamDefensePolicy(channelId);

const form = ref<SpamFormState | null>(null);
const panelState = usePanelState(policy);
const canManage = computed<boolean>(() => can(MANAGE));

function submit(): void {
  if (form.value === null) return;

  save.mutate(form.value.body, {
    onSuccess: () => toast.success(t('dashboard.settings.spamDefense.saved')),
    onError: (error: Error) =>
      toast.error(t('dashboard.settings.spamDefense.saveError', { reason: error.message })),
  });
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.spamDefense')"
    :crumb="$t('dashboard.settings.spamDefense.crumb')"
  >
    <template #status>
      <span v-if="form?.dirty" class="text-sm text-muted-foreground" role="status">
        {{ $t("dashboard.settings.spamDefense.unsaved") }}
      </span>
    </template>
    <template v-if="canManage" #actions>
      <Button
        data-save
        :disabled="!form?.dirty || form.invalid || save.isPending.value"
        @click="submit"
      >
        <Check aria-hidden="true" />
        {{
          save.isPending.value
            ? $t("dashboard.settings.spamDefense.saving")
            : $t("dashboard.settings.spamDefense.save")
        }}
      </Button>
    </template>
  </PageHeader>
  <PageBody data-page-body>
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.settings.spamDefense.explain") }}</p>
    <SpamSettingsForm
      v-if="panelState === 'ready' && policy.data.value"
      :policy="policy.data.value"
      :action="MANAGE"
      @change="(state) => (form = state)"
    />
    <PanelCard
      v-else
      tier="lead"
      class="min-h-40"
      :title="$t('frame.nav.settingsPages.spamDefense')"
      :state="panelState"
      :error-text="$t('dashboard.settings.spamDefense.loadError')"
      @retry="policy.refetch()"
    />
  </PageBody>
</template>
