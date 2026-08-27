$ErrorActionPreference = 'Stop'
$app = 'C:\Users\Firefly\Documents\PCSTUFF\AncestorsEnhancedConfigurator\src\AncestorsEnhanced.App'
$files = @("$app\Views\MainWindow.axaml", "$app\Views\AlreadyRunningWindow.axaml", "$app\App.axaml")

foreach ($f in $files) {
  $t = [IO.File]::ReadAllText($f)
  $groups = [regex]::Matches($t, '[^\x00-\x7F]') | Group-Object Value | Sort-Object Count -Descending
  $summary = ($groups | ForEach-Object { 'U+{0:X4} x{1}' -f [int][char]$_.Name[0], $_.Count }) -join ', '
  '{0} ({1} bytes): {2}' -f (Split-Path $f -Leaf), (Get-Item $f).Length, $summary
}

# Show sample lines containing non-ASCII from MainWindow
$t = [IO.File]::ReadAllText("$app\Views\MainWindow.axaml")
$lines = $t -split "`n"
$i = 0
foreach ($line in $lines) {
  $i++
  if ($line -match '[^\x00-\x7F]') { '{0}: {1}' -f $i, $line.Trim().Substring(0, [Math]::Min(150, $line.Trim().Length)) }
}
