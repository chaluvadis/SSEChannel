@echo off
echo SSE Channel Demo - MSTest Runner
echo =================================

echo Building test project...
dotnet build --configuration Release
if %errorlevel% neq 0 (
    echo Build failed!
    exit /b 1
)

echo.
echo Running all tests...
dotnet test --configuration Release --logger console --logger trx --verbosity normal

if %errorlevel% equ 0 (
    echo.
    echo All tests passed!
) else (
    echo.
    echo Some tests failed. Check the output above for details.
)

pause