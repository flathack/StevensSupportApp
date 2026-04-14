$alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
$bytes = New-Object byte[] 20
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
-join ($bytes | ForEach-Object { $alphabet[($_ % $alphabet.Length)] })
