param(
    [string]$Root = 'C:\SharpML2CalibrationShare',
    [string]$ShareName = 'SharpML2Calibration',
    [int]$NoiseFileCount = 5000,
    [int]$PositiveCount = 200,
    [int]$NegativeCount = 800,
    [switch]$CreateShare,
    [switch]$Reset
)

$ErrorActionPreference = 'Stop'

if ($NoiseFileCount -lt 0 -or $PositiveCount -lt 1 -or $NegativeCount -lt 1) {
    throw 'NoiseFileCount must be non-negative and PositiveCount/NegativeCount must be at least 1.'
}

$fullRoot = [System.IO.Path]::GetFullPath($Root)
$rootPath = [System.IO.Path]::GetPathRoot($fullRoot)
if ($fullRoot.TrimEnd('\\') -eq $rootPath.TrimEnd('\\')) {
    throw "Refusing to use a drive root as the lab directory: $fullRoot"
}

if (Test-Path -LiteralPath $fullRoot) {
    if (-not $Reset) {
        throw "Calibration directory already exists: $fullRoot. Re-run with -Reset to recreate it."
    }
    Remove-Item -LiteralPath $fullRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $fullRoot -Force | Out-Null
$random = [System.Random]::new(20260918)
$alpha = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'

function New-RandomValue([int]$Length) {
    $chars = for ($i = 0; $i -lt $Length; $i++) { $alpha[$random.Next(0, $alpha.Length)] }
    -join $chars
}

function Write-CaseFile {
    param(
        [string]$CaseId,
        [int]$Label,
        [string]$Category,
        [string]$SecretType,
        [string]$Directory,
        [string]$FileName,
        [string[]]$Lines
    )

    $dir = Join-Path $fullRoot $Directory
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null
    $path = Join-Path $dir $FileName
    [System.IO.File]::WriteAllLines($path, $Lines)

    [pscustomobject]@{
        case_id = $CaseId
        label = $Label
        category = $Category
        secret_type = $SecretType
        relative_path = $path.Substring($fullRoot.Length).TrimStart('\\')
    }
}

Write-Host "Creating $NoiseFileCount ordinary files ..."
for ($i = 0; $i -lt $NoiseFileCount; $i++) {
    $batchIndex = [int][math]::Floor($i / 200)
    $dir = Join-Path $fullRoot ("Noise\dept-{0:D2}\batch-{1:D3}" -f ($i % 40), $batchIndex)
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null
    $path = Join-Path $dir ("record-{0:D6}.conf" -f $i)
    $content = @(
        "service=service-$($i % 80)",
        "region=uksouth",
        "owner=team-$($i % 25)",
        "timeout_seconds=$($random.Next(5,180))",
        "setting_a=$(New-RandomValue 24)",
        "setting_b=$(New-RandomValue 28)",
        "setting_c=$(New-RandomValue 18)"
    )
    [System.IO.File]::WriteAllLines($path, $content)
}

$truth = New-Object System.Collections.Generic.List[object]
$caseNo = 1

Write-Host "Creating $PositiveCount labelled positive candidates ..."
for ($i = 0; $i -lt $PositiveCount; $i++) {
    $caseId = 'CAL{0:D5}' -f $caseNo; $caseNo++
    $mode = $i % 3
    $value = New-RandomValue (28 + ($i % 17))
    if ($mode -eq 0) {
        $truth.Add((Write-CaseFile -CaseId $caseId -Label 1 -Category 'deployed-production' -SecretType 'password' -Directory ("Finance\Production\ERP\node-{0:D3}" -f $i) -FileName 'database.ini' -Lines @(
            '[database]',
            'environment=production',
            "case_id=$caseId",
            'username=svc_finance_lab',
            "password=$value",
            'enabled=true'
        )))
    }
    elseif ($mode -eq 1) {
        $truth.Add((Write-CaseFile -CaseId $caseId -Label 1 -Category 'deployed-production' -SecretType 'api_key' -Directory ("Platform\Production\Apps\app-{0:D3}" -f $i) -FileName 'settings.env' -Lines @(
            'APP_ENV=production',
            "case_id=$caseId",
            "API_KEY=$value",
            'ROTATION_REQUIRED=true'
        )))
    }
    else {
        $truth.Add((Write-CaseFile -CaseId $caseId -Label 1 -Category 'deployed-production' -SecretType 'token' -Directory ("Operations\Deploy\service-{0:D3}" -f $i) -FileName 'deploy.conf' -Lines @(
            'environment=production',
            "case_id=$caseId",
            "access_token=$value",
            'scope=deployment'
        )))
    }
}

Write-Host "Creating $NegativeCount labelled negative candidates ..."
$placeholders = @('changeme','test123','example123','replace_me_please','placeholder_value','not_a_real_secret')
for ($i = 0; $i -lt $NegativeCount; $i++) {
    $caseId = 'CAL{0:D5}' -f $caseNo; $caseNo++
    $bucket = $i % 4
    if ($bucket -eq 0) {
        $value = $placeholders[$i % $placeholders.Count]
        $truth.Add((Write-CaseFile -CaseId $caseId -Label 0 -Category 'placeholder' -SecretType 'password' -Directory ("Documentation\Examples\sample-{0:D3}" -f ($i % 100)) -FileName 'example.conf' -Lines @(
            '# Example configuration only',
            "case_id=$caseId",
            "password=$value",
            '# Replace before deployment'
        )))
    }
    elseif ($bucket -eq 1) {
        $value = New-RandomValue (30 + ($i % 10))
        $truth.Add((Write-CaseFile -CaseId $caseId -Label 0 -Category 'documentation-random-looking' -SecretType 'api_key' -Directory ("Docs\Tutorials\guide-{0:D3}" -f ($i % 100)) -FileName 'walkthrough.env' -Lines @(
            '# Documentation fixture - synthetic example',
            "case_id=$caseId",
            "api_key=$value",
            '# This value is not connected to any service'
        )))
    }
    elseif ($bucket -eq 2) {
        $value = New-RandomValue (26 + ($i % 12))
        $truth.Add((Write-CaseFile -CaseId $caseId -Label 0 -Category 'test-fixture' -SecretType 'token' -Directory ("Engineering\Test\Fixtures\suite-{0:D3}" -f ($i % 100)) -FileName 'fixture.yaml' -Lines @(
            'environment=test',
            "case_id=$caseId",
            "token=$value",
            'fixture_only=true'
        )))
    }
    else {
        # Deliberately difficult negative: production-like location and random-looking value,
        # but the surrounding record states it is disabled synthetic test material.
        $value = New-RandomValue (34 + ($i % 8))
        $truth.Add((Write-CaseFile -CaseId $caseId -Label 0 -Category 'hard-negative-production-path' -SecretType 'password' -Directory ("Finance\Production\MigrationFixtures\case-{0:D3}" -f ($i % 100)) -FileName 'legacy.ini' -Lines @(
            '# Synthetic migration fixture - not used for authentication',
            'enabled=false',
            "case_id=$caseId",
            "password=$value",
            'fixture_only=true'
        )))
    }
}

$truthPath = Join-Path $fullRoot '_sharpml2_ground_truth.csv'
$truth | Export-Csv -LiteralPath $truthPath -NoTypeInformation -Encoding UTF8

$manifest = [ordered]@{
    created_utc = [DateTime]::UtcNow.ToString('o')
    root = $fullRoot
    noise_files = $NoiseFileCount
    labelled_positive_candidates = $PositiveCount
    labelled_negative_candidates = $NegativeCount
    labelled_candidates = $PositiveCount + $NegativeCount
    positive_base_rate = [math]::Round($PositiveCount / [double]($PositiveCount + $NegativeCount), 4)
    ground_truth = [System.IO.Path]::GetFileName($truthPath)
    note = 'All credentials are synthetic and must never be used for authentication.'
}
$manifestPath = Join-Path $fullRoot '_sharpml2_calibration_manifest.json'
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

if ($CreateShare) {
    if (-not (Get-Command New-SmbShare -ErrorAction SilentlyContinue)) {
        throw 'New-SmbShare is unavailable. Run this on Windows with SMB server support.'
    }
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $existing = Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        if ($existing.Path -ne $fullRoot) {
            throw "SMB share '$ShareName' already exists at '$($existing.Path)'. Remove it manually or choose another ShareName."
        }
    }
    else {
        New-SmbShare -Name $ShareName -Path $fullRoot -ReadAccess $identity | Out-Null
    }
}

Write-Host ''
Write-Host 'Calibration corpus complete.'
Write-Host "Root:              $fullRoot"
Write-Host "Noise files:       $NoiseFileCount"
Write-Host "Labelled positives:$PositiveCount"
Write-Host "Labelled negatives:$NegativeCount"
Write-Host "Ground truth:      $truthPath"
Write-Host "Manifest:          $manifestPath"
if ($CreateShare) { Write-Host "UNC path:          \\localhost\$ShareName" }
Write-Host ''
Write-Host 'No planted value is connected to a real account or service.'
