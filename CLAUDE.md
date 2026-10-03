# Claude Instructions

Before implementing features, read:

- docs/vision.md

The project's core principle is:

> History is the desktop.

Prioritize:

- Timeline UI
- Application persistence
- Resume-from-history workflows

Avoid assuming:
- Traditional desktop paradigms
- Window managers
- Taskbars

Primary stack:
- C#
- .NET
- PowerShell integration

User interaction
- User does all commits
- When ready to commit something, stage it and provide commit message and how to test commit
- Verify code builds before handing back to user