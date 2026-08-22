param(
    [string]$BuildDirectory = "$(Join-Path (Split-Path -Parent $PSScriptRoot) 'WPELibrary\bin\MemoryReaderValidation')"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repo "WPELibrary\Lib\Memory\ReadOnlyProcessMemoryReader.cs"
$libraryPath = Join-Path $BuildDirectory "WPELibrary.dll"

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "The read-only memory reader source file is missing."
}
if (-not (Test-Path -LiteralPath $libraryPath)) {
    throw "The built WPELibrary.dll is missing: $libraryPath"
}

$source = [System.IO.File]::ReadAllText($sourcePath, [System.Text.Encoding]::UTF8)
$forbiddenApis = @(
    "WriteProcessMemory",
    "NtWriteVirtualMemory",
    "VirtualAllocEx",
    "VirtualQueryEx",
    "CreateRemoteThread",
    "CreateToolhelp32Snapshot"
)
foreach ($api in $forbiddenApis) {
    if ($source.Contains($api)) {
        throw "The read-only memory reader must not reference $api."
    }
}

Add-Type -Path $libraryPath

$identity = $null
$identityError = $null
if (-not [WPELibrary.Lib.Memory.ReadOnlyProcessIdentity]::TryCapture(
        $PID,
        [ref]$identity,
        [ref]$identityError)) {
    throw "Capturing the current test process identity failed: $identityError"
}
if ($identity.ProcessId -ne $PID -or
    ($identity.PointerSize -ne 4 -and $identity.PointerSize -ne 8)) {
    throw "The captured process identity is incomplete."
}

$reader = $null
$buffer = [System.IntPtr]::Zero
$pointerBuffer = [System.IntPtr]::Zero
try {
    $readerError = $null
    if (-not [WPELibrary.Lib.Memory.ReadOnlyProcessMemoryReader]::TryOpen(
            $identity,
            [ref]$reader,
            [ref]$readerError)) {
        throw "Opening the current test process for read-only access failed: $readerError"
    }

    $payload = [byte[]](0x78, 0x56, 0x34, 0x12, 0x09, 0x08, 0x07, 0x06)
    $buffer = [System.Runtime.InteropServices.Marshal]::AllocHGlobal($payload.Length)
    [System.Runtime.InteropServices.Marshal]::Copy($payload, 0, $buffer, $payload.Length)
    $address = $buffer.ToInt64()

    $readBytes = $null
    $readError = $null
    if (-not $reader.TryReadBytes($address, $payload.Length, [ref]$readBytes, [ref]$readError)) {
        throw "Reading the self-owned test buffer failed: $readError"
    }
    if ([Convert]::ToBase64String($readBytes) -ne [Convert]::ToBase64String($payload)) {
        throw "Read bytes do not match the self-owned test buffer."
    }

    $readInt32 = 0
    $readError = $null
    if (-not $reader.TryReadInt32($address, [ref]$readInt32, [ref]$readError) -or
        $readInt32 -ne 0x12345678) {
        throw "Little-endian Int32 reading failed: $readError"
    }

    $pointerBytes = if ($identity.PointerSize -eq 4) {
        [System.BitConverter]::GetBytes([uint32]$address)
    }
    else {
        [System.BitConverter]::GetBytes($address)
    }
    $pointerBuffer = [System.Runtime.InteropServices.Marshal]::AllocHGlobal($identity.PointerSize)
    [System.Runtime.InteropServices.Marshal]::Copy(
        $pointerBytes,
        0,
        $pointerBuffer,
        $pointerBytes.Length)
    $pointerValue = 0L
    $readError = $null
    if (-not $reader.TryReadPointer(
            $pointerBuffer.ToInt64(),
            [ref]$pointerValue,
            [ref]$readError) -or
        $pointerValue -ne $address) {
        throw "Pointer reading failed: $readError"
    }

    $invalidBytes = $null
    $readError = $null
    if ($reader.TryReadBytes(0, 1, [ref]$invalidBytes, [ref]$readError)) {
        throw "Address zero must be rejected."
    }
    $readError = $null
    if ($reader.TryReadBytes($address, 0, [ref]$invalidBytes, [ref]$readError)) {
        throw "A zero-length read must be rejected."
    }
    if (-not $reader.EnsureProcessIdentity([ref]$readError)) {
        throw "The current test process identity was not retained: $readError"
    }

    $reader.Dispose()
    if (-not $reader.IsDisposed) {
        throw "The reader did not report its disposed state."
    }
    $readError = $null
    if ($reader.TryReadBytes($address, 1, [ref]$invalidBytes, [ref]$readError)) {
        throw "Reads after disposal must be rejected."
    }

    Write-Output "ReadOnlyProcessMemoryReaderRegression: PASS"
}
finally {
    if ($reader -ne $null) {
        $reader.Dispose()
    }
    if ($buffer -ne [System.IntPtr]::Zero) {
        [System.Runtime.InteropServices.Marshal]::FreeHGlobal($buffer)
    }
    if ($pointerBuffer -ne [System.IntPtr]::Zero) {
        [System.Runtime.InteropServices.Marshal]::FreeHGlobal($pointerBuffer)
    }
}
