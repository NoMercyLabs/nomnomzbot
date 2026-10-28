<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Platform admin: the OAuth app behind every provider. Secrets are write-only; the page shows only the source.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAdminProviders } from '@/api/adminProviders';
import { ApiError } from '@/api/errors';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';

import ProviderCredentialRow from './components/ProviderCredentialRow.vue';

const { t } = useI18n();

const providers = useAdminProviders();
const state = usePanelState(providers, (data) =>
  data.length === 0);

const errorText = computed<string>(() =>
  providers.error.value instanceof ApiError && providers.error.value.status === 403
    ? t('dashboard.admin.providers.forbidden')
    : t('dashboard.admin.providers.loadError'),
);
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.providers.title')" :crumb="$t('dashboard.admin.providers.crumb')" />
  <PageBody data-page-body>
    <p class="text-sm text-muted-foreground">{{ $t('dashboard.admin.providers.explain') }}</p>
    <p data-restricted class="text-sm text-muted-foreground">{{ $t('dashboard.admin.providers.restricted') }}</p>
    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.providers.listTitle')"
      :state="state"
      :empty-text="$t('dashboard.admin.providers.empty')"
      :error-text="errorText"
      @retry="providers.refetch()"
    >
      <ul class="divide-y divide-border">
        <ProviderCredentialRow
          v-for="credential in providers.data.value"
          :key="credential.provider"
          :credential="credential"
        />
      </ul>
    </PanelCard>
  </PageBody>
</template>
