-- =============================================================
-- ServerCat Seed Data
-- Run after schema is applied (or migrations have been run)
-- =============================================================

-- ── Credential References ─────────────────────────────────────
INSERT INTO credential_references (id, name, description, auth_method, username_hint, secret_store_key, created_by)
VALUES
  ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'CORP WinRM Account', 'Domain service account for WinRM/PowerShell polling',
   'WinRmKerberos', 'CORP\svc-servercat-poll', 'vault:placeholder-replace-at-runtime', 'system'),
  ('b2c3d4e5-f6a7-8901-bcde-f12345678901', 'SNMP Read Community', 'SNMPv2 read-only community for network devices',
   'SnmpV2', null, 'vault:placeholder-replace-at-runtime-snmp', 'system')
ON CONFLICT (name) DO NOTHING;

-- ── Server Groups ─────────────────────────────────────────────
INSERT INTO server_groups (id, name, description, color)
VALUES
  ('c3d4e5f6-a7b8-9012-cdef-123456789012', 'Web Servers', 'IIS and application servers', '#3B82F6'),
  ('d4e5f6a7-b8c9-0123-defa-234567890123', 'Database Servers', 'SQL Server instances', '#8B5CF6'),
  ('e5f6a7b8-c9d0-1234-efab-345678901234', 'Domain Controllers', 'Active Directory DCs', '#EF4444'),
  ('f6a7b8c9-d0e1-2345-fabc-456789012345', 'DMZ Servers', 'Perimeter network servers', '#F59E0B')
ON CONFLICT (name) DO NOTHING;

-- ── Sample Servers ────────────────────────────────────────────
INSERT INTO servers (id, hostname, ip_address, display_name, environment, owner, site, criticality, os_type,
                     collector_type, credential_ref_id, polling_enabled, polling_interval_minutes, tags, created_by)
VALUES
  ('11111111-1111-1111-1111-111111111111',
   'APP-PROD-01', '10.0.1.10', 'Primary App Server',
   'Production', 'Platform Engineering', 'DC-East', 'Critical', 'Windows',
   'winrm', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
   true, 60, ARRAY['web','iis','production'], 'system'),

  ('22222222-2222-2222-2222-222222222222',
   'APP-PROD-02', '10.0.1.11', 'Secondary App Server',
   'Production', 'Platform Engineering', 'DC-East', 'High', 'Windows',
   'winrm', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
   true, 60, ARRAY['web','iis','production'], 'system'),

  ('33333333-3333-3333-3333-333333333333',
   'SQL-PROD-01', '10.0.1.20', 'Primary SQL Server',
   'Production', 'DBA Team', 'DC-East', 'Critical', 'Windows',
   'winrm', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
   true, 30, ARRAY['sql','database','production'], 'system'),

  ('44444444-4444-4444-4444-444444444444',
   'DC-01', '10.0.1.5', 'Primary Domain Controller',
   'Production', 'Identity Team', 'DC-East', 'Critical', 'Windows',
   'winrm', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
   true, 30, ARRAY['dc','ad','identity'], 'system'),

  ('55555555-5555-5555-5555-555555555555',
   'APP-STAGING-01', '10.0.2.10', 'Staging App Server',
   'Staging', 'Platform Engineering', 'DC-East', 'Medium', 'Windows',
   'winrm', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
   true, 120, ARRAY['web','iis','staging'], 'system'),

  ('66666666-6666-6666-6666-666666666666',
   'DMZ-WEB-01', '192.168.1.50', 'DMZ Web Server',
   'Production', 'Network Team', 'DC-East', 'High', 'Windows',
   'winrm', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
   true, 60, ARRAY['web','dmz','iis'], 'system')
ON CONFLICT (hostname) DO NOTHING;

-- ── Group Memberships ─────────────────────────────────────────
INSERT INTO server_group_memberships (server_id, group_id, added_by)
VALUES
  ('11111111-1111-1111-1111-111111111111', 'c3d4e5f6-a7b8-9012-cdef-123456789012', 'system'),
  ('22222222-2222-2222-2222-222222222222', 'c3d4e5f6-a7b8-9012-cdef-123456789012', 'system'),
  ('33333333-3333-3333-3333-333333333333', 'd4e5f6a7-b8c9-0123-defa-234567890123', 'system'),
  ('44444444-4444-4444-4444-444444444444', 'e5f6a7b8-c9d0-1234-efab-345678901234', 'system'),
  ('66666666-6666-6666-6666-666666666666', 'c3d4e5f6-a7b8-9012-cdef-123456789012', 'system'),
  ('66666666-6666-6666-6666-666666666666', 'f6a7b8c9-d0e1-2345-fabc-456789012345', 'system')
ON CONFLICT (server_id, group_id) DO NOTHING;

-- ── Sample Notes ──────────────────────────────────────────────
INSERT INTO notes (id, server_id, content, is_pinned, created_by)
VALUES
  (gen_random_uuid(), '33333333-3333-3333-3333-333333333333',
   '## SQL Server Maintenance Window

Scheduled: **Every Sunday 02:00–04:00 UTC**

- Index rebuild runs automatically via SQL Agent
- Log backups every 15 minutes
- Full backup nightly at 01:00

Contact: dba-team@corp.local for emergency changes.',
   true, 'system'),

  (gen_random_uuid(), '44444444-4444-4444-4444-444444444444',
   '## Domain Controller Notes

This is the **primary PDC emulator**. All password changes process through this DC.

Do not reboot without notifying identity-team@corp.local.',
   true, 'system')
ON CONFLICT DO NOTHING;

-- ── Operational Log seed entries ──────────────────────────────
INSERT INTO operational_logs (server_id, event_type, severity, summary, details, actor)
VALUES
  ('11111111-1111-1111-1111-111111111111', 'server.created', 'Info',
   'Server APP-PROD-01 added to catalog',
   '{"hostname": "APP-PROD-01", "environment": "Production"}'::jsonb, 'system'),

  ('22222222-2222-2222-2222-222222222222', 'server.created', 'Info',
   'Server APP-PROD-02 added to catalog',
   '{"hostname": "APP-PROD-02", "environment": "Production"}'::jsonb, 'system'),

  ('33333333-3333-3333-3333-333333333333', 'server.created', 'Info',
   'Server SQL-PROD-01 added to catalog',
   '{"hostname": "SQL-PROD-01", "environment": "Production"}'::jsonb, 'system'),

  ('44444444-4444-4444-4444-444444444444', 'server.created', 'Info',
   'Server DC-01 added to catalog',
   '{"hostname": "DC-01", "environment": "Production"}'::jsonb, 'system'),

  ('55555555-5555-5555-5555-555555555555', 'server.created', 'Info',
   'Server APP-STAGING-01 added to catalog',
   '{"hostname": "APP-STAGING-01", "environment": "Staging"}'::jsonb, 'system'),

  ('66666666-6666-6666-6666-666666666666', 'server.created', 'Info',
   'Server DMZ-WEB-01 added to catalog',
   '{"hostname": "DMZ-WEB-01", "environment": "Production"}'::jsonb, 'system');

-- ── App Users (local auth mode only) ─────────────────────────
-- Password hashes: use bcrypt $2a$12$...
-- Default password for ALL seed users: "ChangeMe123!"
-- IMPORTANT: Change all passwords before production use.
INSERT INTO app_users (id, username, display_name, role, password_hash, is_active, created_by)
VALUES
  (gen_random_uuid(), 'admin', 'System Administrator', 'Admin',
   '$2a$12$SeedHashChangeBeforeProductionUseadminpassword', true, 'system'),
  (gen_random_uuid(), 'engineer', 'Platform Engineer', 'Engineer',
   '$2a$12$SeedHashChangeBeforeProductionUseengineerpass', true, 'system'),
  (gen_random_uuid(), 'viewer', 'Read Only User', 'Viewer',
   '$2a$12$SeedHashChangeBeforeProductionUseviewerpasswd', true, 'system')
ON CONFLICT (username) DO NOTHING;

SELECT 'Seed data loaded successfully.' AS status;
