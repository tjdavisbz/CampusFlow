#!/usr/bin/env python3
"""One-way CampusFlow PostgreSQL refresh: production -> nonproduction only.

No direction/connection overrides are accepted. Requires Azure CLI authentication,
PostgreSQL 18+ client tools, and nonproduction maintenance mode enabled.
Preview is the default; --apply performs the refresh. Credentials never go in argv.
"""
import argparse
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import tempfile

SUBSCRIPTION = '3cc0ce6b-9bac-4bc8-81c2-8b22bd8d5059'
GROUP = 'rg_campusflow'
SOURCE_APP = 'app-campusflow-prod-001'
TARGET_APP = 'app-campusflow-nonprod-001'
HOST = 'psql-campusflow-prod-001.postgres.database.azure.com'
SOURCE_DATABASE = 'postgres'
TARGET_DATABASE = 'campusflow_nonprod'
TARGET_ROLE = 'campusflow_nonprod_app'


def connection_fields(value):
    """Parse quoted ADO.NET connection fields without exposing their values."""
    fields = {}
    index = 0
    while index < len(value):
        while index < len(value) and (value[index].isspace() or value[index] == ';'):
            index += 1
        if index == len(value):
            break
        equals = value.find('=', index)
        if equals < 0:
            raise ValueError('Invalid connection-string field')
        key = value[index:equals].strip().lower()
        index = equals + 1
        while index < len(value) and value[index].isspace():
            index += 1
        text = ''
        if index < len(value) and value[index] in ['"', "'"]:
            quote = value[index]
            index += 1
            while index < len(value):
                char = value[index]
                index += 1
                if char == quote:
                    if index < len(value) and value[index] == quote:
                        text += quote
                        index += 1
                    else:
                        break
                else:
                    text += char
            else:
                raise ValueError('Unclosed quoted connection value')
            while index < len(value) and value[index].isspace():
                index += 1
            if index < len(value) and value[index] != ';':
                raise ValueError('Invalid quoted connection value')
        else:
            end = value.find(';', index)
            if end < 0:
                end = len(value)
            text = value[index:end].strip()
            index = end
        fields[key] = text
    return fields


def az(*args):
    result = subprocess.run(['az', *args, '--subscription', SUBSCRIPTION,
                             '--only-show-errors', '-o', 'json'], capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError('Azure operation failed; credential-bearing output withheld')
    return json.loads(result.stdout) if result.stdout.strip() else None


def setting(name):
    return az('webapp', 'config', 'appsettings', 'list', '-g', GROUP, '-n', TARGET_APP,
              '--query', f"[?name=='{name}'].value | [0]")


def guarded_environment(connection, target=False):
    fields = connection_fields(connection)
    database = TARGET_DATABASE if target else SOURCE_DATABASE
    if fields.get('host') != HOST or fields.get('database') != database:
        raise ValueError('Database direction guard failed; no dump or restore allowed')
    user = fields.get('username', fields.get('user id'))
    if not user or not fields.get('password'):
        raise ValueError('Required database credentials missing')
    if target and user != TARGET_ROLE:
        raise ValueError('Restore must use the dedicated nonproduction role')
    if not target and user == TARGET_ROLE:
        raise ValueError('Nonproduction credentials cannot be a source')
    # Discard inherited PG variables, including alternate service/connection targets.
    env = {k: v for k, v in os.environ.items() if not k.startswith('PG')}
    env.update(PGHOST=HOST, PGPORT=fields.get('port', '5432'), PGDATABASE=database,
               PGUSER=user, PGPASSWORD=fields['password'], PGSSLMODE='verify-full',
               PGSSLROOTCERT='system', PGCONNECT_TIMEOUT='20', PGAPPNAME='CampusFlow-prod-to-nonprod-refresh')
    if not target:
        env['PGOPTIONS'] = '-c default_transaction_read_only=on -c lock_timeout=10000'
    return env


def client(name):
    executable = shutil.which(name)
    fallback = Path('/opt/homebrew/opt/libpq/bin') / name
    if not executable and fallback.exists():
        executable = str(fallback)
    if not executable:
        raise RuntimeError(f'PostgreSQL client {name} is required')
    return executable


def query(env, sql):
    result = subprocess.run([client('psql'), '-X', '-A', '-t', '-v', 'ON_ERROR_STOP=1',
                             '-c', sql], env=env, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError('Database query failed; private output withheld')
    return result.stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    source = az('webapp', 'config', 'connection-string', 'list', '-g', GROUP,
                '-n', SOURCE_APP, '--query', "[?name=='Default'].value | [0]")
    source_env = guarded_environment(source)
    target_env = guarded_environment(setting('ConnectionStrings__Default'), target=True)
    if query(source_env, "SELECT current_database() || '|' || current_setting('transaction_read_only')") != SOURCE_DATABASE + '|on':
        raise RuntimeError('Source identity/read-only verification failed')
    if query(target_env, "SELECT current_database() || '|' || current_user") != TARGET_DATABASE + '|' + TARGET_ROLE:
        raise RuntimeError('Restore target identity verification failed')
    # The restore role must not be capable of modifying the production app tables.
    restricted_source = {**target_env, 'PGDATABASE': SOURCE_DATABASE}
    write_access = query(restricted_source, "SELECT count(*) FROM pg_tables WHERE schemaname='public' AND (has_table_privilege(current_user,quote_ident(schemaname)||'.'||quote_ident(tablename),'INSERT') OR has_table_privilege(current_user,quote_ident(schemaname)||'.'||quote_ident(tablename),'UPDATE') OR has_table_privilege(current_user,quote_ident(schemaname)||'.'||quote_ident(tablename),'DELETE') OR pg_has_role(current_user,tableowner,'MEMBER'))")
    if write_access != '0':
        raise RuntimeError('Restore-role production-write isolation check failed')
    can_create = query(restricted_source, "SELECT has_database_privilege(current_user,current_database(),'CREATE') OR has_schema_privilege(current_user,'public','CREATE')")
    if can_create != 'f':
        raise RuntimeError('Restore role must not create production schemas or objects')
    if str(setting('MaintenanceMode__Enabled')).lower() != 'true':
        raise RuntimeError('Enable NONPRODUCTION maintenance mode before refreshing')
    print(f'Verified one-way refresh: {SOURCE_APP}/{SOURCE_DATABASE} -> {TARGET_APP}/{TARGET_DATABASE}')
    print('Source read-only; restore role has no production table write/ownership privileges.')
    if not args.apply:
        print('Preview only. Use --apply to replace only the dedicated nonproduction database contents.')
        return
    az('webapp', 'stop', '-g', GROUP, '-n', TARGET_APP)
    print('Stopped only the nonproduction app for a consistent restore and validation.')
    os.umask(0o077)
    evidence = Path(tempfile.mkdtemp(prefix='campusflow-prod-to-nonprod-'))
    dump = evidence / 'production.dump'
    rollback = evidence / 'nonproduction-before.dump'
    log = evidence / 'private-restore.log'
    with log.open('w') as output:
        def run(command, env):
            result = subprocess.run(command, env=env, stdout=output, stderr=subprocess.STDOUT)
            if result.returncode:
                raise RuntimeError(f'Refresh stopped; private diagnostics and rollback dump: {evidence}')
        run([client('pg_dump'), '--format=custom', '--no-owner', '--no-acl', '--exclude-extension=azure', '--exclude-extension=pgaadauth', '--file', str(rollback)], target_env)
        run([client('pg_dump'), '--format=custom', '--no-owner', '--no-acl', '--exclude-extension=azure', '--exclude-extension=pgaadauth', '--file', str(dump)], source_env)
        # No connection overrides, --create, or production credentials reach pg_restore.
        run([client('pg_restore'), '--dbname', TARGET_DATABASE, '--clean', '--if-exists',
             '--no-owner', '--no-acl', '--exit-on-error', '--single-transaction', str(dump)], target_env)
    # Count rows from the captured source dump without printing any record contents.
    expected = {}
    table = None
    extraction = subprocess.Popen([client('pg_restore'), '--data-only', '--file=-', str(dump)],
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    for line in extraction.stdout:
        if table is not None:
            if line.rstrip('\n') == r'\.':
                table = None
            else:
                expected[table] += 1
        else:
            match = re.match(r'^COPY public\.(?:"((?:[^"]|"")+)"|(\w+)) \(', line)
            if match:
                table = (match.group(1) or match.group(2)).replace('""', '"')
                expected[table] = 0
    extraction.communicate()
    if extraction.returncode or not expected:
        raise RuntimeError('Source snapshot row-count extraction failed')
    def literal(value):
        return "'" + value.replace("'", "''") + "'"
    def identifier(value):
        return '"' + value.replace('"', '""') + '"'
    counts_sql = ' UNION ALL '.join('SELECT ' + literal(name) + ' || \'|\' || count(*) FROM public.' + identifier(name) for name in expected)
    actual = dict(line.rsplit('|', 1) for line in query(target_env, counts_sql).splitlines())
    if any(int(actual.get(name, -1)) != count for name, count in expected.items()):
        raise RuntimeError('Restored row counts differ from the captured production snapshot')
    print('Verified snapshot row counts for tables:', len(expected))
    # Retain tenant identity, but never retain a per-tenant production DB override.
    tenant_overrides = query(target_env, 'SELECT count(*) FROM "AbpTenantConnectionStrings"')
    query(target_env, 'DELETE FROM "AbpTenantConnectionStrings"')
    print('Production snapshot restored successfully into nonproduction only.')
    print('Removed nonproduction tenant connection overrides:', tenant_overrides)
    az('webapp', 'start', '-g', GROUP, '-n', TARGET_APP)
    print('Restarted only the nonproduction app; maintenance remains enabled.')
    print('Private snapshot and pre-refresh rollback files:', evidence)


if __name__ == '__main__':
    main()
