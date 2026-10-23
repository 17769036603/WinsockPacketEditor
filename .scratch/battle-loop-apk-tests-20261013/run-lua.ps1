param(
    [Parameter(Mandatory = $true)][string]$LuaFile,
    [Parameter(Mandatory = $false)][string]$SetupFile,
    [Parameter(Mandatory = $false)][string]$TestsFile,
    [Parameter(Mandatory = $false)][string]$BeforeStopFile,
    [Parameter(Mandatory = $false)][string]$ConfigLiteral = "{resultPath='result',bindingPath='binding',leasePath='lease',workerPath='worker',stateKey='__codex_battle_loop_v1',runId='test-run',expectedPlayer='101',expectedManagerTostring='manager',mode='tick'}",
    [Parameter(Mandatory = $false)][string]$StopConfigLiteral
)
$ErrorActionPreference = 'Stop'
$luaDir = 'C:\Users\Administrator\AppData\Local\Programs\darktable\bin'
$luaDll = Join-Path $luaDir 'lua54.dll'
if (-not (Test-Path -LiteralPath $luaDll)) { throw "Lua runtime unavailable: $luaDll" }
if (-not (Test-Path -LiteralPath $LuaFile)) { throw "Lua source unavailable: $LuaFile" }
if (-not ('BattleLoopLuaHost' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BattleLoopLuaHost {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  private static extern bool SetDllDirectory(string path);
  [DllImport("lua54.dll", CallingConvention=CallingConvention.Cdecl)]
  private static extern IntPtr luaL_newstate();
  [DllImport("lua54.dll", CallingConvention=CallingConvention.Cdecl)]
  private static extern void luaL_openlibs(IntPtr state);
  [DllImport("lua54.dll", CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Ansi)]
  private static extern int luaL_loadstring(IntPtr state, string source);
  [DllImport("lua54.dll", CallingConvention=CallingConvention.Cdecl)]
  private static extern int lua_pcallk(IntPtr state, int nargs, int nresults, int errorFunction, IntPtr context, IntPtr continuation);
  [DllImport("lua54.dll", CallingConvention=CallingConvention.Cdecl)]
  private static extern IntPtr lua_tolstring(IntPtr state, int index, out UIntPtr length);
  [DllImport("lua54.dll", CallingConvention=CallingConvention.Cdecl)]
  private static extern void lua_close(IntPtr state);
  public static void Run(string dllDirectory, string source) {
    if (!SetDllDirectory(dllDirectory)) throw new InvalidOperationException("SetDllDirectory failed: " + Marshal.GetLastWin32Error());
    IntPtr state = luaL_newstate();
    if (state == IntPtr.Zero) throw new InvalidOperationException("luaL_newstate returned null");
    try {
      luaL_openlibs(state);
      int status = luaL_loadstring(state, source);
      if (status != 0) throw LuaError(state, "load", status);
      status = lua_pcallk(state, 0, -1, 0, IntPtr.Zero, IntPtr.Zero);
      if (status != 0) throw LuaError(state, "run", status);
    } finally { lua_close(state); }
  }
  private static Exception LuaError(IntPtr state, string phase, int status) {
    UIntPtr length; IntPtr ptr = lua_tolstring(state, -1, out length);
    string message = ptr == IntPtr.Zero ? "unknown Lua error" : Marshal.PtrToStringAnsi(ptr);
    return new InvalidOperationException("Lua " + phase + " failed (status " + status + "): " + message);
  }
}
'@
}
$template = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $LuaFile), [System.Text.Encoding]::UTF8)
$source = $template.Replace('__CONFIG__', $ConfigLiteral)
$chunks = @()
if ($SetupFile) {
    if (-not (Test-Path -LiteralPath $SetupFile)) { throw "Setup source unavailable: $SetupFile" }
    $chunks += [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $SetupFile), [System.Text.Encoding]::UTF8)
}
$chunks += "local __battle_loop_entry = function()`n" + $source + "`nend`n__battle_loop_entry()"
if ($BeforeStopFile) {
    if (-not (Test-Path -LiteralPath $BeforeStopFile)) { throw "Before-stop source unavailable: $BeforeStopFile" }
    $chunks += [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $BeforeStopFile), [System.Text.Encoding]::UTF8)
}
if ($StopConfigLiteral) {
    $stopSource = $template.Replace('__CONFIG__', $StopConfigLiteral)
    $chunks += "local __battle_loop_stop = function()`n" + $stopSource + "`nend`n__battle_loop_stop()"
}
if ($TestsFile) {
    if (-not (Test-Path -LiteralPath $TestsFile)) { throw "Test source unavailable: $TestsFile" }
    $chunks += [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $TestsFile), [System.Text.Encoding]::UTF8)
}
[BattleLoopLuaHost]::Run($luaDir, ($chunks -join "`n"))
Write-Output 'Lua harness completed successfully.'
