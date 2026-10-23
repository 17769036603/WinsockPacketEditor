function ConvertFrom-SharedFridaSmapsRollup {
    param([string]$Text)
    $map = @{}
    $rawMetricLines = [Collections.Generic.List[string]]::new()
    foreach ($line in ($Text -split "`n")) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^(Pss|Rss|Private_Dirty):\s+(\d+)\s+kB$') {
            $key = $Matches[1]; $value = [int64]$Matches[2]
            $map[$key] = $value
            $rawMetricLines.Add($trimmed)
        }
    }
    [pscustomobject]@{
        PssKiB=$map['Pss']; RssKiB=$map['Rss']; PrivateDirtyKiB=$map['Private_Dirty']
        PssConfirmed=$map.ContainsKey('Pss'); PrivateDirtyConfirmed=$map.ContainsKey('Private_Dirty')
        Source='proc-smaps_rollup'; RawMetricLines=@($rawMetricLines)
    }
}
