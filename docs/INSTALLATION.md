# Installation

The local installer targets Windows x64 and installs the self-contained application under Program Files. Ollama and AI models are separate dependencies and are never bundled. User data belongs in per-user AppData locations, outside the install directory, so app upgrades do not replace it. The application catalog requires Windows Package Manager (WinGet) and internet access to install packages; browsing the built-in list does not install anything.

For a local installer build, see [release/README.md](../release/README.md). Unsigned local builds can show Windows publisher warnings. The current installer installs only application files; it does not include Ollama or a privileged helper.
