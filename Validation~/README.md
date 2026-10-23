# GFramework validation

`validate-package.ps1` checks assembly names/references and can optionally invoke the Unity Addressables validator. It does not claim Unity compilation or PlayMode validation.

For the repository Unity host project:

```powershell
./Validation~/validate-package.ps1 `
  -UnityPath 'C:/Program Files/Unity/Hub/Editor/2022.3.x/Editor/Unity.exe' `
  -UnityProjectPath 'D:/Projects/MyUnityHost'
```

The script invokes the Starter Addressables validator. The host owns Addressables Settings and builds local content. Unity EditMode/PlayMode and Player tests remain host-project responsibilities; no license, credentials, or success is fabricated by this package script.

## Common module tests

`validate-common.ps1 -UnityPath '<Unity.exe>'` runs the host's `GFramework.Common.Tests` EditMode assembly and verifies a fresh NUnit XML report, including a nonzero test count. Import TMP Essential Resources in the host before testing. Results and editor logs are written under the host's ignored `Logs` folder.

`-Isolated` creates a temporary source-only Unity project containing a copy of Core, the tests, and the host's cached uGUI/TMP/Test Framework/NUnit sources. It imports TMP Essential Resources and runs with `-noUpm`, allowing actual Unity tests when the local package manager is broken. The original host and package dependencies are unchanged; the temporary host is retained for inspection. This verifies source compilation and behavior, not UPM installation or the original host's full integration. `-UseCachedPackages` alone is intended for such a prepared source host; it cannot load packages from a normal UPM project.

Tests cover timers/sequences, state transitions, typed events, RuntimeScope ownership, frame group pause/unscaled time, virtual list reuse, pointer interactions, tooltip ownership and existing asynchronous button submission guards. One EditMode coroutine temporarily enters PlayMode to verify real disable/destroy callbacks. Player builds and device interaction still require separate host validation.

Verified on 2026-10-23 with Unity 2022.3.62f3 using `validate-common.ps1 -Isolated`: **38/38 passed**, including the PlayMode lifecycle coroutine. Assembly boundary/cycle checks passed. The original 2022.3.62f3c1 host could not resolve packages (`The "path" argument must be of type string. Received undefined`), so its UPM integration and full scene flow were not validated. The local result copy is `UnityProject~/Logs/common-validation-final.xml` (ignored by Git).
