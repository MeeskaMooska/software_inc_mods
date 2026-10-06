param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Software Inc',
    [string[]]$TypeNames,
    [string]$MethodPattern = '.',
    [switch]$IncludeBody,
    [switch]$MembersOnly
)

# Read metadata and IL; do not load or execute the game assembly.
$ErrorActionPreference = 'Stop'
$managedPath = Join-Path $GamePath 'Software Inc_Data\Managed'
Add-Type -Path (Join-Path $managedPath 'Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managedPath 'Assembly-CSharp.dll'))

function Write-TypeInfo($type) {
    'TYPE ' + $type.FullName + ' BASE ' + $type.BaseType
    foreach ($iface in $type.Interfaces) { 'INTERFACE ' + $iface.InterfaceType.FullName }
    foreach ($field in $type.Fields) {
        'FIELD ' + $field.Attributes + ' ' + $field.FieldType + ' ' + $field.Name
        if ($field.HasConstant) { 'CONSTANT ' + $field.Name + ' = ' + $field.Constant }
    }
    foreach ($property in $type.Properties) { 'PROPERTY ' + $property.FullName }
    foreach ($method in $type.Methods) {
        if ($method.Name -notmatch $MethodPattern) { continue }
        'METHOD ' + $method.Attributes + ' ' + $method.FullName
        if ($IncludeBody -and $method.HasBody -and -not $MembersOnly) {
            foreach ($variable in $method.Body.Variables) { 'LOCAL ' + $variable.Index + ' ' + $variable.VariableType }
            foreach ($instruction in $method.Body.Instructions) { $instruction.ToString() }
        }
    }
}

foreach ($name in $TypeNames) {
    $type = $assembly.MainModule.Types | Where-Object { $_.FullName -eq $name }
    if (-not $type) {
        foreach ($parent in $assembly.MainModule.Types) {
            $type = $parent.NestedTypes | Where-Object { $_.FullName -eq $name }
            if ($type) { break }
        }
    }
    if (-not $type) { throw "Type not found: $name" }
    Write-TypeInfo $type
}
