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
