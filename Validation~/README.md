# GFramework validation

`validate-package.ps1` runs the two pure C# regression suites and checks assembly names/references. It does not claim Unity compilation or Addressables content validation.

For a host Unity project with the Combined sample imported:

```powershell
./Validation~/validate-package.ps1 `
  -UnityPath 'C:/Program Files/Unity/Hub/Editor/2022.3.x/Editor/Unity.exe' `
  -UnityProjectPath 'D:/Projects/MyUnityHost'
```

The script invokes `CombinedSampleAddressablesValidator.ValidateCommandLine`. The host owns Addressables Settings and must build local content separately. Unity EditMode/PlayMode and Player tests remain host-project responsibilities; no license, credentials, or success is fabricated by this package script.
