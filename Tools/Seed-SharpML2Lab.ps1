param(
    [string]$Root = 'C:\SharpML2LabShare',
    [int]$NoiseFileCount = 15000,
    [int]$DecoyFileCount = 300,
    [int]$LinesPerNoiseFile = 24,
    [string]$ShareName = 'SharpML2Lab',
    [switch]$CreateShare,
    [switch]$Reset
)

$ErrorActionPreference = 'Stop'

if ($NoiseFileCount -lt 0 -or $DecoyFileCount -lt 0 -or $LinesPerNoiseFile -lt 1) {
    throw 'Counts must be non-negative and LinesPerNoiseFile must be at least 1.'
}

$fullRoot = [System.IO.Path]::GetFullPath($Root)
$rootPath = [System.IO.Path]::GetPathRoot($fullRoot)
if ($fullRoot.TrimEnd('\\') -eq $rootPath.TrimEnd('\\')) {
    throw "Refusing to use a drive root as the lab directory: $fullRoot"
}

if (Test-Path -LiteralPath $fullRoot) {
    if (-not $Reset) {
        throw "Lab directory already exists: $fullRoot. Re-run with -Reset to recreate it."
    }
    Remove-Item -LiteralPath $fullRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $fullRoot -Force | Out-Null

$random = [System.Random]::new(424242)
$extensions = @('.txt', '.ini', '.conf', '.config', '.json', '.yaml', '.env', '.properties', '.ps1', '.cs', '.sql', '.toml')
$regions = @('uksouth', 'ukwest', 'westeurope', 'northeurope')
$services = @('billing', 'reporting', 'inventory', 'crm', 'search', 'etl', 'portal', 'telemetry')
$owners = @('platform', 'finance-apps', 'data', 'operations', 'engineering')

function New-RandomAlphaNumeric([int]$Length) {
    $stringChars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
    $chars = for ($i = 0; $i -lt $Length; $i++) {
        $stringChars[$random.Next(0, $stringChars.Length)]
    }
    return -join $chars
}

Write-Host "Seeding $NoiseFileCount benign files under $fullRoot ..."

for ($i = 0; $i -lt $NoiseFileCount; $i++) {
    $dept = 'dept-{0:D2}' -f ($i % 50)
    $batchIndex = [int][math]::Floor($i / 250)
    $batch = 'batch-{0:D3}' -f $batchIndex
    $dir = Join-Path $fullRoot (Join-Path 'noise' (Join-Path $dept $batch))
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null

    $ext = $extensions[$i % $extensions.Count]
    $file = Join-Path $dir ('record-{0:D6}{1}' -f $i, $ext)

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("service=$($services[$random.Next($services.Count)])")
    [void]$sb.AppendLine("region=$($regions[$random.Next($regions.Count)])")
    [void]$sb.AppendLine("owner=$($owners[$random.Next($owners.Count)])")
    [void]$sb.AppendLine("timeout_seconds=$($random.Next(5, 180))")
    [void]$sb.AppendLine("enabled=$([bool]($random.Next(0, 2)))")

    for ($line = 0; $line -lt $LinesPerNoiseFile; $line++) {
        $name = 'setting_{0:D2}' -f $line
        $value = New-RandomAlphaNumeric 28
        [void]$sb.AppendLine("$name=$value")
    }

    [System.IO.File]::WriteAllText($file, $sb.ToString())
}

Write-Host "Adding $DecoyFileCount obvious placeholder-secret decoys ..."
$decoys = @(
    'password=changeme',
    'password=test123',
    'api_key=example123',
    'token=placeholder',
    'client_secret=replace_me_please'
)

for ($i = 0; $i -lt $DecoyFileCount; $i++) {
    $dir = Join-Path $fullRoot (Join-Path 'documentation' ('sample-{0:D3}' -f ($i % 40)))
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null
    $file = Join-Path $dir ('example-{0:D5}.conf' -f $i)
    $decoy = $decoys[$i % $decoys.Count]
    $content = @"
# Example configuration only
service=demo-service
region=uksouth
$decoy
# Do not use sample values in production
"@
    [System.IO.File]::WriteAllText($file, $content)
}

# Exactly one intended needle. It is synthetic and is not used to authenticate anywhere.
$needleDir = Join-Path $fullRoot 'Finance\Production\ERP\config'
[System.IO.Directory]::CreateDirectory($needleDir) | Out-Null
$needlePath = Join-Path $needleDir 'database.ini'
$needleMarker = 'SHARPML2_NEEDLE_20260918'
$needleValue = 'Z9vK3pR8mT2qW6xN4cL7sF1hJ5dB0gY2'
$needleContent = @"
[database]
environment=production
host=prod-db-lab.invalid
username=svc_finance_lab
needle_id=$needleMarker
password=$needleValue
connection_timeout=30
"@
[System.IO.File]::WriteAllText($needlePath, $needleContent)

$manifest = [ordered]@{
    root = $fullRoot
    noise_files = $NoiseFileCount
    decoy_files = $DecoyFileCount
    expected_needles = 1
    needle_marker = $needleMarker
    needle_relative_path = 'Finance\Production\ERP\config\database.ini'
    content_files = $NoiseFileCount + $DecoyFileCount + 1
    total_files_including_manifest = $NoiseFileCount + $DecoyFileCount + 2
}
$manifestPath = Join-Path $fullRoot '_sharpml2_lab_manifest.json'
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
Write-Host 'Lab seed complete.'
Write-Host "Root:            $fullRoot"
Write-Host "Content files:   $($manifest.content_files)"
Write-Host "Total files:     $($manifest.total_files_including_manifest) (including manifest)"
Write-Host "Needle marker:   $needleMarker"
Write-Host "Needle path:     $needlePath"
Write-Host "Manifest:        $manifestPath"
if ($CreateShare) {
    Write-Host "UNC path:        \\localhost\$ShareName"
}
Write-Host ''
Write-Host 'The synthetic password value is intentionally NOT printed.'
