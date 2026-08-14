// Generated from content/guides by scripts/generate-docs.mjs.
const docs = [
  {
    "id": "accessibility-and-responsive-use",
    "title": "Accessibility and responsive use",
    "category": "Using the site",
    "body": "Keyboard use The site includes a skip link, visible focus, semantic tab and panel roles, keyboard-operable buttons and forms, Escape cancellation for transient overlays, and a global Ctrl+Shift+F command shortcut. Responsive layout The navigation collapses to an icon strip at phone widths, content becomes single-column, overlays stay inside the viewport, wide state and inventory data scroll inside their own containers, and touch targets remain at least 40 to 48 pixels high. Assistive technology Interactive controls expose names, states, values, and live outcomes. Emoji is decorative, code values remain selectable, reduced motion disables non-essential transitions, and the TOTP result does not announce every second.",
    "sections": [
      {
        "heading": "Keyboard use",
        "paragraphs": [
          "The site includes a skip link, visible focus, semantic tab and panel roles, keyboard-operable buttons and forms, Escape cancellation for transient overlays, and a global Ctrl+Shift+F command shortcut."
        ]
      },
      {
        "heading": "Responsive layout",
        "paragraphs": [
          "The navigation collapses to an icon strip at phone widths, content becomes single-column, overlays stay inside the viewport, wide state and inventory data scroll inside their own containers, and touch targets remain at least 40 to 48 pixels high."
        ]
      },
      {
        "heading": "Assistive technology",
        "paragraphs": [
          "Interactive controls expose names, states, values, and live outcomes. Emoji is decorative, code values remain selectable, reduced motion disables non-essential transitions, and the TOTP result does not announce every second."
        ]
      }
    ],
    "suggested": [
      "navigation-search-and-command-palette",
      "material-appearance-and-logo",
      "offline-documentation"
    ]
  },
  {
    "id": "authenticator-toy-locks-and-support",
    "title": "Authenticator, toy locks, and Support Tickets",
    "category": "Local tools",
    "body": "In-memory TOTP The static site offers a user-initiated RFC 6238 calculation using SHA-1, six digits, and a 30-second period. The Base32 secret remains in memory and is never written to site storage. QR import, secure credential-vault storage, and secrets export are unavailable here. Toy locks Each locked tab has its own locally hashed password and a separate 15-minute unlock window. A toy lock is a self-imposed speed bump, not security, protection, or encryption. Clearing this site's browser storage resets the lock list. Support Tickets Support Tickets is an entirely local joke desk with a real disclosure: nothing is sent, no external ticket exists, no network request is made, no data is collected, and nobody is reading it. The site gives browser storage-reset instructions but does not delete data in-app.",
    "sections": [
      {
        "heading": "In-memory TOTP",
        "paragraphs": [
          "The static site offers a user-initiated RFC 6238 calculation using SHA-1, six digits, and a 30-second period. The Base32 secret remains in memory and is never written to site storage. QR import, secure credential-vault storage, and secrets export are unavailable here."
        ]
      },
      {
        "heading": "Toy locks",
        "paragraphs": [
          "Each locked tab has its own locally hashed password and a separate 15-minute unlock window. A toy lock is a self-imposed speed bump, not security, protection, or encryption. Clearing this site's browser storage resets the lock list."
        ]
      },
      {
        "heading": "Support Tickets",
        "paragraphs": [
          "Support Tickets is an entirely local joke desk with a real disclosure: nothing is sent, no external ticket exists, no network request is made, no data is collected, and nobody is reading it. The site gives browser storage-reset instructions but does not delete data in-app."
        ]
      }
    ],
    "suggested": [
      "privacy-local-data-and-history",
      "accessibility-and-responsive-use",
      "local-ollama-manager"
    ]
  },
  {
    "id": "build-and-installer-route",
    "title": "Build and installer route",
    "category": "Development",
    "body": "Application build Run build.bat /s at the repository root to restore required packages and build the primary Release executable. The primary project targets .NET Framework 4.7.2 and the output is Windows-Server-Tools/Windows-Server-Tools/bin/Release/Windows-Server-Tools.exe. Installer build Run build-installer.bat /s to call the application build and produce the unsigned installer through the repository's supported packaging path. The script verifies the output file, source commit, SHA-256 digest, and unsigned status before reporting success. Publication separation The local scripts never publish, tag, push, or create a release. Shipping is a separate operation that verifies the exact built artifact, immutable tag, non-draft release, asset download, and published evidence boundary.",
    "sections": [
      {
        "heading": "Application build",
        "paragraphs": [
          "Run build.bat /s at the repository root to restore required packages and build the primary Release executable. The primary project targets .NET Framework 4.7.2 and the output is Windows-Server-Tools/Windows-Server-Tools/bin/Release/Windows-Server-Tools.exe."
        ]
      },
      {
        "heading": "Installer build",
        "paragraphs": [
          "Run build-installer.bat /s to call the application build and produce the unsigned installer through the repository's supported packaging path. The script verifies the output file, source commit, SHA-256 digest, and unsigned status before reporting success."
        ]
      },
      {
        "heading": "Publication separation",
        "paragraphs": [
          "The local scripts never publish, tag, push, or create a release. Shipping is a separate operation that verifies the exact built artifact, immutable tag, non-draft release, asset download, and published evidence boundary."
        ]
      }
    ],
    "suggested": [
      "product-overview",
      "releases-changelog-and-downloads",
      "resilient-recovery"
    ]
  },
  {
    "id": "language-school-mode-and-narration",
    "title": "Language, School mode, and narration",
    "category": "Personalization",
    "body": "Language choices The site provides English, playful Hong Kong Cantonese, and bilingual presentation. Independent funny-level controls style the voice of each language without altering release numbers, dates, hashes, failure facts, or safety boundaries. School mode School mode is a renameable local experience setting. When enabled, the site forces English and suppresses Cantonese, funny-level, personal-vocabulary, and surprise-content controls. It is not a security boundary and can be reset by clearing this site's browser data. Dialog emoji The persisted dialog-emoji toggle changes non-semantic decoration in dialogs and notifications. It never changes button labels, field labels, accessible names, or factual copy. Narrator Narration is off by default and uses browser speech synthesis only after the user enables it. Installed voices are enumerated at runtime and refreshed when the browser reports a change. Choose automatically remains the default, with rate and pitch controls available.",
    "sections": [
      {
        "heading": "Language choices",
        "paragraphs": [
          "The site provides English, playful Hong Kong Cantonese, and bilingual presentation. Independent funny-level controls style the voice of each language without altering release numbers, dates, hashes, failure facts, or safety boundaries."
        ]
      },
      {
        "heading": "School mode",
        "paragraphs": [
          "School mode is a renameable local experience setting. When enabled, the site forces English and suppresses Cantonese, funny-level, personal-vocabulary, and surprise-content controls. It is not a security boundary and can be reset by clearing this site's browser data."
        ]
      },
      {
        "heading": "Dialog emoji",
        "paragraphs": [
          "The persisted dialog-emoji toggle changes non-semantic decoration in dialogs and notifications. It never changes button labels, field labels, accessible names, or factual copy."
        ]
      },
      {
        "heading": "Narrator",
        "paragraphs": [
          "Narration is off by default and uses browser speech synthesis only after the user enables it. Installed voices are enumerated at runtime and refreshed when the browser reports a change. Choose automatically remains the default, with rate and pitch controls available."
        ]
      }
    ],
    "suggested": [
      "privacy-local-data-and-history",
      "material-appearance-and-logo",
      "accessibility-and-responsive-use"
    ]
  },
  {
    "id": "local-file-converter",
    "title": "Local file converter",
    "category": "Local tools",
    "body": "Available adapters The browser surface converts a bounded local JSON or text source to formatted JSON, JSON Lines, or a Markdown table. It never uploads the source, modifies it in place, or reports success before the output is constructed. Visible capability gaps Documents and PDF, Images, Audio, Video, Archives, Structured Data and Spreadsheets, Code and Text, and Binary Encodings remain named in the catalog. Formats that require unbundled decoders, native tools, or an isolated worker are described as unavailable instead of disappearing. Safety limits The browser converter caps each source at 2 MiB and writes a new download. The installed application remains the correct home for sandboxed adapters, output round-trip validation, persistent resumable queues, storage preflight, batch progress, and overwrite confirmation.",
    "sections": [
      {
        "heading": "Available adapters",
        "paragraphs": [
          "The browser surface converts a bounded local JSON or text source to formatted JSON, JSON Lines, or a Markdown table. It never uploads the source, modifies it in place, or reports success before the output is constructed."
        ]
      },
      {
        "heading": "Visible capability gaps",
        "paragraphs": [
          "Documents and PDF, Images, Audio, Video, Archives, Structured Data and Spreadsheets, Code and Text, and Binary Encodings remain named in the catalog. Formats that require unbundled decoders, native tools, or an isolated worker are described as unavailable instead of disappearing."
        ]
      },
      {
        "heading": "Safety limits",
        "paragraphs": [
          "The browser converter caps each source at 2 MiB and writes a new download. The installed application remains the correct home for sandboxed adapters, output round-trip validation, persistent resumable queues, storage preflight, batch progress, and overwrite confirmation."
        ]
      }
    ],
    "suggested": [
      "privacy-local-data-and-history",
      "local-ollama-manager",
      "authenticator-toy-locks-and-support"
    ]
  },
  {
    "id": "local-ollama-manager",
    "title": "Local Ollama manager",
    "category": "Local tools",
    "body": "Static-site boundary This site can make an explicit user-initiated request to Ollama's documented loopback version and tags endpoints. Browser cross-origin rules can block that request even when Ollama is healthy. No proxy or cloud fallback is used. Installed-app capability The installed application is the appropriate surface for exhaustive catalog pagination, installed and running model reconciliation, bounded pulls, hardware-fit evidence, streamed local chat, durable sessions, snapshots, rollback, and allowlisted harness launch. Honest failure states Missing runtime, stopped service, unhealthy API, cross-origin blocking, offline catalog, insufficient storage, unsupported hardware, pull failure, and harness failure are separate diagnoses. The site does not invent a model list or promise a model will run from its name.",
    "sections": [
      {
        "heading": "Static-site boundary",
        "paragraphs": [
          "This site can make an explicit user-initiated request to Ollama's documented loopback version and tags endpoints. Browser cross-origin rules can block that request even when Ollama is healthy. No proxy or cloud fallback is used."
        ]
      },
      {
        "heading": "Installed-app capability",
        "paragraphs": [
          "The installed application is the appropriate surface for exhaustive catalog pagination, installed and running model reconciliation, bounded pulls, hardware-fit evidence, streamed local chat, durable sessions, snapshots, rollback, and allowlisted harness launch."
        ]
      },
      {
        "heading": "Honest failure states",
        "paragraphs": [
          "Missing runtime, stopped service, unhealthy API, cross-origin blocking, offline catalog, insufficient storage, unsupported hardware, pull failure, and harness failure are separate diagnoses. The site does not invent a model list or promise a model will run from its name."
        ]
      }
    ],
    "suggested": [
      "local-file-converter",
      "authenticator-toy-locks-and-support",
      "privacy-local-data-and-history"
    ]
  },
  {
    "id": "material-appearance-and-logo",
    "title": "Material appearance and local logo customization",
    "category": "Personalization",
    "body": "Material 3 The site uses role-based colors, expressive type, rounded shapes, tonal surfaces, bounded elevation, reduced-motion support, and responsive density. Light, dark, and high-contrast modes share the same information architecture. Appearance editor Tab context menus and Shift+right-click expose an anchored appearance editor. The current static implementation edits accent, panel width, and representative shape controls. Platform-only typography and effects remain documented as unavailable rather than silently disappearing. Custom mark The shipped mark is a 1024 by 1024 transparent PNG from source commit a06419b7f387927cff647d945e7bf51e471879d4 with SHA-256 8e6333f433bc875a5829bfe7ad13e89630f7cbfbd7725a38be998593f769d03c. The custom mark picker accepts genuine PNG, JPEG, and WebP bytes up to 1 MiB, limits decoded dimensions and pixel count, and keeps the previous mark if validation fails. The image stays in browser storage and changes presentation only; it never changes package identity, update feed, or storage keys.",
    "sections": [
      {
        "heading": "Material 3",
        "paragraphs": [
          "The site uses role-based colors, expressive type, rounded shapes, tonal surfaces, bounded elevation, reduced-motion support, and responsive density. Light, dark, and high-contrast modes share the same information architecture."
        ]
      },
      {
        "heading": "Appearance editor",
        "paragraphs": [
          "Tab context menus and Shift+right-click expose an anchored appearance editor. The current static implementation edits accent, panel width, and representative shape controls. Platform-only typography and effects remain documented as unavailable rather than silently disappearing."
        ]
      },
      {
        "heading": "Custom mark",
        "paragraphs": [
          "The shipped mark is a 1024 by 1024 transparent PNG from source commit a06419b7f387927cff647d945e7bf51e471879d4 with SHA-256 8e6333f433bc875a5829bfe7ad13e89630f7cbfbd7725a38be998593f769d03c. The custom mark picker accepts genuine PNG, JPEG, and WebP bytes up to 1 MiB, limits decoded dimensions and pixel count, and keeps the previous mark if validation fails. The image stays in browser storage and changes presentation only; it never changes package identity, update feed, or storage keys."
        ]
      }
    ],
    "suggested": [
      "navigation-search-and-command-palette",
      "accessibility-and-responsive-use",
      "privacy-local-data-and-history"
    ]
  },
  {
    "id": "navigation-search-and-command-palette",
    "title": "Navigation, search, and the command palette",
    "category": "Using the site",
    "body": "Dockable tabs The browser-style tab strip can dock on the left, right, top, or bottom and persists that choice on this device. Product, Explore, and Configure groups remain distinct. Pinned tabs keep a visible marker, and phone layouts collapse labels to a horizontal icon strip. Four discovery searches The site exposes current-strip search, per-group tab search, group-name search, and master tab search. Each field owns its own anchored regular expression builder and keeps plain text as the default. Regular expression safety Patterns use the browser's ECMAScript engine, are bounded to 160 characters, and evaluate only against a bounded sample. The builder supports literals, character classes, anchors, groups, alternation, quantifiers, and flags without transmitting the pattern. Command palette Press Ctrl+Shift+F to open the command palette. Results open the exact destination or focus the matching upload control. Escape closes the palette and other transient overlays.",
    "sections": [
      {
        "heading": "Dockable tabs",
        "paragraphs": [
          "The browser-style tab strip can dock on the left, right, top, or bottom and persists that choice on this device. Product, Explore, and Configure groups remain distinct. Pinned tabs keep a visible marker, and phone layouts collapse labels to a horizontal icon strip."
        ]
      },
      {
        "heading": "Four discovery searches",
        "paragraphs": [
          "The site exposes current-strip search, per-group tab search, group-name search, and master tab search. Each field owns its own anchored regular expression builder and keeps plain text as the default."
        ]
      },
      {
        "heading": "Regular expression safety",
        "paragraphs": [
          "Patterns use the browser's ECMAScript engine, are bounded to 160 characters, and evaluate only against a bounded sample. The builder supports literals, character classes, anchors, groups, alternation, quantifiers, and flags without transmitting the pattern."
        ]
      },
      {
        "heading": "Command palette",
        "paragraphs": [
          "Press Ctrl+Shift+F to open the command palette. Results open the exact destination or focus the matching upload control. Escape closes the palette and other transient overlays."
        ]
      }
    ],
    "suggested": [
      "accessibility-and-responsive-use",
      "material-appearance-and-logo",
      "offline-documentation"
    ]
  },
  {
    "id": "offline-documentation",
    "title": "Offline documentation and publication",
    "category": "Documentation",
    "body": "Bundled articles Every Markdown article in this directory is read at build time and emitted into a local TypeScript bundle. The documentation browser searches article titles and bodies without fetching a remote document. Offline cache A small service worker caches the built page and same-origin static requests after they are visited. It does not intercept non-GET requests or cache cross-origin resources. Publication boundary The source includes Sites hosting metadata with no platform database or object-storage binding. No hosted URL is claimed until a validated version is published. GitHub Pages is not enabled in the repository at the time of this build and remains an external publication step.",
    "sections": [
      {
        "heading": "Bundled articles",
        "paragraphs": [
          "Every Markdown article in this directory is read at build time and emitted into a local TypeScript bundle. The documentation browser searches article titles and bodies without fetching a remote document."
        ]
      },
      {
        "heading": "Offline cache",
        "paragraphs": [
          "A small service worker caches the built page and same-origin static requests after they are visited. It does not intercept non-GET requests or cache cross-origin resources."
        ]
      },
      {
        "heading": "Publication boundary",
        "paragraphs": [
          "The source includes Sites hosting metadata with no platform database or object-storage binding. No hosted URL is claimed until a validated version is published. GitHub Pages is not enabled in the repository at the time of this build and remains an external publication step."
        ]
      }
    ],
    "suggested": [
      "navigation-search-and-command-palette",
      "accessibility-and-responsive-use",
      "product-overview"
    ]
  },
  {
    "id": "privacy-local-data-and-history",
    "title": "Privacy, personal vocabulary, and local data",
    "category": "Privacy",
    "body": "Device-local state Appearance, navigation, notices, local tickets, and toy-lock digests are stored in this browser profile. There is no analytics, remote font, content-delivery script, or application-owned account. Personal vocabulary The visible personal-vocabulary upload accepts a version 1 JSON object with bounded string replacements. The complete payload is validated before it is cached. Unknown versions, oversized files, unsafe object keys, excessive entry counts, and invalid field types are rejected without partial application. Export boundary Private vocabulary data, custom-image bytes, passwords, authenticator secrets, and raw local-model payloads do not belong in ordinary exports, diagnostics, screenshots, logs, prompts, or public records. Clearing the relevant local cache immediately restores shipped wording or the shipped mark.",
    "sections": [
      {
        "heading": "Device-local state",
        "paragraphs": [
          "Appearance, navigation, notices, local tickets, and toy-lock digests are stored in this browser profile. There is no analytics, remote font, content-delivery script, or application-owned account."
        ]
      },
      {
        "heading": "Personal vocabulary",
        "paragraphs": [
          "The visible personal-vocabulary upload accepts a version 1 JSON object with bounded string replacements. The complete payload is validated before it is cached. Unknown versions, oversized files, unsafe object keys, excessive entry counts, and invalid field types are rejected without partial application."
        ]
      },
      {
        "heading": "Export boundary",
        "paragraphs": [
          "Private vocabulary data, custom-image bytes, passwords, authenticator secrets, and raw local-model payloads do not belong in ordinary exports, diagnostics, screenshots, logs, prompts, or public records. Clearing the relevant local cache immediately restores shipped wording or the shipped mark."
        ]
      }
    ],
    "suggested": [
      "language-school-mode-and-narration",
      "schedules-notifications-and-local-history",
      "authenticator-toy-locks-and-support"
    ]
  },
  {
    "id": "product-overview",
    "title": "Windows Server Setupper overview",
    "category": "Product",
    "body": "What it is Windows Server Setupper is a collection of Windows desktop tools for configuring server roles, baseline settings, directory services, shared folders, and selected software. The primary application is a .NET Framework 4.7.2 WPF desktop application. Operating boundary The tools can change operating-system roles, network settings, security settings, scheduled tasks, and directory-service data. Evaluate them on an appropriate test server, review each requested operation, and use administrative rights only when the operation requires them. Current release The latest verified release is [Windows build 8.1 · Dried Scallop Shrimp Dumpling · 瑤柱蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-8.1-ba3d587a), published on 2026-08-14 at 05:37:03 UTC as tag `windows-8.1-ba3d587a` from commit `ba3d587a6b1240d960ea390a43b6c8928e521ff1`. Its WPF and Exchange installers are intentionally unsigned. The release records every attached asset's exact size and SHA-256 digest, its workflow timing, and the checks that were and were not run.",
    "sections": [
      {
        "heading": "What it is",
        "paragraphs": [
          "Windows Server Setupper is a collection of Windows desktop tools for configuring server roles, baseline settings, directory services, shared folders, and selected software. The primary application is a .NET Framework 4.7.2 WPF desktop application."
        ]
      },
      {
        "heading": "Operating boundary",
        "paragraphs": [
          "The tools can change operating-system roles, network settings, security settings, scheduled tasks, and directory-service data. Evaluate them on an appropriate test server, review each requested operation, and use administrative rights only when the operation requires them."
        ]
      },
      {
        "heading": "Current release",
        "paragraphs": [
          "The latest verified release is [Windows build 8.1 · Dried Scallop Shrimp Dumpling · 瑤柱蝦餃](https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-8.1-ba3d587a), published on 2026-08-14 at 05:37:03 UTC as tag `windows-8.1-ba3d587a` from commit `ba3d587a6b1240d960ea390a43b6c8928e521ff1`. Its WPF and Exchange installers are intentionally unsigned. The release records every attached asset's exact size and SHA-256 digest, its workflow timing, and the checks that were and were not run."
        ]
      }
    ],
    "suggested": [
      "resilient-recovery",
      "releases-changelog-and-downloads",
      "build-and-installer-route"
    ]
  },
  {
    "id": "releases-changelog-and-downloads",
    "title": "Releases, changelog, and verified downloads",
    "category": "Releases",
    "body": "Immutable release facts Windows build 8.1 · Dried Scallop Shrimp Dumpling · 瑤柱蝦餃 was published on 2026-08-14 at 05:37:03 UTC as tag `windows-8.1-ba3d587a` from commit `ba3d587a6b1240d960ea390a43b6c8928e521ff1`. The release is non-draft and non-prerelease. Its `Windows release` GitHub Actions run [31773190945](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31773190945) completed successfully. The release records workflow timing from the GitHub Actions job `started_at` value to the server-reported non-draft publication time: started `2026-08-14T05:30:14Z`, completed `2026-08-14T05:37:03Z`, duration `00:06:49`. Immutable release assets | Role | Immutable download | Bytes | SHA-256 | | --- | --- | ---: | --- | | WPF installer | [WindowsServerTools-Setup-ba3d587a6b1240d960ea390a43b6c8928e521ff1.exe](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/WindowsServerTools-Setup-ba3d587a6b1240d960ea390a43b6c8928e521ff1.exe) | 6,572,044 | `3e3e72e125671736df93661067e01c42d644f6c75f01b7053e1aafb7dff032c1` | | Exchange Squirrel setup | [ExchangeAutoInstaller-1.8.1-x64-Setup.exe](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/ExchangeAutoInstaller-1.8.1-x64-Setup.exe) | 142,329,856 | `a5d40df90018ed6ba2ea15e26612c8353189b93c61512f23210e3cc91446d800` | | Exchange Squirrel update index | [RELEASES](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/RELEASES) | 94 | `a2c276e594eafb206949b83958184d7e5e46442fc9a5a2f674b138c32fecb8bc` | | Exchange Squirrel full package | [exchange-auto-installer-1.8.1-full.nupkg](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/exchange-auto-installer-1.8.1-full.nupkg) | 141,268,448 | `6ca88065dd39820538b84f794b3f19e42b4a812fd040f4b26b77c037220e8b31` | | Artifact manifest | [artifact-manifest.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/artifact-manifest.json) | 1,196 | `1fd6e87dea7636922bdfdab574a6a99a0dff74a137bfeeec95454bbbc2e81468` | | Dim-sum metadata | [dim-sum.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/dim-sum.json) | 616 | `a3ace61688499c48a13a39df0d50ba370a8e9b1bc6827cb7bd45c60b403b6c68` | | Exchange package metadata | [exchange-package.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/exchange-package.json) | 1,064 | `3e3e3c97e41984bd64599912fa5a5b8287e74439ada61d26378f5ac0a3bde585` | | Line-count data | [line-count.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/line-count.json) | 2,496 | `d8e84eef8418ef15c20f8ad79793f6f359cfb7b715a12ab40e10d579b358680e` | | Line-count report | [line-count.md](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/line-count.md) | 1,443 | `60ea1e3b11591c740c9c25e9abf64bd6742244563ea08b71b578ffeb5593a143` | | Release dependency inventory | [release-dependencies.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/release-dependencies.json) | 5,442 | `ceb745eb32480afb029276c47db2e473dda3630c88afead03d34909b539e5252` | | Run context | [run-context.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/run-context.json) | 490 | `a732c9d0d0cbb1385d06ffab53cd09d4c561cfe3b3c2d5db32f3adc3f1e48e17` | | Checksum inventory | [SHA256SUMS.txt](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/SHA256SUMS.txt) | 1,030 | `370c380c4efab5c1d4cd996395cb09958c9ee872f408dfcc14d452855b147f16` | Release notes: https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-8.1-ba3d587a. Publication run: https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31773190945. Unsigned status Both installers are intentionally unsigned and may trigger Unknown Publisher, SmartScreen, or reputation warnings. Verify the applicable SHA-256 digest before running an installer. The project does not claim authenticity through a code signature. Verification boundary The application build and installer package scripts completed for both application families. Tests, linting, reviews, audits, runtime UI launch, installer execution, and screenshots were intentionally not run in that expedited delivery pass. A release note that omits those facts would overstate the evidence.",
    "sections": [
      {
        "heading": "Immutable release facts",
        "paragraphs": [
          "Windows build 8.1 · Dried Scallop Shrimp Dumpling · 瑤柱蝦餃 was published on 2026-08-14 at 05:37:03 UTC as tag `windows-8.1-ba3d587a` from commit `ba3d587a6b1240d960ea390a43b6c8928e521ff1`. The release is non-draft and non-prerelease. Its `Windows release` GitHub Actions run [31773190945](https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31773190945) completed successfully.",
          "The release records workflow timing from the GitHub Actions job `started_at` value to the server-reported non-draft publication time: started `2026-08-14T05:30:14Z`, completed `2026-08-14T05:37:03Z`, duration `00:06:49`."
        ]
      },
      {
        "heading": "Immutable release assets",
        "paragraphs": [
          "| Role | Immutable download | Bytes | SHA-256 |",
          "| --- | --- | ---: | --- |",
          "| WPF installer | [WindowsServerTools-Setup-ba3d587a6b1240d960ea390a43b6c8928e521ff1.exe](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/WindowsServerTools-Setup-ba3d587a6b1240d960ea390a43b6c8928e521ff1.exe) | 6,572,044 | `3e3e72e125671736df93661067e01c42d644f6c75f01b7053e1aafb7dff032c1` |",
          "| Exchange Squirrel setup | [ExchangeAutoInstaller-1.8.1-x64-Setup.exe](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/ExchangeAutoInstaller-1.8.1-x64-Setup.exe) | 142,329,856 | `a5d40df90018ed6ba2ea15e26612c8353189b93c61512f23210e3cc91446d800` |",
          "| Exchange Squirrel update index | [RELEASES](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/RELEASES) | 94 | `a2c276e594eafb206949b83958184d7e5e46442fc9a5a2f674b138c32fecb8bc` |",
          "| Exchange Squirrel full package | [exchange-auto-installer-1.8.1-full.nupkg](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/exchange-auto-installer-1.8.1-full.nupkg) | 141,268,448 | `6ca88065dd39820538b84f794b3f19e42b4a812fd040f4b26b77c037220e8b31` |",
          "| Artifact manifest | [artifact-manifest.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/artifact-manifest.json) | 1,196 | `1fd6e87dea7636922bdfdab574a6a99a0dff74a137bfeeec95454bbbc2e81468` |",
          "| Dim-sum metadata | [dim-sum.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/dim-sum.json) | 616 | `a3ace61688499c48a13a39df0d50ba370a8e9b1bc6827cb7bd45c60b403b6c68` |",
          "| Exchange package metadata | [exchange-package.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/exchange-package.json) | 1,064 | `3e3e3c97e41984bd64599912fa5a5b8287e74439ada61d26378f5ac0a3bde585` |",
          "| Line-count data | [line-count.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/line-count.json) | 2,496 | `d8e84eef8418ef15c20f8ad79793f6f359cfb7b715a12ab40e10d579b358680e` |",
          "| Line-count report | [line-count.md](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/line-count.md) | 1,443 | `60ea1e3b11591c740c9c25e9abf64bd6742244563ea08b71b578ffeb5593a143` |",
          "| Release dependency inventory | [release-dependencies.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/release-dependencies.json) | 5,442 | `ceb745eb32480afb029276c47db2e473dda3630c88afead03d34909b539e5252` |",
          "| Run context | [run-context.json](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/run-context.json) | 490 | `a732c9d0d0cbb1385d06ffab53cd09d4c561cfe3b3c2d5db32f3adc3f1e48e17` |",
          "| Checksum inventory | [SHA256SUMS.txt](https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/windows-8.1-ba3d587a/SHA256SUMS.txt) | 1,030 | `370c380c4efab5c1d4cd996395cb09958c9ee872f408dfcc14d452855b147f16` |",
          "Release notes: https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/windows-8.1-ba3d587a. Publication run: https://github.com/cafepromenade/Windows-Server-Setupper/actions/runs/31773190945."
        ]
      },
      {
        "heading": "Unsigned status",
        "paragraphs": [
          "Both installers are intentionally unsigned and may trigger Unknown Publisher, SmartScreen, or reputation warnings. Verify the applicable SHA-256 digest before running an installer. The project does not claim authenticity through a code signature."
        ]
      },
      {
        "heading": "Verification boundary",
        "paragraphs": [
          "The application build and installer package scripts completed for both application families. Tests, linting, reviews, audits, runtime UI launch, installer execution, and screenshots were intentionally not run in that expedited delivery pass. A release note that omits those facts would overstate the evidence."
        ]
      }
    ],
    "suggested": [
      "product-overview",
      "resilient-recovery",
      "build-and-installer-route"
    ]
  },
  {
    "id": "resilient-recovery",
    "title": "Resilient recovery and uncertain outcomes",
    "category": "Reliability",
    "body": "Durable state Recovery uses the windows-server-tools-recovery-v3 format. Each operation is recorded before and after execution with a state, attempt count, generation, timestamps, bounded error summary, and an integrity record over canonical content. Retry rules Automatic retry is reserved for operations explicitly declared idempotent and remains bounded by an attempt budget. A failed persistence write after an action starts or completes produces an uncertain result rather than a success claim. Reconciliation An indeterminate action has two separate reviewed outcomes: it completed and should be preserved, or it was confirmed stopped without completing and may enter a new retry generation. One answer is never applied to several uncertain actions. Failure modes Corrupt state blocks replay. External-process timeouts remain indeterminate unless the entire contained process tree is proven stopped. Cleanup failure is independently retryable and never silently repeats completed server work.",
    "sections": [
      {
        "heading": "Durable state",
        "paragraphs": [
          "Recovery uses the windows-server-tools-recovery-v3 format. Each operation is recorded before and after execution with a state, attempt count, generation, timestamps, bounded error summary, and an integrity record over canonical content."
        ]
      },
      {
        "heading": "Retry rules",
        "paragraphs": [
          "Automatic retry is reserved for operations explicitly declared idempotent and remains bounded by an attempt budget. A failed persistence write after an action starts or completes produces an uncertain result rather than a success claim."
        ]
      },
      {
        "heading": "Reconciliation",
        "paragraphs": [
          "An indeterminate action has two separate reviewed outcomes: it completed and should be preserved, or it was confirmed stopped without completing and may enter a new retry generation. One answer is never applied to several uncertain actions."
        ]
      },
      {
        "heading": "Failure modes",
        "paragraphs": [
          "Corrupt state blocks replay. External-process timeouts remain indeterminate unless the entire contained process tree is proven stopped. Cleanup failure is independently retryable and never silently repeats completed server work."
        ]
      }
    ],
    "suggested": [
      "product-overview",
      "releases-changelog-and-downloads",
      "build-and-installer-route"
    ]
  },
  {
    "id": "schedules-notifications-and-local-history",
    "title": "Schedules, notifications, and local history",
    "category": "Personalization",
    "body": "Scheduled settings The static site provides a local weekday and time-window editor using the browser's configured timezone and daylight-saving behavior. API and Home Assistant sources remain unavailable because this surface has no privileged request boundary or credential vault. Notifications Informational, success, warning, and error messages appear as non-blocking corner notifications. The bounded local history persists on this device and each notification can be dismissed independently. History boundary The installed application is the correct place for isolated Git-backed history with append-only restore, encrypted or redacted snapshots, retention, diff, and export. This static site keeps only small browser records and does not claim that they provide the same history guarantees.",
    "sections": [
      {
        "heading": "Scheduled settings",
        "paragraphs": [
          "The static site provides a local weekday and time-window editor using the browser's configured timezone and daylight-saving behavior. API and Home Assistant sources remain unavailable because this surface has no privileged request boundary or credential vault."
        ]
      },
      {
        "heading": "Notifications",
        "paragraphs": [
          "Informational, success, warning, and error messages appear as non-blocking corner notifications. The bounded local history persists on this device and each notification can be dismissed independently."
        ]
      },
      {
        "heading": "History boundary",
        "paragraphs": [
          "The installed application is the correct place for isolated Git-backed history with append-only restore, encrypted or redacted snapshots, retention, diff, and export. This static site keeps only small browser records and does not claim that they provide the same history guarantees."
        ]
      }
    ],
    "suggested": [
      "language-school-mode-and-narration",
      "privacy-local-data-and-history",
      "accessibility-and-responsive-use"
    ]
  }
] as const;
export default docs;
