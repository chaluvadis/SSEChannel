# PowerShell script to run MSTest tests with different configurations

param(
    [string]$TestCategory = "All",
    [switch]$Performance,
    [switch]$Integration,
    [switch]$Unit,
    [switch]$Verbose,
    [switch]$Coverage
)

Write-Host "SSE Channel Demo - MSTest Runner" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

# Build the project first
Write-Host "Building test project..." -ForegroundColor Yellow
dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Prepare test command
$testCommand = "dotnet test --configuration Release --logger console --logger trx"

# Add verbosity if requested
if ($Verbose) {
    $testCommand += " --verbosity detailed"
}

# Add code coverage if requested
if ($Coverage) {
    $testCommand += " --collect:`"XPlat Code Coverage`""
}

# Run tests based on category
switch ($TestCategory) {
    "All" {
        if ($Performance) {
            Write-Host "Running Performance Tests..." -ForegroundColor Cyan
            $testCommand += " --filter TestCategory=Performance"
        }
        elseif ($Integration) {
            Write-Host "Running Integration Tests..." -ForegroundColor Cyan
            $testCommand += " --filter FullyQualifiedName~Integration"
        }
        elseif ($Unit) {
            Write-Host "Running Unit Tests..." -ForegroundColor Cyan
            $testCommand += " --filter FullyQualifiedName!~Integration&TestCategory!=Performance"
        }
        else {
            Write-Host "Running All Tests..." -ForegroundColor Cyan
        }
    }
    default {
        Write-Host "Running tests with filter: $TestCategory" -ForegroundColor Cyan
        $testCommand += " --filter $TestCategory"
    }
}

Write-Host "Command: $testCommand" -ForegroundColor Gray
Write-Host ""

# Execute the test command
Invoke-Expression $testCommand

# Check results
if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "All tests passed!" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Some tests failed. Check the output above for details." -ForegroundColor Red
}

# Display test results location
$testResultsPath = Get-ChildItem -Path "TestResults" -Filter "*.trx" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($testResultsPath) {
    Write-Host "Test results saved to: $($testResultsPath.FullName)" -ForegroundColor Gray
}

exit $LASTEXITCODE