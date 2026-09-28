Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$process = Start-Process -FilePath 'E:\Work\dsx\.tmpfiles\verify-app\DsxLite.App.exe' -PassThru
try {
    $process.WaitForInputIdle(10000) | Out-Null
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $window = $null
    for ($i=0; $i -lt 50 -and $null -eq $window; $i++) {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -eq $window) { Start-Sleep -Milliseconds 100 }
    }
    if ($null -eq $window) { throw '未找到验证进程窗口' }
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $all | ForEach-Object { if ($_.Current.Name) { '{0}: {1}' -f $_.Current.ControlType.ProgrammaticName, $_.Current.Name } }
    $connect = $all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name -eq '连接' } | Select-Object -First 1
    if ($null -eq $connect) { throw '未找到连接按钮' }
    ([System.Windows.Automation.InvokePattern]$connect.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 800
    '===连接后==='
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $all | Where-Object { $_.Current.Name -match '校准|°/s|加速度|已连接|断开' } | ForEach-Object { $_.Current.Name }
    Add-Type -AssemblyName System.Drawing
    $bounds = $window.Current.BoundingRectangle
    $bitmap = [System.Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.Left, [int]$bounds.Top, 0, 0, $bitmap.Size)
        $bitmap.Save('E:\Work\dsx\.tmpfiles\verify-app-connected.png')
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    $disconnect = $all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name -eq '断开' } | Select-Object -First 1
    if ($null -eq $disconnect) { throw '连接后未找到断开按钮' }
    ([System.Windows.Automation.InvokePattern]$disconnect.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 300
    '===断开后==='
    $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -match '校准|运动数据|已断开' } | ForEach-Object { $_.Current.Name }
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Confirm:$false; $process.WaitForExit() }
}
