using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

        public static Task<IReadOnlyList<string>> GetCommandOutput(string application, string[] arguments)
        {
            TaskCompletionSource<IReadOnlyList<string>> tsc = new TaskCompletionSource<IReadOnlyList<string>>();
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