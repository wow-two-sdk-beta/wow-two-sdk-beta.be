-- 001-distributed-cache — drops the cache table and its index; cached entries are lost, which a cache tolerates.

DROP TABLE IF EXISTS {{schema}}.distributed_cache;
