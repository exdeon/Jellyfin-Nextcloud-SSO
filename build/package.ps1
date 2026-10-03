<#
.SYNOPSIS
    Собирает пакет плагина Nextcloud OAuth2 и генерирует manifest.json для каталога Jellyfin.

.DESCRIPTION
    1. Собирает проект (dotnet build -c Release).
    2. Складывает в stage: Jellyfin.Plugin.NextcloudOAuth2.dll + Logo.png + meta.json.
    3. Упаковывает stage в dist/<имя>-<версия>.zip.
    4. Считает MD5 архива (Jellyfin сравнивает через OrdinalIgnoreCase).
    5. Пишет manifest.json в корень репозитория — JSON-массив PackageInfo[]
       для репозитория Jellyfin (попадает в git, отдаётся GitHub по raw-URL).

    Требования Jellyfin (Emby.Server.Implementations/Updates/InstallationManager.cs):
      - манифест десериализуется как PackageInfo[], ключи в нижнем регистре;
      - targetAbi должен быть <= версии сервера, иначе версия отфильтруется;
      - sourceUrl обязан заканчиваться на .zip и быть доступен с сервера Jellyfin;
      - имя пакета должно совпадать с "name" в meta.json (по нему Jellyfin
        подчищает старые версии плагина при старте).

.EXAMPLE
    .\build\package.ps1
    .\build\package.ps1 -Version 1.1.0.0 -Changelog "Добавлена поддержка PKCE"
#>
[CmdletBinding()]
param(
    # Версия пакета. По умолчанию берётся из meta.json.
    [string]$Version,

    # Базовый URL репозитория для ассетов релизов, например https://github.com
    [string]$BaseUrl = 'https://github.com',

    # Хост raw-файлов (каталог и логотип должны отдаваться анонимно).
    [string]$RawBaseUrl = 'https://raw.githubusercontent.com',

    # Путь к репозиторию относительно BaseUrl/RawBaseUrl, exdeon/Jellyfin-Nextcloud-SSO
    [string]$RepoPath = 'exdeon/Jellyfin-Nextcloud-SSO',

    # Ветка, из которой raw-файлы отдаются каталогу.
    [string]$Branch = 'main',

    # Тег релиза, в котором лежит архив.
    [string]$Tag,

    # Текст изменений для manifest.json. По умолчанию берётся из build.yaml.
    [string]$Changelog,

    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$DistDir = Join-Path $Root 'dist'
$StageDir = Join-Path $DistDir 'stage'
$ProjectFile = Join-Path $Root 'NextcloudOAuth2.csproj'

function Read-Utf8Json([string]$Path) {
    $json = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false))
    return $json | ConvertFrom-Json
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    # BOM ломает десериализацию System.Text.Json на стороне Jellyfin.
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

# --- 1. Метаданные ---------------------------------------------------------

$MetaPath = Join-Path $Root 'meta.json'
$Meta = Read-Utf8Json $MetaPath

if (-not $Version) {
    if ($Meta.PSObject.Properties.Name -contains 'version' -and $Meta.version) {
        $Version = $Meta.version
    }
    else {
        throw 'meta.json не содержит поле version, и -Version не задан.'
    }
}

if (-not $Tag) {
    $Tag = "v$Version"
}

# build.yaml — YAML, а не JSON: разбираем нужные поля регулярными выражениями.
$BuildYamlPath = Join-Path $Root 'build.yaml'
$BuildYamlText = if (Test-Path $BuildYamlPath) { [IO.File]::ReadAllText($BuildYamlPath) } else { '' }

if (-not $Changelog) {
    $Changelog = ([regex] '(?m)^changelog:\s*"?([^"\r\n]+)"?').Match($BuildYamlText).Groups[1].Value
}
if (-not $Changelog) {
    $Changelog = $Version
}

$PackageName = $Meta.name
$GuidValue = ([guid]$Meta.guid).ToString('N')
$TargetAbi = $Meta.targetAbi

foreach ($Field in @('name', 'guid', 'targetAbi', 'category', 'owner')) {
    if (-not $Meta.$Field) {
        throw "meta.json не содержит поле $Field."
    }
}

# Сверяем версии meta.json / build.yaml, чтобы не раскатать каталог с другой версией.
$YamlVersion = ([regex] '(?m)^version:\s*"?([^"\r\n]+)"?').Match($BuildYamlText).Groups[1].Value
if ($YamlVersion -and $YamlVersion -ne $Version) {
    Write-Warning "build.yaml объявляет версию '$YamlVersion', а пакет собирается как '$Version'."
}

if ($Meta.version -and $Meta.version -ne $Version) {
    Write-Warning "meta.json содержит версию '$($Meta.version)', а пакет собирается как '$Version'."
}

# Версия в csproj нужна только для ручных сборок: упаковка передаёт -p:Version.
$CsprojVersion = ([regex] '<Version>([^<]+)</Version>').Match([IO.File]::ReadAllText($ProjectFile)).Groups[1].Value
if ($CsprojVersion -ne $Version) {
    Write-Warning "csproj объявляет <Version>$CsprojVersion</Version>, а пакет собирается как '$Version'. Обновите <Version> в csproj, иначе ручные сборки будут отдавать другую версию."
}

# --- 2. Сборка -------------------------------------------------------------

Write-Host "Сборка $PackageName $Version ($Configuration)..."
# -p:Version фиксирует AssemblyVersion = версии пакета, чтобы не зависеть от csproj.
$BuildOutput = & dotnet build $ProjectFile -c $Configuration "-p:Version=$Version" 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build завершился с ошибкой:`n$BuildOutput"
}
Write-Host 'Сборка успешна.'

$DllName = 'Jellyfin.Plugin.NextcloudOAuth2.dll'
$DllPath = Join-Path (Join-Path $Root 'bin') (Join-Path $Configuration (Join-Path 'net10.0' $DllName))
if (-not (Test-Path $DllPath)) {
    throw "Не найден собранный файл: $DllPath"
}

# Jellyfin сравнивает версию assembly (PluginInfo.Version) с версией из meta.json:
# Enable/Disable/Uninstall ищут плагин по ним, рассинхрон даёт 404 на странице плагина.
$AssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($DllPath).Version
$ExpectedVersion = [version]$Version
if ($ExpectedVersion.Revision -lt 0) {
    $ExpectedVersion = [version]::new($ExpectedVersion.Major, $ExpectedVersion.Minor, [math]::Max($ExpectedVersion.Build, 0), 0)
}
if ($AssemblyVersion -ne $ExpectedVersion) {
    throw "Версия сборки ($AssemblyVersion) не совпадает с версией пакета ($Version)."
}

# --- 3. Стейдж и архив -----------------------------------------------------

if (Test-Path $StageDir) {
    Remove-Item $StageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StageDir -Force | Out-Null

Copy-Item $DllPath (Join-Path $StageDir $DllName)

# Логотип плагина: meta.json ссылается на него через imagePath, Jellyfin отдаёт
# его по /Plugins/{guid}/{version}/Image (HasImage = imagePath непустой).
$LogoPath = Join-Path (Join-Path $Root 'Branding') 'Logo.png'
if (-not (Test-Path $LogoPath)) {
    throw "Не найден логотип: $LogoPath"
}
Copy-Item $LogoPath (Join-Path $StageDir 'Logo.png')

if (-not $Meta.imagePath) {
    Write-Warning 'meta.json не содержит imagePath — у плагина не будет логотипа в Dashboard.'
}

# meta.json попадает в архив с актуальной версией — Jellyfin читает его при загрузке плагина.
$Meta | Add-Member -NotePropertyName version -NotePropertyValue $Version -Force
Write-Utf8NoBom (Join-Path $StageDir 'meta.json') (ConvertTo-Json -InputObject $Meta -Depth 10)

# Compress-Archive пишет BOM в имена? нет, но нормализуем разделители путей:
$ZipName = 'Nextcloud-OAuth2-{0}.zip' -f $Version
$ZipPath = Join-Path $DistDir $ZipName
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path (Join-Path $StageDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal
Write-Host "Архив: $ZipPath"

# --- 4. Контрольная сумма --------------------------------------------------

$Checksum = (Get-FileHash -Algorithm MD5 -LiteralPath $ZipPath).Hash
$ZipSize = (Get-Item -LiteralPath $ZipPath).Length
Write-Host "MD5:   $Checksum ($ZipSize байт)"

# --- 5. manifest.json ------------------------------------------------------

$SourceUrl = '{0}/{1}/releases/download/{2}/{3}' -f $BaseUrl.TrimEnd('/'), $RepoPath.TrimEnd('/'), $Tag, $ZipName

# raw-URL каталога: манифест и логотип должны отдаваться анонимно любому серверу Jellyfin.
$RawUrl = '{0}/{1}/{2}' -f $RawBaseUrl.TrimEnd('/'), $RepoPath.TrimEnd('/'), $Branch.Trim('/')

# Логотип для карточки в каталоге: сервер скачивает его в папку плагина при установке.
$ImageUrl = "$RawUrl/Branding/Logo.png"

# Накапливаем версии из прошлого manifest.json: установленная версия должна
# находиться в каталоге, иначе страница плагина показывает «Unknown» вместо
# репозитория, а история релизов пропадает. Сортировка по убыванию — Jellyfin и
# веб-интерфейс считают versions[0] актуальной версией.
$ManifestPath = Join-Path $Root 'manifest.json'
$Versions = [System.Collections.Generic.List[object]]::new()

if (Test-Path $ManifestPath) {
    # Read-Utf8Json отдаёт JSON-массив одним объектом: присваиваем в переменную,
    # иначе @(<команда>) вкладывает массив и цикл ниже не находит пакет.
    $ExistingManifest = Read-Utf8Json $ManifestPath

    foreach ($Package in @($ExistingManifest)) {
        if (-not ($Package.PSObject.Properties.Name -contains 'guid')) { continue }
        if (([guid]$Package.guid).ToString('N') -ne $GuidValue) { continue }
        if (-not ($Package.PSObject.Properties.Name -contains 'versions')) { continue }

        foreach ($Entry in @($Package.versions)) {
            if (($Entry.PSObject.Properties.Name -contains 'version') -and $Entry.version -ne $Version) {
                $Versions.Add($Entry)
            }
        }
    }

    if ($Versions.Count -eq 0) {
        Write-Warning 'В прошлом manifest.json не найдено ни одной прошлой версии — история каталога потеряна.'
    }
}

$Versions.Add([ordered]@{
    version     = $Version
    targetAbi   = $TargetAbi
    sourceUrl   = $SourceUrl
    checksum    = $Checksum
    changelog   = $Changelog
})

$Versions = @($Versions | Sort-Object -Property @{ Expression = { [version]$_.version }; Descending = $true })

$Manifest = @(
    [ordered]@{
        name        = $PackageName
        description = "$PackageName — вход в Jellyfin через Nextcloud OAuth2 (SSO)."
        overview    = 'Позволяет пользователям входить в Jellyfin через Nextcloud.'
        owner       = $Meta.owner
        category    = $Meta.category
        guid        = $GuidValue
        imageUrl    = $ImageUrl
        versions    = $Versions
    }
)

$ManifestJson = ConvertTo-Json -InputObject $Manifest -Depth 10
# Windows PowerShell 5.1 при передаче массива через конвейер сворачивает его в объект —
# Jellyfin обязан получить массив PackageInfo[].
if (-not $ManifestJson.TrimStart().StartsWith('[')) {
    $ManifestJson = '[' + $ManifestJson + ']'
}

# manifest.json лежит в корне репозитория: он должен попасть в git,
# чтобы GitHub отдавал его по raw-URL.
Write-Utf8NoBom $ManifestPath $ManifestJson

# Убираем stage, он больше не нужен.
Remove-Item $StageDir -Recurse -Force

Write-Host ''
Write-Host 'Готово.'
Write-Host "  манифест: $ManifestPath"
Write-Host "  архив:    $ZipPath"
Write-Host "  sourceUrl: $SourceUrl"
Write-Host ''
Write-Host "1. Загрузите $ZipName как ассет релиза $Tag."
Write-Host '2. Закоммитьте manifest.json в ветку main.'
Write-Host '3. Добавьте в Jellyfin Dashboard -> Plugins -> Repositories URL:'
Write-Host "   $RawUrl/manifest.json"
