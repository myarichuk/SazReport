using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fiddler;
using System.Reflection;
using System.IO;

namespace SazReport
{
    public class RequestData
    {
        public string RequestRaw { get; set; }

        public string ResponseRaw { get; set; }
    }

    public class Stats<T> where T :struct
    {
        public T Min { get; set; }

        public T Max { get; set; }

        public T Average { get; set; }
    }

    public class SazReport
    {
        public Stats<TimeSpan> RequestLatencyStats { get; private set; } = new Stats<TimeSpan>();

        public Stats<double> RequestSizeStats { get; private set; } = new Stats<double>();

        public Dictionary<int, int> ResponseCodeStats { get; private set; } = new Dictionary<int, int>();

        public List<RequestData> LargestRequests { get; private set; } = new List<RequestData>();

        public List<RequestData> LongestRequests { get; private set; } = new List<RequestData>();

        public Dictionary<string, int> UrlStats { get; private set; } = new Dictionary<string, int>();
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Fiddler SAZ Reporting Tool");
            if (args.Length != 1)
            {
                Console.WriteLine("Usage : SazReport.exe [full path to Fiddler SAZ session file]");
                return;
            }

            if(!File.Exists(args[0]))
            {
                Console.WriteLine("Couldn't find file at {0}, and didn't know what else I can do...", args[0]);
                return;
            }

            if (!FiddlerApplication.oTranscoders
                                   .ImportTranscoders(Assembly.GetExecutingAssembly().Location))
            {
                Console.WriteLine("This assembly was not compiled with a SAZ-exporter, cannot continue.");
                Console.WriteLine("It is definitely not supposed to happen, something is very wrong");
                return;
            }

            var dictOptions = new Dictionary<string, object>();
            dictOptions.Add("Filename", args[0]);

            Console.Write("Loading SAZ file...");
            var sessions = FiddlerApplication.DoImport("SAZ", false, dictOptions, null);

            if(sessions == null)
            {
                Console.WriteLine("failed. FiddlerApplication.DoImport() returned null...");
                return;
            }

            Console.WriteLine("done");

            Console.Write("Analyzing...");
            var report = new SazReport();

            report.RequestLatencyStats.Max = sessions.Max(x => x.Timers.ClientDoneResponse - x.Timers.ClientConnected);
            report.RequestLatencyStats.Min = sessions.Min(x => x.Timers.ClientDoneResponse - x.Timers.ClientConnected);
            report.RequestLatencyStats.Average = TimeSpan.FromMilliseconds(sessions.Average(x => (x.Timers.ClientDoneResponse - x.Timers.ClientConnected).TotalMilliseconds));

            Console.WriteLine("done");
        }
    }
}
