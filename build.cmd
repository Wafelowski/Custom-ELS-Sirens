@echo off
setlocal
cd /d "%~dp0"
where msbuild >nul 2>&1
if errorlevel 1 (
  echo Open a Visual Studio Developer Command Prompt with the .NET Framework 4.8 targeting pack installed.
  exit /b 1
)
msbuild CustomELSSirens.csproj /t:Rebuild /p:Configuration=Release /m /v:minimal
if errorlevel 1 exit /b 1
msbuild Tests\RegressionTests.csproj /t:Rebuild /p:Configuration=Release /m /v:minimal
if errorlevel 1 exit /b 1
Tests\bin\Release\CustomELSSirens.RegressionTests.exe
if errorlevel 1 exit /b 1
echo Plugin built and regression tests passed: bin\Release\CustomELSSirens.dll
