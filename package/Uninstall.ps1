# Hallow Clips uninstaller (run from Windows Settings > Apps, or directly).
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow Clips'
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.MessageBox]::Show("Uninstall Hallow Clips?", 'Uninstall Hallow Clips', 'YesNo', 'Question') -ne 'Yes') { return }
Get-Process HallowClips -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'HallowClips' -ErrorAction SilentlyContinue
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) { Remove-Item (Join-Path $dir 'Hallow Clips.lnk') -ErrorAction SilentlyContinue }
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowClips' -Recurse -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA 'HallowClips') -Recurse -Force -ErrorAction SilentlyContinue   # settings, FFmpeg and the replay buffer - your saved clips (Videos\Hallow Clips) stay
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$dest`"" -WindowStyle Hidden
[System.Windows.Forms.MessageBox]::Show('Hallow Clips is uninstalled. Your clips are still in Videos\Hallow Clips.', 'Hallow Clips') | Out-Null