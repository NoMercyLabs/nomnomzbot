<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The detail of one channel of the registry: the facts the bot holds about it, and one way on to the tenant
// console, where it is managed. The list row already carries the live state; the rest comes from the tenant read.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';

import { useAdminTenant, type AdminTenant } from '@/api/admin';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

const props = defineProps<{ channel: AdminTenant }>();

const { t } = useI18n();
const time = useTime();

const detail = useAdminTenant(() => props.channel.id);
const tenant = computed(() => detail.data.value ?? null);
const state = usePanelState(detail, (data) =>
  data === null);

const facts = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.admin.detail.owner'), value: tenant.value?.ownerDisplayName },
  { label: t('dashboard.admin.detail.status'), value: t(`dashboard.admin.status.${props.channel.status}`) },
  {
    label: t('dashboard.admin.detail.live'),
    value: props.channel.isLive
      ? t('dashboard.admin.live.live')
      : t('dashboard.admin.live.offline'),
  },
  { label: t('dashboard.admin.detail.tier'), value: tenant.value?.billingTierKey },
  { label: t('dashboard.admin.detail.mode'), value: tenant.value?.deploymentMode },
  { label: t('dashboard.admin.detail.members'), value: Number(tenant.value?.membershipCount ?? 0) },
  { label: t('dashboard.admin.detail.twitchId'), value: props.channel.twitchChannelId },
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
      <Button as-child class="justify-self-start">
        <RouterLink :to="{ name: 'dashboard-admin-tenants', query: { tenant: channel.id } }">
          {{ t('dashboard.admin.detail.manage') }}
        </RouterLink>
      </Button>
    </div>
  </PanelCard>
</template>
