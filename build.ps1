param([switch]$Test, [string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src/Viso/Viso.csproj'
$outputPath = Join-Path $PSScriptRoot $OutputDirectory
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $outputPath 'LEEME.md')
if ($Test) {
    $process = Start-Process -FilePath (Join-Path $outputPath 'Viso.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    Get-Content (Join-Path $outputPath 'self-test-result.txt')
    if ($process.ExitCode -ne 0) { throw 'Fallaron las pruebas.' }
}
Write-Host "Ejecutable listo: $outputPath/Viso.exe"
