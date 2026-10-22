# GFramework validation

`validate-package.ps1` checks assembly names/references and can optionally invoke the Unity Addressables validator. It does not claim Unity compilation or PlayMode validation.

For the repository Unity host project:

```powershell
./Validation~/validate-package.ps1 `
  -UnityPath 'C:/Program Files/Unity/Hub/Editor/2022.3.x/Editor/Unity.exe' `
  -UnityProjectPath 'D:/Projects/MyUnityHost'
```

The script invokes the Starter Addressables validator. The host owns Addressables Settings and builds local content. Unity EditMode/PlayMode and Player tests remain host-project responsibilities; no license, credentials, or success is fabricated by this package script.
