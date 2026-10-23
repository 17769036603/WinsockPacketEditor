[CmdletBinding()]
param(
    [string]$PythonExecutable = "",
    [string]$EnvironmentDirectory = "",
    [switch]$Offline
)

$ErrorActionPreference = "Stop"
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$workerDirectory = Join-Path (Split-Path -Parent $scriptDirectory) "vision_worker"
if (-not (Test-Path -LiteralPath (Join-Path $workerDirectory "requirements.txt"))) {
    throw "vision_worker requirements.txt was not found: $workerDirectory"
}

if ([string]::IsNullOrWhiteSpace($EnvironmentDirectory)) {
    $EnvironmentDirectory = Join-Path $env:LOCALAPPDATA "XNAS\WPE\vision-worker\.venv"
}

function Resolve-Python([string]$Configured) {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($Configured)) {
        $candidates += $Configured
    } else {
        $candidates += "python.exe"
        $candidates += "py.exe"
    }
    foreach ($candidate in $candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($command) {
            return $command.Source
        }
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

$embeddedPython = Join-Path $env:LOCALAPPDATA "XNAS\WPE\vision-worker\python\python.exe"
$python = if ([string]::IsNullOrWhiteSpace($PythonExecutable) -and (Test-Path -LiteralPath $embeddedPython)) {
    (Resolve-Path -LiteralPath $embeddedPython).Path
}
else {
    Resolve-Python $PythonExecutable
}
if ($null -eq $python) {
    throw "Python 3 was not found. Install Python 3 x64 and rerun this script."
}

$venvPython = $python
if ($python -ne $embeddedPython) {
    $environmentParent = Split-Path -Parent $EnvironmentDirectory
    New-Item -ItemType Directory -Force -Path $environmentParent | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $EnvironmentDirectory "Scripts\python.exe"))) {
        & $python -m venv $EnvironmentDirectory
    }
    $venvPython = Join-Path $EnvironmentDirectory "Scripts\python.exe"
    if (-not (Test-Path -LiteralPath $venvPython)) {
        throw "Python virtual environment was not created: $venvPython"
    }
    if (-not $Offline) {
        & $venvPython -m pip install --upgrade pip
        & $venvPython -m pip install -r (Join-Path $workerDirectory "requirements.txt")
    }
}

if (-not $Offline) {
    & $venvPython -m pip check
    if ($LASTEXITCODE -ne 0) { throw "Vision worker dependency check failed." }
}

$modelDirectory = Join-Path $workerDirectory "models\ocr"
$requiredModels = @(
    "ch_ppocr_mobile_v2.0_cls_mobile.onnx",
    "PP-OCRv6_det_small.onnx",
    "PP-OCRv6_rec_small.onnx"
)
foreach ($model in $requiredModels) {
    if (-not (Test-Path -LiteralPath (Join-Path $modelDirectory $model))) {
        throw "RapidOCR model is missing: $model"
    }
}
$manifestPath = Join-Path $modelDirectory "models.manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "RapidOCR model manifest is missing: $manifestPath"
}
$manifest = Get-Content -Raw -Encoding UTF8 $manifestPath | ConvertFrom-Json
foreach ($entry in @($manifest.files)) {
    $modelPath = Join-Path $modelDirectory ([string]$entry.path)
    if (-not (Test-Path -LiteralPath $modelPath)) {
        throw "RapidOCR model manifest references a missing file: $($entry.path)"
    }
    $modelInfo = Get-Item -LiteralPath $modelPath
    if ([int64]$modelInfo.Length -ne [int64]$entry.size) {
        throw "RapidOCR model size check failed: $($entry.path)"
    }
    $modelHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $modelPath).Hash.ToLowerInvariant()
    if ($modelHash -ne ([string]$entry.sha256).ToLowerInvariant()) {
        throw "RapidOCR model SHA-256 check failed: $($entry.path)"
    }
}
$pingResponse = '{"id":1,"method":"ping"}' |
    & $venvPython (Join-Path $workerDirectory "worker.py") --jsonl
if ($LASTEXITCODE -ne 0) {
    throw "Vision worker ping failed with exit code $LASTEXITCODE."
}
$ping = $pingResponse | ConvertFrom-Json
if ($ping.id -ne 1 -or $ping.ok -ne $true) {
    throw "Vision worker ping returned an invalid JSONL response."
}
$warmupRequest = @{
    id = 2
    method = "warmup"
    language = "chi_sim+eng"
} | ConvertTo-Json -Compress
$warmupResponse = $warmupRequest | & $venvPython (Join-Path $workerDirectory "worker.py") --jsonl
$warmup = $warmupResponse | ConvertFrom-Json
if ($warmup.id -ne 2 -or $warmup.ok -ne $true -or $warmup.success -ne $true) {
    throw "Vision worker warmup failed: $($warmup.error)"
}
$smokeRequest = & $venvPython -c "import base64,io,json,sys; from PIL import Image,ImageDraw; image=Image.new('RGB',(320,96),'white'); ImageDraw.Draw(image).text((16,24),'WPE 42',fill='black'); output=io.BytesIO(); image.save(output,format='PNG'); print(json.dumps({'id':2,'method':'ocr','image_base64':base64.b64encode(output.getvalue()).decode('ascii'),'model_directory':sys.argv[1],'recognition_threshold':0.2,'detection_threshold':0.2,'max_image_side':960},separators=(',',':')))" $modelDirectory
$smokeResponse = $smokeRequest | & $venvPython (Join-Path $workerDirectory "worker.py") --jsonl
$smoke = $smokeResponse | ConvertFrom-Json
if ($smoke.id -ne 2 -or $smoke.ok -ne $true -or $smoke.available -ne $true) {
    throw "Vision worker OCR smoke test failed: $($smoke.error)"
}
Write-Host "Vision worker installed and verified: $venvPython"
