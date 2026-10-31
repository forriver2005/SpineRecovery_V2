$ErrorActionPreference = 'Stop'
$policy = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets\Scripts\TrackerLanPolicy.cs'
Add-Type -TypeDefinition (Get-Content -LiteralPath $policy -Raw)
$script:checks = 0

function Assert-MotorAllowed([string]$Address, [string]$Expected, [int]$Duration = 100) {
    $actual = ''
    if (![TrackerLanPolicy]::TryCreateMotorUrl($Address, $Duration, [ref]$actual) -or $actual -cne $Expected) {
        throw "Expected a local motor URL for '$Address'; got '$actual'"
    }
    $script:checks++
}
function Assert-MotorDenied([AllowNull()][string]$Address) {
    $actual = ''
    if ([TrackerLanPolicy]::TryCreateMotorUrl($Address, 100, [ref]$actual) -or $actual) {
        throw "Unexpectedly accepted motor target '$Address'"
    }
    $script:checks++
}
function Assert-Discovery([string]$Address, [AllowNull()][string]$Expected) {
    $actual = ''
    $accepted = [TrackerLanPolicy]::TryDiscoveryUrl($Address, [ref]$actual)
    if ($accepted -ne [bool]$Expected -or ($Expected -and $actual -cne $Expected)) {
        throw "Unexpected discovery URL policy result for '$Address': '$actual'"
    }
    $script:checks++
}

Assert-MotorAllowed '192.168.31.137' 'http://192.168.31.137/motor?duration_ms=100'
Assert-MotorAllowed '10.0.0.2:8080' 'http://10.0.0.2:8080/motor?duration_ms=100'
Assert-MotorAllowed '172.16.0.1' 'http://172.16.0.1/motor?duration_ms=100'
Assert-MotorAllowed '172.31.255.254:65535' 'http://172.31.255.254:65535/motor?duration_ms=100'
Assert-MotorAllowed ' http://192.168.1.2:80/ ' 'http://192.168.1.2:80/motor?duration_ms=100'
Assert-MotorAllowed 'https://192.168.1.2:8443/' 'https://192.168.1.2:8443/motor?duration_ms=100'
Assert-MotorAllowed 'HTTP://10.0.0.2' 'http://10.0.0.2/motor?duration_ms=1' 0
Assert-MotorAllowed '10.0.0.2' 'http://10.0.0.2/motor?duration_ms=60000' 999999

foreach ($address in @($null, '', 'api.metaspine.cn', 'http://api.metaspine.cn',
    'https://example.com', '192.168.1.2.example.com', 'localhost', '127.0.0.1',
    '0.0.0.0', '8.8.8.8', '172.15.255.254', '172.32.0.1', '192.169.1.1',
    '100.64.1.1', '169.254.1.1', '224.0.0.1', '255.255.255.255',
    '10.0.0.256', '010.0.0.1', '10.00.0.1', '10.1', '167772161', '0x0a000001',
    '10.0.0.1.', '[::1]', '[::ffff:10.0.0.1]', '10.0.0.1:0', '10.0.0.1:65536',
    '10.0.0.1:080', '10.0.0.1/path', '10.0.0.1?x=1', '10.0.0.1#fragment',
    'user@10.0.0.1', '10.0.0.1@8.8.8.8', 'ftp://10.0.0.1', 'http://10.0.0.1\@8.8.8.8',
    'http://%31%30.0.0.1', "http://10.0.`n0.1")) { Assert-MotorDenied $address }

Assert-Discovery 'http://127.0.0.1:35903/api/trackers?enabled=true' 'http://127.0.0.1:35903/api/trackers?enabled=true'
Assert-Discovery 'https://192.168.1.2:8443/trackers' 'https://192.168.1.2:8443/trackers'
foreach ($address in @('https://example.com/trackers', 'http://8.8.8.8/trackers',
    'http://localhost/trackers', 'http://user@10.0.0.1/trackers',
    'http://010.0.0.1/trackers', 'http://10.0.0.1:0/trackers',
    'http://10.0.0.1/trackers#fragment', 'http://10.0.0.1\@8.8.8.8/trackers')) {
    Assert-Discovery $address $null
}
Write-Output "Tracker LAN policy: $script:checks checks passed."
