$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..'))
dotnet --version
dotnet run --project .\tests\ArzType.Tests\ArzType.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
dotnet publish .\src\ArzType.App\ArzType.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o .\publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Write-Host 'Published to .\publish\ArzType.exe (single file, language packs included)'
