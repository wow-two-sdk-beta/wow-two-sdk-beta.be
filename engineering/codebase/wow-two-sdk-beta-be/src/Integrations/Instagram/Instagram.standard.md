# Instagram client contract

- MUST send credentials only in the Authorization header.
- MUST validate the configured HTTPS Graph origin before obtaining credentials.
- MUST reject invalid version, querying account, username and cursor inputs locally.
- MUST make at most one transport attempt per call through the supplied registration.
- MUST bound response bytes, JSON depth, page size and total call duration.
- MUST propagate caller cancellation without retrying.
- MUST NOT follow redirects or provider paging URLs.
- MUST obtain continuation from the cursor, without requiring a next URL.
- MUST reject repeated or malformed continuation cursors on nonempty pages.
- MUST terminate empty pages without interpreting their paging metadata.
- MUST omit credentials, full query strings and provider error text from diagnostics.
- MUST leave authorization policy, schedules, ingestion and persistence to consumers.
