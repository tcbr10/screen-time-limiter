function SLPipe([string]$json) {
  $p = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'ScreenLimiter.v1', [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::None, [System.Security.Principal.TokenImpersonationLevel]::Identification)
  $p.Connect(4000)
  $b = [Text.Encoding]::UTF8.GetBytes($json + "`n"); $p.Write($b, 0, $b.Length); $p.Flush()
  $r = New-Object IO.StreamReader($p, [Text.Encoding]::UTF8); $line = $r.ReadLine(); $p.Dispose(); $line
}
function SLStatus { ($(SLPipe '{"Op":"status"}') | ConvertFrom-Json).Status }
