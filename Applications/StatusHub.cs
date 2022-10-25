
namespace UACloudTwin
{
    using Microsoft.AspNetCore.SignalR;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    public class StatusHub : Hub
    {
        // this is our SignalR Status Hub
    }

    public class StatusHubClient
    {
        public Dictionary<string, Tuple<string, string>> TableEntries { get; set; } = new Dictionary<string, Tuple<string, string>>();

        public Dictionary<string, string[]> ChartEntries { get; set; } = new Dictionary<string, string[]>();

        private readonly IHubContext<StatusHub> _hubContext;

        public StatusHubClient(IHubContext<StatusHub> hubContext)
        {
            _hubContext = hubContext;

            _ = Task.Run(() => SendMessageViaSignalR());
        }

        private async Task SendMessageViaSignalR()
        {
            while (true)
            {
                await Task.Delay(3000).ConfigureAwait(false);

                lock (TableEntries)
                {
                    foreach (string displayName in TableEntries.Keys)
                    {
                        _hubContext.Clients.All.SendAsync("addDatasetToChart", displayName).GetAwaiter().GetResult();
                    }

                    foreach (KeyValuePair<string, string[]> entry in ChartEntries)
                    {
                        _hubContext.Clients.All.SendAsync("addDataToChart", entry.Key, entry.Value).GetAwaiter().GetResult();
                    }
                    ChartEntries.Clear();

                    CreateTableForTelemetry();
                }
            }
        }

        private void CreateTableForTelemetry()
        {
            // create HTML table
            StringBuilder sb = new StringBuilder();
            sb.Append("<table width='1000px' cellpadding='3' cellspacing='3'>");

            // header
            sb.Append("<tr>");
            sb.Append("<th><b>Name</b></th>");
            sb.Append("<th><b>Latest Value</b></th>");
            sb.Append("<th><b>Time Stamp</b></th>");
            sb.Append("</tr>");

            // rows
            foreach (KeyValuePair<string, Tuple<string, string>> item in TableEntries)
            {
                sb.Append("<tr>");
                sb.Append("<td style='width:400px'>" + item.Key + "</td>");
                sb.Append("<td style='width:400px'>" + item.Value.Item1 + "</td>");
                sb.Append("<td style='width:200px'>" + item.Value.Item2 + "</td>");
                sb.Append("</tr>");
            }

            sb.Append("</table>");

            _hubContext.Clients.All.SendAsync("addTable", sb.ToString()).GetAwaiter().GetResult();
        }
    }
}
