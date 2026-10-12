# Security

Do not post private recordings, transcripts, authentication tokens or personal settings in issues. Use GitHub's private vulnerability reporting for security problems: https://github.com/softyoda/dictee-locale/security/advisories/new.

The loopback server authenticates requests using a fresh per-launch token and binds to 127.0.0.1. It is not intended as a network service. Installer downloads and dependency updates are separate from offline inference. Model cleanup treats the transcript as data and applies heuristic guards; it is not a trusted decision system.

Release binaries are currently unsigned. Published SHA-256 files help verify download integrity; they are not a substitute for trusting the publisher.
