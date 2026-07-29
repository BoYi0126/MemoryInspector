[CmdletBinding()]
param(
    [ValidatePattern(
        '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version = "1.0.0",

    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot,

    [switch]$SkipValidation,

    [switch]$SkipSmokeTest
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot "artifacts/release"
}

$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$releaseName = "MemoryInspector-$Version-$Runtime"
$packageDirectory = Join-Path $OutputRoot $releaseName
$symbolsDirectory = Join-Path `
    $OutputRoot `
    "$releaseName-symbols"
$packageArchive = Join-Path $OutputRoot "$releaseName.zip"
$symbolsArchive = Join-Path `
    $OutputRoot `
    "$releaseName-symbols.zip"
$buildDirectory = Join-Path `
    $OutputRoot `
    ".build-$releaseName"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullParent = (
        [System.IO.Path]::GetFullPath($Parent)
    ).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar

    if (-not $fullPath.StartsWith(
            $fullParent,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$fullPath' is outside '$fullParent'."
    }
}

function Reset-Directory {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Assert-ChildPath -Path $Path -Parent $OutputRoot

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    $null = New-Item -ItemType Directory -Path $Path
}

function Remove-OutputFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Assert-ChildPath -Path $Path -Parent $OutputRoot

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Copy-Documentation {
    param(
        [Parameter(Mandatory)]
        [string]$Destination
    )

    $documentation = @(
        "Architecture.md",
        "UserGuide.md",
        "ScannerGuide.md",
        "FilterPipelineGuide.md",
        "ScanTreeGuide.md",
        "TempStorageGuide.md",
        "PluginGuide.md",
        "Troubleshooting.md",
        "SecurityAndPrivacy.md"
    )

    $null = New-Item `
        -ItemType Directory `
        -Path $Destination `
        -Force

    foreach ($document in $documentation) {
        Copy-Item `
            -LiteralPath (Join-Path $repositoryRoot "docs/$document") `
            -Destination $Destination
    }
}

function Get-RelativePackagePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $packageRoot = $packageDirectory.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)

    if (-not $fullPath.StartsWith(
            $packageRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Package path '$fullPath' is outside '$packageRoot'."
    }

    return $fullPath.Substring($packageRoot.Length) `
        -replace '\\', '/'
}

function Assert-PackageContents {
    $requiredPaths = @(
        "MemoryInspector.Wpf.exe",
        "README.md",
        "README.en.md",
        "CHANGELOG.md",
        "LICENSE",
        "release-manifest.json",
        "docs/UserGuide.md",
        "docs/Architecture.md",
        "samples/MemoryInspector.SamplePlugin/plugin.json",
        "samples/MemoryInspector.SamplePlugin/MemoryInspector.SamplePlugin.dll",
        "tools/MemoryInspector.TestTarget/MemoryInspector.TestTarget.exe"
    )

    foreach ($relativePath in $requiredPaths) {
        $path = Join-Path `
            $packageDirectory `
            ($relativePath -replace '/', '\')

        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required package file is missing: $relativePath"
        }
    }

    $forbidden = Get-ChildItem `
            -LiteralPath $packageDirectory `
            -Recurse `
            -Force |
        Where-Object {
            $relative = Get-RelativePackagePath $_.FullName
            $relative -match '(^|/)(TestResults|bin|obj|\.vs)(/|$)' -or
            $relative -match '\.(tmp|diag|trx)$' -or
            $relative -match '\.tmp-'
        }

    if ($forbidden) {
        $paths = $forbidden |
            ForEach-Object {
                Get-RelativePackagePath $_.FullName
            }
        throw "Forbidden package content: $($paths -join ', ')"
    }

    $pdbFiles = Get-ChildItem `
        -LiteralPath $packageDirectory `
        -Recurse `
        -File `
        -Filter "*.pdb"

    if ($pdbFiles) {
        throw "The main package contains PDB files."
    }
}

function Write-HashSidecar {
    param(
        [Parameter(Mandatory)]
        [string]$Archive
    )

    $hash = Get-FileHash `
        -LiteralPath $Archive `
        -Algorithm SHA256
    $line = "$($hash.Hash.ToLowerInvariant())  " +
        [System.IO.Path]::GetFileName($Archive)
    Set-Content `
        -LiteralPath "$Archive.sha256" `
        -Value $line `
        -Encoding ascii
}

function Invoke-WpfSmokeTest {
    $applicationPath = Join-Path `
        $packageDirectory `
        "MemoryInspector.Wpf.exe"
    $process = Start-Process `
        -FilePath $applicationPath `
        -PassThru `
        -WindowStyle Hidden

    try {
        Start-Sleep -Seconds 5
        $process.Refresh()

        if ($process.HasExited) {
            throw "Packaged WPF application exited during startup " +
                "with code $($process.ExitCode)."
        }

        Write-Host (
            "WPF smoke test started PID $($process.Id), " +
            "working set $($process.WorkingSet64) bytes.")

        $closed = $process.CloseMainWindow()

        if ($closed) {
            $null = $process.WaitForExit(5000)
        }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id
            $process.WaitForExit()
        }
    }

    if ($process.ExitCode -ne 0) {
        throw "Packaged WPF application smoke test exited with " +
            "code $($process.ExitCode)."
    }
}

function Invoke-TestTargetSmokeTest {
    $testTargetPath = Join-Path `
        $packageDirectory `
        "tools/MemoryInspector.TestTarget/MemoryInspector.TestTarget.exe"
    $output = @(
        "GET",
        "EXIT"
    ) | & $testTargetPath

    if ($LASTEXITCODE -ne 0) {
        throw "Packaged Test Target exited with code $LASTEXITCODE."
    }

    $expectedPrefixes = @(
        "READY|",
        "VALUES|123456789|12.5",
        "BYE"
    )

    foreach ($prefix in $expectedPrefixes) {
        if (-not ($output | Where-Object {
                    $_.StartsWith(
                        $prefix,
                        [System.StringComparison]::Ordinal)
                })) {
            throw "Packaged Test Target did not emit '$prefix'."
        }
    }

    Write-Host "Test Target smoke test passed."
}

$null = New-Item `
    -ItemType Directory `
    -Path $OutputRoot `
    -Force
Reset-Directory $packageDirectory
Reset-Directory $symbolsDirectory
Reset-Directory $buildDirectory
Remove-OutputFile $packageArchive
Remove-OutputFile $symbolsArchive
Remove-OutputFile "$packageArchive.sha256"
Remove-OutputFile "$symbolsArchive.sha256"

Push-Location $repositoryRoot

try {
    if (-not $SkipValidation) {
        & (Join-Path `
            $repositoryRoot `
            "scripts/Invoke-Phase32Validation.ps1") `
            -Configuration Release `
            -SkipPerformanceMetrics

        if ($LASTEXITCODE -ne 0) {
            throw "Phase 32 validation failed."
        }
    }

    Invoke-DotNet @(
        "publish",
        "src/MemoryInspector.Wpf/MemoryInspector.Wpf.csproj",
        "--configuration",
        "Release",
        "--runtime",
        $Runtime,
        "--self-contained",
        "true",
        "--output",
        $packageDirectory,
        "-p:Version=$Version",
        "-p:PublishSingleFile=false",
        "-p:PublishTrimmed=false",
        "-p:PublishReadyToRun=false",
        "-p:DebugType=portable",
        "-p:DebugSymbols=true"
    )

    $samplePublishDirectory = Join-Path `
        $buildDirectory `
        "sample-plugin"
    Invoke-DotNet @(
        "publish",
        "samples/MemoryInspector.SamplePlugin/MemoryInspector.SamplePlugin.csproj",
        "--configuration",
        "Release",
        "--output",
        $samplePublishDirectory,
        "-p:Version=$Version",
        "-p:DebugType=portable",
        "-p:DebugSymbols=true"
    )

    $sampleDestination = Join-Path `
        $packageDirectory `
        "samples/MemoryInspector.SamplePlugin"
    $null = New-Item `
        -ItemType Directory `
        -Path $sampleDestination `
        -Force

    foreach ($fileName in @(
            "plugin.json",
            "MemoryInspector.SamplePlugin.dll",
            "MemoryInspector.SamplePlugin.deps.json",
            "MemoryInspector.SamplePlugin.pdb")) {
        $source = Join-Path $samplePublishDirectory $fileName

        if (Test-Path -LiteralPath $source) {
            Copy-Item `
                -LiteralPath $source `
                -Destination $sampleDestination
        }
    }

    $testTargetDestination = Join-Path `
        $packageDirectory `
        "tools/MemoryInspector.TestTarget"
    Invoke-DotNet @(
        "publish",
        "tests/MemoryInspector.TestTarget/MemoryInspector.TestTarget.csproj",
        "--configuration",
        "Release",
        "--runtime",
        $Runtime,
        "--self-contained",
        "true",
        "--output",
        $testTargetDestination,
        "-p:Version=$Version",
        "-p:PublishSingleFile=false",
        "-p:PublishTrimmed=false",
        "-p:DebugType=portable",
        "-p:DebugSymbols=true"
    )

    Copy-Item `
        -LiteralPath "README.md" `
        -Destination $packageDirectory
    Copy-Item `
        -LiteralPath "README.en.md" `
        -Destination $packageDirectory
    Copy-Item `
        -LiteralPath "CHANGELOG.md" `
        -Destination $packageDirectory
    Copy-Item `
        -LiteralPath "LICENSE" `
        -Destination $packageDirectory
    Copy-Documentation `
        -Destination (Join-Path $packageDirectory "docs")

    $pdbFiles = Get-ChildItem `
        -LiteralPath $packageDirectory `
        -Recurse `
        -File `
        -Filter "*.pdb"

    foreach ($pdbFile in $pdbFiles) {
        $relativePath = Get-RelativePackagePath $pdbFile.FullName
        $symbolPath = Join-Path `
            $symbolsDirectory `
            ($relativePath -replace '/', '\')
        $symbolParent = Split-Path -Parent $symbolPath
        $null = New-Item `
            -ItemType Directory `
            -Path $symbolParent `
            -Force
        Copy-Item `
            -LiteralPath $pdbFile.FullName `
            -Destination $symbolPath
        Remove-Item -LiteralPath $pdbFile.FullName -Force
    }

    $manifestFiles = Get-ChildItem `
            -LiteralPath $packageDirectory `
            -Recurse `
            -File |
        Sort-Object FullName |
        ForEach-Object {
            [ordered]@{
                path = Get-RelativePackagePath $_.FullName
                size = $_.Length
                sha256 = (
                    Get-FileHash `
                        -LiteralPath $_.FullName `
                        -Algorithm SHA256
                ).Hash.ToLowerInvariant()
            }
        }

    $manifest = [ordered]@{
        schemaVersion = 1
        product = "MemoryInspector"
        version = $Version
        runtime = $Runtime
        architecture = "x64"
        deployment = "self-contained"
        entryPoint = "MemoryInspector.Wpf.exe"
        generatedUtc = (
            Get-Date
        ).ToUniversalTime().ToString("O")
        files = @($manifestFiles)
    }
    $manifest |
        ConvertTo-Json -Depth 5 |
        Set-Content `
            -LiteralPath (
                Join-Path `
                    $packageDirectory `
                    "release-manifest.json") `
            -Encoding utf8

    Assert-PackageContents

    if (-not $SkipSmokeTest) {
        Invoke-TestTargetSmokeTest
        Invoke-WpfSmokeTest
    }

    Compress-Archive `
        -LiteralPath $packageDirectory `
        -DestinationPath $packageArchive `
        -CompressionLevel Optimal
    Compress-Archive `
        -LiteralPath $symbolsDirectory `
        -DestinationPath $symbolsArchive `
        -CompressionLevel Optimal

    Write-HashSidecar $packageArchive
    Write-HashSidecar $symbolsArchive

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead(
        $packageArchive)

    try {
        $forbiddenEntries = $archive.Entries |
            Where-Object {
                $_.FullName -match `
                    '(^|/)(TestResults|bin|obj|\.vs)(/|$)' -or
                $_.FullName -match '\.(pdb|tmp|diag|trx)$' -or
                $_.FullName -match '\.tmp-'
            }

        if ($forbiddenEntries) {
            throw "The release archive contains forbidden files."
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Host ""
    Write-Host "Release package created:"
    Write-Host "  $packageArchive"
    Write-Host "  $packageArchive.sha256"
    Write-Host "  $symbolsArchive"
    Write-Host "  $symbolsArchive.sha256"
}
finally {
    Pop-Location

    if (Test-Path -LiteralPath $buildDirectory) {
        Assert-ChildPath `
            -Path $buildDirectory `
            -Parent $OutputRoot
        Remove-Item `
            -LiteralPath $buildDirectory `
            -Recurse `
            -Force
    }
}
