## Asset Lock for Git-LFS

This is a simple package that is used to interface with Git-LFS inside the Unity editor.  Currently only binary 
files are considered to be lockable (as text based files can just be merged).

### Features
- Locking and unlocking assets in the Unity editor
- Checking the lock status of assets in the Unity editor
- Respect locked files by disabling the ability to modify them in the Unity editor
- Works using the Git and Git-LFS command line tools
  - Optionally use the Git-LFS http API for faster performance
- By default, operates asynchronously to avoid blocking the Unity editor

### Getting Started
1. Install the package
2. Configure the settings under 'Asset Lock' in both project settings and user preferences
3. Open the 'Asset Lock Browser' window from the 'Window' menu to manage locks
4. Lock and unlock assets using the 'Asset Lock' context menu in the project window

The Lock Browser uses UI Toolkit by default. In **Preferences > AssetLock**,
enable **Use Legacy Lock Browser** to switch to the original browser. Enable
**Warn Instead of Blocking** to allow edit, save, move and delete operations
when a lock is missing, unknown or owned by another user; warnings appear in the
Inspector for the selected asset. Auto Lock still attempts to acquire locks for
edits, saves, moves and deletes without blocking the operation. A moved or deleted
file may no longer be available by the time the asynchronous request runs.
Failed attempts show a dialog;
disable it using **Suppress Auto Lock Failure Dialogs** in Preferences.
Enforcement remains enabled by default.

To receive an unlock prompt for pushes, configure destination branch names in
**Project Settings > AssetLock > Push Unlock Prompt Branches** (defaults: `main`
and `master`). In **Preferences > AssetLock**, install the push hook and enable
**Prompt to Unlock on Push**. The hook preserves an existing pre-push hook; use
**Remove Push Hook** to restore it. If the hook was modified separately, removal
requires resolving the hook and backup manually. Git clients must run local hooks,
and the editor must be open to receive the prompt.

Git's pre-push hook reports an attempt before the push completes, not its result.
Check that the push succeeded before choosing **Unlock All My Locks**: this
releases every lock you own, including files outside the push. Closing the prompt
or choosing **Keep Locks** leaves them locked. **Don't show again** and the
**Suppress Push Unlock Prompts** preference always skip future prompts and keep
locks; they never authorize automatic unlocking. Notifications made while the
editor is closed are discarded.

Browser search ignores case and matches substrings. To use a wildcard, add `*`
for any number of characters: for example, `*.prefab` matches file names and
`Assets/*/House*` matches paths. Patterns containing `*` match the whole name,
path or owner; use a leading and trailing `*` for a match anywhere.

If a lock refresh fails, automatic refresh pauses to avoid repeatedly opening
credential prompts. Check your Git credentials, then select **Refresh** in the
Lock Browser to retry. HTTP errors do not automatically switch to the Git LFS
command line; configure the intended mode and its credentials in Preferences.

HTTP lock requests use the project's configured Git credential helper by default.
In **Preferences > AssetLock > Git Credentials**, enter the account username,
choose **Use Current LFS Server**, and select **Sign In and Test Connection**.
The helper handles sign-in and secret storage; AssetLock keeps only the account
name and server in user preferences. Background refreshes request credentials
noninteractively. The helper must be installed and configured, and its own storage
configuration determines how securely secrets are stored.

Use **Save Profile for Other Projects** to save an account reference, then
**Import Profile** in another project. Importing copies the reference: editing
or deleting a saved profile does not change projects that already imported it.
Project selections are tied to the local project path, so a moved or cloned
project needs its profile imported again. Account matching also depends on the
helper and Git's `credential.useHttpPath` configuration. Git LFS command-line
operations use Git's own credential configuration independently.
Lock ownership still uses the repository's Git `user.name`; the HTTP account
selection does not change Git configuration or commit identity.

For existing installations, disable **Use Git Credential Helper** and choose
**Import Previous Shared Credentials** to copy the old values to this project's
private preferences. Legacy credentials remain in Unity's user preferences,
which are not a credential vault. The original shared values remain available
for other projects to import.

### Tests

On Windows, run the deterministic EditMode suite from the repository root:

```powershell
./scripts/Run-AssetLockTests.ps1 -UnityEditor 'C:\path\to\Unity\2022.3.9f1\Editor\Unity.exe'
```

The runner creates a temporary Unity project, copies the package, and passes
`-assetLockTestMode`. That flag suppresses automatic manager startup and stores
AssetLock settings in memory. Do not use it for normal editor sessions. The
suite uses fake Git processes and a local HTTP server; it needs no Git remote or
credentials. XML results and editor logs are written to `Temp/AssetLockTestResults`
unless `-ResultsDirectory` is supplied.

Run the separate process integration tests with `-Integration`. They require Git,
Git LFS and Windows PowerShell, and create a disposable Git repository without a
remote. Unity 2022.3.9f1, its license, and the package dependencies must be available
to the runner. The first run may need Unity Package Manager's registry or cache.

For GitLab, invoke the script from a Windows shell runner and publish the XML:

```yaml
assetlock-tests:
  tags: [windows]
  script:
    - powershell -NoProfile -File scripts/Run-AssetLockTests.ps1 -UnityEditor "$env:UNITY_EDITOR" -ResultsDirectory "$env:CI_PROJECT_DIR/TestResults"
  artifacts:
    when: always
    reports:
      junit: TestResults/AssetLockUnit.junit.xml
    paths: [TestResults/]
```

For a real startup smoke test, use a disposable Windows account and a disposable
Unity project with the package installed. Launch Unity in batch mode without
`-assetLockTestMode`, first with fresh AssetLock preferences and then after
configuring Git for that account and rebooting AssetLock. Check that startup
completes without exceptions and callbacks become active only when ready. Repeat
under LocalSystem or with the VFX editor when those runner features are available.
