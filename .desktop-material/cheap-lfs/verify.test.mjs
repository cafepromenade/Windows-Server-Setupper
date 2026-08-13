import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import {
  calculatePointerSetSha256,
  normalizeCheckedOutPointerText,
  validateCloneInventory,
  validateInventory,
} from './hydrate.mjs'

const helperDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(helperDirectory, '..', '..')
const hydrationPath = resolve(helperDirectory, 'hydrate-inventory.json')
const clonePath = resolve(helperDirectory, 'inventory.json')
const pointerPath = resolve(
  repositoryRoot,
  'Windows-Server-Tools',
  'Deps',
  'exchange.iso'
)

const hydrationSource = await readFile(hydrationPath, 'utf8')
const cloneSource = await readFile(clonePath, 'utf8')
const pointerBytes = await readFile(pointerPath)
const hydrationInventory = JSON.parse(hydrationSource)
const cloneInventory = JSON.parse(cloneSource)

assert.equal(normalizeCheckedOutPointerText('a\nb\n'), 'a\nb\n')
assert.equal(normalizeCheckedOutPointerText('a\r\nb\r\n'), 'a\nb\n')
assert.equal(normalizeCheckedOutPointerText('a\r\nb\n'), null)
assert.equal(normalizeCheckedOutPointerText('a\rb\r'), null)

const validated = validateInventory(hydrationInventory)
validateCloneInventory(cloneInventory, validated)
assert.equal(
  cloneInventory.pointerSetSha256,
  calculatePointerSetSha256(cloneInventory.assets)
)

const changedPointerMetadata = structuredClone(hydrationInventory)
changedPointerMetadata.entries[0].pointerSha256 = '0'.repeat(64)
assert.throws(
  () => validateInventory(changedPointerMetadata),
  /pointer identity is invalid/
)
const negativePointerMetadataTurnedRed = true

const changedClonePointer = structuredClone(cloneInventory)
changedClonePointer.assets[0].pointerBlobSha256 = '0'.repeat(64)
assert.throws(
  () => validateCloneInventory(changedClonePointer, validated),
  /does not match hydration metadata/
)
const negativePointerInventoryMismatchTurnedRed = true

const changedPartMetadata = structuredClone(hydrationInventory)
changedPartMetadata.entries[0].source.parts[0].storedSizeInBytes =
  changedPartMetadata.entries[0].source.parts[0].sizeInBytes
assert.throws(
  () => validateInventory(changedPartMetadata),
  /inconsistent Release part sizes/
)
const negativePartMetadataTurnedRed = true

const restored = validateInventory(JSON.parse(hydrationSource))
validateCloneInventory(JSON.parse(cloneSource), restored)
const restoredMetadataTurnedGreen = true

const staticRun = spawnSync(
  process.execPath,
  [resolve(helperDirectory, 'hydrate.mjs'), '--static'],
  {
    cwd: repositoryRoot,
    encoding: 'utf8',
    timeout: 10_000,
    maxBuffer: 1024 * 1024,
    windowsHide: true,
  }
)
assert.equal(staticRun.signal, null)
assert.equal(staticRun.status, 0, staticRun.stderr)
const staticResult = JSON.parse(staticRun.stdout)
assert.equal(staticResult.status, 'verified')
assert.equal(staticResult.mode, 'static')
assert.equal(staticResult.downloadedBytes, 0)
assert.equal(staticResult.checked.length, 1)
assert.equal(staticResult.checked[0].state, 'pointer')
assert.equal(
  staticResult.checked[0].checkedOutPointerSha256,
  createHash('sha256').update(pointerBytes).digest('hex')
)
assert.equal(
  staticResult.checked[0].canonicalPointerSha256,
  '6f0ec9d2483b079911f6fa66c9b281e00913062122e0cd5cc6fd411a484698f7'
)

console.log(
  JSON.stringify({
    status: 'passed',
    negativePointerMetadataTurnedRed,
    negativePointerInventoryMismatchTurnedRed,
    negativePartMetadataTurnedRed,
    restoredMetadataTurnedGreen,
    staticVerificationDownloadedBytes: staticResult.downloadedBytes,
  })
)
