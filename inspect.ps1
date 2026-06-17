$ErrorActionPreference = 'Stop'
$nuget = "C:\Users\f-cak\.nuget\packages"
$probe = @{}
# Sort so higher package versions win (descending), and prefer net10/net8 libs
Get-ChildItem $nuget -Recurse -Filter "*.dll" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match "\\lib\\(net10.0|net8.0|netstandard2.0)\\" } | Sort-Object FullName -Descending | ForEach-Object {
    if (-not $probe.ContainsKey($_.Name)) { $probe[$_.Name] = $_.FullName }
}
# Force System.ClientModel 1.12.0
$scm = Get-ChildItem "$nuget\system.clientmodel\1.12.0" -Recurse -Filter "System.ClientModel.dll" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match "net8.0" } | Select-Object -First 1
if ($scm) { $probe["System.ClientModel.dll"] = $scm.FullName; [System.Reflection.Assembly]::LoadFrom($scm.FullName) | Out-Null }
$onResolve = {
    param($s, $e)
    $name = ($e.Name -split ',')[0] + ".dll"
    if ($probe.ContainsKey($name)) {
        try { return [System.Reflection.Assembly]::LoadFrom($probe[$name]) } catch { return $null }
    }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($onResolve)

$dlls = @(
  "C:\Users\f-cak\.nuget\packages\azure.ai.agentserver.core\1.0.0-beta.24\lib\net8.0\Azure.AI.AgentServer.Core.dll",
  "C:\Users\f-cak\.nuget\packages\azure.ai.agentserver.responses\1.0.0-beta.5\lib\net8.0\Azure.AI.AgentServer.Responses.dll"
)
foreach ($dll in $dlls) {
  Write-Host "########## $dll"
  $asm = [System.Reflection.Assembly]::LoadFrom($dll)
  try { $types = $asm.GetExportedTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types }
  $types = $types | Where-Object { $_ -ne $null }
  foreach ($t in ($types | Where-Object { $_.Name -match "Extension" })) {
    Write-Host "TYPE: $($t.FullName)"
    try {
      $t.GetMethods('Public,Static,DeclaredOnly') | ForEach-Object {
        $ps = ($_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        Write-Host "   $($_.ReturnType.Name) $($_.Name)($ps)"
      }
    } catch { Write-Host "   <method enum failed: $($_.Exception.Message)>" }
  }
}
