<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The detail of one channel of the registry: the facts the bot holds about it, and one way on to the tenant
// console, where it is managed. The list row already carries the live state; the rest comes from the tenant read.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';

import { useAdminTenant, type AdminTenant } from '@/api/admin';
import { useBillingTiers } from '@/api/adminBilling';
import { tierNameOf } from '@/api/billing';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { useTime } from '@/composables/useTime';

import ActAsDialog, { type ActAsChannel } from './ActAsDialog.vue';

const props = defineProps<{ channel: AdminTenant }>();

const { t, te } = useI18n();
const time = useTime();
const { can } = usePlatformCan();

// Acting as someone opens a support session first, so it takes both keys.
const canActAs = computed<boolean>(() => can('tenant:access') && can('user:impersonate'));
const actingOn = ref<ActAsChannel | null>(null);

const detail = useAdminTenant(() => props.channel.id);
const tenant = computed(() => detail.data.value ?? null);
const state = usePanelState(detail, (data) =>
  data === null);

const tiers = useBillingTiers();

const tierName = computed(() => tierNameOf(tiers.data.value, tenant.value?.billingTierKey));

const modeName = computed(() => {
  const mode = tenant.value?.deploymentMode;

  return mode !== undefined && te(`dashboard.admin.detail.modes.${mode}`)
    ? t(`dashboard.admin.detail.modes.${mode}`)
    : undefined;
});

const facts = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.admin.detail.owner'), value: tenant.value?.ownerDisplayName },
  { label: t('dashboard.admin.detail.status'), value: t(`dashboard.admin.status.${props.channel.status}`) },
  {
    label: t('dashboard.admin.detail.live'),
    value: props.channel.isLive
      ? t('dashboard.admin.live.live')
      : t('dashboard.admin.live.offline'),
  },
  { label: t('dashboard.admin.detail.tier'), value: tierName.value },
  { label: t('dashboard.admin.detail.mode'), value: modeName.value },
  { label: t('dashboard.admin.detail.members'), value: Number(tenant.value?.membershipCount ?? 0) },
  { label: t('dashboard.admin.detail.created'), value: time.date(props.channel.createdAt) },
  { label: t('dashboard.admin.detail.suspendedReason'), value: tenant.value?.suspendedReason },
]);
</script>

<template>
  <PanelCard
    data-channel-detail
    pad
    :title="channel.name"
    :description="t('dashboard.admin.detail.description')"
    :state="state"
    :error-text="t('dashboard.admin.tenants.error')"
    @retry="detail.refetch()"
  >
    <div class="grid gap-4">
      <KeyValueList :items="facts" />
      <p v-if="tenant?.deploymentMode === 'saas'" data-saas-restricted class="text-sm text-muted-foreground">
        {{ t('dashboard.admin.detail.restricted') }}
      </p>
      <div class="flex flex-wrap items-center gap-2">
        <Button as-child>
          <RouterLink :to="{ name: 'dashboard-admin-tenants', query: { tenant: channel.id } }">
            {{ t('dashboard.admin.detail.manage') }}
          </RouterLink>
        </Button>
        <Button
          v-if="canActAs"
          variant="outline"
          data-act-as
          @click="actingOn = { id: channel.id, name: channel.name }"
        >
          {{ t('dashboard.admin.actAs.open') }}
        </Button>
      </div>
    </div>
  </PanelCard>
  <ActAsDialog v-if="canActAs && actingOn !== null" :channel="actingOn" @close="actingOn = null" />
</template>
