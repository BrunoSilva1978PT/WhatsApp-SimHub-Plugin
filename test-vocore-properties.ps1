# Carregar DLLs
$simhubPath = 'C:\Program Files (x86)\SimHub'
[System.Reflection.Assembly]::LoadFrom("$simhubPath\SimHub.Plugins.dll") | Out-Null

# Obter o tipo VOCORESettings
$type = [SimHub.Plugins.OutputPlugins.GraphicalDash.VOCORESettings]

Write-Host "=== VOCORESettings Properties ===" -ForegroundColor Green
$type.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) |
    Where-Object { $_.Name -like "*Overlay*" -or $_.Name -like "*Dashboard*" } |
    ForEach-Object {
        Write-Host "$($_.PropertyType.Name) $($_.Name) { get; set; }"
    }

Write-Host "`n=== BitmapDisplaySettings Properties ===" -ForegroundColor Yellow
$baseType = $type.BaseType
$baseType.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) |
    Where-Object { $_.Name -like "*Overlay*" -or $_.Name -like "*Dashboard*" } |
    ForEach-Object {
        Write-Host "$($_.PropertyType.Name) $($_.Name) { get; set; }"
    }
