param(
    [Parameter(Mandatory=$true)][string]$GroundTruth,
    [Parameter(Mandatory=$true)][string]$Findings,
    [string]$OutputDirectory = '.\calibration-results',
    [int]$BinCount = 10
)

$ErrorActionPreference = 'Stop'
if ($BinCount -lt 2 -or $BinCount -gt 50) { throw 'BinCount must be between 2 and 50.' }
if (-not (Test-Path -LiteralPath $GroundTruth)) { throw "Ground truth not found: $GroundTruth" }
if (-not (Test-Path -LiteralPath $Findings)) { throw "Findings not found: $Findings" }

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$truthRows = Import-Csv -LiteralPath $GroundTruth
$truth = @{}
foreach ($row in $truthRows) { $truth[$row.case_id] = $row }

$joined = New-Object System.Collections.Generic.List[object]
$sources = New-Object System.Collections.Generic.HashSet[string]

Get-Content -LiteralPath $Findings | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($_)) { return }
    $finding = $_ | ConvertFrom-Json
    $caseId = $null
    foreach ($line in @($finding.candidate.context)) {
        $m = [regex]::Match([string]$line, '\bcase_id=(CAL\d+)\b')
        if ($m.Success) { $caseId = $m.Groups[1].Value; break }
    }
    if ($null -eq $caseId -or -not $truth.ContainsKey($caseId)) { return }

    $row = $truth[$caseId]
    $score = [double]$finding.classification.likely_real
    $source = [string]$finding.classification.source
    [void]$sources.Add($source)
    $latency = $null
    if ($null -ne $finding.classification.latency_ms) { $latency = [double]$finding.classification.latency_ms }

    $joined.Add([pscustomobject]@{
        case_id = $caseId
        label = [int]$row.label
        category = $row.category
        secret_type_truth = $row.secret_type
        secret_type_predicted = [string]$finding.classification.secret_type
        score = $score
        priority_score = [double]$finding.classification.priority_score
        confidence = [double]$finding.classification.confidence
        source = $source
        latency_ms = $latency
        file_path = [string]$finding.candidate.file_path
    })
}

$joinedPath = Join-Path $OutputDirectory 'joined-results.csv'
$joined | Sort-Object case_id | Export-Csv -LiteralPath $joinedPath -NoTypeInformation -Encoding UTF8

$unique = $joined | Group-Object case_id | ForEach-Object { $_.Group | Select-Object -First 1 }
$matched = @($unique).Count
$totalTruth = @($truthRows).Count
if ($matched -eq 0) { throw 'No labelled cases could be joined to the findings. Check that context includes case_id markers.' }

$brier = (($unique | ForEach-Object { [math]::Pow(([double]$_.score - [int]$_.label), 2) } | Measure-Object -Average).Average)

$bins = New-Object System.Collections.Generic.List[object]
$ece = 0.0
for ($i = 0; $i -lt $BinCount; $i++) {
    $low = $i / [double]$BinCount
    $high = ($i + 1) / [double]$BinCount
    if ($i -eq $BinCount - 1) {
        $members = @($unique | Where-Object { [double]$_.score -ge $low -and [double]$_.score -le $high })
    } else {
        $members = @($unique | Where-Object { [double]$_.score -ge $low -and [double]$_.score -lt $high })
    }
    if ($members.Count -eq 0) {
        $bins.Add([pscustomobject]@{ bin = ("{0:N2}-{1:N2}" -f $low,$high); count = 0; mean_score = $null; observed_positive_rate = $null; absolute_gap = $null })
        continue
    }
    $meanScore = (($members | Measure-Object -Property score -Average).Average)
    $obs = (($members | Measure-Object -Property label -Average).Average)
    $gap = [math]::Abs($meanScore - $obs)
    $ece += ($members.Count / [double]$matched) * $gap
    $bins.Add([pscustomobject]@{
        bin = ("{0:N2}-{1:N2}" -f $low,$high)
        count = $members.Count
        mean_score = [math]::Round($meanScore,4)
        observed_positive_rate = [math]::Round($obs,4)
        absolute_gap = [math]::Round($gap,4)
    })
}

$binsPath = Join-Path $OutputDirectory 'reliability-bins.csv'
$bins | Export-Csv -LiteralPath $binsPath -NoTypeInformation -Encoding UTF8

$latencies = @($unique | Where-Object { $null -ne $_.latency_ms } | ForEach-Object { [double]$_.latency_ms } | Sort-Object)
function Get-Percentile([double[]]$Values, [double]$P) {
    if ($Values.Count -eq 0) { return $null }
    $index = [math]::Ceiling($P * $Values.Count) - 1
    $index = [math]::Max(0, [math]::Min($Values.Count - 1, $index))
    return $Values[$index]
}

$summary = [ordered]@{
    ground_truth_cases = $totalTruth
    matched_cases = $matched
    detection_coverage = [math]::Round($matched / [double]$totalTruth, 4)
    positives_in_ground_truth = @($truthRows | Where-Object { [int]$_.label -eq 1 }).Count
    negatives_in_ground_truth = @($truthRows | Where-Object { [int]$_.label -eq 0 }).Count
    brier_score = [math]::Round($brier, 6)
    expected_calibration_error = [math]::Round($ece, 6)
    sources = @($sources)
    latency_samples = $latencies.Count
    p50_latency_ms = if ($latencies.Count) { [math]::Round((Get-Percentile $latencies 0.50),2) } else { $null }
    p95_latency_ms = if ($latencies.Count) { [math]::Round((Get-Percentile $latencies 0.95),2) } else { $null }
}

$summaryPath = Join-Path $OutputDirectory 'summary.json'
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ''
Write-Host 'Calibration evaluation complete.'
Write-Host "Matched cases: $matched / $totalTruth"
Write-Host ("Detection coverage: {0:P2}" -f ($matched / [double]$totalTruth))
Write-Host ("Brier score: {0:N6}" -f $brier)
Write-Host ("ECE:         {0:N6}" -f $ece)
Write-Host "Sources:     $(@($sources) -join ', ')"
if (@($sources | Where-Object { $_ -ne 'jev' }).Count -gt 0) {
    Write-Warning 'This report contains non-jev sources. Do NOT describe the Brier/ECE values as raw Jev calibration unless the input was produced with --jev-only.'
}
if ($latencies.Count -gt 0) {
    Write-Host ("Latency p50:  {0:N2} ms" -f (Get-Percentile $latencies 0.50))
    Write-Host ("Latency p95:  {0:N2} ms" -f (Get-Percentile $latencies 0.95))
}
Write-Host "Joined CSV:  $joinedPath"
Write-Host "Bins CSV:    $binsPath"
Write-Host "Summary:     $summaryPath"
