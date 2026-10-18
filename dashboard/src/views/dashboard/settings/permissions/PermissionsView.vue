<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Permissions: the team and its roles, the lowest role for each action, and the dangerous capabilities given to one person.
// What the caller may change follows the bot's own gate (roles:manage); a reader sees the page without the controls.
import { useChannelAccess } from '@/api/access';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import ActionMatrixPanel from './components/ActionMatrixPanel.vue';
import PersonGrantsPanel from './components/PersonGrantsPanel.vue';
import TeamRolesPanel from './components/TeamRolesPanel.vue';

const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.permissions')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <div data-view="permissions" class="flex flex-col gap-4">
      <TeamRolesPanel :can-manage="access.can('roles:manage')" />
      <ActionMatrixPanel :can-manage="access.can('roles:manage')" />
      <PersonGrantsPanel :can-manage="access.can('roles:manage')" />
    </div>
  </PageBody>
</template>
