param(
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $PSScriptRoot "..\WPELibrary\bin\Debug"
}

$libraryDll = Join-Path $BuildDirectory "WPELibrary.dll"
if (-not (Test-Path -LiteralPath $libraryDll)) {
    throw "WPELibrary.dll was not found: $libraryDll"
}

Add-Type -Path $libraryDll
$socketFormSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot "..\WPELibrary\Socket_Form.cs")

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw "ASSERT FAILED: $message"
    }
}

Assert-True ($socketFormSource.Contains('Name = "cmsSocketList_JevAnalyze"')) `
    "The packet-list context menu must expose JEV analysis."
Assert-True ($socketFormSource.Contains("client.EvaluateAsync(")) `
    "The packet-list action must call the TypeSafe Jev client."
Assert-True ($socketFormSource.Contains('maxAnalyzedBytes = 4096')) `
    "Jev packet analysis must retain the remote input cap."

$choiceCriteria = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$choiceCriteria.Add("capture", "A packet captured from the current session")
$choiceCriteria.Add("other", "Any other input")

$scoreCriteria = New-Object 'System.Collections.Generic.List[string]'
$scoreCriteria.Add("low")
$scoreCriteria.Add("high")

$questions = New-Object 'System.Collections.Generic.Dictionary[string,WPELibrary.Lib.TypeSafe.JevQuestion]'
$questions.Add(
    "packet_kind",
    [WPELibrary.Lib.TypeSafe.JevQuestion]::Choice(
        "Which category best describes this packet?",
        $choiceCriteria))
$questions.Add(
    "risk",
    [WPELibrary.Lib.TypeSafe.JevQuestion]::Score(
        "How risky is this packet to automate?",
        $scoreCriteria))
$questions.Add(
    "is_binary",
    [WPELibrary.Lib.TypeSafe.JevQuestion]::Noul(
        "Does this state describe a binary packet?"))

$state = [Newtonsoft.Json.Linq.JObject]::Parse(
    '{"packet_type":"WS2_Send","hex":"4D 5A 00 00","direction":"send"}')
$request = New-Object WPELibrary.Lib.TypeSafe.JevRequest($state, $questions)
$requestJson = $request.ToJson().ToString([Newtonsoft.Json.Formatting]::None)
$requestRoot = [Newtonsoft.Json.Linq.JObject]::Parse($requestJson)

Assert-True ($requestRoot["model"].ToString() -eq "jev-latest") `
    "Jev requests must use jev-latest by default."
Assert-True ($requestRoot["state"]["packet_type"].ToString() -eq "WS2_Send") `
    "Jev requests must preserve structured state."
Assert-True ($requestRoot["questions"]["packet_kind"]["type"].ToString() -eq "choice") `
    "Choice question type was not serialized."
Assert-True ($requestRoot["questions"]["risk"]["criteria"].Count -eq 2) `
    "Score criteria were not serialized."

$response = [WPELibrary.Lib.TypeSafe.JevResponse]::Parse(@'
{
  "model": "jev-1.13.0",
  "answers": {
    "packet_kind": {
      "type": "choice",
      "choice": "capture",
      "probabilities": {"capture": 0.9, "other": 0.1},
      "confidence": 0.8
    },
    "risk": {
      "type": "score",
      "score": 0.1,
      "legend": {"0": "low", "1": "high"},
      "probabilities": {"0": 0.9, "1": 0.1},
      "confidence": 0.8
    },
    "is_binary": {"type": "noul", "noul": 1.0}
  },
  "usage": {"input_tokens": 10, "output_tokens": 4}
}
'@)

Assert-True ($response.Answers["packet_kind"].Choice -eq "capture") `
    "Choice answer was not parsed."
Assert-True ($response.Answers["risk"].Score -eq 0.1) `
    "Score answer was not parsed."
Assert-True ($response.Answers["is_binary"].Noul -eq 1.0) `
    "Noul answer was not parsed."
Assert-True ($response.Usage.InputTokens -eq 10 -and $response.Usage.OutputTokens -eq 4) `
    "Token usage was not parsed."

Write-Output "TypeSafe Jev client offline regression checks passed."
