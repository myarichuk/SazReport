using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fiddler;
using System.Reflection;
using System.IO;
using ServiceStack.Text;

namespace SazReport
{

    public class RequestResponseInfo
    {
        public string Url { get; set; }

        public int RequestLength { get; set; }

        public int ResponseLength { get; set; }

        public string[] RequestHeaders { get; set; } = new string[0];

        public string[] ResponseHeaders { get; set; } = new string[0];

        public string Method { get; set; }

        public string RequestBody { get; set; }

        public string ResponseBody { get; set; }

        public TimeSpan RequestLatency { get; set; }

        public int ResponseCode { get; set; }
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

        public Dictionary<int, int> ResponseCodeStats { get; set; } = new Dictionary<int, int>();

        public List<RequestResponseInfo> LargestRequests { get; set; } = new List<RequestResponseInfo>();

        public List<RequestResponseInfo> LargestResponses { get; set; } = new List<RequestResponseInfo>();

        public List<RequestResponseInfo> LongestRequests { get; set; } = new List<RequestResponseInfo>();

        public Dictionary<string, int> UrlStats { get; set; } = new Dictionary<string, int>();
    }

    class Program
    {
        private const int TopItemCount = 5;

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
                Console.WriteLine("Before all else, please verify that the project was compiled with SAZ_SUPPORT conditional symbol.");
                Console.WriteLine("This is a prerequisite for a fiddler library to support handling SAZ session archives");
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

            //make sure they are readable
            foreach(var session in sessions)
            {
                session.utilDecodeResponse();
                session.utilDecodeRequest();
            }

            var report = new SazReport();

            var requestResponseInformation = (from session in sessions
                                             let timing = session.Timers.ClientDoneResponse -
                                                          session.Timers.ClientConnected
                                             select new RequestResponseInfo
                                             {
                                                 RequestHeaders = session.RequestHeaders.Select(x => $"{x.Name} = {x.Value}").ToArray(),
                                                 ResponseHeaders = session.ResponseHeaders.Select(x => $"{x.Name} = {x.Value}").ToArray(),
                                                 Method = session.RequestMethod,
                                                 RequestBody = session.GetRequestBodyAsString(),
                                                 ResponseBody = session.GetResponseBodyAsString(),
                                                 Url = session.fullUrl,
                                                 RequestLatency = timing,
                                                 RequestLength = session.RequestBody.Length,
                                                 ResponseLength = session.ResponseBody.Length,
                                                 ResponseCode = session.responseCode
                                             }).ToList();
            
            report.RequestLatencyStats.Max = requestResponseInformation.Max(x => x.RequestLatency);
            report.RequestLatencyStats.Min = requestResponseInformation.Min(x => x.RequestLatency);
            report.RequestLatencyStats.Average = TimeSpan.FromMilliseconds(requestResponseInformation.Average(x => x.RequestLatency.TotalMilliseconds));



            report.LongestRequests = requestResponseInformation.OrderByDescending(x => x.RequestLatency)
                                                               .Take(TopItemCount).ToList();

            report.RequestSizeStats.Max = requestResponseInformation.Max(x => x.RequestLength);
            report.RequestSizeStats.Min = requestResponseInformation.Min(x => x.RequestLength);
            report.RequestSizeStats.Average = requestResponseInformation.Average(x => x.RequestLength);

            report.LargestRequests = requestResponseInformation.OrderByDescending(x => x.RequestLength)
                                                               .Take(TopItemCount).ToList();

            report.LargestResponses = requestResponseInformation.OrderByDescending(x => x.ResponseLength)
                                                                .Take(TopItemCount).ToList();

            report.ResponseCodeStats = (from requestResponse in requestResponseInformation
                                        group requestResponse by requestResponse.ResponseCode into g
                                        select new
                                        {
                                            Code = g.Key,
                                            Count = g.Count()
                                        }).ToDictionary(x => x.Code, x => x.Count);

            report.UrlStats = (from requestResponse in requestResponseInformation
                               let uri = new Uri(requestResponse.Url).GetLeftPart(UriPartial.Path)
                               group requestResponse by uri into g
                               select new
                               {
                                   Url = g.Key,
                                   Count = g.Count()
                               }).ToDictionary(x => x.Url, x => x.Count);

            Console.WriteLine("done");

            var sazFileInfo = new FileInfo(args[0]);

            Console.Write("Writing request information to csv...");
            var csvPath = $"{sazFileInfo.Directory.FullName}{Path.DirectorySeparatorChar}{sazFileInfo.Name}.csv";
            File.WriteAllText(csvPath,CsvSerializer.SerializeToCsv(requestResponseInformation.ToArray()));
            Console.WriteLine($"done, {csvPath}");

            Console.Write("Writing SAZ report...");
            var jsonPath = $"{sazFileInfo.Directory.FullName}{Path.DirectorySeparatorChar}{sazFileInfo.Name}.json";

            File.WriteAllText(jsonPath, report.Dump());
            Console.WriteLine($"done, {jsonPath}");
        }
    }
}
