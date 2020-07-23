using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Vidcron.Errors;
using Vidcron.Sources;

namespace Vidcron
{
    class Program
    {
        private static readonly Dictionary<string, Func<SourceConfig, ISource>> SourceFactories = new Dictionary<string, Func<SourceConfig, ISource>>
        {
            {nameof(YoutubeDl).ToLower(), sc => new YoutubeDl(sc)},
        };

        static int Main(string[] args)
        {
            // Process command line args
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Invalid number of arguments provided");
                PrintUsage();
                return -1;
            }
            if (args[0] == "-h")
            {
                PrintUsage();
                return 0;
            }

            try
            {
                // Load the config file from disk
                string configPath = args[0];
                if (!File.Exists(configPath))
                {
                    Console.Error.WriteLine("Path to config does not exist");
                    return -2;
                }

                string configString = File.ReadAllText(configPath);
                GlobalConfig globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(configString);
                ProcessConfig(globalConfig);

                return 0;
            }
            catch (Exception e)
            {
                // Handle unhandled exceptions to make it a bit pretty
                Console.Error.WriteLine("Unhandled exception:");
                Console.Error.WriteLine(e);
                return Int32.MinValue;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("    vidcron -h                    Prints usage information and exits");
            Console.WriteLine("    vidcron /path/to/config.json  Executes vidcron with provided config");
        }

        static void ProcessConfig(GlobalConfig globalConfig)
        {
            // Step 1: Create the sources and get the list of downloads to perform
            List<DownloadJob> allJobs = new List<DownloadJob>();
            Console.WriteLine("Discovering download jobs...");
            foreach (SourceConfig sourceConfig in globalConfig.Sources)
            {
                try
                {
                    // Make sure we know how to process this
                    Func<SourceConfig, ISource> sourceFactory;
                    if (!SourceFactories.TryGetValue(sourceConfig.Type.ToLowerInvariant(), out sourceFactory))
                    {
                        throw new InvalidConfigurationException($"Cannot process source config of type: {sourceConfig.Type}");
                    }

                    // Get the download jobs for the source
                    ISource source = sourceFactory(sourceConfig);
                    allJobs.AddRange(source.GetAllDownloads().Result);
                }
                catch (Exception e)
                {
                    // TODO: If fail on error is set, fail HARD
                    Console.Error.WriteLine($"Error getting downloads from source {sourceConfig.Name} skipping...");
                    Console.Error.WriteLine(e.Message);
                }
            }

            // TODO: Step 2: Run the download actions
            Console.WriteLine("Running download jobs...");
            List<DownloadResult> results = new List<DownloadResult>();
            foreach (DownloadJob job in allJobs)
            {
                DownloadResult result;
                try
                {
                    // Get job result from db

                    // IF has end date:
                    // Log and continue

                    // IF has start date:
                    // Verify and set result

                    // Case 3: Run the job
                    // TODO: Use job's logger
                    Console.WriteLine($"Downloading: {job.DisplayName} => {job.UniqueId}");
                    result = job.RunJob().Result;
                }
                catch (Exception e)
                {
                    // Record a failure in the logs
                    result = new DownloadResult
                    {
                        Error = e,
                        Status = DownloadStatus.Failed,
                        DisplayName = job.DisplayName,
                        UniqueId = job.UniqueId
                    };
                }

                results.Add(result);

                // TODO: Store the result in the DB
            }

            // TODO: Step 3: Send email with details
        }
    }
}