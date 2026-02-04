# Load all required assemblies
$simhubPath = 'C:\Program Files (x86)\SimHub'
[System.Reflection.Assembly]::LoadFrom("$simhubPath\GameReaderCommon.dll") | Out-Null
[System.Reflection.Assembly]::LoadFrom("$simhubPath\Newtonsoft.Json.dll") | Out-Null
$assembly = [System.Reflection.Assembly]::LoadFrom("$simhubPath\SimHub.Plugins.dll")

# Get PluginManager type
$pluginManagerType = $assembly.GetExportedTypes() | Where-Object { $_.Name -eq 'PluginManager' }

if ($pluginManagerType) {
    Write-Host "=== PluginManager PUBLIC METHODS ===" -ForegroundColor Green
    Write-Host ""

    $methods = $pluginManagerType.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::DeclaredOnly)

    foreach ($method in $methods | Sort-Object Name) {
        $params = $method.GetParameters()
        $paramString = ($params | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        Write-Host "$($method.ReturnType.Name) $($method.Name)($paramString)"
    }

    Write-Host ""
    Write-Host "=== PluginManager ALL METHODS (including internal) ===" -ForegroundColor Yellow
    Write-Host ""

    $allMethods = $pluginManagerType.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)

    foreach ($method in $allMethods | Where-Object { $_.Name -like '*Device*' -or $_.Name -like '*Display*' } | Sort-Object Name) {
        $params = $method.GetParameters()
        $paramString = ($params | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "

        $visibility = if ($method.IsPublic) { "PUBLIC" } elseif ($method.IsAssembly) { "INTERNAL" } else { "PRIVATE" }
        Write-Host "[$visibility] $($method.ReturnType.Name) $($method.Name)($paramString)"
    }
} else {
    Write-Host "PluginManager type not found!" -ForegroundColor Red
}
