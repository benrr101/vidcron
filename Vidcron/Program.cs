using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Vidcron.DataModel;
using Vidcron.Errors;
using Vidcron.Sources;

namespace Vidcron
{
    class Program
    {
        private static readonly Dictionary<string, Func<SourceConfig, GlobalConfig, ISource>> SourceFactories = new Dictionary<string, Func<SourceConfig, GlobalConfig, ISource>>
        {
            {nameof(YoutubeDl).ToLower(), (sc, gc) => new YoutubeDl(sc, gc)},
        };

        private static Logger GlobalLogger;

        public static async Task<int> Main(string[] args)
        {
            // Process command line args
            if (args.Length != 1)
            {
                await Console.Error.WriteLineAsync("Invalid number of arguments provided");
                PrintUsage();
                return -1;
            }
            if (args[0] == "-h")
            {
                PrintUsage();
                return 0;
            }

            // Make sure that the database is fully migrated
            try
            {
                await using (DownloadsDbContext db = new DownloadsDbContext())
                {
                    await db.Database.MigrateAsync();
                }
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync("Failed to migrate downloads database, database may be corrupt.");
                await Console.Error.WriteLineAsync(e.ToString());
                return -3;
            }

            try
            {
                // Load the config file from disk
                string configPath = args[0];
                if (!File.Exists(configPath))
                {
                    await Console.Error.WriteLineAsync("Path to config does not exist");
                    return -2;
                }

                string configString = await File.ReadAllTextAsync(configPath);
                GlobalConfig globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(configString);

                GlobalLogger = new Logger("GLOBAL", globalConfig.LogLevel);
                await ProcessConfig(globalConfig);

                return 0;
            }
            catch (Exception e)
            {
                // Handle unhandled exceptions to make it a bit pretty
                await GlobalLogger.Error($"Unhandled exception: {e}");
                return int.MinValue;
            }
        }

        private static async Task<List<DownloadJob>> GetAllJobs(GlobalConfig globalConfig)
        {
            List<DownloadJob> allJobs = new List<DownloadJob>();
            await GlobalLogger.Info("Discovering download jobs...");
            foreach (SourceConfig sourceConfig in globalConfig.Sources)
            {
                try
                {
                    // Make sure we know how to process this
                    Func<SourceConfig, GlobalConfig, ISource> sourceFactory;
                    if (sourceConfig.Type == null || !SourceFactories.TryGetValue(sourceConfig.Type.ToLowerInvariant(), out sourceFactory))
                    {
                        throw new InvalidConfigurationException(
                            $"Cannot process source config of type: {sourceConfig.Type}");
                    }

                    // Get the download jobs for the source
                    ISource source = sourceFactory(sourceConfig, globalConfig);
                    allJobs.AddRange(await source.GetAllDownloads());
                }
                catch (ProcessFailureException pfe)
                {
                    // TODO: If fail on error is set, fail HARD
                    await GlobalLogger.Error($"Error getting downloads from source {sourceConfig.Name} skipping... ");
                    await GlobalLogger.Error(pfe.Message);
                    await GlobalLogger.Error(string.Join(Environment.NewLine, pfe.StandardError));
                }
                catch (Exception e)
                {
                    // TODO: If fail on error is set, fail HARD
                    await GlobalLogger.Error($"Error getting downloads from source {sourceConfig.Name} skipping... ");
                    await GlobalLogger.Error(e.Message);
                }
            }

            return allJobs;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("    vidcron -h                    Prints usage information and exits");
            Console.WriteLine("    vidcron /path/to/config.json  Executes vidcron with provided config");
        }

        private static async Task ProcessConfig(GlobalConfig globalConfig)
        {
            // Step 1: Create the sources and get the list of downloads to perform
            List<DownloadJob> allJobs = await GetAllJobs(globalConfig);

            ConcurrentBag<DownloadResult> results = new ConcurrentBag<DownloadResult>();

            // Step 2: Run the download actions
            await GlobalLogger.Info("Running download jobs...");
            var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
            Parallel.ForEach(allJobs, options, job =>
            {
                using (DownloadsDbContext dbContext = new DownloadsDbContext())
                {
                    DownloadResult result;
                    try
                    {
                        // Get job result from db
                        DownloadRecord oldRecord = dbContext.DownloadRecords.SingleOrDefault(r => r.Id == job.UniqueId);
                        if (oldRecord != default(DownloadRecord))
                        {
                            // Case 1: Job finished
                            if (oldRecord.EndTime != null)
                            {
                                //job.Logger.Info($"Download job has already been completed: {job.DisplayName}");
                                return;
                            }

                            // Case 2: Job hasn't finished
                            job.Logger.Info($"Verifying job: {job.DisplayName}");
                            result = job.VerifyJob().Result;
                            job.Logger.Info($"Job verification result: {result.Status}");

                            if (result.Status == DownloadStatus.Completed)
                            {
                                oldRecord.EndTime = DateTime.Now;
                            }
                        }
                        else
                        {
                            // Case 3: Job never ran - run the job
                            job.Logger.Info($"Downloading: {job.DisplayName}");
                            result = job.RunJob().Result;
                            job.Logger.Info($"Job {job.DisplayName} completed with status: {result.Status}");
                        }
                    }
                    catch (Exception e)
                    {
                        // Record a failure in the logs, but don't create a record of it in the db.
                        // This allows us to retry the download next time.
                        result = new DownloadResult
                        {
                            Error = e,
                            Status = DownloadStatus.Failed
                        };
                    }

                    if (result.Status == DownloadStatus.Failed)
                    {
                        job.Logger.Error($"Job {job.DisplayName} failed: {result.Error}");
                    }
                    else
                    {
                        dbContext.DownloadRecords.Add(new DownloadRecord
                        {
                            Id = job.UniqueId,
                            StartTime = result.StartTime,
                            EndTime = result.EndTime
                        });

                        job.Logger.Info("Storing job record to database");
                        dbContext.SaveChanges();
                    }

                    results.Add(result);
                }
            });

            // TODO: Step 3: Send email with details
        }
    }
}