// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// How an editor answers: with its own simple reply text, or by running a full pipeline that has its own reply step.
export type ReplyMode = 'simple' | 'full';

// A pipeline that is picked sends this id. The bot reads the empty id as "no pipeline" (the sentinel of its PUT calls).
export const noPipelineId = '00000000-0000-0000-0000-000000000000';
