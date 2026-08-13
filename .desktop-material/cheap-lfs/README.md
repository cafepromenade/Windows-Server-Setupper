<!-- desktop-material-managed-cheap-lfs-clone-helper:v1 -->
# Restore Cheap LFS files

Desktop Material generated this helper from the Cheap LFS pointers committed in
this repository. It downloads the selected GitHub Release assets with GitHub
CLI, verifies every decoded part and the complete file by exact byte size and
SHA-256, then replaces the matching pointer. Existing files that do not match
the managed pointer or the already-restored payload are left untouched.

Requirements: Node.js and GitHub CLI. For a private repository, authenticate
GitHub CLI with an account that can read the repository and its Releases.

## Windows

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\.desktop-material\cheap-lfs\hydrate.ps1"
```

## Linux

```sh
sh ./.desktop-material/cheap-lfs/hydrate.sh
```

With no arguments the helper restores every supported pointer in
`hydrate-inventory.json`. The separate `inventory.json` is Desktop
Material's bounded clone-selection hint. To restore only chosen files, append
one or more
`--path "repository/relative/file"` arguments. Use `--list` to print the
available paths.

## Verify without downloading

Run the release-metadata verification before starting a large transfer:

```powershell
node .\.desktop-material\cheap-lfs\hydrate.mjs --verify-only
```

This validates both inventories, the checked-out pointer, its canonical LF
identity, the complete part sequence, and each GitHub Release asset's unique
name, uploaded state, exact stored byte size, and SHA-256 digest. The GitHub CLI
metadata request is bounded to 16 MiB and 30 seconds. The command downloads no
asset content and reports `"downloadedBytes":0` in its success JSON.

For an offline local-only check, use either of these equivalent forms:

```powershell
node .\.desktop-material\cheap-lfs\hydrate.mjs --verify-only --static
node .\.desktop-material\cheap-lfs\hydrate.mjs --static
```

Static verification checks the local pointer/inventory contract but cannot
prove that release assets are still published. On Windows, Git may check the
text pointer out with CRLF line endings. The verifier reports both the exact
on-disk SHA-256 and the deterministic canonical LF SHA-256, and accepts only
content whose normalized lines exactly match the managed pointer.

If a previously hydrated payload is available outside this checkout, verify it
without copying or replacing the tracked pointer:

```powershell
node .\.desktop-material\cheap-lfs\hydrate.mjs --static --verify-payload "D:\verified-cache\exchange.iso"
```

The external file must be a single-link regular file with the exact declared
size and SHA-256. The verifier opens and hashes it read-only, reports the proof
in `payloadProof`, and never installs, moves, renames, or deletes that file.

The current Exchange ISO contract is 6,402,453,504 bytes with SHA-256
`cd2b13f2c297187776af4cff3541b4be3c677cf907cca69d85ab0e2b70377bd1`.
After verification succeeds, run the normal hydration command above to perform
the actual download, per-part raw-DEFLATE decoding, and complete-file hash
verification.

Run the focused metadata regression with:

```powershell
node .\.desktop-material\cheap-lfs\verify.test.mjs
```

The regression deliberately changes pointer and part metadata in memory, proves
the validators reject each change, restores the source metadata, and proves the
static command returns green without downloading content.

Raw, multipart, and raw-DEFLATE GitHub Release pointers are supported. The
helper stops with an actionable message for encrypted Release or OCI registry
pointers; restore those through Desktop Material so its password and immutable
registry validation remain in the trusted app flow.
