Option Explicit
Dim shell, fileSystem, panelScript, command
Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")
panelScript = fileSystem.BuildPath(fileSystem.GetParentFolderName(WScript.ScriptFullName), "start-panel.ps1")
command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " & Chr(34) & panelScript & Chr(34)
shell.Run command, 0, False
