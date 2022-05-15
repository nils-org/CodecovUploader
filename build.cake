#addin nuget:?package=Cake.Http&version=1.3.0
#addin nuget:?package=Cake.Json&version=7.0.1

var target = Argument("target", "Default");
var version = Argument<string>("tool-version", null);
var binDir = Directory("./bin");

private string GetLatestVersion()
{
   var verJson = HttpGet(
      "https://uploader.codecov.io/linux/latest",
      new HttpSettings
      {
         Headers = new Dictionary<string, string>
         {
            {"Accept", "application/json"}
         }
      });

   return (string)ParseJson(verJson)["version"];

}

private void DownloadExecutable(string distro, DirectoryPath dir)
{
   if(string.IsNullOrEmpty(version))
   {
      version = GetLatestVersion();
   }

   var json = HttpGet(
      $"https://uploader.codecov.io/{distro}/{version}",
      new HttpSettings
      {
         Headers = new Dictionary<string, string>
         {
            {"Accept", "application/json"}
         }
      });

   var obj = ParseJson(json);
   var link = (string)obj["link"];
   var name = (string)obj["artifact"]["file"];

   Information($"Downloading: {name} - {version} from {link}");
   DownloadFile(link, dir.CombineWithFilePath(File(name)));
}

Task("Clean")
.Does(() => {
   CleanDirectory(binDir);
});

Task("GetExecutables")
.Does(() => {
   var dir = binDir + Directory("content");
   CleanDirectory(dir);
   DownloadExecutable("linux", dir);
   DownloadExecutable("macos", dir);
   DownloadExecutable("windows", dir);
});

Task("Pack")
.IsDependentOn("GetExecutables")
.Does(() => {
   if(string.IsNullOrEmpty(version)) 
   {
      Error("version is not set.");
      throw new ArgumentNullException("version");
   }

   var dir = binDir + Directory("./package");
   CleanDirectory(dir);

   var files = GetFiles((binDir + File("content/*")).Path.FullPath).ToArray();
   Information($"Packing {files.Length} files.");

   var ver = version.Substring(1);
   NuGetPack(new NuGetPackSettings 
   {
      Id                      = "CodecovUploader",
      Version                 = ver,
      Title                   = "CodecovUploader",
      Authors                 = new[] {"Codecov"},
      Description             = "Unofficial package of the official Codecov-Uploader",
      Summary                 = "Unofficial package of the official Codecov-Uploader",
      ProjectUrl              = new Uri("https://github.com/nils-org/CodecovUploader/"),
      IconUrl                 = new Uri("https://cdn.jsdelivr.net/gh/codecov/media@0953f4e0d5315fb6d526a248bc88e1bc16506a37/logos/pink.png"),
      LicenseUrl              = new Uri("https://github.com/codecov/uploader/blob/main/LICENSE"),
      Copyright               = "Codecov",
      ReleaseNotes            = new [] {$"Version {ver} of the Codecov-Uploader", $"https://github.com/codecov/uploader/releases/tag/{version}"},
      Tags                    = new [] {"Codecov", "upload", "test", "coverage"},
      RequireLicenseAcceptance= false,
      Symbols                 = false,
      NoPackageAnalysis       = true,
      Files                   = files.Select(f => new NuSpecContent { Source = f.FullPath, Target = $"tools/{f.GetFilename()}" }).ToArray(),
      BasePath                = binDir + Directory("content"),
      OutputDirectory         = dir
   });
});

Task("Push")
 .IsDependentOn("Pack")
 .Does(() => 
{
    var package = GetFiles((binDir + File("package/*.nupkg")).Path.ToString()).Single();
    var apiKey = EnvironmentVariable("NuGet_ApiKey");

   if(string.IsNullOrEmpty(apiKey)) 
   {
      Error("NuGet_ApiKey is not set.");
      throw new ArgumentNullException("NuGet_ApiKey");
   }

   NuGetPush(package, new NuGetPushSettings {
      Source = "https://api.nuget.org/v3/index.json",
      ApiKey = apiKey
   });
});

Task("CI")
 .Does(() =>
{
   var settings =  new HttpSettings
   {
      Headers = new Dictionary<string, string>
      {
         {"Accept", "application/json"}
      }
   };

   var json = HttpGet("https://api.nuget.org/v3/index.json", settings);

   /* old PackageBaseAddress
   var url = (string)ParseJson(json)["resources"].AsJEnumerable().First(x => (string)x["@type"] == "PackageBaseAddress/3.0.0")["@id"];
   url = url + "CodecovUploader/index.json";
   Information("Fetching NuGet versions from: "+url);
   json = HttpGet(url, settings);
   var versions = ParseJson(json)["versions"].AsJEnumerable().Select(t => (string)t).ToArray();
   */

   var url = (string)ParseJson(json)["resources"].AsJEnumerable().First(x => (string)x["@type"] == "RegistrationsBaseUrl")["@id"];
   url = url + "codecovuploader/index.json";
   Information("Fetching NuGet versions from: "+url);
   json = HttpGet(url, settings);
   var versions = ParseJson(json)["items"].AsJEnumerable().First()["items"].AsJEnumerable().Select(i => (string)i["catalogEntry"]["version"]).ToArray();

   Information("versions in NuGet: " + string.Join(", ", versions));

   var currentVer = GetLatestVersion().Substring(1);
   Information("Current version in Codecov: " + currentVer);
   if(versions.Contains(currentVer)) 
   {
      Information("Version already in NuGet. Nothing to do.");
      return;
   }

   version = "v"+currentVer;
   RunTarget("Push");
});

Task("Default")
 .IsDependentOn("Clean")
 .IsDependentOn("Pack");

RunTarget(target);