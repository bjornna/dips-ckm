#load "common/common.csx" 

var dipsReleaseVersion = "18.1.0";
var buildCounter = Environment.GetEnvironmentVariable("BUILD_COUNTER") ?? "1";
var version = $"{dipsReleaseVersion}.{buildCounter}";

readonly string ChocoOutputDirectory = Path.Combine(BuildEnv.OutputDir, "choco");
readonly string NugetOutputDirectory = Path.Combine(BuildEnv.OutputDir, "nuget");
readonly string DocDirectory = Path.Combine(BuildEnv.RootDir, "doc");
readonly string DocOutputDirectory = Path.Combine(BuildEnv.OutputDir, "doc");
readonly string ChocoDirectory = Path.Combine(BuildEnv.RootDir, "choco");
readonly string NugetDirectory = Path.Combine(BuildEnv.RootDir, "nuget");

// TODO: add document paths and names (without .adoc)
readonly Dictionary<string, string> Documents = new Dictionary<string, string>
{
	// { directory, documentname }
};

var pmSourceDir = Path.Combine(BuildEnv.SrcDir);


public void HandleSingleArgument(string argument)
{
    switch(argument.ToLower())
    {
        case "exit":
            System.Environment.Exit(0);
            break;

        case "clean":
            FileUtil.DeleteDirectory(BuildEnv.OutputDir, true, true);
            break;

        case "init":
            Console.WriteLine("##teamcity[buildNumber '" + version + "']");
            break;

        case "pminit":
            Command.Build("pm.exe", $"init \"{pmSourceDir}\"").Execute();
            break;

        case "build":
            HandleSingleArgument("clean");                
            
            FileUtil.CreateCleanDirectory(NugetOutputDirectory);
            
            Command.Build("pm.exe", $"create package \"{pmSourceDir}\" -o \"{NugetOutputDirectory}\"").Execute();       

            // foreach (var document in Documents)
	        // {
	        // 	var documentPath = Path.Combine(DocDirectory, document.Key, document.Value + ".adoc");
	        // 	var outPutDir = Path.Combine(DocOutputDirectory, document.Key);
	        // 	Asciidoc.Build(documentPath, outPutDir);
	        // }       

            break;

        case "package":
            
            // Nuget.CreateAllPackages(
            //     path: NugetDirectory,
            //     version: version,
            //     recursive: true,
            //     outputDirectory: DocOutputDirectory,
            //     additionalProperties: "RootFolder=" + BuildEnv.RootDir
            // );

            if(Directory.EnumerateFiles(NugetOutputDirectory, "*.nupkg").Any())
            {
                Command.Build("pm.exe", $"create distribution \"{pmSourceDir}\" -o \"{BuildEnv.OutputDir}\"\\ArenaLogisticsDashboard-{version}.zip").Execute();
                
                FileUtil.CreateCleanDirectory(ChocoOutputDirectory);
                
                Chocolatey.CreateAllPackages(
                    path: BuildEnv.ChocoDir,
                    version: version,
                    recursive: true,
                    outputDirectory: ChocoOutputDirectory,
                    additionalProperties: $"RootDir={BuildEnv.RootDir}"
                );
            }

            break;			

        case "publish":

            // Nuget.PublishAllPackages(
            //     nugetFeedUrl: Feeds.DIPSRC,
            //     path: DocOutputDirectory,
            //     exceptionOnConflict: false
            // );

            foreach (var nugetPackage in Directory.EnumerateFiles(NugetOutputDirectory, "*.nupkg"))
            {
                Nuget.PublishPackage(
                    nugetFeedUrl: "https://dips-nuget/nuget/Arena-18.1-Resources-Nuget",
                    packagePath: nugetPackage,
                    exceptionOnConflict: false,
                    onlyPublishFromBuildServer: false
                );
            }

            foreach (var nugetPackage in Directory.EnumerateFiles(ChocoOutputDirectory, "*.nupkg"))
            {
                Nuget.PublishPackage(
                    nugetFeedUrl: Feeds.DIPSDev,
                    packagePath: nugetPackage,
                    exceptionOnConflict: false,
                    onlyPublishFromBuildServer: false
                );
            }

            break;

        case "promote":
            foreach (var chocoPackage in Directory.EnumerateFiles(ChocoOutputDirectory, "*.nupkg"))
			{
				var packageId = Nuget.GetPackageId(chocoPackage);
				var packageVersion = Nuget.GetPackageVersion(chocoPackage);
				Proget.PromotePackageAsync(packageId, packageVersion, Feeds.DIPSDev, "https://dips-nuget/nuget/Arena-18.1-Resources-Choco").Wait();
			}

            break;           

        case "usage":
        case "help":
            Display.AvailableCommands(availableCommands);
            break;

        default:
            Write.Error(argument + " is not a valid command. Available commands:\n");
            HandleSingleArgument("help");
            break;
    }
}


string[] availableCommands = new []{
    "init       - fetches internal dependencies and restores nuget packages",
    "arena      - installs an Arena client in the current repository",
    "compile    - compiles the solution(s)",
    "package    - creates chocolatey package(s)",
    "ci         - runs 'clean init compile test builddoc package'",
    "publish    - publishes nuget/chocolatey package(s)",
    "clean      - cleans the solution(s)",
    "help/usage - shows this help message",
    "exit       - exits this buildwindow"
};

// TODO: insert project name
Display.DipsLogo("Surgery (Configured Project)");


///
/// The code below this should probably not be changed.
/// It executes the methods above.
///

// Allows us to call this file with arguments from, say, the buildserver
if(Env.ScriptArgs.Count > 0)
{
    // If arguments are passed in, we are on the buildserver
    // Just execute the arguments and exit
    ParseArguments(Env.ScriptArgs.ToArray());
    System.Environment.Exit(0);
}
else
{
    // If no arguments are passed in, the buildwindow has been launched from buildwindow.bat
    // List the available comands, and listen for input
    Display.AvailableCommands(availableCommands);
    OpenBuildwindow();
}

// This method runs the buildwindow in an endless loop until "exit" is typed
// or CTRL+C is pressed.
public void OpenBuildwindow()
{
    while(true)
    {
        Write.Caption("\nEnter a command:\n >> ", newline: false);
        try
        {
            ParseArguments(Console.ReadLine().Split(' '));
        }
        catch(Exception e)
        {
            Write.Error(e.ToString());
        }
    }
}

// Parses all the arguments (e.g. "clean init compile test") and runs them
// in succession
public void ParseArguments(params string[] arguments)
{
    var properties = arguments.Where(arg => arg.StartsWith("/")).Select(arg => arg.Substring(1)).Select(n => n.ToLower());
    Properties.ActivateProperties(properties);

    foreach (var argument in arguments.Where(arg => !arg.StartsWith("/")).Select(x => x.ToLower()))
    {
        HandleSingleArgument(argument);
    }
}
