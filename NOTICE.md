# VoiceCraft Server Mobile — Modification & Attribution Notice

VoiceCraft Server Mobile is a modified and extended distribution based on the open-source **VoiceCraft** project by **AvionBlock**.

## Original project

- Project: VoiceCraft
- Upstream organization: AvionBlock
- GitHub mirror: https://github.com/AvionBlock/VoiceCraft
- Primary upstream development repository: https://gitlab.avion.team/voicecraft/VoiceCraft
- Upstream documentation: https://docs.voicecraft.chat
- License: GNU General Public License version 3 (GPL-3.0)

This repository currently pins VoiceCraft upstream v1.7.1 at commit:

`85aaccccbb58adb23e8c87144e8b1c24bf4b2011`

## Modified distribution

This repository is not presented as an official AvionBlock release. It is an Android/mobile-server adaptation and integration project built on top of VoiceCraft.

Material changes in this distribution include, among other project-specific work:

- Android foreground-service hosting for the VoiceCraft server runtime.
- Android headless/runtime compatibility changes.
- Android UI and mobile server configuration workflow.
- Endstone integration for Minecraft Bedrock player state and `/vc` controls.
- Render WebSocket control-plane relay.
- Primary/Backup relay failover and recovery behavior.
- Bind, real unbind, snapshot and reconnect coordination between Endstone and Android.
- Registered-account and device-local Free Account application layer.
- Android Keystore/AES-GCM storage hardening for local account/session data.
- Build, CI and release automation used by this repository.

The VoiceCraft audio/network runtime, protocol concepts and substantial upstream source remain attributable to the original VoiceCraft project and its contributors.

## Upstream credits

The VoiceCraft v1.7.1 client credits list includes:

- SineVector241 — Author, Programmer
- Miniontoby — Translator, Programmer
- Unny — Translator
- AlphaMSq — Translator, Programmer
- R JustGuyz — Translator

Additional upstream contributors are recorded in the upstream repository history and contributor graph:

https://github.com/AvionBlock/VoiceCraft/graphs/contributors

## License and source availability

VoiceCraft upstream is distributed under GPL-3.0. This modified distribution is provided with GPL attribution and source availability appropriate to the covered work.

The complete GPL text is kept at the repository root as `LICENSE.md` and is also included with release assets as `GPL-3.0.txt`.

Each production binary release is intended to include a Corresponding Source archive containing the repository source, the exact pinned VoiceCraft upstream source used for that build, modification scripts and build/release scripts needed to reproduce the covered binary.

Repository source:

https://github.com/samsosleepy2007/VoiceCraft-Server-Mobile

Upstream source:

https://github.com/AvionBlock/VoiceCraft

## Branding

The names, artwork and branding associated with VoiceCraft originate from or are associated with the original VoiceCraft project. Inclusion of attribution in this repository does not imply endorsement, sponsorship or official status by AvionBlock.

This notice was added to make the modified nature of this distribution and its upstream origin clear to users and downstream distributors.
