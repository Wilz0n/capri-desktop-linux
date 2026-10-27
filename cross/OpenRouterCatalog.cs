using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class OpenRouterCatalogResponse
    {
        [DataMember(Name = "data")] internal OpenRouterCatalogItem[] Data;
    }

    [DataContract]
    internal sealed class OpenRouterCatalogItem
    {
        [DataMember(Name = "id")] internal string Id;
        [DataMember(Name = "context_length")] internal int ContextLength;
        [DataMember(Name = "expiration_date")] internal string ExpirationDate;
        [DataMember(Name = "pricing")] internal OpenRouterPricing Pricing;
        [DataMember(Name = "supported_parameters")] internal string[] SupportedParameters;
        [DataMember(Name = "architecture")] internal OpenRouterArchitecture Architecture;
    }

    [DataContract]
    internal sealed class OpenRouterPricing
    {
        [DataMember(Name = "prompt")] internal string Prompt;
        [DataMember(Name = "completion")] internal string Completion;
        [DataMember(Name = "request")] internal string Request;
        [DataMember(Name = "image")] internal string Image;

        internal bool IsZero()
        {
            string[] values = { Prompt, Completion, Request, Image };
            bool found = false;
            foreach (string price in values)
            {
                if (price == null) continue;
                found = true;
                decimal value;
                if (!decimal.TryParse(price, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value != 0m) return false;
            }
            return found && Prompt != null && Completion != null;
        }
    }

    [DataContract]
    internal sealed class OpenRouterArchitecture
    {
        [DataMember(Name = "output_modalities")] internal string[] OutputModalities;
    }

    internal static class OpenRouterCatalog
    {
        internal const string Endpoint = "https://openrouter.ai/api/v1/models";
        internal const int CacheMinutes = 60;
        internal static Func<string> DownloadOverride;

        internal static string[] Load(string preferred = null, string cachePath = null)
        {
            if (cachePath == null) cachePath = CachePath;
            try
            {
                string json = DownloadOverride != null ? DownloadOverride() : Download();
                string[] models = Parse(json, preferred);
                if (models.Length == 0) throw new InvalidOperationException("OpenRouter returned no eligible free text/tool models.");
                WriteCache(cachePath, json);
                return models;
            }
            catch (Exception)
            {
                string cached = ReadCache(cachePath);
                if (cached != null)
                {
                    string[] models = Parse(cached, preferred);
                    if (models.Length > 0) return models;
                }
                return Order(OpenRouterPool.PreferredDefaults, preferred);
            }
        }

        internal static string[] Parse(string json, string preferred = null)
        {
            OpenRouterCatalogResponse response;
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json ?? "{}")))
                response = (OpenRouterCatalogResponse)new DataContractJsonSerializer(typeof(OpenRouterCatalogResponse)).ReadObject(stream);
            List<OpenRouterCatalogItem> eligible = new List<OpenRouterCatalogItem>();
            if (response == null || response.Data == null) return new string[0];
            DateTime today = DateTime.UtcNow.Date;
            foreach (OpenRouterCatalogItem item in response.Data)
            {
                if (item == null || !OpenRouterPool.IsFreeModel(item.Id) || OpenRouterPool.IsFreeRouter(item.Id)) continue;
                if (item.Pricing == null || !item.Pricing.IsZero()) continue;
                if (item.SupportedParameters == null || !item.SupportedParameters.Contains("tools", StringComparer.OrdinalIgnoreCase)) continue;
                string[] outputs = item.Architecture == null ? null : item.Architecture.OutputModalities;
                if (outputs == null || outputs.Length != 1 || !string.Equals(outputs[0], "text", StringComparison.OrdinalIgnoreCase)) continue;
                DateTime expires;
                if (!string.IsNullOrWhiteSpace(item.ExpirationDate) && (!DateTime.TryParse(item.ExpirationDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out expires) || expires.Date <= today)) continue;
                eligible.Add(item);
            }
            return OrderItems(eligible, preferred);
        }

        internal static string[] Order(IEnumerable<string> models, string preferred = null)
        {
            List<OpenRouterCatalogItem> items = new List<OpenRouterCatalogItem>();
            if (models != null)
                foreach (string model in models)
                    items.Add(new OpenRouterCatalogItem { Id = model, ContextLength = 0 });
            return OrderItems(items, preferred);
        }

        private static string[] OrderItems(IEnumerable<OpenRouterCatalogItem> source, string preferred)
        {
            List<OpenRouterCatalogItem> items = source.Where(delegate(OpenRouterCatalogItem item) { return item != null && OpenRouterPool.IsFreeModel(item.Id); })
                .GroupBy(delegate(OpenRouterCatalogItem item) { return item.Id; }, StringComparer.OrdinalIgnoreCase)
                .Select(delegate(IGrouping<string, OpenRouterCatalogItem> group) { return group.OrderByDescending(delegate(OpenRouterCatalogItem item) { return item.ContextLength; }).First(); }).ToList();
            List<OpenRouterCatalogItem> ordered = new List<OpenRouterCatalogItem>();
            OpenRouterCatalogItem chosen = items.FirstOrDefault(delegate(OpenRouterCatalogItem item) { return string.Equals(item.Id, preferred, StringComparison.OrdinalIgnoreCase); });
            if (chosen != null) ordered.Add(chosen);
            foreach (string seed in OpenRouterPool.PreferredDefaults)
            {
                OpenRouterCatalogItem item = items.FirstOrDefault(delegate(OpenRouterCatalogItem candidate) { return string.Equals(candidate.Id, seed, StringComparison.OrdinalIgnoreCase); });
                if (item != null && !ordered.Contains(item)) ordered.Add(item);
            }
            foreach (OpenRouterCatalogItem item in items.Where(delegate(OpenRouterCatalogItem candidate) { return !ordered.Contains(candidate); })
                .OrderByDescending(delegate(OpenRouterCatalogItem item) { return item.ContextLength; })
                .ThenBy(delegate(OpenRouterCatalogItem item) { return item.Id; }, StringComparer.OrdinalIgnoreCase)) ordered.Add(item);
            return ordered.Select(delegate(OpenRouterCatalogItem item) { return item.Id; }).ToArray();
        }

        internal static string CachePath
        {
            get
            {
                string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrWhiteSpace(config)) config = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                return System.IO.Path.Combine(config, "Capri", "openrouter-free.json");
            }
        }

        private static string Download()
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Endpoint);
            request.Method = "GET";
            request.Accept = "application/json";
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            using (WebResponse response = request.GetResponse())
            using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }

        private static string ReadCache(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                TimeSpan age = DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(path);
                if (age > TimeSpan.FromMinutes(CacheMinutes) || age < TimeSpan.Zero) return null;
                return System.IO.File.ReadAllText(path);
            }
            catch (Exception) { return null; }
        }

        private static void WriteCache(string path, string json)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllText(path, json ?? "{}", new UTF8Encoding(false));
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) System.IO.File.SetUnixFileMode(path, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite);
            }
            catch (Exception) { }
        }
    }
}
