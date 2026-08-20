$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$worker = Join-Path $repo "vision_worker\worker.py"
$requirements = Join-Path $repo "vision_worker\requirements.txt"
$bridge = Join-Path $repo "WPELibrary\Lib\Vision\VisionPythonWorkerTextRecognizer.cs"
$project = Join-Path $repo "WinsockPacketEditor\WinsockPacketEditor.csproj"

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True (Test-Path -LiteralPath $worker) "Python vision worker is missing."
Assert-True (Test-Path -LiteralPath $requirements) "Python worker requirements are missing."
Assert-True (Test-Path -LiteralPath $bridge) "C# JSONL bridge is missing."
Assert-True ((Get-Content -Raw -Encoding UTF8 $worker).Contains('"protocol": "jsonl-v1"')) `
    "The worker protocol version is not declared."
Assert-True ((Get-Content -Raw -Encoding UTF8 $worker).Contains('sys.stdout.buffer.flush()')) `
    "The worker must flush one JSONL response per request."
Assert-True ((Get-Content -Raw -Encoding UTF8 $worker).Contains('"warmup"')) `
    "The worker must expose a warmup health operation."
Assert-True ((Get-Content -Raw -Encoding UTF8 $worker).Contains('RotatingFileHandler')) `
    "The worker must use a bounded diagnostic log."
Assert-True ((Get-Content -Raw -Encoding UTF8 $worker).Contains('self._input_sessions.pop')) `
    "Airtest authorization must be one-shot."
Assert-True ((Get-Content -Raw -Encoding UTF8 $bridge).Contains('ReadLineAsync')) `
    "The C# bridge must use line-oriented response reads."
Assert-True ((Get-Content -Raw -Encoding UTF8 $bridge).Contains('CheckHealth')) `
    "The C# bridge must expose a worker health check."
Assert-True (Test-Path -LiteralPath (Join-Path $repo "vision_worker\models\ocr\models.manifest.json")) `
    "The bundled OCR model manifest is missing."
Assert-True ((Get-Content -Raw -Encoding UTF8 $project).Contains('vision_worker\worker.py')) `
    "The worker must be included in the desktop application output."

$pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
if (-not $pythonCommand) { $pythonCommand = Get-Command py.exe -ErrorAction SilentlyContinue }
$pythonPath = if ($pythonCommand) { $pythonCommand.Source } else { $null }
if (-not $pythonPath) {
    $deployedPython = Join-Path $env:LOCALAPPDATA "XNAS\WPE\vision-worker\python\python.exe"
    if (Test-Path -LiteralPath $deployedPython) {
        $pythonPath = $deployedPython
    }
}
if ($pythonPath) {
    $arguments = @()
    if ([IO.Path]::GetFileName($pythonPath) -ieq "py.exe") { $arguments += "-3" }
    $arguments += @((Resolve-Path -LiteralPath $worker).Path, "--jsonl")
    $ping = '{"id":1,"method":"ping"}' | & $pythonPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "Python worker ping exited with $LASTEXITCODE." }
    $pingObject = $ping | ConvertFrom-Json
    Assert-True ($pingObject.id -eq 1 -and $pingObject.ok -eq $true) `
        "Python worker ping did not return a valid JSONL response."
    Write-Host "Python worker protocol runtime check passed."
} else {
    Write-Host "Python not installed; static worker protocol checks passed."
}

Write-Host "Vision Python worker protocol regression passed."
