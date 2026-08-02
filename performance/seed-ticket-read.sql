\set ON_ERROR_STOP on

DO $guard$
BEGIN
    IF current_database() <> 'VS_HelpDesk_Perf' THEN
        RAISE EXCEPTION
            'Refusing ticket-read fixture mutation in database %; expected VS_HelpDesk_Perf',
            current_database();
    END IF;
END
$guard$;

BEGIN;

SELECT pg_advisory_xact_lock(86020802);

DO $identity_guard$
BEGIN
    IF EXISTS (
        WITH performance_users AS (
            SELECT CASE
                WHEN user_sequence = 1 THEN 'perf-admin'
                ELSE 'perf-user-' || to_char(user_sequence, 'FM000')
            END AS username
            FROM generate_series(1, 100) AS users(user_sequence)
        )
        SELECT 1
        FROM "Users" AS existing_user
        INNER JOIN performance_users
            ON performance_users.username = existing_user."Username"
        WHERE existing_user."Id" <> (
            substr(md5(performance_users.username), 1, 8) || '-' ||
            substr(md5(performance_users.username), 9, 4) || '-' ||
            substr(md5(performance_users.username), 13, 4) || '-' ||
            substr(md5(performance_users.username), 17, 4) || '-' ||
            substr(md5(performance_users.username), 21, 12)
        )::uuid)
    THEN
        RAISE EXCEPTION
            'Refusing ticket-read fixture: an existing performance username has a non-deterministic UUID';
    END IF;
END
$identity_guard$;

TRUNCATE TABLE
    "TicketAttachments",
    "TicketMessages",
    "ProcessedEmailMessages",
    "Tickets"
RESTART IDENTITY CASCADE;

WITH performance_users AS (
    SELECT
        user_sequence,
        CASE
            WHEN user_sequence = 1 THEN 'perf-admin'
            ELSE 'perf-user-' || to_char(user_sequence, 'FM000')
        END AS username
    FROM generate_series(1, 100) AS users(user_sequence)
)
INSERT INTO "Users" (
    "Id",
    "FullName",
    "Username",
    "Email",
    "PasswordHash",
    "Role",
    "IsActive",
    "CreatedAt",
    "LastLoginAt")
SELECT
    (
        substr(md5(username), 1, 8) || '-' ||
        substr(md5(username), 9, 4) || '-' ||
        substr(md5(username), 13, 4) || '-' ||
        substr(md5(username), 17, 4) || '-' ||
        substr(md5(username), 21, 12)
    )::uuid,
    CASE
        WHEN user_sequence = 1 THEN 'Performance Administrator'
        ELSE 'Performance Support User ' || to_char(user_sequence, 'FM000')
    END,
    username,
    CASE
        WHEN user_sequence = 1 THEN 'perf-user-001@example.invalid'
        ELSE username || '@example.invalid'
    END,
    CASE
        WHEN user_sequence = 1 THEN
            'AQAAAAIAAYagAAAAEH19eWisSBiBBiS4PgCPGoHn4odr4nvfKzN3win7wJ4DVsUCrWY7oA+IL5degKZrPg=='
        ELSE 'non-login-performance-fixture-hash-' || to_char(user_sequence, 'FM000')
    END,
    CASE WHEN user_sequence = 1 THEN 1 ELSE 0 END,
    true,
    TIMESTAMPTZ '2026-01-01 00:00:00+00' + make_interval(secs => user_sequence),
    NULL
FROM performance_users
ON CONFLICT ("Username") DO UPDATE
SET
    "FullName" = EXCLUDED."FullName",
    "Email" = EXCLUDED."Email",
    "PasswordHash" = EXCLUDED."PasswordHash",
    "Role" = EXCLUDED."Role",
    "IsActive" = EXCLUDED."IsActive",
    "CreatedAt" = EXCLUDED."CreatedAt",
    "LastLoginAt" = EXCLUDED."LastLoginAt";

WITH ticket_source AS (
    SELECT
        ticket_sequence,
        to_char(ticket_sequence, 'FM000000') AS padded_sequence,
        ((ticket_sequence - 1) % 4) + 1 AS status,
        ((ticket_sequence - 1) % 100) + 1 AS assigned_user_sequence,
        TIMESTAMPTZ '2025-01-01 00:00:00+00'
            + make_interval(days => ticket_sequence % 365, mins => ticket_sequence % 1440)
            AS created_at
    FROM generate_series(1, 100000) AS tickets(ticket_sequence)
),
ticket_rows AS (
    SELECT
        ticket_source.*,
        created_at + make_interval(mins => (ticket_sequence % 720) + 1) AS last_activity_at,
        CASE
            WHEN assigned_user_sequence = 1 THEN 'perf-admin'
            ELSE 'perf-user-' || to_char(assigned_user_sequence, 'FM000')
        END AS assigned_username
    FROM ticket_source
)
INSERT INTO "Tickets" (
    "Id",
    "TicketNumber",
    "Subject",
    "CustomerName",
    "CustomerEmail",
    "Status",
    "AssignedUserId",
    "WaitingCustomerSince",
    "CreatedAt",
    "UpdatedAt",
    "ResolvedAt",
    "LastActivityAt",
    "ClosedByUserId")
SELECT
    (
        substr(md5('perf-ticket-' || padded_sequence), 1, 8) || '-' ||
        substr(md5('perf-ticket-' || padded_sequence), 9, 4) || '-' ||
        substr(md5('perf-ticket-' || padded_sequence), 13, 4) || '-' ||
        substr(md5('perf-ticket-' || padded_sequence), 17, 4) || '-' ||
        substr(md5('perf-ticket-' || padded_sequence), 21, 12)
    )::uuid,
    'VS-PERF-' || padded_sequence,
    'Performance issue ' || padded_sequence,
    'Performance Customer ' || padded_sequence,
    'perf-customer-' || padded_sequence || '@example.invalid',
    status,
    (
        substr(md5(assigned_username), 1, 8) || '-' ||
        substr(md5(assigned_username), 9, 4) || '-' ||
        substr(md5(assigned_username), 13, 4) || '-' ||
        substr(md5(assigned_username), 17, 4) || '-' ||
        substr(md5(assigned_username), 21, 12)
    )::uuid,
    CASE WHEN status = 2 THEN last_activity_at - INTERVAL '30 minutes' ELSE NULL END,
    created_at,
    last_activity_at,
    CASE WHEN status = 4 THEN last_activity_at ELSE NULL END,
    last_activity_at,
    CASE
        WHEN status = 4 THEN (
            substr(md5(assigned_username), 1, 8) || '-' ||
            substr(md5(assigned_username), 9, 4) || '-' ||
            substr(md5(assigned_username), 13, 4) || '-' ||
            substr(md5(assigned_username), 17, 4) || '-' ||
            substr(md5(assigned_username), 21, 12)
        )::uuid
        ELSE NULL
    END
FROM ticket_rows;

WITH message_source AS (
    SELECT
        ticket_sequence,
        message_position,
        to_char(ticket_sequence, 'FM000000') AS padded_ticket_sequence,
        ((ticket_sequence + message_position - 2) % 100) + 1 AS support_user_sequence
    FROM generate_series(1, 100000) AS tickets(ticket_sequence)
    CROSS JOIN generate_series(1, 5) AS positions(message_position)
),
message_rows AS (
    SELECT
        message_source.*,
        'perf-message-' || padded_ticket_sequence || '-' || message_position AS message_identity,
        CASE
            WHEN support_user_sequence = 1 THEN 'perf-admin'
            ELSE 'perf-user-' || to_char(support_user_sequence, 'FM000')
        END AS support_username
    FROM message_source
)
INSERT INTO "TicketMessages" (
    "Id",
    "TicketId",
    "SenderType",
    "UserId",
    "Content",
    "IsHtml",
    "CreatedAt")
SELECT
    (
        substr(md5(message_identity), 1, 8) || '-' ||
        substr(md5(message_identity), 9, 4) || '-' ||
        substr(md5(message_identity), 13, 4) || '-' ||
        substr(md5(message_identity), 17, 4) || '-' ||
        substr(md5(message_identity), 21, 12)
    )::uuid,
    (
        substr(md5('perf-ticket-' || padded_ticket_sequence), 1, 8) || '-' ||
        substr(md5('perf-ticket-' || padded_ticket_sequence), 9, 4) || '-' ||
        substr(md5('perf-ticket-' || padded_ticket_sequence), 13, 4) || '-' ||
        substr(md5('perf-ticket-' || padded_ticket_sequence), 17, 4) || '-' ||
        substr(md5('perf-ticket-' || padded_ticket_sequence), 21, 12)
    )::uuid,
    CASE WHEN message_position % 2 = 1 THEN 1 ELSE 2 END,
    CASE
        WHEN message_position % 2 = 1 THEN NULL
        ELSE (
            substr(md5(support_username), 1, 8) || '-' ||
            substr(md5(support_username), 9, 4) || '-' ||
            substr(md5(support_username), 13, 4) || '-' ||
            substr(md5(support_username), 17, 4) || '-' ||
            substr(md5(support_username), 21, 12)
        )::uuid
    END,
    'Performance fixture message ' || message_position ||
        ' for ticket ' || padded_ticket_sequence,
    false,
    TIMESTAMPTZ '2025-01-01 00:00:00+00'
        + make_interval(days => ticket_sequence % 365, mins => message_position)
FROM message_rows;

WITH attachment_source AS (
    SELECT
        message_ordinal,
        message_ordinal / 10 AS attachment_sequence,
        ((message_ordinal - 1) / 5) + 1 AS ticket_sequence,
        ((message_ordinal - 1) % 5) + 1 AS message_position
    FROM generate_series(10, 500000, 10) AS messages(message_ordinal)
),
attachment_rows AS (
    SELECT
        attachment_source.*,
        to_char(ticket_sequence, 'FM000000') AS padded_ticket_sequence,
        to_char(attachment_sequence, 'FM000000') AS padded_attachment_sequence
    FROM attachment_source
)
INSERT INTO "TicketAttachments" (
    "Id",
    "TicketMessageId",
    "FileName",
    "StoredFileName",
    "FilePath",
    "ContentType",
    "FileSize",
    "CreatedAt")
SELECT
    (
        substr(md5('perf-attachment-' || padded_attachment_sequence), 1, 8) || '-' ||
        substr(md5('perf-attachment-' || padded_attachment_sequence), 9, 4) || '-' ||
        substr(md5('perf-attachment-' || padded_attachment_sequence), 13, 4) || '-' ||
        substr(md5('perf-attachment-' || padded_attachment_sequence), 17, 4) || '-' ||
        substr(md5('perf-attachment-' || padded_attachment_sequence), 21, 12)
    )::uuid,
    (
        substr(md5('perf-message-' || padded_ticket_sequence || '-' || message_position), 1, 8) || '-' ||
        substr(md5('perf-message-' || padded_ticket_sequence || '-' || message_position), 9, 4) || '-' ||
        substr(md5('perf-message-' || padded_ticket_sequence || '-' || message_position), 13, 4) || '-' ||
        substr(md5('perf-message-' || padded_ticket_sequence || '-' || message_position), 17, 4) || '-' ||
        substr(md5('perf-message-' || padded_ticket_sequence || '-' || message_position), 21, 12)
    )::uuid,
    'performance-attachment-' || padded_attachment_sequence || '.bin',
    'performance-attachment-' || padded_attachment_sequence || '.bin',
    'perf-fixture/not-a-real-file',
    'application/octet-stream',
    CASE
        WHEN (attachment_sequence - 1) % 20 < 16 THEN 65536
        WHEN (attachment_sequence - 1) % 20 < 19 THEN 262144
        ELSE 5242880
    END,
    TIMESTAMPTZ '2025-01-01 00:00:00+00'
        + make_interval(days => ticket_sequence % 365, mins => message_position)
FROM attachment_rows;

ANALYZE "Tickets";
ANALYZE "TicketMessages";
ANALYZE "TicketAttachments";

DO $verify$
DECLARE
    performance_user_count bigint;
    ticket_count bigint;
    message_count bigint;
    attachment_count bigint;
    small_attachment_count bigint;
    medium_attachment_count bigint;
    large_attachment_count bigint;
BEGIN
    SELECT count(*)
    INTO performance_user_count
    FROM "Users"
    WHERE "Username" = 'perf-admin'
       OR "Username" = ANY (
            ARRAY(
                SELECT 'perf-user-' || to_char(user_sequence, 'FM000')
                FROM generate_series(2, 100) AS users(user_sequence)));

    SELECT count(*) INTO ticket_count FROM "Tickets";
    SELECT count(*) INTO message_count FROM "TicketMessages";
    SELECT count(*) INTO attachment_count FROM "TicketAttachments";
    SELECT count(*) INTO small_attachment_count
        FROM "TicketAttachments" WHERE "FileSize" = 65536;
    SELECT count(*) INTO medium_attachment_count
        FROM "TicketAttachments" WHERE "FileSize" = 262144;
    SELECT count(*) INTO large_attachment_count
        FROM "TicketAttachments" WHERE "FileSize" = 5242880;

    IF performance_user_count <> 100
       OR ticket_count <> 100000
       OR message_count <> 500000
       OR attachment_count <> 50000 THEN
        RAISE EXCEPTION
            'Fixture count mismatch: users=%, tickets=%, messages=%, attachments=%',
            performance_user_count,
            ticket_count,
            message_count,
            attachment_count;
    END IF;

    IF small_attachment_count <> 40000
       OR medium_attachment_count <> 7500
       OR large_attachment_count <> 2500 THEN
        RAISE EXCEPTION
            'Attachment size distribution mismatch: small=%, medium=%, large=%',
            small_attachment_count,
            medium_attachment_count,
            large_attachment_count;
    END IF;
END
$verify$;

COMMIT;
