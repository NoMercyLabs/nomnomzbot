<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
