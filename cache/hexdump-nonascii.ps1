$ErrorActionPreference = 'Stop'
$f = 'C:\Users\Firefly\Documents\PCSTUFF\AncestorsEnhancedConfigurator\src\AncestorsEnhanced.App\Views\MainWindow.axaml'
$t = [IO.File]::ReadAllText($f)
# Show every non-ASCII char with 25 chars of context, as code points
foreach ($m in [regex]::Matches($t, '[^\x00-\x7F]+')) {
  $start = [Math]::Max(0, $m.Index - 25)
  $len = [Math]::Min(60, $m.Index + $m.Length + 25 - $start)
  $ctx = $t.Substring($start, $len) -replace "`r|`n", ' '
  $codes = ($m.Value.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' '
  '{0}  |  {1}' -f $codes, $ctx
}
