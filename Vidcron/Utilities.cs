using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

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

        public static string[] GetCommandOutput(string application, string[] arguments)
        {
            // Launch the process
            ProcessStartInfo processStart = new ProcessStartInfo
            {
                Arguments = arguments == null ? "" : string.Join(" ", arguments),
                FileName = application,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            Console.WriteLine($"Launching process `{processStart.FileName} {processStart.Arguments}`");
            Process process = Process.Start(processStart);
            if (process == null)
            {
                throw new ApplicationException($"Could not start {processStart.FileName}");
            }

            // Read the standard output and error, wait for the process to finish
            string stdOutput = process.StandardOutput.ReadToEnd();
            string stdError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Console.WriteLine($"Process completed with exit code {process.ExitCode}");

            // If the process failed, we throw
            if (process.ExitCode != 0)
            {
                throw new ProcessFailureException(
                    $"Process {processStart.FileName} failed with exit code {process.ExitCode}",
                    stdOutput,
                    stdError
                );
            }

            // Process didn't fail, so return the output, split by line
            return stdOutput.Split("\n").Select(l => l.TrimEnd()).ToArray();
        }
    }
}