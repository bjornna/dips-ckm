#r "System.Management"

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Microsoft.Win32;

public partial class BuildEnv
{
    public const string CWD_ENV = "CurrentWorkingDirectory";
    public const string BUILD = "build";
    public const string BINARIES = "binaries";
    public const string PACKAGES = "packages";

    private readonly string rootDir;
    
    private readonly string workDir;

    public BuildEnv()
        : this(Path.Combine(Environment.CurrentDirectory, ".."), Environment.GetEnvironmentVariable(CWD_ENV) ?? Environment.CurrentDirectory)
    {}

    public BuildEnv(string rootDir, string workDir)
    {
        this.rootDir = Path.GetFullPath(rootDir);
        this.workDir = Path.GetFullPath(workDir);
    }

    static BuildEnv()
    {
        Instance = new BuildEnv();
    }

    public static BuildEnv Instance { get; set; }

    public static string RootDir { get { return Instance.rootDir; } }
    
    public static string WorkDir { get { return Instance.workDir; } }

    public static string BuildDir { get { return Path.Combine(RootDir, BUILD); } }
    public static string BinariesDir { get { return Path.Combine(RootDir, BINARIES); } }
    public static string PackagesDir { get { return Path.Combine(RootDir, PACKAGES); } }
}

public static partial class MsBuild
{
    private static Regex warningRegex = new Regex(@"warning \w+\d+:");
    private static Regex errorRegex = new Regex(@"error \w+\d+:");
    
    private static Dictionary<string, BuildResult> buildstatusCounter = new Dictionary<string, BuildResult>();   
    
    private static string currentSolutionFile;      
    
    public static void Clean(string[] pathsToSolutionFiles, string configuration = "Release", string verbosity = "quiet")
    {
        Build(pathsToSolutionFiles, configuration, verbosity, "Clean");
    }

    public static void Clean(string pathToSolutionFile, string configuration = "Release", string verbosity = "quiet")
    {
        Build(pathToSolutionFile, configuration, verbosity, "Clean");
    }

    public static void Build(string[] pathsToSolutionFiles, string configuration = "Release", string verbosity = "quiet", string target = "Build", string arguments = "")
    {
        buildstatusCounter.Clear();        
        foreach (var pathToSolutionFile in pathsToSolutionFiles) {
            Build(pathToSolutionFile, configuration, verbosity, target, arguments);
        }        
        WriteBuildSummary();
    }

    public static void Build(string pathToSolutionFile, string configuration = "Release", string verbosity = "quiet", string target = "Build", string arguments = "")
    {
        var stopWatch = Stopwatch.StartNew();                      
        currentSolutionFile = Path.GetFileName(pathToSolutionFile);
        if(!buildstatusCounter.ContainsKey(currentSolutionFile))
        {
            buildstatusCounter.Add(currentSolutionFile,new BuildResult());
        }
        
        Write.Caption("Running MsBuild for " + pathToSolutionFile + " (" + configuration + ")");
        if(verbosity == "quiet") Write.Caption("Only showing warnings and errors.");
        string pathToMsBuild = PathUtils.MsBuildPath();

        Command.Execute(pathToMsBuild, pathToSolutionFile + String.Format(" /property:Configuration={0} /t:{1} /verbosity:{2} /nologo {3} {4}",
                                                     configuration, target, verbosity, arguments, GetExtraParameters()), LineHandler, ErrorHandler);
        stopWatch.Stop();
        buildstatusCounter[currentSolutionFile].Elapsed = stopWatch.Elapsed;                                                     
    }
    
    private static void WriteBuildSummary()
    {
        if(BuildServer.IsBuildServer) return;
        
        Write.Info(string.Empty);
        Write.Info("Build summary:");
        var maxLength = buildstatusCounter.Keys.Max(k=>k.Length) + 1;
        Write.Info("Solution".PadRight(maxLength)+"| Warnings | Errors | Elapsed");
        Write.Info("-".PadRight(maxLength + 38,'-'));
        foreach(var v in buildstatusCounter.OrderBy(v => v.Key))
        {
            Write.Info("{0}|{1} |{2} | {3}",v.Key.PadRight(maxLength),v.Value.Warnings.ToString().PadLeft(9), v.Value.Errors.ToString().PadLeft(7), v.Value.Elapsed);
        }
        Write.Info(string.Empty);
    }           
        
      
    private static void LineHandler(string data)
    {
        if(BuildServer.IsBuildServer) return;
        if(string.IsNullOrEmpty(data)) return;
        
        if(warningRegex.IsMatch(data))
        {            
            buildstatusCounter[currentSolutionFile].Warnings++;
            Write.Warning(data);
        } else if(errorRegex.IsMatch(data))
        {
            buildstatusCounter[currentSolutionFile].Errors++;
            Write.Error(data);
        }  
        else 
        {
            Write.Info(data);
        }
    }        
    
    private static void ErrorHandler(string data)
    {
        if(BuildServer.IsBuildServer) return;
        if(string.IsNullOrEmpty(data)) return;
        
        buildstatusCounter[currentSolutionFile].Errors++;
        Write.Error(data);          
    }
        
    private static string GetExtraParameters()
    {
        var parameters = "";

        if (BuildServer.IsTeamCity)
        {
            var agentDir =  Environment.GetEnvironmentVariable("AGENT_HOME");

            if (!string.IsNullOrEmpty(agentDir))
            {
                var pluginPath = Path.Combine(agentDir, "plugins/dotnetplugin/bin/JetBrains.BuildServer.MSBuildLoggers.dll");
                if (File.Exists(pluginPath))
                {
                    Write.Info("Running on TeamCity. Adding TeamCity MSBuild logger");
                    parameters = "/l:JetBrains.BuildServer.MSBuildLoggers.MSBuildLogger," + pluginPath;
                }
            }
        }
        if (BuildServer.IsTfsBuild)
        {
            var tfsLoggerPath = PathUtils.TfsMsBuildLoggerPath();

            if (BuildServer.IsTfs2015Build)
            {
                var agentFolder = Environment.GetEnvironmentVariable("AGENT_HOMEDIRECTORY");
                if (!string.IsNullOrEmpty(agentFolder))
                {
                    var agentLogger = Path.Combine(agentFolder, "agent/worker/Microsoft.TeamFoundation.DistributedTask.MSBuild.Logger.dll");
                    parameters = string.Format("\"/dl:CentralLogger,{0}*ForwardingLogger,{0}\"",agentLogger);
                }
            }
            else 
            {
                var tfsBuildUri = Environment.GetEnvironmentVariable("TF_BUILD_BUILDURI");
                var informationNodeId = Environment.GetEnvironmentVariable("TF_BUILD_INFORMATIONNODEID");
                var tfsUri = Environment.GetEnvironmentVariable("TF_BUILD_COLLECTIONURI") ;

                parameters = string.Format("\"/dl:WorkflowCentralLogger,{0};Verbosity=Normal;BuildUri={1};informationNodeId={2};TFSUrl={3};*WorkflowForwardingLogger,{0};Verbosity=Normal;\"", 
                                        tfsLoggerPath, tfsBuildUri, informationNodeId, tfsUri);
            }
        }
        
        // Note: The default vale for nodeReuse is true, which causes trouble on build servers where the msbuild process
        // keeps files locked and prevents successive builds from cleaning the working folder.
        if (BuildServer.IsBuildServer)
        {
            parameters += " /nodeReuse:false";
        }

        return parameters;
    }
}

public static partial class BuildServer
{ 
    public static void StartTask(string taskName)
    {
        if (IsTeamCity)
        {
            Write.Info(string.Format("##teamcity[blockOpened name='{0}']", taskName));
            Write.Info(string.Format("##teamcity[progressMessage 'Running {0}']", taskName));
        }
    }

    public static void EndTask(string taskName)
    {
        if (IsTeamCity)
        {
            Write.Info(string.Format("##teamcity[progressFinish '{0} finished']", taskName));
            Write.Info(string.Format("##teamcity[blockClosed name='{0}']", taskName));
        }
    }

    public static void ReportBuildNumber(string buildNumber)
    {
        if (IsTeamCity)
        {
            Write.Info(string.Format("##teamcity[buildNumber '{0}']", buildNumber));
        }
    }

    public static void PublishArtifact(string path)
    {
        if (IsTeamCity)
        {
            Write.Info(string.Format("##teamcity[publishArtifacts '{0}']", path));
        }
        else if (IsTfs2015Build)
        {
            Write.Info(string.Format("##vso[artifact.upload]{0}", path));
        }
    }

    public static bool IsBuildServer
    {
        get
        {
            return IsTeamCity || IsTfsBuild;
        }
    }

    public static bool IsTfsBuild
    {
        get
        {
            return Environment.GetEnvironmentVariable("TF_BUILD") != null;
        }
    }

    public static bool IsTfs2015Build
    {
        get
        {
            return IsTfsBuild && (Environment.GetEnvironmentVariable("BUILD_BUILDURI") != null);
        }
    }

    public static bool IsTeamCity
    {
        get
        {
            return Environment.GetEnvironmentVariable("TEAMCITY_VERSION") != null;
        }
    }
    
    public static string GetCIBuildNumber()
    {
        return "-CI" + GetPaddedBuildNumber();
    }
    
    public static string GetRCBuildNumber()
    {
        return "-RC" + GetPaddedBuildNumber();
    }
    
    public static string GetPreBuildNumber()
    {
        return "-pre" + GetPaddedBuildNumber();
    }
    
    public static string GetPaddedBuildNumber()
    {
        var buildnumber = GetBuildNumber();
	    return buildnumber.PadLeft(4,'0');
    }
    
    public static string GetBuildNumber()
    {
        if (IsTeamCity)
        {
            return Environment.GetEnvironmentVariable("build_number");
        }
        
        if (IsTfs2015Build)
        {
            return Environment.GetEnvironmentVariable("BUILD_BUILDNUMBER");
        }
	    
        Write.Warning("Not on build server, unable to retrieve build number.");
        return "0";
    }
}

public static partial class VsTest
{
    public static void Run(string pathToTestAssembly, string arguments = "")
    {
        Run(new [] { pathToTestAssembly }, arguments);
    }
    
    public static void RunWithCodeCoverage(string[] pathsToTestAssemblies, int minimumCoveragePercentage = 0, string arguments = "", string modulePattern = null) {
        Write.Caption("Running VsTest with Code Coverage");
        
        const string CoverageFile = "VisualStudio.coverage";
        const string CoverageXML = "VisualStudio.coverage.xml";
        if (File.Exists(CoverageFile))
        {
            Write.Caption("Found coverage files from previous run, deleting...");
            File.Delete(CoverageFile);
            if (File.Exists(CoverageXML))
            {
                File.Delete(CoverageXML);
            }
        }
        
        Write.Caption("Test run start");
        var runArguments = String.Format("collect /output:{0} {1} {2} {3} /Logger:trx", CoverageFile, PathUtils.VsTestPath(), NormalizeTestAssembliesPaths(pathsToTestAssemblies), arguments);
        Command.Execute(PathUtils.CodeCoveragePath(), runArguments);
        
        Write.Caption("Converting Code Coverage Results");
        
        var analyzeArguments = string.Format("analyze /output:{0} VisualStudio.coverage", CoverageXML);
        Command.Execute(PathUtils.CodeCoveragePath(), analyzeArguments);
        
        float currentCodeCoverage = AnalyzeCodeCoverage(CoverageXML, modulePattern);
        if (currentCodeCoverage < minimumCoveragePercentage)
        {
            throw new Exception("Code Coverage treshold is " + minimumCoveragePercentage + "%, current Code Coverage is " + currentCodeCoverage + "%.");
        }
    }

    public static void Run(string[] pathsToTestAssemblies, string arguments = "")
    {
        Write.Caption("Running VsTest");
        string pathToVsTest = PathUtils.VsTestPath();
        var testAssemblies = NormalizeTestAssembliesPaths(pathsToTestAssemblies);
        Command.Execute(pathToVsTest,  String.Format("{0} {1} /Logger:trx", testAssemblies, arguments));
    }

    public static void RunIntegration(string pathToTestAssembly, string arguments = "")
    {
        RunIntegration(new [] { pathToTestAssembly }, arguments);
    }

    public static void RunUnit(string pathToTestAssembly, string arguments = "")
    {
        RunUnit(new [] { pathToTestAssembly }, arguments);
    }

    public static void RunIntegration(string[] pathsToTestAssemblies, string arguments = "")
    {
        Run(pathsToTestAssemblies, "/TestCaseFilter:TestCategory=IntegrationTest|TestCategory=LongRunningTest|TestCategory=DIPSApiTest " + arguments);
    }

    public static void RunUnit(string[] pathsToTestAssemblies, string arguments = "")
    {
        Run(pathsToTestAssemblies, "/TestCaseFilter:TestCategory=UnitTest " + arguments);
    }
    
    static string NormalizeTestAssembliesPaths(string[] pathToTestAssemblies) {
        return String.Join(" ", pathToTestAssemblies);
    }
    
    static float AnalyzeCodeCoverage(string coverageXMlPath, string modulePattern = null) {
        Write.Caption("Analyzing Code Coverage");
        var xml = XElement.Load(coverageXMlPath);
        IEnumerable<XElement> modules;
        if (modulePattern == null)
        {
            modules = xml.Elements("modules")
                .Elements("module")
                .Where(e => !e.Attribute("name").Value.StartsWith("testdips", true, CultureInfo.InvariantCulture));
        }
        else
        {
            modulePattern = modulePattern.ToLowerInvariant();
            var regex = new Regex(modulePattern);
            modules = xml.Elements("modules")
                .Elements("module")
                .Where(e => !e.Attribute("name").Value.StartsWith("testdips", true, CultureInfo.InvariantCulture) 
                         && (e.Attribute("name").Value.Contains(modulePattern) || regex.IsMatch(e.Attribute("name").Value)));
        }
        
        modules = modules.GroupBy(e => e.Attribute("name").Value)
                         .Select(grp => grp.OrderByDescending(e => e.Attribute("block_coverage").Value).First());

        var blocksCovered = 0;
        var blocksNotCovered = 0;
        Console.WriteLine("{0,40}{1,20}{2,20}{3,20}", "Name of DLL", "Covered", "Covered", "Not covered");
        Console.WriteLine("{0,40}{1,20}{2,20}{3,20}", "", "%", "blocks", "blocks");
        foreach (var module in modules)
        {
            var blocksCoveredString = module.Attribute("blocks_covered").Value;
            blocksCovered += int.Parse(blocksCoveredString);
            var blocksNotCoveredString = module.Attribute("blocks_not_covered").Value;
            blocksNotCovered += int.Parse(blocksNotCoveredString);
            Console.WriteLine("{0,40}{1,20}{2,20}{3,20}", module.Attribute("name").Value, module.Attribute("block_coverage").Value, blocksCoveredString, blocksNotCoveredString);
        }

        Console.WriteLine();
        var coverage = (int)((blocksCovered / (float)(blocksNotCovered + blocksCovered)) * 100);
        Console.WriteLine("{0,40}{1,20}{2,20}{3,20}", "Summary", coverage.ToString(), blocksCovered.ToString(), blocksNotCovered.ToString());
        return coverage;
    }
}

public static partial class Feeds
{
    public const string DIPSNuget = "http://dips-nuget/nuget/DIPS";
    public const string DIPS3rdParty = "http://dips-nuget/nuget/3rdParty";
    public const string DIPSDev = "http://dips-nuget/nuget/DIPS-Dev";
    public const string DIPSRC = "http://dips-nuget/nuget/DIPS-RC";
    public const string DIPSRelease = "http://dips-nuget/nuget/DIPS-Release";
    public const string DIPSDBDev = "http://dips-nuget/nuget/DIPS-DB-Dev";
    public const string DIPSDBRC = "http://dips-nuget/nuget/DIPS-DB-RC";
    public const string DIPSDBRelease = "http://dips-nuget/nuget/DIPS-DB-Release";
}

public static partial class Proget
{
    public static bool FeedContainsPackage(string feedUrl, string pathToNupkgFile)
    {
        string fileName = Path.GetFileName(pathToNupkgFile);
        const string strictSemanticVersionRegex = @"(?<Version>\d+(\.\d+){2})(?<Release>-[a-z][0-9a-z-]*)?";
        Regex semverRegex = new Regex(strictSemanticVersionRegex, RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
        Match regexMatches = semverRegex.Match(fileName);
        if(regexMatches.Success){
            string version = regexMatches.Value;
            string packageId = fileName.Substring(0, regexMatches.Index - 1);
            return FeedContainsPackage(feedUrl, packageId, version);
        }
        
        Write.Warning("Unable to get version number from package specified.");
        return false;
    }
    
    public static bool FeedContainsPackage(string feedUrl, string packageId, string versionNumber)
    {
        int feedId = GetFeedId(feedUrl);
        return FeedContainsPackage(feedId, packageId, versionNumber);
    }
    
    public static bool FeedContainsPackage(int feedId, string packageId, string versionNumber)
    {
        string url = "http://dips-nuget/api/json/Packages_GetPackage?API_Key=Admin:Admin&Feed_Id="
                   + feedId
                   + "&Package_Id="
                   + packageId
                   + "&Version_Text="
                   + versionNumber;
                   
        using (WebClient webClient = new WebClient())
        {
            string response = webClient.DownloadString(url);
            return response.Contains(packageId);
        }
    }
    
    private static int GetFeedId(string feedUrl)
    {
        switch (feedUrl)
        {
            case Feeds.DIPSNuget:
                return 2;
            case Feeds.DIPS3rdParty:
                return 3;
            case Feeds.DIPSDev:
                return 6;
            case Feeds.DIPSRC:
                return 7;
            case Feeds.DIPSRelease:
                return 8;
            case Feeds.DIPSDBDev:
                return 10;
            case Feeds.DIPSDBRC:
                return 11;
            case Feeds.DIPSDBRelease:
                return 12;
            default:
                throw new Exception("Unable to get feedId from " + feedUrl + "\nConsider using FeedContainsPackage(int,string,string)");
        }
    }
}

public static partial class Paket
{
    private static readonly string paketDependencies = "paket.dependencies";
    private static readonly string paketDependenciesTemplate = "paket.dependencies.template";
    private static readonly string paketVersionsDir = Path.Combine(BuildEnv.RootDir, "paketversions");
    private static readonly string paketDir = Path.Combine(BuildEnv.RootDir, ".paket");
    private static readonly string paketExe = Path.Combine(paketDir, "paket.exe");
    private static readonly string paketBootstrapperExe = Path.Combine(paketDir, "paket.bootstrapper.exe");
    private static readonly string paketDependenciesFile = Path.Combine(BuildEnv.RootDir, paketDependencies);
    private static readonly string paketLockFile = Path.Combine(BuildEnv.RootDir, "paket.lock");
    
    public static void Install(string bootstrapperArguments = "")
    {
        ExecutePaketCommand("install", bootstrapperArguments);
    }
    
    public static void Update(string bootstrapperArguments = "")
    {
        ExecutePaketCommand("update", bootstrapperArguments);
    }
    
    public static void Restore(string bootstrapperArguments = "")
    {
        if(!File.Exists(paketLockFile))
        {
            Write.Warning("'paket.lock' was not found in your module's root directory");
            ExecutePaketCommand("install", bootstrapperArguments);    
        }
        
        ExecutePaketCommand("restore", bootstrapperArguments);
    }

    public static void Pack(string outputDir, string version, string additionalArguments = "", string bootstrapperArguments = "")
    {
        var packArguments = "pack -v output " + outputDir + " version " + version + " " + additionalArguments;
        ExecutePaketCommand(packArguments, bootstrapperArguments);
    }
    
    private static void ExecutePaketCommand(string argument, string bootstrapperArguments = "")
    {
        if(!Directory.Exists(paketDir))
        {
            Write.Error(paketDir + @" was not found, go to https://fsprojects.github.io/Paket/index.html and follow instructions");
            throw new DirectoryNotFoundException("Was not able to locate " + paketDir);
        }

        if(!File.Exists(paketDependenciesFile))
        {
            Write.Error("Could not find paket.dependencies in your module's root directory");
            throw new FileNotFoundException("Could not find 'paket.dependencies' in your module's root directory");
        }

        if(!File.Exists(paketBootstrapperExe))
        {
            Write.Error(paketBootstrapperExe + @" was not found, go to https://fsprojects.github.io/Paket/index.html and follow instructions");
            throw new FileNotFoundException("Was not able to find 'paket.bootstrapper.exe' in " + paketDir);
        }
        
        Write.Caption("Installing or updating paket.exe");
        Command.Execute(paketBootstrapperExe, bootstrapperArguments);
        
        Command.WorkingDirectory = BuildEnv.RootDir;
        Write.Caption("Running paket " + argument);
        Command.Execute(paketExe, argument);
    }
    
    public static void GetVersionRangesFromNugetPackage(string nugetPackage = null, string sourceFeed = null)
    {
        nugetPackage = nugetPackage ?? "Arena.Framework.Delivery.PaketDependencies";
        sourceFeed = sourceFeed ?? Feeds.DIPSNuget;     

        DownloadPaketDependenciesFromSourceFeed(nugetPackage, sourceFeed);
        
        var versionPlaceholder = "{version}";
        var paketDependenciesLines = new List<string>();
        var deliveryDependenciesLines = File.ReadAllLines(Path.Combine(paketVersionsDir, paketDependencies));
        var paketDependenciesTemplateLines = File.ReadLines(Path.Combine(BuildEnv.RootDir, paketDependenciesTemplate));
        foreach (var templateLine in paketDependenciesTemplateLines)
        {
            var paketDependenciesLine = templateLine;
            if (templateLine.Contains(versionPlaceholder))
            {
                var templateLineSplit = templateLine.Split(' ');
                var packageName = templateLineSplit[1];
                foreach (var deliveryLine in deliveryDependenciesLines)
                {
                    var extendedPackageName = "nuget " + packageName + " ";
                    if (deliveryLine.Contains(extendedPackageName))
                    {
                        var versionRange = deliveryLine.Replace(extendedPackageName, "");
                        Write.Info("Replacing " + versionPlaceholder + " with " + versionRange + " for package " + packageName);
                        paketDependenciesLine = templateLine.Replace(versionPlaceholder, versionRange);
                    }
                }
            }
            
            paketDependenciesLines.Add(paketDependenciesLine);
        }
        
        VerifyThatAllVersionsAreSet(paketDependenciesLines, versionPlaceholder);
        
        var path = Path.Combine(BuildEnv.RootDir, paketDependencies);
        Write.Info("Writing " + paketDependencies + " contents to file " + path + "...");
        File.WriteAllLines(path, paketDependenciesLines);
        
        Directory.Delete(paketVersionsDir, true);
    }
    
    private static void DownloadPaketDependenciesFromSourceFeed(string nugetPackage, string sourceFeed)
    {
        var tempdir = BuildEnv.RootDir + @"\temp";
        
        Write.Info("Downloading " + nugetPackage + "...");
        Nuget.Install(nugetPackage + " -Source " + sourceFeed + "  -Prerelease -NoCache -o " + StringUtils.Quote(tempdir));
        
        var paketDependenciesPath = GetPaketDependenciesDirectoryPath(tempdir);
        Write.Info("Copying " + paketDependencies + " from temporary directory to " + paketVersionsDir + "...");
        RoboCopy.CopyFile(paketDependenciesPath, paketVersionsDir, paketDependencies, arguments: "/E", quiet: true);
        
        Write.Info("Deleting temporary directory...");
        FileUtil.DeleteDirectory(tempdir, true);
    }
    
    private static string GetPaketDependenciesDirectoryPath(string tempdir)
    {
        return Path.GetDirectoryName(Directory.EnumerateFiles(tempdir, "paket.dependencies", SearchOption.AllDirectories).First());
    }
    
    private static void VerifyThatAllVersionsAreSet(IEnumerable<string> paketDependenciesLines, string versionPlaceholder)
    {
        var versionsNotSet = new List<string>();
        
        foreach (var line in paketDependenciesLines)
        {
            if (line.Contains(versionPlaceholder))
            {
                versionsNotSet.Add(line);
            }
        }
        
        if (versionsNotSet.Count != 0)
        {
            var stringBuilder = new StringBuilder("The following versions have not been set:");
            foreach (var versionNotSet in versionsNotSet)
            {
                stringBuilder.Append("\n");
                stringBuilder.Append(versionNotSet);
            }
            
            stringBuilder.Append("\n");
            
            throw new Exception(stringBuilder.ToString());
        }   
    }
}

public static partial class Nuget
{
    public static void DeleteOldNupkgFiles(string fileNamePattern = "*.nupkg", string path = null)
    {
        path = path ?? System.IO.Directory.GetCurrentDirectory();
        foreach (string file in Directory.GetFiles(path, fileNamePattern, SearchOption.AllDirectories))
        {
            File.Delete(file);
        }
    }

    public static void Restore(string[] solutionPaths, string arguments = "")
    {
        foreach(string solutionPath in solutionPaths)
        {
            Restore(solutionPath, arguments);
        }
    }

    public static void Restore(string solutionPath, string arguments = "")
    {
        Command.Execute("nuget", string.Format("restore {0} {1}", solutionPath, arguments));
    }

    /// Updates nuget.exe
    public static void UpdateSelf()
    {
        Write.Caption("Updating nuget");
        var arguments = "update -self";
        Command.Execute("nuget", arguments);
    }

    public static void Install(string arguments = "")
    {
        Console.WriteLine("nuget install " + arguments);
        Command.Execute("nuget", string.Format("install {0}", arguments));
    }

    public static string CreatePackage(string nuspecPath, string version = "0.0.0.0", string outputDirectory = ".", string additionalProperties = "", string additionalArguments = "")
    {
        return Chocolatey.CreatePackage(nuspecPath,version,outputDirectory,additionalProperties, additionalArguments);
    }

    public static void PublishPackage(string nugetFeedUrl, string packagePath, string apiKey = "\"Admin:Admin\"")
    {
        Chocolatey.PublishPackage(nugetFeedUrl,packagePath,apiKey);
    }

    public static void ClearLocalCache()
    {
        var nugetCachePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\NuGet\Cache\";
        if (Directory.Exists(nugetCachePath))
        {
            Write.Info("Deleting " + nugetCachePath);
            Directory.Delete(nugetCachePath, true);
        }
    }
}

public static partial class InternalDependencies
{
    public static void Fetch(string[] pathsToExternalAssemblies, string folderName = @"..\lib", string secondFolder = null)
    {
        string scriptBasePath = Directory.GetCurrentDirectory();
        string combinedPath = Path.Combine(scriptBasePath, folderName);
        string fullDestinationPath = Path.GetFullPath(combinedPath);

        Directory.CreateDirectory(fullDestinationPath);
        Write.Caption("Copying files to " + fullDestinationPath);

        var sources = pathsToExternalAssemblies.Select(p => Path.GetDirectoryName(p)).Distinct();

        foreach (string source in sources)
        {
            var fileNames = pathsToExternalAssemblies.Where(p => Path.GetDirectoryName(p) == source).Select(p => Path.GetFileName(p));
            var fileNamesArg = string.Join(" ", fileNames.Select(f => StringUtils.Quote(f)));
            
            RoboCopy.CopyFile(StringUtils.Quote(source), StringUtils.Quote(fullDestinationPath), fileNamesArg, quiet: false);

            if (secondFolder != null)
            {
                RoboCopy.CopyFile(StringUtils.Quote(fullDestinationPath), StringUtils.Quote(secondFolder), fileNamesArg, quiet: true);
            }
        }
    }

    public static void FetchByRegex(string pathToExternalAssemblies, string[] regexStrings, string folderName = @"..\lib", string secondFolder = null)
    {
        var regexString = @"^(";
        foreach (var sRegex in regexStrings)
        {
            regexString += "(" + sRegex + ")|";
        }

        regexString = regexString.Substring(0, regexString.Length - 1) + ")$";
        var regex = new System.Text.RegularExpressions.Regex(regexString);
        var matches = new List<string>();
        foreach (var entry in Directory.GetFiles(pathToExternalAssemblies))
        {
            if (regex.Match(Path.GetFileName(entry)).Success){
                matches.Add(entry);
            }
        }

        Fetch(matches.ToArray(), folderName, secondFolder);
    }
}

public static partial class Chocolatey
{
    public static string CreatePackage(string nuspecPath, string version = "0.0.0", string outputDirectory = ".", string additionalProperties = "", string additionalArguments = "")
    {
        Write.Caption("Creating chocolatey package from " + nuspecPath);
        var arguments = string.Format("-version {0} -Properties \"Version={1};" + additionalProperties + "\" -NoPackageAnalysis -OutputDirectory {2} {3}",
            version, version, StringUtils.Quote(outputDirectory), additionalArguments);
        Write.Warning(arguments);
        Command.Execute("nuget", string.Format("pack {0} {1}", nuspecPath, arguments));

        var chocolateyPackageRelativePath = outputDirectory + "/" + Path.GetFileNameWithoutExtension(nuspecPath) + "." + version + ".nupkg";
        return Path.GetFullPath(chocolateyPackageRelativePath);
    }

    public static void PublishPackage(string chocolateyFeedUrl, string packagePath, string apiKey = "\"Admin:Admin\"")
    {
        var arguments = string.Format("push {0} {1} -s {2}", packagePath, apiKey, chocolateyFeedUrl);
        Command.Execute("nuget", arguments);
    }

    public static void Deploy(string computerName, string packageName, string version, string feedName, bool preRelease = false, string arguments = "")
    {
        Command.RemoteExecute(computerName, string.Format("choco install {0} -version {1} -source {2} -force {3} -y {4}", packageName, version, feedName, preRelease ? "-pre" : "", arguments));
    }

    public static void InstallLocalArenaClient(string arenaServerHostName, bool useHttps = true, string installDir = null, string sourceFeed = null, bool allowPrerelease = true)
    {
        installDir = installDir ?? BuildEnv.RootDir;
        sourceFeed = sourceFeed ?? Feeds.DIPSDev;
        var parameters = new Dictionary<string, string>();
        
        parameters.Add("ArenaServerHostName", arenaServerHostName);
        parameters.Add("UseHttps", useHttps.ToString());
        
        var arenaChocolateyPackages = new Dictionary<string, Dictionary<string, string>>
        {
            { "dips-choco-utility", parameters },
            { "dips-arena-desktop-client-config", parameters },
            { "dips-arena-dependencies-client", parameters },
            { "dips-arena-framework-client", parameters },
            { "dips-arena-desktop-client", parameters },
        };

        InstallLocalArenaClientPackages(arenaChocolateyPackages, sourceFeed, allowPrerelease, installDir);
    }
    
    public static void InstallLocalArenaClientPackages(Dictionary<string, Dictionary<string, string>> chocolateyPackages, string sourceFeed = null, bool allowPrerelease = true, string installDir = null)
    {
        sourceFeed = sourceFeed ?? Feeds.DIPSDev;
        installDir = installDir ?? BuildEnv.RootDir;
        
        var additionalArguments = @" -Source " + sourceFeed + " -fy";
        
        if (allowPrerelease)
        {
            additionalArguments = additionalArguments + " -pre";
        }

        foreach (var chocolateyPackage in chocolateyPackages) 
        {
            var parameters = chocolateyPackage.Value ?? new Dictionary<string, string>();
            
            if (!parameters.ContainsKey("InstallLocation"))
            { 
                parameters.Add("InstallLocation", installDir);
            }
            
            var parameterString = CreateParameterString(parameters);
            InstallLocalArenaClientPackage(chocolateyPackage.Key, additionalArguments, parameterString);
        }
    }
    
    private static void InstallLocalArenaClientPackage(string packageName, string additionalArguments, string parameters)
    {
        const string Upgrade = "upgrade ";
        const string Choco = "choco";
        var arguments = Upgrade + packageName + additionalArguments + parameters;
        Command.Execute(Choco, arguments); 
    }
    
    private static string CreateParameterString(Dictionary<string, string> parameters)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append(" --parameters \"");
        foreach (var parameter in parameters)
        {
            stringBuilder.Append("/");
            stringBuilder.Append(parameter.Key);
            stringBuilder.Append(":");
            stringBuilder.Append(parameter.Value);
            stringBuilder.Append(" ");
        }

        stringBuilder.Append("\"");

        return stringBuilder.ToString();
    }

    [Obsolete("Use 'InstalLocalArenaClient(string arenaServerHostName))' instead")]
    public static void InstallLocalArenaClient(string installDir, string parameters, string sourceFeed = null)
    {
        sourceFeed = sourceFeed ?? Feeds.DIPSDev;
        
        var arenaChocolateyPackages = new []
        {
            "dips-arena-desktop-client-config --parameters \"/InstallLocation:" + installDir + " " + parameters + "\"",
            "dips-arena-dependencies-client --parameters \"/InstallLocation:" + installDir + "\"",
            "dips-arena-framework-client --parameters \"/InstallLocation:" + installDir + "\"",
            "dips-arena-desktop-client --parameters \"/InstallLocation:" + installDir + "\"",
        };

        InstallLocalArenaClientPackages(installDir, arenaChocolateyPackages);
    }

    [Obsolete("Use 'InstallLocalArenaClientPackages(Dictionary<string, Dictionary<string, string>> chocolateyPackages)' instead")]
    public static void InstallLocalArenaClientPackages(string installDir, string[] chocolateyPackages, string sourceFeed = null)
    {
        sourceFeed = sourceFeed ?? Feeds.DIPSDev;
        
        var upgrade = "upgrade ";
        var additionalArguments = @" -Source " + sourceFeed + " -fy -pre";

        foreach (var chocolateyPackage in chocolateyPackages) 
        {
            var arguments = upgrade + chocolateyPackage + additionalArguments;
            Command.Execute("choco", arguments); 
        }
    }

    public static void ClearLocalCache(string[] packagesToClear)
    {
        var chocolateyCache = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\chocolatey\lib";

        foreach (string package in packagesToClear)
        {
            foreach (string directory in Directory.GetDirectories(chocolateyCache, package))
            {
                Write.Info("Deleting " + directory);
                Directory.Delete(directory, true);
            }
        }
    }
}

public static partial class RoboCopy
{
    public static void CopyFile(string source, string destination, string filename, bool quiet = true, string arguments = "/NFL /NDL /NJH /NJS /nc /ns /np /S")
    {
        Command.Execute("robocopy", string.Format("\"{0}\" \"{1}\" \"{2}\" {3}", source, destination, filename, arguments), quiet: quiet);
    }
    
    public static void CopyDirectory(string source, string destination, bool quiet = true, string arguments = "/NFL /NDL /NJH /NJS /nc /ns /np /S", string[] excludeDirs = null, string[] excludeFiles = null)
    {
        var parameters = string.Format("{0} {1}", source, destination);
        if (excludeDirs != null && excludeDirs.Any())
        {
            parameters = parameters + " /XD " + string.Join(" ", excludeDirs);
        }

        if (excludeFiles != null && excludeFiles.Any())
        {
            parameters = parameters + " /XF " + string.Join(" ", excludeFiles);
        }

        if (!String.IsNullOrEmpty(arguments))
        {
            parameters = parameters + " " + arguments;
        }

        Write.Info("Attempting to copy " + source);
        Command.Execute("robocopy", parameters, quiet: quiet);
    }
}

public static partial class AssemblyInfo
{
    [Obsolete("Do not use SharedAssemblyInfo. Use 'Update(string searchDirectory, string version)' instead, which updates all AssemblyInfo in searchDirectory")]
    public static void UpdateShared(string assemblyInfoPath, string semanticVersion, string buildNumber = "0", string productName = "DIPS EPJ/PAS")
    {
        var assemblyVersion = Regex.Replace(semanticVersion, @"[0-9]+$", "0.0"); // Major.Minor.0.0
        var year = DateTime.Now.Year.ToString();

        var assemblyInfo = @"using System.Reflection;
            [assembly: AssemblyVersion(""" + assemblyVersion + @""")]
            [assembly: AssemblyFileVersion(""" + semanticVersion + "." + buildNumber + @""")]
            [assembly: AssemblyCopyright(""Copyright © 1990-" + year + @" DIPS ASA"")]
            [assembly: AssemblyProduct(""" + productName + @""")]
            [assembly: AssemblyTrademark(""" + productName + @""")]
            [assembly: AssemblyCompany(""DIPS ASA"")]
            [assembly: AssemblyInformationalVersion(""" + semanticVersion + @""")]";

        System.IO.File.WriteAllText (assemblyInfoPath, assemblyInfo, Encoding.UTF8);
        Write.Caption("Updated assembly info of " + assemblyInfoPath);
    }

    public static string CurrentFileVersion(string assemblyInfoPath)
    {
        var content = File.ReadAllText(assemblyInfoPath);

        var pattern = @"(?<=\[assembly: AssemblyFileVersion\(\"")(\d+.\d+.\d+.\d+)";
        Regex rgx = new Regex(pattern);
        var match = rgx.Match(content);
        var fileVersion = match.Groups[0].Value;

        return fileVersion;
    } 
    
    public static string CurrentAssemblyVersion(string assemblyInfoPath)
    {
        var content = File.ReadAllText(assemblyInfoPath);

        var pattern = @"(?<=\[assembly: AssemblyVersion\(\"")(\d+.\d+.\d+.\d+)";
        Regex rgx = new Regex(pattern);
        var match = rgx.Match(content);
        var assemblyVersion = match.Groups[0].Value;

        return assemblyVersion;
    }

    public static string CurrentProductVersion(string assemblyInfoPath)
    {
        var content = File.ReadAllText(assemblyInfoPath);

        var pattern = @"(?<=\[assembly: AssemblyInformationalVersion\(\"")(\d+.\d+.\d+.\d+)";
        Regex rgx = new Regex(pattern);
        var match = rgx.Match(content);
        var productVersion = match.Groups[0].Value;

        return productVersion;
    }
    
    private static string GetMajorVersion(string version)
    {
        return version.Substring(0, version.IndexOf('.')) + ".0.0.0";
    }
    
    ///
    /// Update all AssemblyInfo.cs files in searchDirectory 
    /// Will set version as AssemblyFileVersion, will use only Major version in AssemblyVersion,
    /// Will set AssemblyCompany, AssemblyCopyright 
    /// AssemblyInformationalVersion will be version / versioninfo / git commit hash
    public static void Update(string searchDirectory, string version, string versionInformation = null, bool appendGitCommit = true)
    {
        Write.Info(string.Format("Writing version {0} to all AssemblyInfo.cs files in {1}", version, searchDirectory));

        string assemblyInformationalVersion = version;
        
        if (versionInformation != null)
        {
            assemblyInformationalVersion += " / " + versionInformation;
        }

        if (appendGitCommit)
        {
            assemblyInformationalVersion += " / " + Git.GetLastCommit(searchDirectory);
        }
        
        var assemblyVersion = GetMajorVersion(version);

        var files = Directory.EnumerateFiles(searchDirectory, "AssemblyInfo.cs", SearchOption.AllDirectories);
        var append = new []
        {
            string.Format("[assembly: AssemblyVersion(\"{0}\")]", assemblyVersion),
            string.Format("[assembly: AssemblyFileVersion(\"{0}\")]", version),
            string.Format("[assembly: AssemblyInformationalVersion(\"{0}\")]", assemblyInformationalVersion),
            string.Format("[assembly: AssemblyCopyright(\"Copyright © {0} DIPS ASA\")]", DateTime.Now.ToString("yyyy")),
            "[assembly: AssemblyCompany(\"DIPS ASA\")]"
        };

        var replaceAttributes = new [] { "AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion", "AssemblyCopyright", "AssemblyCompany", "AssemblyTrademark" };

        foreach(var file in files)
        {
            Write.Info("Updating " + file);
            
            var lines = File.ReadAllLines(file)
              .Where(line => !replaceAttributes.Any(attr => line.Contains(attr)))
              .Concat(append);

            File.WriteAllLines(file, lines);
        }
    }
    
    // Can possibly be removed. 
    // AFX & Infrastructure will not use this anymore
    [Obsolete("Use 'Update(string searchDirectory, string version)' instead")]
    public static void UpdateAll(string searchDirectory, string version, string buildNumber)
    {
        Write.Info(string.Format("Writing version {0} to all AssemblyInfo files", version));

        var files = Directory.EnumerateFiles(searchDirectory, "AssemblyInfo.cs", SearchOption.AllDirectories);
        var append = new []
        {
            string.Format("[assembly: AssemblyVersion(\"{0}\")]", version),
            string.Format("[assembly: AssemblyFileVersion(\"{0}\")]", version),
            string.Format("[assembly: AssemblyInformationalVersion(\"{0}.{1}\")]", version, buildNumber),
            string.Format("[assembly: AssemblyCopyright(\"Copyright © 1990-{0} DIPS ASA\")]", DateTime.Now.ToString("yyyy")),
            "[assembly: AssemblyCompany(\"DIPS ASA\")]"
        };

        var replaceAttributes = new [] { "AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion", "AssemblyCopyright", "AssemblyCompany" };

        foreach(var file in files)
        {
            var lines = File.ReadAllLines(file)
              .Where(line => !replaceAttributes.Any(attr => line.Contains(attr)))
              .Concat(append);

            File.WriteAllLines(file, lines);
        }
    }
}

public static partial class Git
{
    public static string GetLastCommit(string directory)
    {
        var result = Command.Execute("git", "log --pretty=format:%h -n 1", true, true);
    
        if (string.IsNullOrEmpty(result))
        {
            throw new Exception("Unable to get latest GIT commit");
        }
        
        return result.Trim();
    }
}

public static partial class Display
{
    public static void DipsLogo(string projectName = "")
    {
        Write.Error( "   -/+ossyyso+/-         ---------.        ----. ---------.         .:://::-   ");
        Write.Error( "    .-/+syyyyyyyy+.     .yyyyyyyyyyys+-   -yyyy/ syyyyyyyyyyy+.  -oyyyyyyyyyys ");
        Write.Error( ".-::::::::/syyyyyyy:    .yyyys///+syyyyo  -yyyy/ syyyy///oyyyyy.:yyyyo:---:+o. ");
        Write.Error( "     .:::::-/yyyyyyy:   .yyyyo     :yyyys -yyyy/ syyyy    /yyyy/oyyyy/         ");
        Write.Error( "        -::::-syyyyyy   .yyyyo      +yyyy:-yyyy/ syyyy   .syyyy--yyyyyyyso+:.  ");
        Write.Error( "         .::::-yyyyyy   .yyyyo      /yyyy/-yyyy/ syyyyyyyyyyys:  .+syyyyyyyyys.");
        Write.Error( "           :::-+yyyys   .yyyyo      syyyy--yyyy/ syyyysssso+-        .-:+syyyys");
        Write.Error( "            :::+yyyy.   .yyyyo    :syyyy/ -yyyy/ syyyy           /-      -yyyyo");
        Write.Error( "            ::-oyys-    .yyyyyyyyyyyyyo-  -yyyy/ syyyy          oyyysooosyyyyo.");
        Write.Error( "            ::-yy+       ssssssssoo/:     -ssss/ ossss          :/osyyyyyso/.  ");
        Write.Error( "            :-o/                                                               ");
        Write.Error( "           -:                                                                  ");
        Write.Error( "          ..                                                                   ");
        Write.Info("\n");
        if(projectName != "")
        {
            Write.Info("Buildwindow for the " + projectName + " project.");
            Write.Info("\n");
        }
    }

    public static void AvailableCommands(string[] values, string heading = "Available commands:")
    {
        Write.Info(heading);
        Write.Info("===================");
        foreach (var value in values) {
            Write.Info(value);
        }
        Write.Info("===================");
    }

    public static void Motivation()
    {
        Write.Caption(Encoding.UTF8.GetString(Convert.FromBase64String(@"
            ICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgLDg4ODhiICANCiAgICAgICAgICAgICAgICAgICAgICAgICAg
            ICBfX19fX19fXyAgICBQIF9fIDhiIA0KICAgICAgICAgICAgICAgICAgICAgIF9fZDg4ODg4ODg4ODg4ODg4OCA4Jyc4IGIgDQog
            ICAgICAgICAgICAgICAgICAgIGQ4OFAnJycnLiAgICAgICcnJycnJzg4OCA5IFAgDQogICAgICAgICAgICAgICAgIHc4OFAnICAg
            ICAgICAnLl8gICAgICAgICAnODhiIA0KICAgICAgICAgICAgICAgZDhQICAgICAgICAgICAuLShfKS0udy4gICAgICAgJzhiLiAg
            DQogICAgICAgICAgICAgZDhQICAgICAgICAgIHcgLyAgICAsICBcJzggICAgICAgICc4OCAgLDg4LiAgIA0KICAgICAgICAgICBf
            ZDhQICAgICAgICAgLjhQKCAgICAgfCAsICAgICAgICAgICAgICc4IDggIDg4ODhiIA0KICAgICAgIF9kODg4ICAgICAgICAgICAg
            JycgICAvLV9fXy0nICAgICAgICAgICAgICA4OCAgOCAgICA4YiANCiAgICAgIGQ4UCAgICAgICAgICAgICAgICAgICAgICAgICAg
            ICAgICAgICAgICAgICAgIDg4OCAgICAgIDhiIA0KICAgICAgOFAgZDggICAgICAgICAgICAgICAgICAgICAgIC4gICAgICAgICAg
            ICAgICAgODggICAgICAgIDg6IA0KICAgICAgOGIgJzggICAgICAgICAgICAgICAgICAgICAgICA6ICAgICAgICAgICAgICAgODgg
            ICAgICAgIDg6IA0KICAgICAgIDhidyAgICAgICAgICAgICAgICAgICAgICAgICA6ICAgICAgICAgICAgICA6OFA4ICAgICAgOFAg
            DQogICAgICAgICAnOGIgICAgICAgICAgICAgICAgICAgICAgIDogICAgICAgICAgICAgLDhQICA4ODg4ODhQIA0KICAgICAgICAg
            ICc4YiAgICAgICAgICAgICAgICAgICAgICA7ICAgICAgICAgICAsODhQICAgICAgZHwgDQogICAgICAgICAgIDg4YiAgICAgICAg
            ICAgICAgICAgICAgICAgICAgICAgICAsODhQICAgICAgIGRQIA0KICAgICAgICAgIGRQJzg4YiAgICAgICAgICAgICAgICAgICAg
            ICAgICAgLDg4OFAgODhiICAgIGRQIA0KICAgICAgICAgZFAgICAnODhiX19fX19fX19fX19fX19fX19fX193d3c4OFAgICAgICA4
            OGJkUCcgDQogICAgICAgIGRQICAgICAgICAnODg4ODg4ODg4ODg4ODg4ODg4ODhQJycgICAgICAgICAgIDg4YiANCiAgICAgIGQ4
            OG4gICAgICAgIGQ4ICAgICAgICAgICAgICAgICAgICAuICAgICAgICAgICAgICc4YiANCiAgICAgZFAgICAgICAgICAgZDggICAg
            ICAgICAgICAgICAgICAgICAgLiAgICAgICAgICAgICA4YiANCiAgICAgODggICAgICAgICA4Yi4gICAgICBZT1UgQVJFICAgICAg
            ICAgICA6ICAgICAgICAgICAgIDhiIA0KICAgICA4OCAgICAgICAgICAgODggICAgICBBV0VTT01FISEgICAgICAgIDogICAgICAg
            ICAgICAnODsgDQogICAgICA4YiAgICAgICA4YiA4OCAgICAgICAgICAgICAgICAgICAgICAgOiAgICAgICAgICAgICA4OCANCiAg
            ICAgICA4YiAgICAgICA4OFAgICAgICAgICAgICAgICAgICAgICAgICA6ICAgICAgICAgICAgIDg4IA0KICAgICAgICAgJzg4ODg4
            OFAnICAgICAgICAgICAgICAgICAgICAgICAgICAuICAgICAgICAgICAgIGQ4IA0KICAgICAgICAgICAgOGIgICAgICAgICAgICAg
            ICAgICAgICAgICAgICAgOiAgICAgICAgICAgICBkOCcgDQogICAgICAgICAgICAsOGIgICAgICAgICAgICAgICAgICAgICAgICAg
            ICAgICAgICAgICAgIF9kOFAgIA0KICAgICAgICAgICAgOFA4ODhiICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIF9fZDg4
            UCAgIA0KICAgICAgICAgICAgODggJzg4ODhiICAgICAgICAgICAgICAgICAgICAgICAgX19kODhQICc4YiANCiAgICAgICAgICAg
            IDg4ICAgICAgICc4ODg4ODhfX19fX19fX19fX19fX3dkODg4UCAnICAgICAgOGINCiAgICAgICAgICAgOFAgICAgICAgICAgICAn
            Jzg4ODg4ODg4ODg4ODhQJycgICAgICAgICAgICA4OA0KICAgICAgICAgIDhQICAgICAgICAgICAgICAgICAgICAgICAgICAgICAg
            ICAgICAgICAgICAgICA4OA0KICAgICAgICAgOFAgICAgICAgICAgICAgICAgICAgICB3XyAgICAgICAgICAgICAgICAgICAgICA4
            OSANCiAgICAgICAgODggICAgICAgICAgICAgICAgICAgICAgICdiICB3ODggICAgICAgICAgICAgICAgODkgDQogICAgICAgIDhi
            ICAgICAgICAgICAgICAgICAgICAgICAgIDhiICcgICAgICAgICAgICAgICAgICA4UCANCiAgICAgICAgICc4YiAgICAgICAgICAg
            ICAgICAgICAgICAgICA4YiAgICAgICAgICAgICAgICAgZFAgIA0KICAgICAgICAgICc4OGIgICAgICAgICAgICAgICAgICAgICAg
            IDhQICAgICAgICAgICAgICAgLmRQIA0KICAgICAgICAgICAgJzg4ODhiX19fX19fX19fX19fX19fX2Q4ODh3XyAgICAgICAgICAg
            Li5kUCcgIA0KICAgICAgICAgICAgICAnJycnODg4ODg4ODg4ODg4ODg4UCAgICAnODg4ODg4ODg4ODg4UCAgICAg")));
    }
}

// Utility classes
public static partial class FileUtil
{
	public static void CreateCleanDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            DeleteDirectory(path, true, false);
        }
        
        Directory.CreateDirectory(path);
    }

    public static void DeleteDirectory(string path, bool recursive = false, bool ignoreError = true)
    {
        try
        {
			if (!Directory.Exists(path))
			{
				Write.Warning(String.Format("The folder does not exist: {0}", path));
				return;
			}

            if (recursive)
            {
                var subfolders = Directory.GetDirectories(path);
                foreach (var s in subfolders)
                {
                    DeleteDirectory(s, recursive);
                }
            }

            var files = Directory.GetFiles(path);
            foreach (var f in files)
            {
                var attr = File.GetAttributes(f);

                if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(f, attr ^ FileAttributes.ReadOnly);
                }

                File.Delete(f);
            }

            Directory.Delete(path);
        }
        catch (Exception e)
        {
            if (ignoreError)
            {
                Write.Warning(String.Format("An error occured deleting folder {0}", path));
            } else
            {
                throw e;
            }
        }
    }
}

public static partial class Command
{
	public static string WorkingDirectory { get; set; }

    public static void RemoteExecute(string computerName, string arguments)
    {
        RemoteExecute(computerName, new []{ arguments });
    }

    public static void RemoteExecute(string computerName, string[] arguments, ConnectionOptions connectionOptions = null, string connectionString = null)
    {
        if (connectionOptions == null)
        {
            connectionOptions = new ConnectionOptions();
        }

        if (connectionString == null)
        {
            connectionString = string.Format(@"\\{0}\ROOT\CIMV2", computerName);
        }

        var objectGetOptions = new ObjectGetOptions();
        var scope = new ManagementScope(connectionString, connectionOptions);
        var path = new ManagementPath("Win32_Process");
        var mc = new ManagementClass(scope, path, objectGetOptions);

        uint returnCode = (uint)mc.InvokeMethod("Create", arguments);

        if (returnCode == 0)
        {
            Write.Caption(string.Format("Remote call to {0} with arguments {1} completed with success.", computerName, arguments[0]));
        }
        else
        {
            throw new Exception(string.Format("Remote call to {0} with arguments {1} completed unsuccessful with return code {2}.", computerName, arguments[0], returnCode));
        }
    }

    public static string Execute(string commandPath, string arguments, bool quiet = false, bool returnOutput = false)
    {
        Write.Debug("Executing {0} {1}", commandPath, arguments);
        var startInformation = CreateProcessStartInfo(commandPath, arguments);
        var process = CreateProcess(startInformation);
        SetVerbosityLevel(process, quiet);
        var output = RunAndWait(process, returnOutput);
        CheckIfSuccessfull(process.ExitCode, commandPath, arguments);
        return output;
    }
    
    public static string Execute(string commandPath, string arguments, Action<string> lineHandler, Action<string> errorHandler)
    {
        Write.Debug("Executing {0} {1}", commandPath, arguments);
        var startInformation = CreateProcessStartInfo(commandPath, arguments);
        var process = CreateProcess(startInformation);
        process.OutputDataReceived += (s, e) => lineHandler(e.Data);
        process.ErrorDataReceived += (s, e) => errorHandler(e.Data);
        var output = RunAndWait(process, false);
        CheckIfSuccessfull(process.ExitCode, commandPath, arguments);
        return output;
    }

    private static ProcessStartInfo CreateProcessStartInfo(string commandPath, string arguments)
    {
        var startInformation = new ProcessStartInfo(StringUtils.Quote(commandPath));
        startInformation.CreateNoWindow = true;
        startInformation.Arguments =  arguments;
        startInformation.UseShellExecute = false;
        startInformation.RedirectStandardOutput = true;
        startInformation.RedirectStandardError = true;

		if (!string.IsNullOrEmpty(WorkingDirectory))
		{
			startInformation.WorkingDirectory = WorkingDirectory;
			WorkingDirectory = null;
		}
		
        return startInformation;
    }

    private static Process CreateProcess(ProcessStartInfo startInformation)
    {
        var process = new Process();
        process.StartInfo = startInformation;
        return process;
    }

    private static void SetVerbosityLevel(Process process, bool quiet)
    {
        if(!quiet)
        {
            process.OutputDataReceived += (s, e) => Write.Info("{0}", e.Data);
            process.ErrorDataReceived += (s, e) => Write.Error("{0}", e.Data);
        }
    }

    private static string RunAndWait(Process process, bool returnOutput)
    {
        process.Start();
        ProcessKiller.KillWhenConsoleTerminates(process);
        
        string output = null;
        if (returnOutput)
        {
            output = process.StandardOutput.ReadToEnd();
        } else
        {
            process.BeginOutputReadLine();
        }

        process.BeginErrorReadLine();
        process.WaitForExit();
        return output;
    }

    private static void CheckIfSuccessfull(int exitCode, string commandPath, string arguments)
    {
        if (exitCode != 0 && commandPath != "robocopy")
        {
            throw new Exception(
                String.Format(
                    "Command returned exit code: {0}. Command path: {1} Arguments {2}", 
                    exitCode, 
                    commandPath, 
                    arguments));
        }
    }
}

public enum Threshold
{
    Debug, Info, Warning, Error
}

public static partial class Write
{
    private const ConsoleColor CaptionColor = ConsoleColor.Cyan;
    private const ConsoleColor ErrorColor   = ConsoleColor.Red;
    private const ConsoleColor WarningColor = ConsoleColor.Yellow;
    private const ConsoleColor DebugColor   = ConsoleColor.DarkGray;

    public static Threshold Threshold { get; set; }

    public static void Caption(string format, params object[] args) {
        Caption(format, true, args);
    }

    public static void Caption(string format, bool newline, params object[] args) {
        WriteMessage(Threshold.Error, CaptionColor, format, newline, args);
    }

    public static void Error(string format, params object[] args) {
        Error(format, true, args);
    }

    public static void Error(string format, bool newline, params object[] args) {
        WriteMessage(Threshold.Error, ErrorColor, format, newline, args);
    }

    public static void Warning(string format, params object[] args) {
        Warning(format, true, args);
    }

    public static void Warning(string format, bool newline, params object[] args) {
        WriteMessage(Threshold.Warning, WarningColor, format, newline, args);
    }

    public static void Info(string format, params object[] args) {
        Info(format, true, args);
    }

    public static void Info(string format, bool newline, params object[] args) {
        WriteMessage(Threshold.Info, null, format, newline, args);
    }

    public static void Debug(string format, params object[] args) {
        Debug(format, true, args);
    }

    public static void Debug(string format, bool newline, params object[] args) {
        WriteMessage(Threshold.Debug, DebugColor, format, newline, args);
    }

    private static void WriteMessage(Threshold threshold, ConsoleColor? color, string format, bool newline, object[] args)
    {
        if (Threshold <= threshold)
        {
            try {
                if (color != null) { Console.ForegroundColor = color.Value; }
                WriteConsole(format, newline, args);
            }
            finally {
                if (color != null) { Console.ResetColor(); }
            }
        }
    }
    
    private static void WriteConsole(string format, bool newline, object[] args)
    {
        if (args.Length == 0) {
            Console.Write(format);
        }
        else {
            Console.Write(format, args);
        }
        if (newline) {
            Console.WriteLine();
        }
    }
}

public static partial class PathUtils
{
    public static string TfPath()
    {
        return Path.Combine(VsPath(), "tf.exe");
    }

    public static string VsTestPath()
    {
        return Path.Combine(VsPath(), "CommonExtensions", "Microsoft", "TestWindow", "vstest.console.exe");
    }

    public static string VsTestExtensionPath()
    {
        return Path.Combine(VsPath(), "CommonExtensions", "Microsoft", "TestWindow", "Extensions");
    }

    public static string CodeCoveragePath()
    {
        var path = Path.Combine(VisualStudioPath(), "Team Tools", "Dynamic Code Coverage Tools", "amd64", "CodeCoverage.exe");
        if (!File.Exists(path))
        {
            throw new Exception("CodeCoverage.exe not found on this computer");
        }

        return path;
    }

    public static string MsBuildPath()
    {
        string keyName = @"SOFTWARE\Wow6432Node\Microsoft\MSBuild\ToolsVersions";

        RegistryKey key = Registry.LocalMachine.OpenSubKey(keyName);
        var msbuildVersion = key.GetSubKeyNames()
            .Select(DecimalUtil.Parse)
            .Where(n => n.CompareTo(12.0m) >= 0)
            .OrderByDescending(n => n)
            .FirstOrDefault();

        if (msbuildVersion == null)
        {
            throw new Exception("MSBuild not found on this machine");
        }

        RegistryKey latestVersionSubKey = key.OpenSubKey(DecimalUtil.ToString(msbuildVersion));
        string pathToMsBuildTools = (string)latestVersionSubKey.GetValue("MSBuildToolsPath");
        return Path.Combine(pathToMsBuildTools, "MsBuild.exe");
    }

    public static string VsPath()
    {
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        var versions = new [] {"15.0", "14.0", "13.0", "12.0", "11.0"};
        foreach(var version in versions)
        {
            var path = Path.Combine(programFiles, "Microsoft Visual Studio " + version, "Common7", "IDE");
            if (Directory.Exists(path))
            {
                return path;
            }
        }
        throw new Exception("Visual Studio not found on this machine");
    }

    private static string SelectedVisualStudioPath;
    private static string SelectedTfsPath;

    public static string VisualStudioPath()
    {
        if (SelectedVisualStudioPath != null)
        {
            return SelectedVisualStudioPath;
        }

        const string directoryName = "Microsoft Visual Studio";
        var programFiles = Environment.GetEnvironmentVariable("PROGRAMFILES(X86)");

        var visualStudioVersion = Directory.EnumerateDirectories(programFiles)
            .Select(Path.GetFileName)
            .Where(n => n.StartsWith(directoryName))
            .Select(n => n.Substring(directoryName.Length))
            .Select(DecimalUtil.Parse)
            .OrderByDescending(n => n)
            .Select(DecimalUtil.ToString)
            .FirstOrDefault();

        if (visualStudioVersion == null)
        {
            throw new Exception("Visual Studio not found on this machine");
        }

        SelectedVisualStudioPath = Path.Combine(programFiles, directoryName + " " + visualStudioVersion);

        if (!Directory.Exists(SelectedVisualStudioPath))
        {
            throw new Exception("Visual Studio not found at " + SelectedVisualStudioPath);
        }

        return SelectedVisualStudioPath;
    }

    public static string TfsMsBuildLoggerPath()
    {
        var result = Path.Combine(TfsPath(), "Tools", "Microsoft.TeamFoundation.Build.Server.Logger.dll");

        if (!File.Exists(result))
        {
            throw new Exception("Msbuild TFS logger does not exist at " + result);
        }

        return result;
    }

    public static string TfsPath()
    {
        const string directoryName = "Microsoft Team Foundation Server";
        var programFiles = Environment.GetEnvironmentVariable("PROGRAMFILES");

        var tfs = Directory.EnumerateDirectories(programFiles)
            .Select(Path.GetFileName)
            .Where(n => n.StartsWith(directoryName))
            .Select(n => n.Substring(directoryName.Length))
            .Select(DecimalUtil.Parse)
            .OrderByDescending(n => n)
            .Select(DecimalUtil.ToString)
            .FirstOrDefault();

            if (tfs == null)
            {
                throw new Exception("Team Foundation Server not found on this machine");
            }

            SelectedTfsPath = Path.Combine(programFiles, directoryName + " " + tfs);

            if (!Directory.Exists(SelectedTfsPath))
            {
                throw new Exception("Team Foundation Server not found on this machine");
            }

            return SelectedTfsPath;
    }
}

public static partial class StringUtils
{
    public static string Quote(string value)
    {
        return "\"" + value + "\"";
    }
}

public static partial class Buildsystem
{
    public static void Upgrade(string version = "")
    {
        if(version != "") {
            version = "-Version " + version;
        }
        Write.Warning("Downloading buildsystem to temporary directory.");
        Nuget.Install("dips-buildsystem -Source http://dips-nuget/nuget/DIPS -o temp " + version);
        var tempdir = @"temp\";
        var source = tempdir + new DirectoryInfo("temp")
                      .GetDirectories()
                      .First()
                      .ToString();
        Write.Warning("Copying buildsystem from temporary directory to build folder.");
        RoboCopy.CopyFile(source, BuildEnv.BuildDir, "*.csx *.bat", arguments: "/E", quiet: true);
        Write.Warning("Deleting temporary directory.");
        FileUtil.DeleteDirectory(tempdir, true);
        Write.Warning("Successfully upgraded buildsystem.");
    }
}

public static partial class XmlSerializerGenerator
{
    public static void GenerateSerializers(string[] assemblyNames, string arguments = "")
    {
        foreach (var assemblyName in assemblyNames)
        {
            GenerateSerializers(assemblyName, arguments);
        }
    }

    public static void GenerateSerializers(string assemblyName, string arguments = "")
    {
        Command.Execute("sgen", string.Format("/f {0} {1}", StringUtils.Quote(assemblyName), arguments));
    }
}

public static partial class DecimalUtil
{
    public static string ToString(decimal input)
    {
        return input.ToString(new NumberFormatInfo { NumberDecimalSeparator = "." });
    }

    public static decimal Parse(string input)
    {
        decimal versionNumber;
        if (decimal.TryParse(input, NumberStyles.Float, new NumberFormatInfo { NumberDecimalSeparator = "."}, out versionNumber))
        {
            return versionNumber;
        }

        return 0;
    }
}

public static partial class TaskRunner
{
    static TaskRunner()
    {
        CreateProperties();
    }
    
    private static Action HelpAction;

    private static List<Action> ActionsToDoBeforeShutdown = new List<Action>();

    private static List<string> TasksFinished = new List<string>();

    private static List<RunTask> Tasks = new List<RunTask>();
    
    private static List<Property> Properties;
    
    private class RunTask
    {
        public bool FailAfterAllActionsHasRun = false;
        
        private List<Action> _actions = new List<Action>();
        public List<Action> Actions { get { return _actions; } }

        public string TaskName { get; set;}
        public string[] TaskDependencies { get; set; }

        public bool finished { get; set;}
        public bool RunOnce { get; set;}

        public string[] Aliases { get; set; }
        public string Description { get; set; }

        public virtual bool SkipWhenProperty(Property property)
        {
            return property.Name == "skip" + TaskName;
        }

        public override string ToString()
        {
            return TaskName;
        }

        public virtual IEnumerable<string> FinishedTaskNames()
        {
            yield return TaskName;
        }

        public RunTask(string taskName, Action action, string[] dependencies = null)
        {
            TaskName = taskName.ToLower();
            Actions.Add(action);
            TaskDependencies = dependencies ?? new string[]{};
        }

        public string GenerateHelpText()
        {
            var taskDesc = TaskName;
            if (Aliases.Length > 0)
            {
                taskDesc += " (";
                taskDesc += string.Join(",", Aliases);
                taskDesc += ")";
            }

            taskDesc = taskDesc.PadRight(20);
            taskDesc += " - " + Description;
            return taskDesc;
        }
        
        public virtual void Run()
        {
            var builder = new StringBuilder();

            foreach (var action in Actions)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception exception)
                {
                    if (FailAfterAllActionsHasRun)
                    {
                        builder
                            .Append("Exception occured while running task ")
                            .AppendLine(TaskName)
                            .AppendLine(exception.ToString());
                        continue;
                    }
                    
                    throw;
                }
            }
            
            if (builder.Length > 0)
            {
                throw new Exception(builder.ToString());
            }
        }
    }

    private static void CreateProperties()
    {
        Properties = new List<Property>
        {
            new Property(
                "showenvvars",
                (arg) =>
                {
                    Console.Out.WriteLine("All environment variables");
                    foreach(DictionaryEntry entry in Environment.GetEnvironmentVariables())
                    {
                        Console.Out.WriteLine("{0} => {1}", entry.Key, entry.Value);
                    }
                },
                "Show environment variables")
        };
    }

    public static void RegisterProperty(string name, Action<string> action, string desc)
    {
        Properties.Add(new Property(name, action, desc));
    }

    public static void RegisterProperty(string name, string parameterName, Action<string> action, string desc)
    {
        Properties.Add(new Property(name, parameterName, action, desc));
    }

    private class Property
    {
        public string Name { get; set; }
        public string ParameterName { get; set; }
        public Action<string> Action { get; set; }
        public string Description { get; set; }

        public bool ShowOnHelp { get; set; }

        public Property(string name, Action<string> action, string desc)
        {
            Name = name;
            Action = action;
            Description = desc;
            
            ShowOnHelp = true;
        }

        public Property(string name, string parameterName, Action<string> action, string desc) : this(name, action, desc)
        {
            ParameterName = parameterName;
        }

        public string GenerateHelpText()
        {
            var propDesc = "/" + Name;
            if (ParameterName != null)
            {
                propDesc += ":[" + ParameterName + "]";
            }

            propDesc = propDesc.PadRight(20);
            propDesc += " - " + Description;
            return propDesc;
        }
    }

    private static Property FindProperty(string argument)
    {
        var property = Properties.FirstOrDefault(
            p => p.Name.Equals(argument) || argument.StartsWith(p.Name + ":"));

        if (property == null)
        {
            if (argument.StartsWith("skip"))
            {
                property = new Property(argument, str => {}, null);
                Properties.Add(property);
            }
            else 
            {
                throw new Exception("Unknown property with name " + argument);
            }
        }

        return property;
    }

    public static void RegisterTask(
        string taskName,
        Action action,
        string[] dependencies,
        string desc = null,
        string[] aliases = null,
        bool runOnce = false)
    {
        var task = new RunTask(taskName, action, dependencies);
        task.Description = desc ?? "description missing";
        task.Aliases = aliases ?? new string[]{};
        task.RunOnce = runOnce;
        Tasks.Add(task);
    }
    
    // Run two tasks but fail after second task if any task failed. Nice for running unit and integration tests
    // without failing before the last test type has been run.
    public static void CombineTasks(string newTaskName, params string[] taskNames)
    {
        var tasks = taskNames.Select(taskName => FindTask(taskName)).ToList();
        foreach (var task in tasks)
        {
            Tasks.Remove(task);
        }
        Tasks.Add(new CombinedTask(newTaskName, tasks));
    }

    public static void AppendAction(string taskName, Action action, bool runAllActionsOnError = false)
    {
        var task = FindTask(taskName);
        
        if (!task.FailAfterAllActionsHasRun)
        {
            task.FailAfterAllActionsHasRun = runAllActionsOnError;
        }
        
        task.Actions.Add(action);
    }

    public static void PrependAction(string taskName, Action action)
    {
        var task = FindTask(taskName);
        task.Actions.Insert(0, action);
    }

    public static void ResetTask(string taskName)
    {
        var taskToReset = FindTask(taskName);
        if (TasksFinished.Any(t => t == taskToReset.TaskName))
        {
            TasksFinished.Remove(taskToReset.TaskName);
            foreach (var depTask in Tasks
                .Where(t => t.TaskDependencies != null && t.TaskDependencies.Contains(taskToReset.TaskName)))
            {
                ResetTask(depTask.TaskName);
            }
        }
    }
    
    public static void ResetTasks()
    {
        TasksFinished = new List<string>();
    }

    private static RunTask FindTask(string argument)
    {
        var task = Tasks.FirstOrDefault(n => n.TaskName == argument || n.Aliases.Contains(argument));

        if (task == null)
        {
            throw new Exception("Unknown task with name " + argument);
        }

        return task;
    }

    public static void DoBeforeShutdown(Action action)
    {
        ActionsToDoBeforeShutdown.Add(action);
    }

    public static void SetHelp(Action action)
    {
        HelpAction = action;
    }

    private static void ShowHelp()
    {
        HelpAction();
    }

    public static void Run(string[] arguments, bool overrideRunOnce = false)
    {
        if (arguments.Length == 0)
        {
            ShowHelp();
            return;
        }

        foreach (var argument in arguments.Where(arg => arg.StartsWith("/")).Select(arg => arg.Substring(1)).Select(n => n.ToLower()))
        {
            var property = FindProperty(argument);
            var parameter = property.ParameterName != null ? argument.Substring(property.Name.Length + 1) : null;
            property.Action(parameter);
        }

        foreach (var argument in arguments.Where(arg => !arg.StartsWith("/")).Select(x => x.ToLower()))
        {
            RunStep(argument, arguments, overrideRunOnce);
        }

        Shutdown();
    }

    private static void Shutdown(int exitCode = 0)
    {
        try
        {
            foreach(var act in ActionsToDoBeforeShutdown)
            {
                act();
            }
        }
        catch(Exception exception)
        {
            throw new Exception("Error occured while running before shutdown procedures");
        }

        if (exitCode != 0)
        {
            throw new Exception("Build failed");
        }
    }

    private static void RunStep(string argument, string[] arguments, bool overrideRunOnce = false)
    {
        var task = FindTask(argument);

        if (task.RunOnce && !overrideRunOnce && TasksFinished.Contains(task.TaskName))
        {
            if (!BuildServer.IsBuildServer)
            {
                Write.Info("Task " + task.TaskName + " already run");
            }

            return;
        }

        var property = Properties.FirstOrDefault(task.SkipWhenProperty);
        if (property != null)
        {
            Write.Debug("Ignoring task " + task.TaskName + " because of /" + property.Name);
        }
        else 
        {
            foreach(var dependency in task.TaskDependencies)
            {
                Write.Debug(string.Format("Running dependent task {0} of task {1}", dependency, task.TaskName));
                RunStep(dependency, arguments, overrideRunOnce);
            }

            RunBuildStep(task);
        }

        TasksFinished.AddRange(task.FinishedTaskNames());
    }

    private static void RunBuildStep(RunTask task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();

        BuildServer.StartTask(task.TaskName);
        Write.Info(string.Format("[{0}] start", task.TaskName));
        try
        {
            task.Run();

            timer.Stop();
            Write.Info(string.Format("[{0}] finished after {1}s", task,  timer.Elapsed.TotalSeconds.ToString("F3")));
        }
        catch(Exception exception)
        {
            Write.Error(string.Format("[{0}] failed with exception \r\n{1}", task, exception));
            Shutdown(-1);
        }

        BuildServer.EndTask(task.TaskName);
    }

    public static string[] BuildTaskDescriptions()
    {
        var taskDescs = new List<string>();
        foreach (var task in Tasks)
        {
            taskDescs.Add(task.GenerateHelpText());
        }

        return taskDescs.ToArray();
    }

    public static string[] BuildPropertyDescriptions()
    {
        var propDescs = new List<string>();
        foreach (var prop in Properties.Where(n => n.ShowOnHelp))
        {
            propDescs.Add(prop.GenerateHelpText());
        }

        return propDescs.ToArray();
    }
    
    public static void Clear()
    {
        HelpAction = null;
        ActionsToDoBeforeShutdown.Clear();
        TasksFinished.Clear();
        Tasks.Clear();
        CreateProperties();
    }

    private class CombinedTask : RunTask
    {
        private IEnumerable<RunTask> tasks;

        public CombinedTask(string taskName, IEnumerable<RunTask> tasks)
            : base(taskName, null, tasks.SelectMany(task => task.TaskDependencies).ToArray())
        {
            this.tasks = tasks;
            Actions[0] = RunCombinedTasks;
            Aliases = tasks.Select(task => task.TaskName).Concat(tasks.SelectMany(task => task.Aliases)).ToArray();
        }

        private void RunCombinedTasks()
        {
            Write.Info("Running combined task");

            var builder = new StringBuilder();
            foreach (var task in tasks)
            {
                RunTask(task, false, builder);
            }
            
            if (builder.Length > 0)
            {
                throw new Exception(builder.ToString());
            }
        }

        private void RunTask(RunTask task, bool skipTask, StringBuilder builder)
        {
            if (skipTask)
            {
                Write.Info("Skipping task {0} because of /skip{0}", task.TaskName);
                return;
            }
            
            try
            {
                task.Run();
            }
            catch (Exception exception)
            {
                builder.Append("Task ").Append(task.TaskName).AppendLine(" failed with exception").AppendLine(exception.ToString());
            }
        }
    }
}

public static partial class Ruby
{
        public static bool IsInstalled()
        {
                return ExistsOnPath("Ruby") || ExeLocation() != null;
        }

        public static string ExeLocation()
        {
                string[] knownRubyLocations = new[]
                {
                        @"C:\Ruby22-x64\bin\ruby.exe",
                        @"C:\tools\ruby22\bin\ruby.exe"
                };

                foreach (var knownRubyLocation in knownRubyLocations)
                {
                        if(File.Exists(knownRubyLocation)){
                                Write.Info("Ruby located at " + knownRubyLocation);
                                return knownRubyLocation;
                        }
                }
                return null;
        }

        private static bool ExistsOnPath(string item)
        {
                var path = Environment.GetEnvironmentVariable("PATH");
                return path.Contains(item);
        }
}

public static partial class Asciidoc
{
        private static string templateDirectoryPath = @"\\p-fs01\dips\KIDS\Maler\Asciidoc\";
        private static string fontAwesomeDirectoryPath = templateDirectoryPath + @"font-awesome-4.4.0\";
        private static string stylesDirectoryPath = @"\\p-fs01\dips\KIDS\Maler\Asciidoc\styles";
        private static string fontsDirectoryPath = @"\\p-fs01\dips\KIDS\Maler\Asciidoc\fonts";

        /// <summary> 
        ///  Build is a method that initiates building of xhtml and pdf based on asciidoc files. The method includes verification of valid installed prerequisites, copies templates and styling, copies images directories and initiates building of the output documents. 
        /// </summary>
        /// <param name="fileName">Holds the name and location of the *.adoc file the output format should be generated from.</param>
        /// <param name="outputDir">Defines the output directory for the generated html and pdf files. Is also used as a target directory for the copies of css files and templates. </param>
        /// <param name="docDir">If set, defines the directory of the *.adoc file in "fileName". Used to find all basefiles and to search for any images directories to copy. </param>
        public static void Build(string fileName, string outputDir, string docDir=null)
        {
                VerifyRubyInstalled();
                CopyAsciiDocTemplate(outputDir);
                if (docDir != null)
                {
                    CopyImagesAndBaseFiles(docDir, outputDir);
                }
                GenerateXhtmlFromAdoc(fileName, outputDir);
                GeneratePdfFromAdoc(fileName, outputDir);
        }

        /// <summary> 
        ///  CopyImagesAndBaseFiles is a method that will copy any *.adoc files and, if present, the images directory to to outputDir. 
        /// </summary>
        /// <param name="fromdir">Defines the directory of the *.adoc file the Build method is processing to find all *.adoc files and search for any images directories to copy. </param>
        /// <param name="outputDir">Defines the targetdir of any *.adoc files and images directories copied. </param>
        public static void CopyImagesAndBaseFiles(string fromdir, string outputDir)
        {
                Write.Info("Copying imagesdir and base files to output directory");
                RoboCopy.CopyDirectory(fromdir + "images", outputDir + @"\images");
                RoboCopy.CopyFile(fromdir, outputDir, "*.adoc");
                
                
        }

        public static void VerifyRubyInstalled()
        {
                if(!Ruby.IsInstalled())
                {
                        throw new Exception(
                            "Ruby is not installed.\n"
                             + "Install Ruby(x64) from http://rubyinstaller.org/downloads/\n"
                             + "Afterwards, open cmd and install asciidoctor:\n"
                             + " > gem install asciidoctor\n"
                             + " > gem install --pre asciidoctor-pdf\n"
                             + "See http://utvikling/t/asciidoc-asciidoctor/446 for more information and discussion.\n"
                        );
                }
        }

        /// <summary> 
        ///  CopyAsciiDocTemplate copies relevant css files and fontfiles such as dips.css and font-awesome css and fonts to the outputDir. The method also makes all css and font files in the outputDir writeable.  
        /// </summary>
        /// <param name="outputDir">Defines the base targetdir of any *.css files and fontfiles. </param>
        public static void CopyAsciiDocTemplate(string outputDir)
        {
                Write.Info("Copying asciidoc template to output directory");
                RoboCopy.CopyDirectory(fontAwesomeDirectoryPath + "css", outputDir + @"\css");
                RoboCopy.CopyDirectory(fontAwesomeDirectoryPath + "fonts", outputDir + @"\fonts");
                RoboCopy.CopyFile(templateDirectoryPath + "styles", outputDir + @"\css", "dips.css");
                RoboCopy.CopyFile(templateDirectoryPath + "styles", outputDir + @"\css", "coderay-asciidoctor.css");
                
                //Make all css and font files writeable
                var cssDir = outputDir + @"\css\";
                var cssfiles = Directory.EnumerateFiles(cssDir,"*.css", SearchOption.TopDirectoryOnly);
                foreach (var file in cssfiles)
                {
                    MakeFileWriteable(file);
                }
                var fontDir = outputDir + @"\fonts\";
                var fontfiles = Directory.EnumerateFiles(fontDir,"*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in fontfiles)
                {
                    MakeFileWriteable(file);
                }

        }

        public static void GenerateXhtmlFromAdoc(string fileName, string outputDir)
        {
                Write.Info("Generating XHtml from asciidoc: " + fileName);
                string arguments = "--trace -D "
                        + outputDir
                        + @" -a stylesheet=""" 
                        + @"dips.css"""
                        + @" -a linkcss -b xhtml5 "
                        + fileName;

                Command.Execute("cmd", "/C asciidoctor " + arguments);
        }

        public static void GeneratePdfFromAdoc(string fileName, string outputDir)
        {
                Write.Info("Generating Pdf from asciidoc: " + fileName);

                string arguments = "--trace -D "
                        + outputDir
                        + @" -a pdf-stylesdir="""
                        + stylesDirectoryPath
                        + @""""
                        + @" -a pdf-style=""dips-pdf-styling"""
                        + @" -a pdf-fontsdir="""
                        + fontsDirectoryPath
                        + @""" "
                        + fileName;

                Command.Execute("cmd", "/C asciidoctor-pdf " + arguments);
        }

        private static void MakeFileWriteable(string filePath)
        {
                Write.Info("Removing read only flag from " + filePath);
                var fullPath = Directory.GetCurrentDirectory() + @"\" + filePath;
                File.SetAttributes(
                        fullPath,
                        File.GetAttributes(fullPath) & ~FileAttributes.ReadOnly
                );
        }
}
private static class ProcessKiller
{
    [System.Runtime.InteropServices.DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

    private delegate bool EventHandler(CtrlType sig);
    
    private enum CtrlType {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }
                         
    public static void KillWhenConsoleTerminates(Process process)
    {
        SetConsoleCtrlHandler(ctrlType => {process.Kill(); return false;}, true );
    }     
}   

private class BuildResult
{
    public int Warnings { get; set;}
    
    public int Errors {get;set;}
    
    public TimeSpan Elapsed {get;set;}
}