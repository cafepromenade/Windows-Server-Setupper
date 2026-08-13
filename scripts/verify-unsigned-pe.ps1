[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

foreach ($candidate in $Path) {
    $resolved = (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).Path
    $stream = [IO.File]::Open($resolved, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64) { throw "$resolved is too short to be a valid PE file." }
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "$resolved does not have an MZ header." }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadUInt32()
        if ($peOffset -gt ($stream.Length - 24)) { throw "$resolved has a PE header offset outside the file." }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "$resolved does not have a PE signature." }
        [void]$reader.ReadUInt16()
        [void]$reader.ReadUInt16()
        [void]$reader.ReadUInt32()
        [void]$reader.ReadUInt32()
        [void]$reader.ReadUInt32()
        $optionalSize = $reader.ReadUInt16()
        [void]$reader.ReadUInt16()
        $optionalStart = $stream.Position
        if ($optionalSize -lt 2 -or ($optionalStart + $optionalSize) -gt $stream.Length) {
            throw "$resolved has an invalid PE optional header."
        }
        $magic = $reader.ReadUInt16()
        if ($magic -eq 0x10B) {
            $directoryCountOffset = 92
            $directoryTableOffset = 96
        }
        elseif ($magic -eq 0x20B) {
            $directoryCountOffset = 108
            $directoryTableOffset = 112
        }
        else {
            throw "$resolved uses an unsupported PE optional-header format."
        }
        if ($optionalSize -lt ($directoryTableOffset + 40)) {
            throw "$resolved does not contain a complete PE security-directory entry."
        }
        $stream.Position = $optionalStart + $directoryCountOffset
        $directoryCount = $reader.ReadUInt32()
        if ($directoryCount -le 4) {
            Write-Output "NotSigned`t$resolved"
            continue
        }
        $stream.Position = $optionalStart + $directoryTableOffset + 32
        $certificateOffset = $reader.ReadUInt32()
        $certificateSize = $reader.ReadUInt32()
        if ($certificateOffset -eq 0 -and $certificateSize -eq 0) {
            Write-Output "NotSigned`t$resolved"
            continue
        }
        if ($certificateOffset -eq 0 -or $certificateSize -eq 0) {
            throw "$resolved has an inconsistent PE security-directory entry."
        }
        if (($certificateOffset + [uint64]$certificateSize) -gt [uint64]$stream.Length) {
            throw "$resolved has a PE certificate table outside the file."
        }
        throw "$resolved contains a PE certificate table at offset $certificateOffset with size $certificateSize."
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}
