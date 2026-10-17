<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Platform admin: the OAuth app behind every provider. Secrets are write-only; the page shows only the source.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAdminProviders } from '@/api/adminProviders';
import { ApiError } from '@/api/errors';
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
    ? t('dashboard.adminProviders.forbidden')
    : t('dashboard.adminProviders.loadError'),
);
</script>

<template>
  <PageHeader :title="$t('dashboard.adminProviders.title')" :crumb="$t('dashboard.adminProviders.crumb')" />
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 overflow-y-auto p-6 max-md:p-4">
    <p class="text-sm text-muted-foreground">{{ $t('dashboard.adminProviders.explain') }}</p>
    <p data-restricted class="text-sm text-muted-foreground">{{ $t('dashboard.adminProviders.restricted') }}</p>
    <PanelCard
      tier="lead"
      :title="$t('dashboard.adminProviders.listTitle')"
      :state="state"
      :empty-text="$t('dashboard.adminProviders.empty')"
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
  </div>
</template>
