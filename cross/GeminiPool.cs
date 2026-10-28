using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class GeminiResponse
    {
        [DataMember(Name = "model")] internal string Model;
        [DataMember(Name = "choices")] internal OpenRouterChoice[] Choices;
        [DataMember(Name = "error")] internal GeminiError Error;
    }

    [DataContract]
    internal sealed class GeminiError
    {
        [DataMember(Name = "code")] internal int Code;
        [DataMember(Name = "message")] internal string Message;
        [DataMember(Name = "status")] internal string Status;
    }

    internal sealed class GeminiException : Exception
    {
        internal readonly int Status;
        internal readonly string Body;
        internal readonly string Model;
        internal readonly int RetryAfterSeconds;
        internal readonly bool DailyLimit;
        internal readonly DateTime? ResetAtUtc;
        internal readonly string UserMessage;

        internal GeminiException(int status, string body, string model = null, int retryAfterSeconds = 0, bool dailyLimit = false, DateTime? resetAtUtc = null)
            : base("Gemini request failed with HTTP " + status + ".")
        {
            Status = status;
            Body = body ?? "";
            Model = model;
            RetryAfterSeconds = retryAfterSeconds;
            DailyLimit = dailyLimit;
            ResetAtUtc = resetAtUtc;
            UserMessage = status >= 400 && status < 500 && !dailyLimit ? SafeMessage(body) : null;
        }

        private static string SafeMessage(string body)
        {
            try
            {
                using (System.IO.MemoryStream stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(body ?? "{}")))
                {
                    GeminiResponse parsed = (GeminiResponse)new DataContractJsonSerializer(typeof(GeminiResponse)).ReadObject(stream);
                    string message = parsed == null || parsed.Error == null ? null : parsed.Error.Message;
                    if (string.IsNullOrWhiteSpace(message)) return null;
                    return message.Length > 240 ? message.Substring(0, 240) : message;
                }
            }
            catch (Exception) { return null; }
        }
    }

    internal sealed class GeminiPool
    {
        internal const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
        internal static readonly string[] Models = { "gemini-3.5-flash-lite", "gemini-3.1-flash-lite", "gemini-3.8-flash" };
        internal static readonly string DefaultModel = "gemini-3.5-flash-lite";
        internal static int Timeout = 12000;
        internal static int ToolTurnTimeout = 25000;
        internal static Func<string, string, string> SendOverride;
        internal readonly string Key;
        internal string Preferred;
        internal string LastModel;
        internal string LastRawResponse;
        internal DateTime BlockedUntil;
        internal readonly Dictionary<string, DateTime> Cooling = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, int> ConsecutiveFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal double LastLatencySeconds = -1;
        internal string LastErrorModel;
        internal DateTime LastRetryAt;
        internal int LastRetryMinutes;

        internal GeminiPool(string key, string preferred = null)
        {
            Key = (key ?? "").Trim();
            Preferred = IsAllowed(preferred) ? preferred : DefaultModel;
        }

        internal bool IsConfigured { get { return !string.IsNullOrWhiteSpace(Key); } }
        internal bool IsBlocked { get { return BlockedUntil > DateTime.UtcNow; } }

        internal static bool IsAllowed(string model) { return model != null && Models.Contains(model, StringComparer.Ordinal); }

        internal string[] OrderedModels()
        {
            List<string> ordered = new List<string> { Preferred };
            ordered.AddRange(Models.Where(delegate(string model) { return !string.Equals(model, Preferred, StringComparison.Ordinal); }));
            return ordered.ToArray();
        }

        internal OpenRouterResponse CompleteRaw(IList<string> messages, bool tools, int maxTokens = 1000)
        {
            if (!IsConfigured) throw new InvalidOperationException("Gemini API key is not configured.");
            if (IsBlocked) throw new GeminiException(429, "daily-quota-blocked", Preferred, 0, true, BlockedUntil);
            List<string> failed = new List<string>();
            foreach (string model in OrderedModels())
            {
                if (IsCooling(model)) continue;
                int requestTimeout = HasToolResult(messages) ? ToolTurnTimeout : 12000;
                int priorTimeout = Timeout;
                Timeout = requestTimeout;
                try
                {
                    bool toolTurn = HasToolResult(messages);
                    string body = Build(model, messages, tools, maxTokens, toolTurn);
                    DateTime started = DateTime.UtcNow;
                    string json = Send(Key, body);
                    LastRawResponse = json;
                    LastLatencySeconds = (DateTime.UtcNow - started).TotalSeconds;
                    GeminiResponse response = Parse(json);
                    if (response.Error != null) throw new GeminiException(response.Error.Code, response.Error.Message, model);
                    if (response.Choices == null || response.Choices.Length == 0 || response.Choices[0].Message == null) throw new GeminiException(502, "Empty Gemini completion.", model);
                    string actual = NormalizeModel(response.Model, model);
                    if (!IsAllowed(actual)) throw new GeminiException(502, "Gemini returned a model outside the free allowlist.", model);
                    RegisterSuccess(actual, LastLatencySeconds);
                    return new OpenRouterResponse { Model = actual, Choices = response.Choices };
                }
                catch (GeminiException error)
                {
                    if (error.DailyLimit || IsDaily(error))
                    {
                        BlockedUntil = error.ResetAtUtc ?? NextPacificMidnightUtc(DateTime.UtcNow);
                        throw new GeminiException(429, error.Body, model, error.RetryAfterSeconds, true, BlockedUntil);
                    }
                    if (error.Status == 401 || error.Status == 403) throw;
                    if (error.Status == 400) throw;
                    if (error.Status == 429 || error.Status == 408 || error.Status >= 500)
                    {
                        RegisterFailure(model);
                        if (error.RetryAfterSeconds > 0)
                        {
                            LastRetryAt = DateTime.UtcNow.AddSeconds(error.RetryAfterSeconds);
                            Cooling[model] = LastRetryAt;
                        }
                        failed.Add(model);
                        continue;
                    }
                    throw;
                }
                catch (WebException error)
                {
                    if (error.Status == WebExceptionStatus.RequestCanceled) throw;
                    RegisterFailure(model);
                    failed.Add(model);
                }
                catch (System.Net.Sockets.SocketException)
                {
                    RegisterFailure(model);
                    failed.Add(model);
                }
                finally
                {
                    Timeout = priorTimeout;
                }
            }
            throw new GeminiException(429, failed.Count == 0 ? "All Gemini free models are cooling down." : "All Gemini free models are unavailable or rate-limited.", Preferred);
        }

        internal static string Build(string model, IList<string> messages, bool tools, int maxTokens = 1000, bool hasToolResult = false)
        {
            if (!IsAllowed(model)) throw new InvalidOperationException("Only allowlisted Gemini free models may be requested.");
            StringBuilder json = new StringBuilder("{\"model\":").Append(OpenRouterWire.Quote(model)).Append(",\"messages\":[");
            if (messages != null) for (int i = 0; i < messages.Count; i++) { if (i > 0) json.Append(','); json.Append(messages[i]); }
            json.Append("],\"max_tokens\":").Append(Math.Max(1, maxTokens)).Append(",\"stream\":false");
            if (model == "gemini-3.8-flash" && hasToolResult) json.Append(",\"reasoning_effort\":\"low\"");
            if (tools) json.Append(",\"tools\":").Append(Hands.Tools).Append(",\"tool_choice\":\"auto\"");
            return json.Append('}').ToString();
        }

        internal static GeminiResponse Parse(string json)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json ?? "{}")))
                return (GeminiResponse)new DataContractJsonSerializer(typeof(GeminiResponse)).ReadObject(stream);
        }

        internal static string ErrorText(string json)
        {
            try
            {
                GeminiResponse parsed = Parse(json);
                return parsed == null || parsed.Error == null ? null : parsed.Error.Message;
            }
            catch (Exception) { return null; }
        }

        internal static OpenRouterToolCall[] ReadToolCalls(string json)
        {
            GeminiResponse response = Parse(json);
            return response == null || response.Choices == null || response.Choices.Length == 0 || response.Choices[0].Message == null ? new OpenRouterToolCall[0] : response.Choices[0].Message.ToolCalls ?? new OpenRouterToolCall[0];
        }

        internal static DateTime NextPacificMidnightUtc(DateTime nowUtc)
        {
            TimeZoneInfo pacific;
            try { pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
            catch (TimeZoneNotFoundException) { pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), pacific);
            DateTime next = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(next, pacific);
        }

        internal static DateTime? ParseRetryReset(string body)
        {
            try
            {
                using (System.IO.MemoryStream stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(body ?? "{}")))
                {
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                    {
                        string json = reader.ReadToEnd();
                        int index = json.IndexOf("retryDelay", StringComparison.OrdinalIgnoreCase);
                        if (index < 0) index = json.IndexOf("retry_delay", StringComparison.OrdinalIgnoreCase);
                        if (index < 0) return null;
                        Match match = Regex.Match(json.Substring(index), @"(\d+(?:\.\d+)?)\s*s", RegexOptions.IgnoreCase);
                    double seconds;
                        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return null;
                        return DateTime.UtcNow.AddSeconds(Math.Max(1, seconds));
                    }
                }
            }
            catch (Exception) { return null; }
        }

        internal string StatusText()
        {
            if (!IsConfigured) return "Gemini: not configured";
            if (IsBlocked) return "Gemini free quota exhausted until " + BlockedUntil.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture) + " local time (midnight Pacific reset)";
            if (LastErrorModel != null && LastRetryAt > DateTime.UtcNow) return "Gemini " + LastErrorModel + " cooling down " + LastRetryMinutes + " min, until " + LastRetryAt.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture);
            if (LastModel != null && LastLatencySeconds >= 0) return "Gemini ready · last: " + LastModel + " · " + LastLatencySeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
            return "Gemini: ready · preferred " + Preferred;
        }

        internal static string NormalizeModel(string responseModel, string requested)
        {
            if (string.IsNullOrWhiteSpace(responseModel)) return requested;
            string id = responseModel.Trim();
            if (id.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) id = id.Substring(7);
            return id;
        }

        private static bool IsDaily(GeminiException error)
        {
            string body = (error == null ? "" : error.Body).ToLowerInvariant();
            return error != null && error.Status == 429 && (body.Contains("perday") || body.Contains("per_day") || body.Contains("per day") || body.Contains("requests per day") || body.Contains("daily quota"));
        }

        internal int RegisterFailure(string model)
        {
            int count;
            ConsecutiveFailures.TryGetValue(model, out count);
            count++;
            ConsecutiveFailures[model] = count;
            int minutes = count == 1 ? 1 : count == 2 ? 5 : 15;
            LastErrorModel = model;
            LastRetryMinutes = minutes;
            LastRetryAt = DateTime.UtcNow.AddMinutes(minutes);
            Cooling[model] = LastRetryAt;
            return minutes;
        }

        internal void RegisterSuccess(string model, double latency)
        {
            ConsecutiveFailures[model] = 0;
            Cooling.Remove(model);
            LastModel = model;
            LastLatencySeconds = latency;
            LastErrorModel = null;
            LastRetryAt = DateTime.MinValue;
            LastRetryMinutes = 0;
        }

        internal bool IsCooling(string model) { DateTime until; return Cooling.TryGetValue(model, out until) && until > DateTime.UtcNow; }

        private static bool HasToolResult(IList<string> messages)
        {
            return messages != null && messages.Any(delegate(string message) { return message != null && message.Contains("\"role\":\"tool\""); });
        }

        private static string Send(string key, string json)
        {
            if (SendOverride != null) return SendOverride(key, json);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Headers["Authorization"] = "Bearer " + key;
            request.Timeout = Timeout;
            request.ReadWriteTimeout = Timeout;
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            request.ContentLength = bytes.Length;
            try
            {
                using (System.IO.Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                using (WebResponse response = request.GetResponse())
                using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null) throw;
                int status = (int)response.StatusCode;
                string retry = response.Headers["Retry-After"];
                string body;
                using (response)
                using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream(), Encoding.UTF8)) body = reader.ReadToEnd();
                int retrySeconds;
                int.TryParse(retry, NumberStyles.Integer, CultureInfo.InvariantCulture, out retrySeconds);
                string providerMessage = ErrorText(body);
                if (providerMessage != null) body = "{\"error\":{\"code\":" + status.ToString(CultureInfo.InvariantCulture) + ",\"message\":" + OpenRouterWire.Quote(providerMessage) + "}}";
                if (status >= 400 && status < 500 && status != 401 && status != 403 && status != 429)
                    throw new GeminiException(status, body, null, retrySeconds, false, ParseRetryReset(body));
                throw new GeminiException(status, body, null, retrySeconds, IsDaily(new GeminiException(status, body)), ParseRetryReset(body));
            }
        }
    }
}
