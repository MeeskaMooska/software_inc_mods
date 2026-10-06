param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Software Inc'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskManaged = Join-Path $GamePath 'Software Inc_Data\Managed'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskPackage = Join-Path $taskRoot 'mods\AI Compute'
$taskBuild = Join-Path $taskRoot 'build'
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskArgs = @('/nologo', '/target:library', '/langversion:3', "/out:$taskBuild\AICompute.dll")
foreach ($taskReference in @('Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll', 'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.UI.dll')) {
    $taskLibrary = Join-Path $taskManaged $taskReference
    if (-not (Test-Path -LiteralPath $taskLibrary)) { throw "Missing installed game library: $taskLibrary" }
    $taskArgs += "/reference:$taskLibrary"
}
$taskArgs += Get-ChildItem -LiteralPath (Join-Path $taskPackage 'DLLMods\AI Compute') -Filter '*.cs' | ForEach-Object FullName
& $taskCompiler @taskArgs
if ($LASTEXITCODE -ne 0) { throw 'Code mod compilation failed.' }
# The game rewrites source before compiling. Compile that output too: a normal
# C# build cannot catch errors introduced by the native exception injector.
$taskPreprocessor = Join-Path $taskBuild 'NativeSourcePreprocessor.exe'
& $taskCompiler /nologo /target:exe /langversion:3 "/out:$taskPreprocessor" (Join-Path $taskRoot 'tests\NativeSourcePreprocessor.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native source preprocessor harness compilation failed.' }
$taskNativeSource = Join-Path $taskBuild 'native-source'
& $taskPreprocessor $taskManaged (Join-Path $taskPackage 'DLLMods\AI Compute') $taskNativeSource
if ($LASTEXITCODE -ne 0) { throw 'Native source preprocessing failed.' }
$taskNativeArgs = @('/nologo', '/target:library', '/langversion:3', "/out:$taskBuild\AICompute-Native.dll")
foreach ($taskReference in @('Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll', 'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.UI.dll')) {
    $taskNativeArgs += "/reference:$(Join-Path $taskManaged $taskReference)"
}
$taskNativeArgs += Get-ChildItem -LiteralPath $taskNativeSource -Filter '*.cs' | ForEach-Object FullName
& $taskCompiler @taskNativeArgs
if ($LASTEXITCODE -ne 0) { throw 'Game-preprocessed source compilation failed.' }
Write-Output 'PASS: installed-game source injection and rewritten-source compilation.'
$taskSecurityFixture = Join-Path $taskBuild 'SecurityRejectedFixture.dll'
& $taskCompiler /nologo /target:library /langversion:3 "/out:$taskSecurityFixture" (Join-Path $taskRoot 'tests\SecurityRejectedFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native security regression fixture compilation failed.' }
& (Join-Path $taskRoot 'tests\Validate-NativeSecurity.ps1') -ManagedPath $taskManaged -BuildPath $taskBuild
$taskRecoveryTest = Join-Path $taskBuild 'NativeUIRecoveryTests.exe'
& $taskCompiler /nologo /target:exe /langversion:3 "/out:$taskRecoveryTest" (Join-Path $taskNativeSource 'UIRecovery.cs') (Join-Path $taskRoot 'tests\NativeUIRecoveryTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Rewritten UI recovery test compilation failed.' }
& $taskRecoveryTest
if ($LASTEXITCODE -ne 0) { throw 'Rewritten UI recovery validation failed.' }
$taskTestArgs = @('/nologo', '/target:exe', '/langversion:3', "/out:$taskBuild\AIComputeTests.exe", "/reference:$taskManaged\Assembly-CSharp-firstpass.dll",
    (Join-Path $taskPackage 'DLLMods\AI Compute\AIRules.cs'), (Join-Path $taskPackage 'DLLMods\AI Compute\ExpansionRules.cs'), (Join-Path $taskPackage 'DLLMods\AI Compute\UIRecovery.cs'), (Join-Path $taskRoot 'tests\AIComputeTests.cs'))
& $taskCompiler @taskTestArgs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $taskBuild 'AIComputeTests.exe') $taskManaged $taskPackage
if ($LASTEXITCODE -ne 0) { throw 'Validation failed.' }
& (Join-Path $taskRoot 'tests\Validate-NativeIntegration.ps1') -GamePath $GamePath -BuildPath $taskBuild -PackagePath $taskPackage
$taskZip = Join-Path $taskBuild 'AI-Compute-0.3.0-source.zip'
Compress-Archive -LiteralPath (Join-Path $taskPackage 'Mods'), (Join-Path $taskPackage 'DLLMods'), (Join-Path $taskPackage 'README.md') -DestinationPath $taskZip -Force
Write-Output "Validated and packaged: $taskZip"
