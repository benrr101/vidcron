using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace Vidcron
{
    public class Utilities
    {
        public static bool IsApplicationInPath(string application)
        {
            // DEBUG
            Console.WriteLine($"Checking for '{application}' in PATH");

            // Determine which "which" to use to find the application
            ProcessStartInfo whichProcessStartInfo = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
                Arguments = application,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            // Run which to figure out if the application exists
            Console.WriteLine($"Launching process: `{whichProcessStartInfo.FileName} {whichProcessStartInfo.Arguments}`");
            Process whichProcess = Process.Start(whichProcessStartInfo);
            if (whichProcess == null)
            {
                throw new ApplicationException($"Could not start {whichProcessStartInfo.FileName}");
            }

            whichProcess.WaitForExit();
            Console.WriteLine($"Process to find {application} returned exit code {whichProcess.ExitCode}");
            return whichProcess.ExitCode == 0;
        }

        // public static string[] GetCommandOutput(string application, string[] arguments)
        // {
        //     // Launch the process
        //     ProcessStartInfo processStart = new ProcessStartInfo
        //     {
        //         Arguments = arguments == null ? "" : string.Join(" ", arguments),
        //         FileName = application,
        //         RedirectStandardError = true,
        //         RedirectStandardOutput = true,
        //         UseShellExecute = false
        //     };
        //
        //     Console.WriteLine($"Launching process `{processStart.FileName} {processStart.Arguments}`");
        //     Process process = Process.Start(processStart);
        //     if (process == null)
        //     {
        //         throw new ApplicationException($"Could not start {processStart.FileName}");
        //     }
        //
        //     // Read the standard output and error, wait for the process to finish
        //     string stdOutput = process.StandardOutput.ReadToEnd();
        //     string stdError = process.StandardError.ReadToEnd();
        //     process.WaitForExit();
        //     Console.WriteLine($"Process completed with exit code {process.ExitCode}");
        //
        //     // If the process failed, we throw
        //     if (process.ExitCode != 0)
        //     {
        //         throw new ProcessFailureException(
        //             $"Process {processStart.FileName} failed with exit code {process.ExitCode}",
        //             stdOutput,
        //             stdError
        //         );
        //     }
        //
        //     // Process didn't fail, so return the output, split by line
        //     return stdOutput.Split("\n").Select(l => l.TrimEnd()).ToArray();
        // }

        public static Task<IReadOnlyCollection<string>> GetCommandOutput(string application, string[] arguments)
        {
            TaskCompletionSource<IReadOnlyCollection<string>> tsc = new TaskCompletionSource<IReadOnlyCollection<string>>();
            List<string> standardOutput = new List<string>();
            List<string> standardError = new List<string>();

            // Setup the process and event handlers
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    Arguments = arguments == null ? "" : string.Join(" ", arguments),
                    FileName = application,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    standardOutput.Add(e.Data);
                }
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    standardError.Add(e.Data);
                }
            };
            process.Exited += (sender, e) =>
            {
                if (process.ExitCode == 0)
                {
                    tsc.SetResult(standardOutput);
                }
                else
                {
                    ProcessFailureException exception = new ProcessFailureException(
                        $"Process {process.StartInfo.FileName} failed with exit code {process.ExitCode}",
                        process.ExitCode,
                        standardOutput,
                        standardError
                    );
                    tsc.SetException(exception);
                }
            };

            // Launch the process
            // TODO: Use provided logger
            Console.WriteLine($"Launching process `{process.StartInfo.FileName} {process.StartInfo.Arguments}`");
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            return tsc.Task;
        }
    }
}