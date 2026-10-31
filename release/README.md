# Local release build

Run `./installer/scripts/build-installer.ps1` from PowerShell on Windows with the .NET 10 SDK and Inno Setup 7 installed. The script publishes a self-contained x64 WPF build, then compiles `NexusPC-Setup-x64.exe` under `artifacts/installer`.

This development installer is unsigned. Production signing and public release remain disabled until release credentials and authorization are supplied. Ollama is not bundled. The installer leaves `%APPDATA%` and `%LOCALAPPDATA%` user data in place when uninstalled.
