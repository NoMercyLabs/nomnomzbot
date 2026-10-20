<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The detail of one user of the registry: every field the bot returns for a person. The server has no
// single-user read and no list of the user's channels, so this shows the list row in full.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminUser } from '@/api/adminUsers';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useTime } from '@/composables/useTime';

const props = defineProps<{ user: AdminUser }>();

const { t } = useI18n();
const time = useTime();

const facts = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.admin.users.detail.login'), value: props.user.login },
  { label: t('dashboard.admin.users.detail.email'), value: props.user.email },
  {
    label: t('dashboard.admin.users.detail.role'),
    value: t(`dashboard.admin.users.role.${props.user.role === 'admin'
      ? 'admin'
      : 'user'}`),
  },
  { label: t('dashboard.admin.users.detail.channels'), value: Number(props.user.channelCount) },
  {
    label: t('dashboard.admin.users.detail.lastActive'),
    value: props.user.lastActive
      ? time.date(props.user.lastActive)
      : t('dashboard.admin.users.never'),
  },
  { label: t('dashboard.admin.users.detail.created'), value: time.date(props.user.createdAt) },
]);
</script>

<template>
  <PanelCard
    data-user-detail
    pad
    :title="user.displayName"
    :description="t('dashboard.admin.users.detail.description')"
  >
    <KeyValueList :items="facts" />
  </PanelCard>
</template>
