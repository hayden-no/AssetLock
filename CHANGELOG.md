## Changelog

### 1.1.0

- Added a UI Toolkit Lock Browser, with an option to use the legacy browser. Both browsers support case-insensitive wildcard search.
- Added a warning mode that displays lock status in the Inspector while allowing asset operations. Auto-lock remains available, with an optional dialog when it fails.
- Added optional prompts to unlock owned files after push attempts to configured branches. Prompts can be suppressed without unlocking files.
- Added project-specific credential identities, reusable account profiles, and support for Git's configured credential helper.
- Improved browser status reporting and paused automatic refresh after failures to prevent repeated credential prompts.
- Added unit and Git integration tests for browser, locking, credentials, and push-hook behavior.
- Various improvements and bug fixes.

### 1.0.0

- Initial release
