# 编译前请求正在运行的 ScreenKit 自己退出。不调用 taskkill。
# 没有退出事件时直接失败，避免接着走全局 slx 的强杀。
$ErrorActionPreference = "SilentlyContinue"
$signaled = $false
try {
	$e = [System.Threading.EventWaitHandle]::OpenExisting("Local\ScreenKit_RequestExit")
	[void]$e.Set()
	$e.Dispose()
	$signaled = $true
	Write-Host "已请求 ScreenKit 退出"
} catch {
	$signaled = $false
}
for ($i = 0; $i -lt 40; $i++) {
	$ps = @(Get-Process -Name ScreenKit -ErrorAction SilentlyContinue)
	if ($ps.Count -eq 0) { exit 0 }
	if (-not $signaled) {
		Write-Host "ScreenKit 正在运行，但没有退出事件。请用托盘「退出」后再编译。未调用 slx。"
		exit 1
	}
	Start-Sleep -Milliseconds 200
}
Write-Host "ScreenKit 未在 8 秒内退出。未调用 slx。"
exit 1
