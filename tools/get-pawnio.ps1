<#
  Coloca o instalador oficial do driver PawnIO (temperatura de CPUs AMD Ryzen) na pasta Drivers\ do pacote.
  Baixa de github.com/namazso/PawnIO.Setup, confere o SHA-256 (o mesmo que o programa confere antes de executar)
  e guarda uma cópia em tools\cache para não baixar de novo.

  Uso (os scripts de build já chamam):  .\tools\get-pawnio.ps1 -Dest publish-folder
#>
param([Parameter(Mandatory)] [string] $Dest)
$ErrorActionPreference = 'Stop'

$version = '2.2.0'
$sha256 = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
$url = "https://github.com/namazso/PawnIO.Setup/releases/download/$version/PawnIO_setup.exe"

$cache = Join-Path $PSScriptRoot 'cache'
$file = Join-Path $cache 'PawnIO_setup.exe'
New-Item -ItemType Directory -Force $cache | Out-Null

function Test-Hash($path) { (Test-Path $path) -and ((Get-FileHash $path -Algorithm SHA256).Hash -eq $sha256) }

if (-not (Test-Hash $file)) {
    Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing
    if (-not (Test-Hash $file)) {
        Remove-Item $file -Force
        throw "O PawnIO_setup.exe baixado não confere com o SHA-256 esperado ($sha256)."
    }
}

$drivers = Join-Path $Dest 'Drivers'
New-Item -ItemType Directory -Force $drivers | Out-Null
Copy-Item $file (Join-Path $drivers 'PawnIO_setup.exe') -Force
@"
PawnIO - driver de entrada/saída (GPL-2.0) - copyright (C) namazso
Versão $version, instalador oficial, sem alteração.
Código-fonte e licença: https://github.com/namazso/PawnIO  -  site: https://pawnio.eu

O Otimizador usa este driver só para ler a temperatura de CPUs AMD Ryzen (e algumas Intel).
Ele é instalado apenas se você aceitar (ao instalar o programa ou no aviso do Monitor).
Para remover: Configurações do Windows > Aplicativos > PawnIO.
"@ | Set-Content (Join-Path $drivers 'LEIAME-PawnIO.txt') -Encoding UTF8
Write-Host "PawnIO $version incluído em $drivers"
