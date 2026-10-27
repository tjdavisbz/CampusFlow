"""Offline checks for the production -> nonproduction safety boundary."""
import os
import unittest
from unittest.mock import patch
from refresh_nonproduction import HOST, guarded_environment, connection_fields


class DirectionGuards(unittest.TestCase):
    def connection(self, database='campusflow_nonprod', user='campusflow_nonprod_app', host=HOST):
        return f'Host={host};Database={database};Username={user};Password="test;password"'

    def test_restore_uses_only_nonproduction_database_and_role(self):
        env = guarded_environment(self.connection(), target=True)
        self.assertEqual(env['PGDATABASE'], 'campusflow_nonprod')
        self.assertEqual(env['PGUSER'], 'campusflow_nonprod_app')
        self.assertEqual(env['PGSSLMODE'], 'verify-full')

    def test_production_can_never_be_restore_destination(self):
        with self.assertRaises(ValueError):
            guarded_environment(self.connection(database='postgres'), target=True)

    def test_source_cannot_be_nonproduction(self):
        with self.assertRaises(ValueError):
            guarded_environment(self.connection(), target=False)

    def test_source_enforces_readonly(self):
        env = guarded_environment(self.connection(database='postgres', user='source_reader'), target=False)
        self.assertIn('default_transaction_read_only=on', env['PGOPTIONS'])

    def test_restore_cannot_use_production_admin_credentials(self):
        with self.assertRaises(ValueError):
            guarded_environment(self.connection(user='production_admin'), target=True)

    def test_alternate_server_is_rejected(self):
        with self.assertRaises(ValueError):
            guarded_environment(self.connection(host='other.postgres.database.azure.com'), target=True)

    def test_inherited_connection_redirects_are_discarded(self):
        with patch.dict(os.environ, {'PGDATABASE': 'postgres', 'PGHOSTADDR': '127.0.0.1',
                                    'PGSERVICE': 'production', 'PGOPTIONS': '-c search_path=other'}):
            env = guarded_environment(self.connection(), target=True)
        self.assertEqual(env['PGDATABASE'], 'campusflow_nonprod')
        self.assertNotIn('PGHOSTADDR', env)
        self.assertNotIn('PGSERVICE', env)
        self.assertNotIn('PGOPTIONS', env)

    def test_quoted_semicolon_does_not_change_destination(self):
        env = guarded_environment(self.connection(), target=True)
        self.assertEqual(env['PGPASSWORD'], 'test;password')
        self.assertEqual(env['PGDATABASE'], 'campusflow_nonprod')

    def test_unterminated_quote_is_rejected(self):
        with self.assertRaises(ValueError):
            connection_fields('Host="unclosed')


if __name__ == '__main__':
    unittest.main()
