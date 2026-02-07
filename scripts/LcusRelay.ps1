param(
  [Parameter(Mandatory=$true)]
  [ValidateSet("On","Off")]
  [string]$State,

  # Es: COM5. Se non lo metti, prova ad auto-detect CH340.
  [string]$Port,

  # Address del modulo (default 1). Di solito LCUS-1 usa 1.
  [ValidateRange(1,255)]
  [int]$Address = 1,

  [int]$BaudRate = 9600,

  # timeout in ms
  [int]$Timeout = 500
)

$ErrorActionPreference = "Stop"

function Find-Ch340ComPort {
  # Cerca dispositivi che contengono "CH340" o "USB-SERIAL" e prova a estrarre (COMx) dal FriendlyName
  $candidates = Get-PnpDevice -PresentOnly |
    Where-Object {
      ($_.FriendlyName -match 'CH340|USB-SERIAL|USB Serial') -or
      ($_.InstanceId   -match '^USB\\VID_1A86&PID_7523')   # VID/PID tipici CH340
    } |
    Select-Object -First 1 -Property FriendlyName, InstanceId

  if (-not $candidates) { return $null }

  $name = $candidates.FriendlyName
  if ($name -match '\((COM\d+)\)') { return $Matches[1] }

  # fallback: prova a leggere la property "PortName"
  try {
    $portProp = Get-PnpDeviceProperty -InstanceId $candidates.InstanceId -KeyName 'DEVPKEY_Device_PortName' -ErrorAction Stop
    if ($portProp.Data -match '^COM\d+$') { return $portProp.Data }
  } catch { }

  return $null
}

if ([string]::IsNullOrWhiteSpace($Port)) {
  $Port = Find-Ch340ComPort
  if (-not $Port) {
    throw "Porta COM non trovata. Passa esplicitamente -Port COMx (es. -Port COM5) oppure verifica driver CH340/Device Manager."
  }
}

# Comando: A0 <addr> <cmd> <checksum>
# cmd: 0x01 = ON, 0x00 = OFF
[byte]$start = 0xA0
[byte]$addr  = [byte]$Address
[byte]$cmd   = if ($State -eq "On") { 0x01 } else { 0x00 }

# checksum: (start + addr + cmd) & 0xFF
[byte]$chk = [byte](($start + $addr + $cmd) -band 0xFF)

[byte[]]$packet = @($start, $addr, $cmd, $chk)

$sp = [System.IO.Ports.SerialPort]::new($Port, $BaudRate, 'None', 8, 'One')
$sp.ReadTimeout  = $Timeout
$sp.WriteTimeout = $Timeout

try {
  $sp.Open()

  # Alcuni adattatori gradiscono DTR/RTS abilitati
  $sp.DtrEnable = $true
  $sp.RtsEnable = $true

  Start-Sleep -Milliseconds 50
  $sp.Write($packet, 0, $packet.Length)
  Start-Sleep -Milliseconds 50

  Write-Host "OK: inviato $State su $Port (addr=$Address) -> $([BitConverter]::ToString($packet).Replace('-',' '))"
}
finally {
  if ($sp.IsOpen) { $sp.Close() }
  $sp.Dispose()
}
