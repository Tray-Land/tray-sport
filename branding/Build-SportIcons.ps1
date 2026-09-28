<#
.SYNOPSIS
    Renders branding\sports\*.svg into Assets\Sports\<sport>.ico (white, for dark taskbars) and
    <sport>-dark.ico (black, for light taskbars), plus matching .svg copies for the settings picker.
.DESCRIPTION
    The SVGs are drawn in white. Each one is recolored to black for the light-taskbar variant, then
    both go through `winapp manifest update-assets` against a throwaway manifest, the same way the
    scaffold renders AppIcon-dark.ico. Rerun after editing or adding a sport glyph.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'Assets\Sports'
New-Item -ItemType Directory -Force $out | Out-Null

function Invoke-RenderIco([string]$svgPath, [string]$icoPath) {
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("sporticon-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Force (Join-Path $tmp 'Assets') | Out-Null
    try {
        Copy-Item (Join-Path $root 'Package.appxmanifest') $tmp
        Copy-Item (Join-Path $root 'Assets\AppIcon.ico') (Join-Path $tmp 'Assets')
        winapp manifest update-assets $svgPath --manifest (Join-Path $tmp 'Package.appxmanifest') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "winapp manifest update-assets failed for $svgPath" }
        Copy-Item (Join-Path $tmp 'Assets\AppIcon.ico') $icoPath -Force
    }
    finally {
        Remove-Item $tmp -Recurse -Force
    }
}

foreach ($svg in Get-ChildItem (Join-Path $PSScriptRoot 'sports') -Filter *.svg | Where-Object Name -notlike '*-dark.svg') {
    $name = $svg.BaseName
    $dark = Join-Path ([System.IO.Path]::GetTempPath()) "$name-dark-$([guid]::NewGuid()).svg"
    $text = [System.IO.File]::ReadAllText($svg.FullName) -replace '(?i)"(white|#fff|#ffffff)"', '"black"'
    [System.IO.File]::WriteAllText($dark, $text)
    try {
        Write-Host "==> $name"
        Invoke-RenderIco $svg.FullName (Join-Path $out "$name.ico")
        Invoke-RenderIco $dark (Join-Path $out "$name-dark.ico")

        # The settings picker draws the SVGs, which stay sharp at any size.
        Copy-Item $svg.FullName (Join-Path $out "$name.svg") -Force
        Copy-Item $dark (Join-Path $out "$name-dark.svg") -Force
    }
    finally {
        Remove-Item $dark -Force
    }
}
