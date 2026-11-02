$ErrorActionPreference = 'Stop'
Get-AppxPackage -Name 'KeyBank.NewText' | Remove-AppxPackage
Write-Host '已移除。'
