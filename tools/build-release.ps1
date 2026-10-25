<#
  Gera os dois pacotes (instalador e portátil) e o SHA256SUMS.txt na pasta dist\.
  O app só aceita atualização cujo arquivo bate com esse SHA256SUMS.txt, então os três arquivos vão juntos na Release.

  Uso:   .\tools\build-release.ps1 -Version 1.2.0
  Depois (com o GitHub CLI):
         gh release create v1.2.0 dist\* --title "Otimizador 1.2.0" --notes "O que mudou"
  Ou só crie a tag e faça push: o workflow .github/workflows/release.yml faz tudo sozinho.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Repo = "Juan3403/Otimizador"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versão inválida '$Version' (use 1.2.3)" }

# 1) versão no projeto
$csproj = Join-Path $root 'Otimizador.csproj'
$xml = Get-Content $csproj -Raw
$xml = $xml -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
$xml = $xml -replace '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
$xml = $xml -replace '<FileVersion>[^<]*</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
Set-Content $csproj $xml -Encoding UTF8

# 2) publicação
Remove-Item publish-folder, dist -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish-folder
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou' }
if ($Repo) { Set-Content (Join-Path $root 'publish-folder\update-repo.txt') $Repo -Encoding ASCII }

# 3) instalador (Inno Setup)
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado (winget install JRSoftware.InnoSetup)' }
& $iscc /Q "/DAppVersion=$Version" (Join-Path $root 'installer\Otimizador.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC falhou' }

# 4) portátil
$port = Join-Path $root 'dist\Otimizador-Portatil'
Copy-Item publish-folder $port -Recurse
Copy-Item README.md, LICENSE $port
Set-Content (Join-Path $port 'portable.txt') 'Remova este arquivo para usar a pasta de dados padrao do Windows.' -Encoding UTF8
Remove-Item (Join-Path $port 'Dados') -Recurse -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $port -DestinationPath (Join-Path $root "dist\Otimizador-Portatil-$Version.zip") -Force
Remove-Item $port -Recurse -Force

# 5) checksums
$lines = Get-ChildItem (Join-Path $root 'dist') -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
}
Set-Content (Join-Path $root 'dist\SHA256SUMS.txt') $lines -Encoding ASCII

Get-ChildItem (Join-Path $root 'dist') | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
Write-Host "`nPronto. Publique com:  gh release create v$Version dist\* --title `"Otimizador $Version`" --notes `"...`""

