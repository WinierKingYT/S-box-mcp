# Security Policy

## Reporting a vulnerability

Do not disclose security vulnerabilities in a public issue.

Until a GitHub Security Advisory contact is configured, report the issue
privately to the repository owner and include:

- affected version and operating system;
- reproduction steps;
- expected and observed behavior;
- whether API keys, filesystem access, or scene mutation is involved.

Do not include real API keys or private project data.

## Security boundaries

- The server is designed for loopback-only access.
- Every MCP endpoint except `/health` requires `x-api-key`.
- Console command execution is disabled by default.
- Screenshot paths are constrained to the configured output directory.
- Scene mutations require editor approval by default.
- Unknown extension tools default to the high-risk approval class.
- Persistent activity logs contain sanitized summaries, not raw request payloads,
  headers, or API keys.
- AI undo/redo tools refuse to alter newer non-MCP history entries.

Internet exposure and OAuth are not supported security boundaries in v1.2.
