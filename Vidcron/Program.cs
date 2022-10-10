using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Vidcron.Config;
using Vidcron.DataModel;
using Vidcron.Errors;
using Vidcron.Sources;

namespace Vidcron
{
    class Program
    {
        private delegate ISource SourceFactory(SourceConfig sourceConfig, GlobalConfig globalConfig);
        
        private static readonly Dictionary<string, SourceFactory> SourceFactories = new Dictionary<string, SourceFactory>
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
            // @TODO: Make parallelized
            foreach (SourceConfig sourceConfig in globalConfig.Sources)
            {
                try
                {
                    // Make sure we know how to process this
                    if (sourceConfig.Type == null || !SourceFactories.TryGetValue(sourceConfig.Type.ToLowerInvariant(), out var sourceFactory))
                    {
                        throw new InvalidConfigurationException(
                            $"Cannot process source config of type: {sourceConfig.Type}");
                    }

                    // Get the download jobs for the source
                    ISource source = sourceFactory(sourceConfig, globalConfig);
                    allJobs.AddRange(await source.GetAllDownloads());
                }
                catch (Exception e)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Error getting downloads from source '{sourceConfig.Name}' skipping:");
                    if (e is ProcessFailureException pfe)
                    {
                        sb.AppendLine(pfe.Message);
                        sb.AppendJoin(Environment.NewLine, pfe.StandardError);
                    }
                    else
                    {
                        sb.AppendLine(e.Message);
                    }

                    await GlobalLogger.Error(sb.ToString());
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
            await GlobalLogger.Info("Getting download jobs...");
            List<DownloadJob> allJobs = await GetAllJobs(globalConfig);

            // Step 2: Run the download actions
            await GlobalLogger.Info("Running download jobs...");
            await RunJobs(allJobs, globalConfig);

            // Step 3: Send email with details
            await GlobalLogger.Info("Emailing job results...");
            await SendEmailResults(allJobs, globalConfig);
        }

        private static Task RunJobs(
            IEnumerable<DownloadJob> jobs,
            GlobalConfig config)
        {
            var mutex = new SemaphoreSlim(config.MaxConcurrentJobs);
            var tasks = jobs.Select(async job =>
            {
                await mutex.WaitAsync();
                try
                {
                    await RunJob(job);
                }
                finally
                {
                    mutex.Release();
                }
            });

            return Task.WhenAll(tasks);
        }

        private static async Task RunJob(DownloadJob job)
        {
            await using DownloadsDbContext dbContext = new DownloadsDbContext();

            // Run the job
            try
            {
                // Try to get an old job result from db
                DownloadRecord oldRecord = dbContext.DownloadRecords.SingleOrDefault(r => r.Id == job.UniqueId);
                if (oldRecord != default(DownloadRecord))
                {
                    // Case 1: Job finished
                    if (oldRecord.EndTime != null)
                    {
                        await job.Logger.Debug("Download job has already been completed, skipping.");
                        job.Result = new DownloadResult { Status = DownloadStatus.CompletedNotRun };
                        return;
                    }
                    
                    // @TODO: None of this code is tested
                    // // Case 2: Job hasn't finished
                    // await job.Logger.Info($"Verifying job: {job.DisplayName}");
                    // job.Result = await job.VerifyJob();
                    // await job.Logger.Info($"Job verification result: {job.Result.Status}");
                    //
                    // if (job.Result.Status == DownloadStatus.Completed)
                    // {
                    //     oldRecord.EndTime = DateTime.Now;
                    // }
                }
                else
                {
                    // Case 3: Job never ran - run the job
                    await job.Logger.Info($"Downloading: {job.DisplayName}");
                    job.Result = await job.RunJob();
                }
            }
            catch (Exception e)
            {
                // Record a failure in the logs, but don't create a record of it in the db.
                // This allows us to retry the download next time.
                job.Result = new DownloadResult
                {
                    Error = e,
                    Status = DownloadStatus.Failed
                };
            }
            
            // Handle failures
            // NOTE: Although it looks like we catch errors in the above catch, RunJob could always
            //    return a failure, so we check for one here.
            if (job.Result.Status == DownloadStatus.Failed)
            {
                await job.Logger.Error($"Failed with error: {job.Result.Error}");
                return;
            }
            
            // Write the job results to the log
            await job.Logger.Info($"Returned status: {job.Result.Status}");
                
            dbContext.DownloadRecords.Add(new DownloadRecord
            {
                Id = job.UniqueId,
                StartTime = job.Result.StartTime,
                EndTime = job.Result.EndTime
            });

            await job.Logger.Debug("Storing job record to database");
            await dbContext.SaveChangesAsync();
        }
        
        private static async Task SendEmailResults(IEnumerable<DownloadJob> jobs, GlobalConfig globalConfig)
        {
            // Step 1) Build a message
            // - Filter results to ones that actually ran, group by source 
            var groupedResults = jobs.GroupBy(j => j.SourceName);
            
            // TODO: Make it pretty for me
            var sb = new StringBuilder();
            sb.AppendLine("Vidcron Run Results");
            sb.AppendLine("--------------------------------------------------------");
            sb.AppendLine($"Run Completed: {DateTime.Now.ToShortDateString()} {DateTime.Now.ToShortTimeString()}");
            sb.AppendLine();

            // Emit global errors
            var globalErrors = GlobalLogger.LogMessages.Where(m => m.Level >= LogLevel.Error).ToArray();
            if (globalErrors.Any())
            {
                sb.AppendLine("Global error messages:");
                foreach (var message in globalErrors)
                {
                    var indentedMessage = message.ToString()
                        .Replace(Environment.NewLine, Environment.NewLine + "    ");
                    sb.AppendLine($"  {indentedMessage}");
                }

                sb.AppendLine();
            }
            
            // Emit job results
            foreach (var grouping in groupedResults)
            {
                sb.AppendLine($"Source: {grouping.Key}");
                sb.AppendLine("-----------------------");

                var filteredJobs = grouping.Where(j => j.Result != null && j.Result.Status != DownloadStatus.CompletedNotRun);
                var anyJobs = false;
                foreach (var job in filteredJobs)
                {
                    anyJobs = true;
                    
                    if (job.Result.Status == DownloadStatus.CompletedNotRun)
                    {
                        continue;
                    }
                    
                    sb.AppendLine($"{job.DisplayName}");
                    sb.AppendLine($"  -> {job.Result.Status}");
                    if (job.Result.Status == DownloadStatus.Failed)
                    {
                        var indentedError = job.Result.Error.ToString()
                            .Replace(Environment.NewLine, Environment.NewLine + "       ");
                        sb.AppendLine($"     {indentedError}");
                    }

                    sb.AppendLine();
                }

                if (!anyJobs)
                {
                    sb.AppendLine("  No new jobs");
                }

                sb.AppendLine();
            }

            // Step 2) Send the email
            try
            {
                var mail = new MailMessage
                {
                    Subject = "Vidcron Run Results",
                    From = new MailAddress(globalConfig.Email.FromAddress, "Vidcron"),
                    To = { new MailAddress(globalConfig.Email.ToAddress) },
                    Body = sb.ToString(),
                    IsBodyHtml = false
                };

                var smtpClient = new SmtpClient
                {
                    // @TODO: Add validation here
                    Host = globalConfig.Email.SmtpServer,
                    Port = globalConfig.Email.SmtpPort,
                    EnableSsl = globalConfig.Email.SmtpIsSsl,
                    UseDefaultCredentials = true,
                    Credentials = new NetworkCredential
                    {
                        UserName = globalConfig.Email.SmtpUsername,
                        Password = globalConfig.Email.SmtpPassword
                    }
                };

                using (smtpClient)
                {
                    await smtpClient.SendMailAsync(mail);
                }
            }
            catch (Exception e)
            {
                await GlobalLogger.Error($"Failed to send email results: {e}");
            }
        }
    }
}