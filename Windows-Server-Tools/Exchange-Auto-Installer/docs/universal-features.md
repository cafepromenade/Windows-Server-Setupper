# Local settings and universal tools

The **Settings and tools** destination provides the implemented cross-surface controls in one tabbed, keyboard-operable Material interface. The hand-written [universal feature inventory](universal-feature-inventory.md) remains the release authority and marks every incomplete feature or evidence boundary as blocking.

## Settings and privacy

Language mode, separate English and Cantonese funny levels, dialog emoji decoration, theme, density, accent, font, reduced motion, tab dock, display name, School mode label, narrator preferences, schedules, and update preference persist as bounded JSON. Unknown fields and unsupported schema versions are refused.

The personal-vocabulary picker is always visible. Its versioned JSON is limited to 256 KiB and 1,000 string replacements, rejects duplicate and unsafe keys, stays local, and can be cleared. Ordinary settings exports state that private vocabulary is omitted.

## Search and command palette

`Ctrl+Shift+F` opens the command palette. Search fields link to a local JavaScript `RegExp` builder supporting literals, character classes, anchors, groups, alternation, quantifiers, flags, bounded sample text, syntax errors, live matches, and capture groups. Plain text remains the default until regex is deliberately applied.

## Converter and Ollama boundaries

The converter registry visibly lists Documents/PDF, Images, Audio, Video, Archives, Structured Data/Spreadsheets, Code/Text, and Binary Encodings. Only JSON formatting, Windows text normalization, and Base64 encoding/decoding are enabled because those adapters are bundled and offline. Every other known format remains visible and disabled with the exact missing adapter. No PATH tool silently enables a format.

The Ollama panel uses only `127.0.0.1:11434` for local version, installed-model, and running-model status. It reports unavailable and incomplete capability states honestly. Pulls, chat, exhaustive store refresh, evidence-backed hardware fit, and harness launch remain disabled until their full contracts are implemented and verified.

## Updates

Packaged builds configure the unsigned Squirrel feed, check on startup and every six hours, and expose manual check, failed, current, downloading, and ready-to-restart states. A persistent ready banner names the version, links release notes, warns that the package is unsigned, and offers **Restart to install update** or **Later**.

## Known incomplete contracts

The inventory currently keeps incomplete universal features fail-closed, including full localization resources, cross-application School mode credentials, complete scheduled external sources, dim-sum startup media, Word-depth per-element appearance, full tab grouping/pinning/bulk close, complete converter/PDF queue operations, complete Ollama store/pull/chat/harness operation, credential-vault-backed locks and authenticator, a landing/documentation site, browser-extension download dialogs, real packaged interactions, and capture evidence.
