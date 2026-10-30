<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The detail of one tenant of the registry that has no channel console: a viewer who signed in. It shows every
// field the bot returns for the person. The server has no single-tenant read, so this shows the list row in full.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminTenant } from '@/api/admin';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useTime } from '@/composables/useTime';

import { platformLine } from './tenantDisplay';

const props = defineProps<{ tenant: AdminTenant }>();

const { t } = useI18n();
const time = useTime();

const facts = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.admin.tenants.detail.login'), value: props.tenant.login },
  { label: t('dashboard.admin.tenants.detail.kind'), value: t(`dashboard.admin.tenants.kind.${props.tenant.kind}`) },
  { label: t('dashboard.admin.tenants.detail.platforms'), value: platformLine(props.tenant.platforms, t) },
  {
    label: t('dashboard.admin.tenants.detail.lastActive'),
    value: props.tenant.lastActive
      ? time.date(props.tenant.lastActive)
      : t('dashboard.admin.tenants.never'),
  },
  { label: t('dashboard.admin.tenants.detail.created'), value: time.date(props.tenant.createdAt) },
]);
</script>

<template>
  <PanelCard
    data-tenant-detail
    pad
    :title="tenant.displayName"
    :description="t('dashboard.admin.tenants.detail.description')"
  >
    <KeyValueList :items="facts" />
  </PanelCard>
</template>
