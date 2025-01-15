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