﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Graphite.System
{
    internal class AppPoolListener
    {
        private readonly string appPoolName;
        private readonly string category = "Process";
        private readonly string counter = "Working Set";

        private string counterName;

        private CounterListener counterListener;
        private bool workingSet;

        public AppPoolListener(string appPoolName, string category, string counter)
        {
            this.appPoolName = appPoolName;
            this.category = category;
            this.counter = counter;

            this.LoadCounterName();
        }

        public AppPoolListener(string appPoolName)
        {
            this.workingSet = true;
            this.appPoolName = appPoolName;

            this.LoadCounterName();
        }

        public bool LoadCounterName()
        {
            string newName = this.GetCounterName(this.appPoolName);

            if (!string.IsNullOrEmpty(newName) && this.counterName != newName)
            {
                if (this.counterListener != null)
                {
                    this.counterListener.Dispose();

                    this.counterListener = null;
                }
				if (!string.IsNullOrEmpty(newName))
				{
					this.counterName = newName;
					return true;
				}
            }
	        return false;
        }

        public float? ReportValue()
        {
            // AppPool not found -> is not started -> 0 memory in use.
            if (string.IsNullOrEmpty(this.counterName) && !LoadCounterName())
                return 0;

            if (this.counterListener == null)
            {
                try
                {
                    this.counterListener = workingSet 
                        ? new CounterListener("Process", this.counterName, "Working Set") 
                        : new CounterListener(category, this.counterName, counter);
                }
                catch (InvalidOperationException)
                { 
                }
            }

            if (this.counterListener == null)
                return 0;

            try
            {


                return this.counterListener.ReportValue(); ;
            }
            catch (InvalidOperationException)
            {
                // counter not available.
                this.counterListener = null;

                return 0;
            }
        }

        private string GetCounterName(string appPool)
        {
            string result;

            this.Execute("list WP", out result, 1000);

            var match = Regex.Match(
                result, 
                "WP \"(?<id>[0-9]+)\" \\(applicationPool:" + Regex.Escape(appPool) + "\\)", 
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            int processId;

            if (match.Success && match.Groups["id"].Success && int.TryParse(match.Groups["id"].Value, out processId))
            {
                return this.ProcessNameById("w3wp", processId);
            }

            return null;
        }

        private string ProcessNameById(string prefix, int processId)
        {
            var category = new PerformanceCounterCategory("Process");

            string[] instances = category.GetInstanceNames()
                .Where(p => p.StartsWith(prefix))
                .ToArray();

            foreach (string instance in instances)
            {
                using (PerformanceCounter counter = new PerformanceCounter("Process", "ID Process", instance, true))
                {
                    long val = counter.RawValue;

                    if (val == processId)
                    {
                        return instance;
                    }
                }
            }

            return null;
        }

        private bool Execute(string arguments, out string result, int maxMilliseconds = 30000)
        {
            string systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(systemPath, "inetsrv\\appcmd.exe"),
                Arguments = arguments,

                RedirectStandardOutput = true,

                UseShellExecute = false,
                CreateNoWindow = true,
            };

	        try
	        {
		        StringBuilder standardOut = new StringBuilder();

		        Process p = Process.Start(startInfo);

		        p.OutputDataReceived += (object s, DataReceivedEventArgs d) => standardOut.AppendLine(d.Data);
		        p.BeginOutputReadLine();

		        bool success = p.WaitForExit(maxMilliseconds);
		        p.CancelOutputRead();

		        if (!success)
		        {
			        try
			        {
				        p.Kill();
			        }
			        catch (Win32Exception)
			        {
				        // unable to kill the process
			        }
			        catch (InvalidOperationException)
			        {
				        // process already stopped
			        }
		        }

		        result = standardOut.ToString();
				return success;
	        }
	        catch (Exception e)
	        {
		        result = string.Empty;
		        return false;
	        }
        }
    }
}
