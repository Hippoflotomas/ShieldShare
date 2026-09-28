param(
    [Parameter(Mandatory)]
    [ValidateSet('Debug','Release')]
    [System.String]$Target,
    
    [Parameter(Mandatory)]
    [System.String]$TargetPath,
    
    [Parameter(Mandatory)]
    [System.String]$TargetAssembly,

    [Parameter(Mandatory)]
    [System.String]$ValheimPath,

    [Parameter(Mandatory)]
    [System.String]$ProjectPath,
    
    [System.String]$DeployPath
)

# Make sure Get-Location is the script path
Push-Location -Path (Split-Path -Parent $MyInvocation.MyCommand.Path)

# Test some preliminaries
("$TargetPath",
 "$ValheimPath",
 "$(Get-Location)\..\libraries"
) | % {
    if (!(Test-Path "$_")) {Write-Error -ErrorAction Stop -Message "$_ folder is missing"}
}

# Plugin name without ".dll"
$name = "$TargetAssembly" -Replace('.dll')

# Create the mdb file
$pdb = "$TargetPath\$name.pdb"
if (Test-Path -Path "$pdb") {
    Write-Host "Create mdb file for plugin $name"
    Invoke-Expression "& `"$(Get-Location)\..\libraries\Debug\pdb2mdb.exe`" `"$TargetPath\$TargetAssembly`""
}

# Main Script
Write-Host "Publishing for $Target from $TargetPath"

if ($Target.Equals("Debug")) {
    if ($DeployPath.Equals("")){
      $DeployPath = "$ValheimPath\BepInEx\plugins"
    }
    
    $plug = New-Item -Type Directory -Path "$DeployPath\$name" -Force
    Write-Host "Copy $TargetAssembly to $plug"
    Copy-Item -Path "$TargetPath\$name.dll" -Destination "$plug" -Force
    Copy-Item -Path "$TargetPath\$name.pdb" -Destination "$plug" -Force
    Copy-Item -Path "$TargetPath\$name.dll.mdb" -Destination "$plug" -Force
}

if($Target.Equals("Release")) {
    # Builds a Thunderstore-ready package in <solution>\publishables:
    #   publishables\<name>\            unzipped, for checking
    #   publishables\<name>-<ver>.zip   upload this one
    Write-Host "Packaging for Thunderstore..."
    $PackageSource = "$ProjectPath\Package"
    $Publishables = Join-Path (Resolve-Path "$(Get-Location)\..").Path "publishables"
    $Staging = "$Publishables\$name"

    $manifest = Get-Content "$PackageSource\manifest.json" -Raw | ConvertFrom-Json
    $version = $manifest.version_number

    # The manifest version and the plugin's own version (PluginVersion in ShieldShare.cs) must agree,
    # or Thunderstore and the in-game version check will disagree about what's installed.
    $dllVersion = [System.Reflection.AssemblyName]::GetAssemblyName("$TargetPath\$TargetAssembly").Version
    $dllVersionText = "$($dllVersion.Major).$($dllVersion.Minor).$($dllVersion.Build)"
    if ($dllVersionText -ne $version) {
        Write-Error -ErrorAction Stop -Message "Version mismatch: manifest.json says $version but $TargetAssembly is $dllVersionText. Update PluginVersion in ShieldShare.cs and version_number in Package\manifest.json together."
    }

    # Fresh staging folder every build so nothing stale gets shipped
    if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
    New-Item -Type Directory -Path "$Staging\plugins" -Force | Out-Null

    foreach ($file in "manifest.json", "icon.png", "README.md", "CHANGELOG.md") {
        if (!(Test-Path "$PackageSource\$file")) { Write-Error -ErrorAction Stop -Message "$PackageSource\$file is missing" }
        Copy-Item -Path "$PackageSource\$file" -Destination $Staging -Force
    }
    Copy-Item -Path "$TargetPath\$TargetAssembly" -Destination "$Staging\plugins\$TargetAssembly" -Force

    # Write every zip entry by name with forward slashes. Windows PowerShell's Compress-Archive AND
    # ZipFile.CreateFromDirectory both write backslashes into zip paths ("plugins\x.dll"), which breaks
    # the folder layout for Thunderstore / r2modman.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = "$Publishables\$name-$version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -Path $Staging -Recurse -File) {
            $entryName = $file.FullName.Substring($Staging.Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Host "Thunderstore package ready: $zip"
}

# Pop Location
Pop-Location