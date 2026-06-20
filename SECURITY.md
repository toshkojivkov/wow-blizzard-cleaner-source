# Security Policy

## Supported Use

WoW Blizzard Cleaner is intended for local cleanup of user-selected WoW, Blizzard, and Battle.net traces.

The project must not be used as:

- an anti-ban tool,
- an anti-detection tool,
- a hardware spoofing tool,
- an account evasion tool,
- a botting support tool,
- a tool for deleting Windows security/system logs.

## Safety Guarantees

- Scan-only actions do not delete anything.
- Cleanup requires user confirmation.
- Registry and file backups are created before deletion.
- System, Microsoft, Windows, System32, Driver, Service, Kernel, Riot, and Vanguard paths are protected.
- Access-denied errors are logged and skipped.

## Reporting Issues

If you find a safety issue, open a GitHub issue with:

- the action you performed,
- the target path or registry key,
- expected behavior,
- actual behavior,
- log output if available.

Do not include private account credentials, personal tokens, or sensitive machine identifiers in public issues.
