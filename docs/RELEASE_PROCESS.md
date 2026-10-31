# Release process

Local build: run `installer/scripts/build-installer.ps1` on Windows with .NET 10 SDK and Inno Setup 7. Check the generated installer and SHA-256 checksum before distribution.

Public releases are not performed by this repository setup. Before enabling them, configure protected GitHub environment approval, Authenticode credentials, Ed25519 signing secrets, a pinned public key, NetSparkle feed hosting, and signature validation. Never commit private signing material.
