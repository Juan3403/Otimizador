<#
  Gera o programa pronto para abrir na pasta "Executavel" (ao lado do código): abra Executavel\Otimizador.exe.
  É o build de Release (pede administrador). Não vai para o GitHub (a pasta está no .gitignore).

  Uso:   .\tools\atualizar-executavel.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$running = Get-Process Otimizador -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$root\Executavel\*" }
if ($running) { throw 'Feche o Otimizador (o da pasta Executavel) antes de atualizar.' }

$tmp = Join-Path $env:TEMP 'otimizador-publish'
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $tmp
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou' }
Set-Content (Join-Path $tmp 'update-repo.txt') 'Juan3403/Otimizador' -Encoding ASCII
& (Join-Path $PSScriptRoot 'get-pawnio.ps1') -Dest $tmp

robocopy $tmp (Join-Path $root 'Executavel') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
Remove-Item $tmp, (Join-Path $root 'bin\Release'), (Join-Path $root 'obj\Release') -Recurse -Force -ErrorAction SilentlyContinue

$exe = Join-Path $root 'Executavel\Otimizador.exe'
Write-Host "Pronto: $exe (versão $((Get-Item $exe).VersionInfo.FileVersion))"
