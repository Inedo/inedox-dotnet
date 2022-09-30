$pkgName = "Inedo.SDK.DevOnly"
$pkgProjFile= "c:\Projects\Inedo.sdk\Inedo.SDK\Inedo.SDK.csproj"

$slnFile = "C:\Projects\inedox-dotnet\DotNet\DotNet.sln"
$projFilesToMunge = @( `
  "C:\Projects\inedox-dotnet\DotNet\InedoExtension\InedoExtension.csproj"
)

dotnet sln "$slnFile" add "$pkgProjFile"
foreach ($projFile in $projFilesToMunge) {
  dotnet remove "$projFile" package "$pkgName"
  dotnet add $projFile reference $pkgProjFile
}

pause