<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot, free software licensed under the GNU Affero
   General Public License v3.0 or later. You may redistribute and/or modify it
   under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

   SPDX-License-Identifier: AGPL-3.0-or-later
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// The channel a role assignment is scoped to, by its name. The assignment holds only the channel id,
// so the name comes from the tenant lookup (cached per channel); the id itself is never shown.
import { computed } from 'vue';

import { useAdminTenant } from '@/api/admin';

const props = defineProps<{ channelId: string }>();

const tenant = useAdminTenant(computed(() => props.channelId));
</script>

<template>
  <span v-if="tenant.data.value">{{
    $t("dashboard.admin.access.principals.scope", { channel: tenant.data.value.name })
  }}</span>
  <span v-else>{{ $t("dashboard.admin.access.principals.scopeUnknown") }}</span>
</template>
