-- 001-distributed-cache — the distributed cache's entries, one row per key.
--
-- PostgresDistributedCache reads, refreshes and sweeps this table; every value it sends is a parameter. `expires_at` is
-- the instant a read stops seeing the row: the absolute expiration, or the last access plus the sliding window, capped
-- by the absolute one. The sweep deletes rows past it through `ix_distributed_cache_expires_at`. A host may rename the
-- table (`Caching:Postgres:TableName`); its broker then rewrites the bare table name below before the engine reads it.

CREATE TABLE IF NOT EXISTS {{schema}}.distributed_cache (
    id                  TEXT COLLATE "C" NOT NULL,
    value               BYTEA            NOT NULL,
    expires_at          TIMESTAMPTZ      NOT NULL,
    sliding_expiration  INTERVAL,
    absolute_expiration TIMESTAMPTZ,

    CONSTRAINT pk_distributed_cache PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_distributed_cache_expires_at ON {{schema}}.distributed_cache (expires_at);
