// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { FakeReply } from '../fakeApi';
import escalation from '../fixtures/moderation-escalation.json';
import spamPolicy from '../fixtures/spam-policy.json';
import { channelPath, fixture, get } from './shape';

// The spam-defense policy of a fresh install, as the server sends it: every setting at its default, the catalog of
// settings with their groups and bounds, and the invariants. Nothing here is edited for the sample.
// The escalation ladder is the server's default ladder and limits (EscalationPolicyDefaults.cs), switched on, which
// is the one thing the sample channel's owner would have changed.
export const moderationReplies: FakeReply[] = [
  get(`${channelPath}/spam-defense/policy`, fixture(spamPolicy)),
  get(`${channelPath}/moderation/escalation`, fixture(escalation)),
];
