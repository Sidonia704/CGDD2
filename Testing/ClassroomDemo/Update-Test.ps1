[CmdletBinding()]
param([string]$InstallRoot=(Join-Path $env:LOCALAPPDATA 'CGDD2-Classroom-Test'),[string]$ArchivePath)
$ErrorActionPreference='Stop'
$channelUrl='https://raw.githubusercontent.com/Sidonia704/CGDD2/codex/classroom-demo-test-channel/Testing/ClassroomDemo/channel.json'
$root=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\','/')
if($root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\','/')){throw 'Choose a named installation folder, not a drive root.'}
function Inside([string]$parent,[string]$child){
 $full=[IO.Path]::GetFullPath($child)
 if(-not $full.StartsWith($parent.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'A path escapes its intended directory.'}
 return $full
}
function NoLinks([string]$path){
 $cursor=[IO.Path]::GetFullPath($path)
 while($cursor){
  if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse points are not supported for the test installation.'}
  $next=Split-Path -Parent $cursor;if($next -eq $cursor){break};$cursor=$next
 }
}
function VerifyPackage([string]$folder,$channel){
 NoLinks $folder
 $identityPath=Inside $folder (Join-Path $folder 'Build-Identity.json')
 $identity=Get-Content -Raw -Encoding UTF8 -LiteralPath $identityPath|ConvertFrom-Json
 if($identity.schemaVersion -ne 1 -or $identity.platform -ne 'Windowsx64' -or $identity.buildGuid -ne $channel.buildGuid){throw 'Package identity does not match the test channel.'}
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 $items=@($identity.files)
 if($items.Count -ne $channel.manifestFiles -or $items.Count -lt 1 -or $items.Count -gt 1000){throw 'Unexpected package manifest size.'}
 foreach($file in $items){
  $relative=[string]$file.path
  if([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or $relative -match '(^|[\\/])\.\.?([\\/]|$)' -or $relative -eq 'Build-Identity.json'){throw 'Unsafe package path.'}
  $normalized=$relative.Replace('\','/')
  if(-not $seen.Add($normalized)){throw 'Duplicate manifest path.'}
  $path=Inside $folder (Join-Path $folder $relative);NoLinks $path
  if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing package file: $relative"}
  if($file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-Item -LiteralPath $path).Length -ne [long]$file.length){throw "Invalid length/hash metadata: $relative"}
  if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw "Package SHA256 mismatch: $relative"}
 }
 foreach($item in Get-ChildItem -LiteralPath $folder -Recurse -Force){
  if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unexpected link in package.'}
  if($item.PSIsContainer){continue}
  $relative=$item.FullName.Substring($folder.Length+1).Replace('\','/')
  if($relative -ne 'Build-Identity.json' -and -not $seen.Contains($relative)){throw "Unexpected package file: $relative"}
 }
 foreach($name in @('exe','gameDll')){
  $critical=$identity.$name;$matched=@($items|Where-Object path -eq $critical.path)
  if($matched.Count -ne 1 -or $matched[0].sha256 -ne $critical.sha256 -or $matched[0].length -ne $critical.length){throw 'Critical file identity mismatch.'}
 }
 if($identity.exe.path -ne 'Starframe-LAN.exe' -or $identity.gameDll.sha256 -ne $channel.gameDllSha256){throw 'Unexpected game executable/assembly identity.'}
}
NoLinks $root
[Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$oldProgress=$ProgressPreference
try{
 $ProgressPreference='SilentlyContinue'
 Write-Host 'Reading the CGDD2 classroom-test channel...'
 $channelRequest=$channelUrl+'?nocache='+[DateTime]::UtcNow.Ticks
 $channel=(Invoke-WebRequest -UseBasicParsing -Uri $channelRequest -UserAgent 'CGDD2-Classroom-TestUpdater/1' -TimeoutSec 60).Content|ConvertFrom-Json
 if($channel.schemaVersion -ne 1 -or $channel.channel -ne 'classroom-test' -or $channel.buildGuid -notmatch '^[a-f0-9]{32}$' -or $channel.tag -notmatch '^test-classroom-[a-zA-Z0-9.-]+$' -or $channel.sha256 -notmatch '^[a-f0-9]{64}$' -or $channel.gameDllSha256 -notmatch '^[a-f0-9]{64}$'){throw 'Invalid test-channel metadata.'}
 if($channel.bytes -le 0 -or $channel.bytes -gt 2000000000){throw 'Unexpected download size.'}
 $expectedPrefix='https://github.com/Sidonia704/CGDD2/releases/download/'+$channel.tag+'/'
 if(-not ([string]$channel.url).StartsWith($expectedPrefix,[StringComparison]::Ordinal) -or $channel.url -match '[?#]' -or $channel.asset -notmatch '^ClassroomDemo-Test-[a-f0-9]{8}(?:-r[2-9][0-9]*)?\.zip$' -or $channel.url -ne ($expectedPrefix+$channel.asset)){throw 'Download URL is outside the explicit CGDD2 test channel.'}
 $versions=Inside $root (Join-Path $root 'Versions');$destination=Inside $versions (Join-Path $versions $channel.buildGuid)
 if(Test-Path -LiteralPath $destination){
  Write-Host 'This version already exists; verifying it before selecting it.';VerifyPackage $destination $channel
 }else{
  $staging=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('CGDD2-Classroom-Test-'+[guid]::NewGuid().ToString('N'))))
  NoLinks $staging;[IO.Directory]::CreateDirectory($staging)|Out-Null
  $archive=Inside $staging (Join-Path $staging $channel.asset)
  if($ArchivePath){
   $existingArchive=[IO.Path]::GetFullPath($ArchivePath)
   if(-not(Test-Path -LiteralPath $existingArchive -PathType Leaf)){throw 'The supplied ZIP does not exist.'}
   Write-Host 'Checking the supplied ZIP against the public channel...'
   Copy-Item -LiteralPath $existingArchive -Destination $archive
  }else{
   Write-Host ('Downloading test version '+$channel.buildGuid+'...')
   Invoke-WebRequest -UseBasicParsing -Uri $channel.url -UserAgent 'CGDD2-Classroom-TestUpdater/1' -OutFile $archive -TimeoutSec 1800
  }
  if((Get-Item -LiteralPath $archive).Length -ne [long]$channel.bytes -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $channel.sha256){throw 'ZIP size or SHA256 verification failed. The active version was not changed.'}
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $extract=Inside $staging (Join-Path $staging 'Extracted');[IO.Directory]::CreateDirectory($extract)|Out-Null
  # Legacy Windows ZIP names use CP936; entries carrying the UTF-8 flag still use UTF-8.
  $zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Read,[Text.Encoding]::GetEncoding(936))
  try{
   $entries=@($zip.Entries);$total=0L;$fileCount=0;$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
   foreach($entry in $entries){
    $relative=$entry.FullName.Replace('\','/')
    if(-not $relative.StartsWith('ClassroomDemo/',[StringComparison]::Ordinal) -or $relative.Contains(':') -or $relative -match '(^|/)\.\.?(/|$)' -or ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000)){throw 'Unsafe ZIP entry.'}
    $path=Inside $extract (Join-Path $extract $relative)
    if(-not $names.Add($relative)){throw 'Duplicate ZIP entry.'}
    if($entry.Name){$total+=$entry.Length;$fileCount++}
   }
   if($total -gt 8000000000 -or $fileCount -ne ($channel.manifestFiles+1)){throw 'Unexpected expanded package size or file count.'}
   foreach($entry in $entries){
    $path=Inside $extract (Join-Path $extract $entry.FullName)
    if(-not $entry.Name){[IO.Directory]::CreateDirectory($path)|Out-Null;continue}
    [IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$path,$false)
   }
  }finally{$zip.Dispose()}
  $package=Inside $extract (Join-Path $extract 'ClassroomDemo')
  Write-Host 'Verifying every package file...';VerifyPackage $package $channel
  [IO.Directory]::CreateDirectory($versions)|Out-Null;NoLinks $versions;NoLinks $destination
  # Both resolved paths stay inside their verified staging/installation roots.
  $package=Inside $extract $package;$destination=Inside $versions $destination
  if(Test-Path -LiteralPath $destination){throw 'Destination appeared during verification; no existing version was overwritten.'}
  Move-Item -LiteralPath $package -Destination $destination
 }
 [IO.Directory]::CreateDirectory($root)|Out-Null;NoLinks $root
 $current=Inside $root (Join-Path $root 'current.json');NoLinks $current
 $next=Inside $root (Join-Path $root ('current-'+[guid]::NewGuid().ToString('N')+'.json'))
 $pointer=[ordered]@{channel='classroom-test';buildGuid=$channel.buildGuid;tag=$channel.tag;folder=$destination;exe=(Join-Path $destination 'Starframe-LAN.exe');verifiedUtc=[DateTime]::UtcNow.ToString('O')}
 [IO.File]::WriteAllText($next,($pointer|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 if(Test-Path -LiteralPath $current){$backup=Inside $root (Join-Path $root ('previous-'+[guid]::NewGuid().ToString('N')+'.json'));[IO.File]::Replace($next,$current,$backup)}else{[IO.File]::Move($next,$current)}
 Write-Host ('Verified. Open this executable manually: '+$pointer.exe)
 Write-Host 'Previous version folders were kept. No game or downloaded script was launched.'
 [pscustomobject]$pointer
}finally{$ProgressPreference=$oldProgress}
