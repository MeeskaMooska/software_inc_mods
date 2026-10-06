param([string]$GamePath, [string]$BuildPath, [string]$PackagePath)
$ErrorActionPreference = 'Stop'
$taskManaged = Join-Path $GamePath 'Software Inc_Data\Managed'
Add-Type -Path (Join-Path $taskManaged 'Mono.Cecil.dll')
$taskGame = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $taskManaged 'Assembly-CSharp.dll'))
$taskMod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $BuildPath 'AICompute.dll'))
$taskController = $taskMod.MainModule.Types | Where-Object FullName -eq 'SDSI.AI.AIComputeController'
[xml]$taskPanel = Get-Content -LiteralPath (Join-Path $PackagePath 'DLLMods\AI Compute\TrainingPanel.xml') -Raw
$taskCallbacks = $taskPanel.SelectNodes('//*[@onClick or @OnSelectedChanged]')
$taskVerified = 0
$taskDataPackage = $taskGame.MainModule.Types | Where-Object FullName -eq 'ModPackage'
$taskDataValidation = $taskDataPackage.Methods | Where-Object Name -eq 'OtherErrors'
if (-not ($taskDataValidation.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq ' has same software type defined twice' })) { throw 'Native company software uniqueness validation changed.' }
$taskVerified++
foreach ($taskElement in $taskCallbacks) {
    $taskAction = if ($taskElement.HasAttribute('onClick')) { $taskElement.GetAttribute('onClick') } else { $taskElement.GetAttribute('OnSelectedChanged') }
    $taskName = $taskAction.Split('(')[0]
    if (-not ($taskController.Methods | Where-Object { $_.IsPublic -and $_.Name -eq $taskName })) { throw "UI callback is missing: $taskAction" }
    $taskVerified++
}
# Read IL only. No Unity initialization or game-method execution.
$taskDate = $taskGame.MainModule.Types | Where-Object FullName -eq 'SDateTime'
$taskYear = $taskDate.Methods | Where-Object Name -eq 'get_RealYear'
if (-not ($taskYear.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldc.i4' -and $_.Operand -eq 1900 })) { throw 'Calendar year conversion changed in installed build.' }
$taskTier = $taskController.Methods | Where-Object Name -Match 'RefreshTiers|StartTraining|DiscoverRivalTraining|InitializeTraining'
$taskYearCalls = @($taskTier | ForEach-Object { $_.Body.Instructions } | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'SDateTime::get_RealYear' })
# Delegate bodies may live on generated display classes; check entire assembly.
$taskBadYears = @(& { foreach ($taskType in $taskMod.MainModule.Types) { foreach ($taskMethod in $taskType.Methods) { if ($taskMethod.HasBody -and $taskMethod.Name -notmatch 'OnHour|OnMonth') { foreach ($taskInstruction in $taskMethod.Body.Instructions) { if ($taskInstruction.OpCode.Name -eq 'ldfld' -and $taskInstruction.Operand.ToString() -eq 'System.Int32 SDateTime::Year') { $taskMethod.FullName } } } } } })
if ($taskBadYears.Count -gt 0) { throw "Unlock dates use the year offset: $taskBadYears" }
if ($taskYearCalls.Count -lt 2) { throw 'Calendar unlock adapter missing.' }
$taskAlpha = $taskGame.MainModule.Types | Where-Object FullName -eq 'SoftwareAlpha'
$taskPromote = $taskAlpha.Methods | Where-Object Name -eq 'PromoteAction'
$taskInstructions = @($taskPromote.Body.Instructions)
$taskProgressCall = $taskInstructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Double SoftwareAlpha::GetCodeArtProgress()' -and $_.Next.OpCode.Name -eq 'ldc.r8' -and $_.Next.Operand -eq 0 -and $_.Next.Next.OpCode.Name -match '^ble' } | Select-Object -First 1
if (-not $taskProgressCall -or $taskProgressCall.Next.OpCode.Name -ne 'ldc.r8' -or $taskProgressCall.Next.Operand -ne 0 -or $taskProgressCall.Next.Next.OpCode.Name -notmatch '^ble') { throw 'Native zero-progress promotion gate changed.' }
$taskApply = $taskController.Methods | Where-Object Name -eq 'ApplyTraining'
if (-not ($taskApply.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'ExpansionRules::NativeProgress' })) { throw 'Training no longer enforces the native promotion gate.' }
$taskVerified += 4
# Native visionary focus is a SoftwareType interest, not an Employee.LeadSpecs
# management specialization. Confirm both founder selection and hired generation.
$taskCustomization = $taskGame.MainModule.Types | Where-Object FullName -eq 'ActorCustomization'
$taskFocusFilter = $taskCustomization.NestedTypes.Methods | Where-Object { $_.Name -match '^<UpdateLeadFocusCombo>.*' -and ($_.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Boolean SoftwareType::OneClient' }) }
$taskFocusIL = @($taskFocusFilter.Body.Instructions | ForEach-Object { $_.ToString() })
if ($taskFocusIL.Count -ne 5 -or $taskFocusIL[1] -notmatch 'ldfld System.Boolean SoftwareType::OneClient$' -or $taskFocusIL[2] -notmatch 'ldc.i4.0$' -or $taskFocusIL[3] -notmatch 'ceq$') { throw 'Founder focus eligibility changed; review AI integration.' }
$taskVerified++
$taskFocusCombo = $taskCustomization.Methods | Where-Object Name -eq 'UpdateLeadFocusCombo'
if (-not ($taskFocusCombo.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'GameData::AllSoftwareTypes' })) { throw 'Founder focus no longer enumerates enabled data mods.' }
$taskVerified++
$taskGenerate = $taskCustomization.Methods | Where-Object Name -eq 'GenerateActors'
if (-not ($taskGenerate.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.ToString() -eq 'System.String Employee::LeadSpecPick' -and $_.Previous.Operand.ToString() -eq 'System.String FounderManager/FounderDescriptor::LeadFocus' })) { throw 'Founder software focus is no longer carried into the employee.' }
$taskVerified++
$taskEmployee = $taskGame.MainModule.Types | Where-Object FullName -eq 'Employee'
$taskRandomFilter = $taskEmployee.NestedTypes.Methods | Where-Object { $_.Name -match '^<RandomizeLeadSpec>.*' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'SoftwareType' }
if (-not ($taskRandomFilter.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Boolean SoftwareType::OneClient' }) -or -not ($taskRandomFilter.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Boolean SoftwareType::IsUnlocked(System.Int32)' })) { throw 'Hired visionary focus eligibility changed.' }
$taskVerified++
$taskListView = $taskGame.MainModule.Types | Where-Object FullName -eq 'GUIListView'
$taskToggleMod = $taskListView.Methods | Where-Object { $_.Name -eq 'ToggleMod' -and $_.Parameters[0].ParameterType.FullName -eq 'ModPackage' }
if (-not ($taskToggleMod.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Void ActorCustomization::UpdateLeadFocusCombo()' })) { throw 'Data mod selection no longer refreshes founder interests.' }
$taskVerified++
$taskOpen = $taskController.Methods | Where-Object Name -eq 'OpenPanel'
if (-not ($taskOpen.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'AIComputeController::WireTrainingUI' })) { throw 'Panel callbacks are not bound after initialization.' }
$taskVerified++
$taskWire = $taskController.Methods | Where-Object Name -eq 'WatchCombo'
if (-not ($taskWire.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'UnityEvent::AddListener' })) { throw 'Native combobox callback binding missing.' }
$taskVerified++
$taskSafe = $taskController.Methods | Where-Object Name -eq 'SafeSelection'
if (-not ($taskSafe.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'System.Math::Max' }) -or -not ($taskSafe.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'System.Math::Min' })) { throw 'Combobox selection bounds are missing.' }
$taskVerified++
$taskLoad = $taskMod.MainModule.Types | Where-Object FullName -eq 'SDSI.AI.TrainingLoad'
if (-not ($taskLoad.Interfaces | Where-Object { $_.InterfaceType.FullName -eq 'ILossable' })) { throw 'Training has no native server-cost attribution.' }
$taskVerified++
if ($taskPanel.SelectNodes('//Combo[@OnSelectedChanged]').Count -ne 0) { throw 'Early XML combobox callbacks can re-enter initialization.' }
$taskVerified++
$taskUIInit = @($taskController.Methods | Where-Object Name -match 'InitializeTrainingUI') + @($taskController.NestedTypes | Where-Object FullName -match 'InitializeTrainingUI' | ForEach-Object { $_.Methods })
foreach ($taskInstruction in $taskUIInit.Body.Instructions) {
    if ($taskInstruction.OpCode.Name -eq 'ldstr' -and $taskInstruction.Operand -match '^[A-Za-z][A-Za-z0-9]*$' -and -not $taskPanel.SelectSingleNode("//*[@id='$($taskInstruction.Operand)']")) { throw "Training view references a missing UI id: $($taskInstruction.Operand)" }
}
$taskVerified++
$taskLoss = $taskLoad.Methods | Where-Object Name -eq 'AddLoss'
if ($taskLoss.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'MakeTransaction|AddTransaction' }) { throw 'Cost attribution must not charge the company twice.' }
$taskVerified++
# Layout XML tags decorate their current object; they do not create containers.
# Derive the tag sets from the installed builder without initializing Unity.
$taskWindowManager = $taskGame.MainModule.Types | Where-Object FullName -eq 'WindowManager'
$taskFactory = $taskWindowManager.Methods | Where-Object Name -eq '.cctor'
function Get-NativeTagNames([string]$fieldName) {
    $taskInstructions = @($taskFactory.Body.Instructions)
    $taskStart = 0
    for ($taskIndex = 0; $taskIndex -lt $taskInstructions.Count; $taskIndex++) {
        $taskInstruction = $taskInstructions[$taskIndex]
        if ($taskInstruction.OpCode.Name -eq 'stsfld') {
            if ($taskInstruction.Operand.Name -eq $fieldName) {
                return @($taskInstructions[$taskStart..$taskIndex] | Where-Object { $_.OpCode.Name -eq 'ldstr' } | ForEach-Object Operand)
            }
            $taskStart = $taskIndex + 1
        }
    }
    throw "Native XML tag dictionary missing: $fieldName"
}
$taskObjectTags = Get-NativeTagNames '_objectTags'
$taskLayoutTags = Get-NativeTagNames '_layoutTags'
function Get-PanelLayoutError([xml]$document) {
    $taskSeen = @{}
    foreach ($taskNode in $document.SelectNodes('//*')) {
        $taskTag = $taskNode.Name.ToLowerInvariant()
        if ($taskTag -notin $taskObjectTags -and $taskTag -notin $taskLayoutTags) { return "Unknown native XML tag: $taskTag" }
        if ($taskTag -notin @('horizontallayout', 'verticallayout', 'gridlayout', 'contentfitter', 'layoutelement')) { continue }
        $taskOwner = $taskNode.ParentNode
        while ($taskOwner -is [System.Xml.XmlElement] -and $taskOwner.Name.ToLowerInvariant() -notin $taskObjectTags) { $taskOwner = $taskOwner.ParentNode }
        if ($taskOwner -isnot [System.Xml.XmlElement]) { return "Layout tag has no object container: $taskTag" }
        if (-not $taskSeen.ContainsKey($taskOwner)) { $taskSeen[$taskOwner] = [System.Collections.Generic.HashSet[string]]::new() }
        $taskComponent = if ($taskTag -in @('horizontallayout', 'verticallayout', 'gridlayout')) { 'LayoutGroup' } else { $taskTag }
        if (-not $taskSeen[$taskOwner].Add($taskComponent)) { return "Conflicting $taskComponent components on <$($taskOwner.Name)>; use an Empty container for a nested row." }
    }
    return $null
}
$taskLayoutError = Get-PanelLayoutError $taskPanel
if ($taskLayoutError) { throw $taskLayoutError }
$taskVerified++
[xml]$taskBrokenLayout = '<Window><ScrollView><VerticalLayout><ContentFitter><HorizontalLayout spacing="8"><Button>Simple</Button></HorizontalLayout></ContentFitter></VerticalLayout></ScrollView></Window>'
if (-not (Get-PanelLayoutError $taskBrokenLayout)) { throw 'Regression validator did not reject the observed nested-layout crash.' }
$taskVerified++
$taskSpawn = $taskOpen.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'WindowManager::SpawnWindow' } | Select-Object -First 1
$taskGenerate = $taskOpen.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'WindowManager::GenerateUI' } | Select-Object -First 1
if (-not $taskSpawn -or -not $taskGenerate -or $taskSpawn.Offset -ge $taskGenerate.Offset) { throw 'Panel root must be tracked before generating children.' }
$taskVerified++
foreach ($taskName in @('Changed', 'RefreshChoices', 'RefreshLabels', 'StartTraining', 'PauseTraining', 'CancelTraining', 'AcquireModel', 'AssignInference', 'CreateRevision', 'PreferModel', 'SetUsageBilling', 'FixedSubscriptionBilling')) {
    $taskAction = $taskController.Methods | Where-Object Name -eq $taskName
    if (-not ($taskAction.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'AIComputeController::RunPanelAction' })) { throw "UI action has no recovery boundary: $taskName" }
    $taskVerified++
}
$taskGuard = $taskMod.MainModule.Types | Where-Object FullName -eq 'SDSI.AI.UIRecovery' | ForEach-Object { $_.Methods | Where-Object Name -eq 'TryRun' }
if ($taskGuard.Body.ExceptionHandlers.Count -lt 2) { throw 'UI action and error reporting failures must both be contained.' }
$taskVerified++
Write-Output "PASS: $taskVerified native integration checks (layout ownership, UI recovery, bindings, cost attribution, calendar, promotion gate and visionary focus). Game runtime was not executed."
