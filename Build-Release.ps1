$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    $appOutput = Join-Path $PSScriptRoot 'outputs/Delta-2.1.11-Windows-x64'
    $payloadZip = Join-Path $PSScriptRoot 'outputs/Delta-2.1.11-Windows-x64.zip'
    dotnet restore src/Delta.Native/Delta.Native.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet publish src/Delta.Native/Delta.Native.csproj --no-restore -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $appOutput
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
    Compress-Archive -LiteralPath $appOutput -DestinationPath $payloadZip -Force
    dotnet publish installer-2.1.11/Delta.Setup.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o outputs/Delta-2.1.11-Installer
    if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }
} finally { Pop-Location }
