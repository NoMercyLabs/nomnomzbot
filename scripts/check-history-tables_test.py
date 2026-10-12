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
# Tests for scripts/check-history-tables.py.  Run: python scripts/check-history-tables_test.py

import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location(
    'check_history_tables', pathlib.Path(__file__).with_name('check-history-tables.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def handler(body):
    return ('public sealed class ChatMessagePersistenceHandler(IDbContextFactory<AppDbContext> f)\n'
            '    : IEventHandler<ChatMessageReceivedEvent>\n{\n' + body + '\n}')


def projection(body):
    return 'public sealed class ChatMessageProjection(AppDbContext db) : IProjection\n{\n' + body + '\n}'


class HistoryTableGate(unittest.TestCase):
    def test_a_table_only_a_live_handler_writes_fails(self):
        # 2026-10-12: ChatMessages was written only by ChatMessagePersistenceHandler, so the 14-month
        # import never reached it and the streamer's top chatter showed as new.
        files = {'ChatMessagePersistenceHandler.cs': handler('db.ChatMessages.Add(row);')}
        self.assertEqual(['ChatMessages'], gate.find_unprojected(files, exempt={}))

    def test_a_table_a_projection_also_writes_passes(self):
        files = {'ChatMessagePersistenceHandler.cs': handler('db.ChatMessages.Add(row);'),
                 'ChatMessageProjection.cs': projection('_db.ChatMessages.Add(row);')}
        self.assertEqual([], gate.find_unprojected(files, exempt={}))

    def test_every_write_form_counts(self):
        body = ('_db.A.AddRange(x); context.B.Update(x); dbContext.C.RemoveRange(x);'
                ' await db.D.Where(p).ExecuteDeleteAsync(ct); await _db.E.AddAsync(x);')
        files = {'H.cs': handler(body)}
        self.assertEqual(['A', 'B', 'C', 'D', 'E'], gate.find_unprojected(files, exempt={}))

    def test_a_query_is_not_a_write(self):
        files = {'H.cs': handler('var n = await db.ChatMessages.CountAsync(ct);')}
        self.assertEqual([], gate.find_unprojected(files, exempt={}))

    def test_an_exempt_table_passes(self):
        files = {'H.cs': handler('db.EarningRules.Add(rule);')}
        self.assertEqual([], gate.find_unprojected(files, exempt={'EarningRules': 'config, not history'}))

    def test_a_write_outside_a_handler_is_not_checked(self):
        files = {'ChatController.cs': 'public sealed class ChatController { void X() { db.ChatMessages.Add(r); } }'}
        self.assertEqual([], gate.find_unprojected(files, exempt={}))

    def test_a_write_inside_a_comment_is_not_a_write(self):
        files = {'H.cs': handler('// the projection, not db.ChatMessages.Add(row), fills the past')}
        self.assertEqual([], gate.find_unprojected(files, exempt={}))

    def test_a_stale_exemption_is_reported(self):
        # An exemption for a table no handler writes any more must be removed, so the list only shrinks.
        files = {'H.cs': handler('db.ChatMessages.Add(row);'), 'P.cs': projection('db.ChatMessages.Add(row);')}
        self.assertEqual(['Streams'], gate.find_stale(files, exempt={'Streams': 'debt'}))


if __name__ == '__main__':
    unittest.main()
