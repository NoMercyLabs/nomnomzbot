# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
#
# brief-check.sh: checks a draft agent brief against every dispatch guard BEFORE the Agent call,
# with this repo's rule registry. Exit 0 = PASS (dispatch the same text with the same type and
# model); exit 1 = the gaps, all at once; exit 2 = a usage error.
# Usage: sh scripts/brief-check.sh <draft.md> --type <subagent_type> --model <model>
exec node "C:/Projects/NoMercy/.claude/scripts/brief-check.mjs" "$@" --project "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
