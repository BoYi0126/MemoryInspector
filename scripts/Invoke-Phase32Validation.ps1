[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SkipPerformanceMetrics
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "MemoryInspector.slnx"
$testProjects = @(
    "tests/MemoryInspector.Core.Tests/MemoryInspector.Core.Tests.csproj",
    "tests/MemoryInspector.Windows.Tests/MemoryInspector.Windows.Tests.csproj",
    "tests/MemoryInspector.IntegrationTests/MemoryInspector.IntegrationTests.csproj"
)
$performanceProjects = @(
    "tests/MemoryInspector.Windows.Tests/MemoryInspector.Windows.Tests.csproj",
    "tests/MemoryInspector.IntegrationTests/MemoryInspector.IntegrationTests.csproj"
)
$validationRunId = Get-Date -Format "yyyyMMdd-HHmmssfff"
$performanceResultsRoot = Join-Path `
    $repositoryRoot `
    "TestResults/Phase32/$validationRunId"

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

Push-Location $repositoryRoot

try {
    Invoke-DotNet @(
        "build",
        $solutionPath,
        "--configuration",
        $Configuration
    )

    foreach ($project in $testProjects) {
        Invoke-DotNet @(
            "test",
            $project,
            "--configuration",
            $Configuration,
            "--no-build",
            "--no-restore"
        )
    }

    if (-not $SkipPerformanceMetrics) {
        foreach ($project in $performanceProjects) {
            $projectName =
                [System.IO.Path]::GetFileNameWithoutExtension($project)
            $resultsDirectory = Join-Path `
                $performanceResultsRoot `
                $projectName
            $null = New-Item `
                -ItemType Directory `
                -Path $resultsDirectory `
                -Force

            Invoke-DotNet @(
                "test",
                $project,
                "--configuration",
                $Configuration,
                "--no-build",
                "--no-restore",
                "--results-directory",
                $resultsDirectory,
                "--diagnostic-output-directory",
                $resultsDirectory,
                "--",
                "--filter",
                "TestCategory=Performance",
                "--output",
                "Detailed",
                "--show-stdout",
                "All",
                "--no-ansi",
                "--progress",
                "off",
                "--diagnostic",
                "--diagnostic-verbosity",
                "Trace"
            )

            $metrics = Get-ChildItem `
                    -Path $resultsDirectory `
                    -Filter "*.diag" `
                    -File |
                Select-String `
                    -Pattern "METRIC [a-z_]+=-?[0-9]+(?:\.[0-9]+)?" `
                    -AllMatches |
                ForEach-Object {
                    $_.Matches.Value
                } |
                Sort-Object -Unique

            Write-Host ""
            Write-Host "Performance metrics ($projectName):"

            foreach ($metric in $metrics) {
                Write-Host "  $metric"
            }

            Write-Host "Diagnostic results: $resultsDirectory"
        }
    }
}
finally {
    Pop-Location
}
