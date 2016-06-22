#load "common.csx"

const string CurrentVersion = "1.0.2";

readonly string RootDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
readonly string DeployDirectory = Path.Combine(RootDir, "Deploy");
readonly string ArenaDirectory = Path.Combine(RootDir, "Arena");
readonly string PackagesDirectory = Path.Combine(RootDir, "packages");
readonly string SourceDirectory = Path.Combine(RootDir, "src");

// doc paths must be relative
const string DocDir = @"..\src\doc";
readonly string OutputDir = @"..\output";

readonly Dictionary<string, string> Documents = new Dictionary<string, string>
{
	// { directory, documentname }
	// { "releasedoc", "index" },
	{ ".", "behandlingsplan" },
	// { "user", "index" },
};

const string ArenaServerHostName = "vt-healthang02.dips.local";

readonly string[] availableCommands = new []
{
	"clean        - cleans the solution(s)",
	"init         - init paket",
	"package      - creates nuget package(s)",
	"publishnuget - publishes nuget packages",
	"motivation   - cheers you up",
	"help/usage   - shows this help message"
};

Display.DipsLogo("DIPS Behandlingsplan");

// Allows us to call this file with arguments from, say, the buildserver
if(Env.ScriptArgs.Count > 0)
{
	ParseArguments(Env.ScriptArgs.ToArray());
	System.Environment.Exit(0);
}
else
{
	Buildsystem.Upgrade();
	Write.Info("\n");
	Display.AvailableCommands(availableCommands);
}

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

public void ParseArguments(params string[] arguments)
{
	foreach (var argument in arguments) {
	    HandleSingleArgument(argument);
	}
}

public void HandleSingleArgument(string argument)
{
	switch(argument.ToLower())
	{
		case "exit":
			System.Environment.Exit(0);
			break;
		case "clean":
			FileUtil.DeleteDirectory(DeployDirectory, true, true);
			break;
		case "init":
			Paket.Update();
			break;
		case "package":
			FileUtil.CreateCleanDirectory(DeployDirectory);
			
			// NuGet packages without build number
			Paket.Pack(DeployDirectory, CurrentVersion, "minimum-from-lock-file include-referenced-projects buildplatform x86");
			break;
	    case "publishnuget":
			if (!BuildServer.IsBuildServer)
			{
				Write.Error("Publishing to NuGet should only be done by the build server!");
				return;
			}

			// NuGet
			foreach (string file in Directory.GetFiles(DeployDirectory, "*.nupkg"))
			{
			   	Nuget.PublishPackage(Feeds.DIPSNuget, file);
			}

			// foreach (string file in Directory.GetFiles(DeployDirectory, "*.nupkg"))
			// {
			//		Chocolatey.PublishPackage(Feeds.DIPSConfiguredProductsDev, file);
			// }
	        break;

		case "motivation":
			Display.Motivation();
			break;

		case "usage":
		case "help":
			Display.AvailableCommands(availableCommands);
			break;

		default:
			Write.Error(argument + " is not a valid command. Available commands:\n");
			Display.AvailableCommands(availableCommands);
			break;
	}
}

public string AppendBuildNumber(string versionNumber)
{
	return versionNumber + "." + GetPaddedBuildNumber();
}

public string AppendCIBuildNumber(string versionNumber)
{
	return versionNumber + "-CI" + GetPaddedBuildNumber();
}

public string AppendRCBuildNumber(string versionNumber)
{
	return versionNumber + "-RC" + GetPaddedBuildNumber();
}


public string GetPaddedBuildNumber()
{
	var buildnumber = GetBuildNumber();
	return buildnumber.PadLeft(4,'0');
}

public string GetBuildNumber()
{
	return Environment.GetEnvironmentVariable("BUILD_BUILDNUMBER") ?? "0";
}

public static class ConfiguredProducts
{
	public static void BuildDoc(string docFolder, string outputFolder)
	{
		FileUtil.CreateCleanDirectory(outputFolder);

		var documentListFile = Path.Combine(docFolder, "documents.txt");
		if (!File.Exists(documentListFile))
		{
			return;
		}

		var documents = File.ReadAllLines(documentListFile);

		foreach (var document in documents)
		{
			var tokens = document.Split(',');
			var documentPath = Path.Combine(docFolder, tokens[0], tokens[1] + ".adoc");
			var resultFolder = Path.Combine(outputFolder, tokens[0]);
			Asciidoc.Build(documentPath, resultFolder);
		}
	}

	public static void Package(string ckmFolder, string deployFolder, string version)
	{
		FileUtil.CreateCleanDirectory(deployFolder);
		CreatePaketTemplate(deployFolder);
		
		CollectArchetypes(ckmFolder, deployFolder);
		CollectTemplates(ckmFolder, deployFolder);
		CollectLocalFiles(deployFolder);

//		RoboCopy.CopyFile(@"..\src\IsmTransitionConfigFiles", Path.Combine(deployFolder, "content", "IsmTransitionConfigFiles"), "*.json");
//		RoboCopy.CopyFile(@"..\src\forms", Path.Combine(deployFolder, "content", "forms"), "*.zip");
//		RoboCopy.CopyFile(@"..\src\tiles", Path.Combine(deployFolder, "content", "tiles"), "*.xml");
//		RoboCopy.CopyFile(@"..\src\vaqms", Path.Combine(deployFolder, "content", "vaqms"), "*.*");
//		RoboCopy.CopyFile(@"..\output", Path.Combine(deployFolder, "content"), "*.pdf");

//		RoboCopy.CopyFile(@"..\src\tools", Path.Combine(deployFolder, "tools"), "*.ps1");

		// NuGet packages without build number
		Paket.Pack(deployFolder, version, "minimum-from-lock-file include-referenced-projects buildplatform x86");
	}

	public static void Publish(string deployFolder)
	{
		if (!BuildServer.IsBuildServer)
       {
	       	Write.Error("Publishing to NuGet should only be done by the build server!");
	       	return;
       }

       	// NuGet
        // foreach (string file in Directory.GetFiles(deployFolder, "*.nupkg"))
        // {
		//    	Nuget.PublishPackage(Feeds.DIPSNuget, file);
		// }

		foreach (string file in Directory.GetFiles(deployFolder, "*.nupkg"))
		{
			Chocolatey.PublishPackage(Feeds.DIPSDev, file);
		}
	}

	private static void CreatePaketTemplate(string deployFolder)
	{
		var product = GetProductNameFromFolder();
		var paketTemplate = @"type file
id DIPS.Arena." + product + @".Configuration
authors DIPS ASA
description Configuration for " + product + @"
files
    .\tools\ ==> tools
	.\content\ ==> content";

		File.WriteAllText(Path.Combine(deployFolder, "paket.template"), paketTemplate);
	}

	private static void CollectArchetypes(string ckmFolder, string deployFolder)
	{
		var archetypeListFile = @"..\src\archetypes.txt";
		if (!File.Exists(archetypeListFile))
		{
			return;
		}

		var archetypes = File.ReadAllLines(archetypeListFile);

		foreach(var file in archetypes)
		{
			if (string.IsNullOrWhiteSpace(file))
			{
				continue;
			}

			var typeOfArchetype = System.Text.RegularExpressions.Regex.Match(file, @"openEHR-EHR-(\w+)\..+").Groups[1].Value.ToLower();
			string subFolder;
			switch (typeOfArchetype)
			{
				case "action":
				case "evaluation":
				case "instruction":
				case "observation":
				case "admin_entry":
					subFolder = @"entry\" + typeOfArchetype;
					break;
				default:
					subFolder = typeOfArchetype;
					break;				
			}

			RoboCopy.CopyFile(Path.Combine(ckmFolder, "archetypes", subFolder), Path.Combine(deployFolder, "content", "archetypes"), file);
		}
	}

	private static void CollectTemplates(string ckmFolder, string deployFolder)
	{
		var templateListFile = @"..\src\templates.txt";
		if (!File.Exists(templateListFile))
		{
			return;
		}

		var templates = File.ReadAllLines(templateListFile);

		foreach(var file in templates)
		{
			if (string.IsNullOrWhiteSpace(file))
			{
				continue;
			}

			RoboCopy.CopyFile(Path.Combine(ckmFolder, "opt"), Path.Combine(deployFolder, "content", "templates"), file);
		}
	}

	private static void CollectLocalFiles(string deployFolder)
	{
		RoboCopy.CopyFile(@"..\src\IsmTransitionConfigFiles", Path.Combine(deployFolder, "content", "IsmTransitionConfigFiles"), "*.json");
		RoboCopy.CopyFile(@"..\src\forms", Path.Combine(deployFolder, "content", "forms"), "*.zip");
		RoboCopy.CopyFile(@"..\src\tiles", Path.Combine(deployFolder, "content", "tiles"), "*.xml");
		RoboCopy.CopyFile(@"..\src\vaqms", Path.Combine(deployFolder, "content", "vaqms"), "*.*");
		RoboCopy.CopyFile(@"..\output", Path.Combine(deployFolder, "content"), "*.pdf");

		RoboCopy.CopyFile(@"..\src\tools", Path.Combine(deployFolder, "tools"), "*.ps1");
	} 

	private static string GetProductNameFromFolder()
	{
		var currentDir = Directory.GetCurrentDirectory();
		var tokens = currentDir.Split(Path.DirectorySeparatorChar);
		var product = tokens[tokens.Length - 2];

		return product;
	}
}