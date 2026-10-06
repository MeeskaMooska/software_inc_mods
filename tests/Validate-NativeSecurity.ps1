param([string]$ManagedPath, [string]$BuildPath)
$ErrorActionPreference = 'Stop'
# Inspect the installed settings constructor; invoke only pure metadata
# restriction verifiers. Do not construct Settings or initialize Unity.
Add-Type -Path (Join-Path $ManagedPath 'Mono.Cecil.dll')
$taskGamePath = Join-Path $ManagedPath 'Assembly-CSharp.dll'
$taskGameAssembly = [Reflection.Assembly]::LoadFrom($taskGamePath)
$taskGameMetadata = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($taskGamePath)
$taskSettings = $taskGameMetadata.MainModule.Types | Where-Object FullName -eq 'DynamicCSharp.DynamicCSharp'
$taskConstructor = $taskSettings.Methods | Where-Object Name -eq '.ctor'
$taskRestrictions = @($taskConstructor.Body.Instructions | Where-Object {
    $_.OpCode.Name -eq 'newobj' -and $_.Operand.DeclaringType.FullName.StartsWith('DynamicCSharp.Security.')
} | ForEach-Object {
    if ($_.Previous.OpCode.Name -ne 'ldstr') { throw 'Native restriction construction changed.' }
    [Activator]::CreateInstance($taskGameAssembly.GetType($_.Operand.DeclaringType.FullName, $true), @([string]$_.Previous.Operand))
})
if ($taskRestrictions.Count -ne 5) { throw 'Native security profile changed; inspect it before release.' }
foreach ($taskFile in @('AICompute.dll', 'AICompute-Native.dll', 'SecurityRejectedFixture.dll')) {
    $taskAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $BuildPath $taskFile))
    $taskRejected = $false
    foreach ($taskRestriction in $taskRestrictions) {
        if (-not $taskRestriction.GetType().GetMethod('Verify').Invoke($taskRestriction, @($taskAssembly.MainModule))) { $taskRejected = $true }
    }
    if ($taskRejected -ne ($taskFile -eq 'SecurityRejectedFixture.dll')) { throw "Native security verification unexpected result: $taskFile" }
}
Write-Output 'PASS: installed-game security verifiers accept original/rewritten mod assemblies and reject the MemberInfo regression fixture. Unity was not executed.'
