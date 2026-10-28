using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Text.RegularExpressions;
using System.Linq;

namespace CapriDesktop
{
    internal static class OpenRouterWire
    {
        internal static string Endpoint = OpenRouterPool.Endpoint;
        internal static string CatalogEndpoint = OpenRouterCatalog.Endpoint;
        internal static string QuotaEndpoint = "https://openrouter.ai/api/v1/key";
        internal static int Timeout = 45000;
        internal static Func<string, string, string> SendOverride;
        internal static Func<string, string> GetOverride;
        internal static Func<string, string, string> GetQuotaOverride;

        internal static OpenRouterResponse Complete(OpenRouterPool pool, ChatMessage[] messages, bool tools, int maxTokens = 1000)
        {
            if (pool == null || string.IsNullOrWhiteSpace(pool.Key)) throw new InvalidOperationException("OpenRouter is not configured.");
            if (pool.IsBlocked) throw new OpenRouterRequestException(429, "free-model-daily-limit", pool.BlockedUntil, true);
            if (pool.Models.Length == 0) throw new InvalidOperationException("No verified free OpenRouter model is available.");
            HashSet<string> skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string model = pool.Models[0];
            int attempt = 0;
            while (model != null && attempt++ < pool.Models.Length)
            {
                try
                {
                    string system = "";
                    string user = "";
                    if (messages != null) foreach (ChatMessage message in messages) { if (message.Role == "system") system = message.Content; else if (message.Role == "user") user = message.Content; }
                    string json = Build(new[] { model }, system, user, tools);
                    OpenRouterResponse response = OpenRouterPool.ParseResponse(Send(pool.Key, json));
                    if (response.Error != null)
                    {
                        if (!OpenRouterPool.Retryable(response.Error.Code, response.Error.Code)) throw new OpenRouterRequestException(response.Error.Code, response.Error.Message);
                        throw new OpenRouterRequestException(response.Error.Code, response.Error.Message);
                    }
                    if (response.Choices == null || response.Choices.Length == 0 || response.Choices[0].Message == null) throw new OpenRouterRequestException(502, "OpenRouter returned an empty completion.");
                    if (!OpenRouterPool.IsFreeModel(response.Model) && response.Model != "openrouter/free") throw new OpenRouterRequestException(402, "OpenRouter selected an unverified or paid model; response rejected.");
                    pool.LastModel = response.Model;
                    return response;
                }
                catch (OpenRouterRequestException error)
                {
                    if (error.Status == 402 && (error.Body ?? "").Contains("unverified or paid")) throw;
                    if (!OpenRouterPool.Retryable(error.Status, error.Status)) throw;
                    skipped.Add(model);
                    model = OpenRouterPool.Next(model, pool.Models, skipped);
                }
                catch (System.Net.WebException error)
                {
                    if (error.Status == System.Net.WebExceptionStatus.RequestCanceled) throw;
                    skipped.Add(model);
                    model = OpenRouterPool.Next(model, pool.Models, skipped);
                }
            }
            throw new OpenRouterRequestException(429, "Every configured free OpenRouter model is unavailable or rate-limited.");
        }

        internal static OpenRouterResponse CompleteRaw(OpenRouterPool pool, IList<string> messages, bool tools, int maxTokens = 1000)
        {
            if (pool == null || string.IsNullOrWhiteSpace(pool.Key)) throw new InvalidOperationException("OpenRouter is not configured.");
            if (pool.IsBlocked) throw new OpenRouterRequestException(429, "free-model-daily-limit", pool.BlockedUntil, true);
            if (pool.Models.Length == 0) throw new InvalidOperationException("No verified free OpenRouter model is available.");
            if (pool.Models.Length == 1 && OpenRouterPool.IsFreeRouter(pool.Models[0]))
            {
                string routerBody = "{\"model\":\"openrouter/free\",\"messages\":[" + JoinMessages(messages) + "],\"tools\":" + Hands.Tools + ",\"tool_choice\":\"auto\",\"stream\":false,\"max_tokens\":" + maxTokens + ",\"provider\":{\"max_price\":{\"prompt\":0,\"completion\":0}}}";
                OpenRouterResponse routed = OpenRouterPool.ParseResponse(Send(pool.Key, routerBody));
                if (routed.Error != null) throw new OpenRouterRequestException(routed.Error.Code, routed.Error.Message);
                if (routed.Choices == null || routed.Choices.Length == 0 || routed.Choices[0].Message == null) throw new OpenRouterRequestException(502, "Empty free-router response.");
                if (!OpenRouterPool.IsFreeModel(routed.Model) || !pool.Models.Contains(routed.Model, StringComparer.OrdinalIgnoreCase)) throw new OpenRouterRequestException(402, "Free router selected a model outside the verified allowlist; response rejected.");
                pool.LastModel = routed.Model;
                if (pool.DailyRemaining > 0) pool.DailyRemaining--;
                if (pool.DailyUsed >= 0) pool.DailyUsed++;
                return routed;
            }
            string[] models = OpenRouterPool.WithoutRouter(pool.Models);
            if (models.Length == 0) throw new InvalidOperationException("No verified free models are configured.");
            OpenRouterRequestException last = null;
            for (int offset = 0; offset < models.Length; offset += 3)
            {
                string[] batch = models.Skip(offset).Take(3).ToArray();
                try
                {
                    string body = BuildPoolJson(batch, messages, true, maxTokens);
                    OpenRouterResponse response = OpenRouterPool.ParseResponse(Send(pool.Key, body));
                    if (response.Error != null) throw new OpenRouterRequestException(response.Error.Code, response.Error.Message);
                    if (response.Choices == null || response.Choices.Length == 0 || response.Choices[0].Message == null) throw new OpenRouterRequestException(502, "Empty response.");
                    if (!OpenRouterPool.IsFreeModel(response.Model) || !pool.Models.Contains(response.Model, StringComparer.OrdinalIgnoreCase)) throw new OpenRouterRequestException(402, "Unverified or non-free model response rejected.");
                    pool.LastModel = response.Model;
                    if (pool.DailyRemaining > 0) pool.DailyRemaining--;
                    if (pool.DailyUsed >= 0) pool.DailyUsed++;
                    return response;
                }
                catch (OpenRouterRequestException error)
                {
                    last = error;
                    if (error.DailyLimit || IsDailyLimit(error))
                    {
                        DateTime reset = error.ResetAtUtc ?? OpenRouterPool.NextDailyResetUtc(DateTime.UtcNow);
                        pool.BlockDaily(reset);
                        pool.LastQuotaCheck = DateTime.UtcNow.AddMinutes(-6);
                        throw new OpenRouterRequestException(429, "free-model-daily-limit", pool.BlockedUntil, true);
                    }
                    if (error.Status == 402 || error.Status == 401 || error.Status == 403) throw;
                    if (!OpenRouterPool.Retryable(error.Status, error.Status)) throw;
                    if (error.Status == 400) throw new OpenRouterRequestException(400, error.Body);
                    if (error.Status == 429 && offset + 3 >= models.Length)
                    {
                        int retry = error.RetryAfterSeconds > 0 ? error.RetryAfterSeconds : 60;
                        throw new OpenRouterRequestException(429, error.Body, error.ResetAtUtc ?? DateTime.UtcNow.AddSeconds(retry), false, retry);
                    }
                }
            }
            throw last ?? new OpenRouterRequestException(503, "All verified free model groups were unavailable.");
        }

        internal static OpenRouterDailyQuota GetQuota(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (GetQuotaOverride != null)
            {
                string overridden = GetQuotaOverride(key, QuotaEndpoint);
                using (MemoryStream fixture = new MemoryStream(Encoding.UTF8.GetBytes(overridden ?? "{}")))
                {
                    OpenRouterQuotaResponse fake = (OpenRouterQuotaResponse)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(OpenRouterQuotaResponse)).ReadObject(fixture);
                    return fake == null || fake.Data == null ? null : fake.Data.FreeModelDailyRequests;
                }
            }
            string json = Get(key, QuotaEndpoint);
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "{}")))
            {
                OpenRouterQuotaResponse response = (OpenRouterQuotaResponse)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(OpenRouterQuotaResponse)).ReadObject(stream);
                return response == null || response.Data == null ? null : response.Data.FreeModelDailyRequests;
            }
        }

        internal static void RefreshQuota(OpenRouterPool pool)
        {
            if (pool == null || string.IsNullOrWhiteSpace(pool.Key)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    OpenRouterDailyQuota quota = GetQuota(pool.Key);
                    if (quota != null) pool.SetDailyQuota(quota.Used, quota.Limit, quota.Remaining);
                }
                catch (Exception) { }
            });
        }

        private static string Get(string key, string endpoint)
        {
            if (GetOverride != null) return GetOverride(endpoint);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "GET";
            request.Accept = "application/json";
            request.Headers["Authorization"] = "Bearer " + key;
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            using (WebResponse response = request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }

        private static bool IsDailyLimit(OpenRouterRequestException error)
        {
            string text = ((error == null ? "" : error.Body) + " " + (error == null ? "" : error.Message)).ToLowerInvariant();
            return error != null && error.DailyLimit || error != null && error.Status == 429 && (text.Contains("free-models-per-day") || text.Contains("free model daily") || text.Contains("daily free model") || text.Contains("daily limit"));
        }

        internal static string BuildPoolJson(IEnumerable<string> models, IList<string> messages, bool tools, int maxTokens = 1000)
        {
            string[] ids = OpenRouterPool.WithoutRouter(models);
            if (ids.Length == 0) throw new InvalidOperationException("Only verified free model IDs can enter the ordered fallback array.");
            if (ids.Length > 3) throw new InvalidOperationException("OpenRouter supports at most three models per request.");
            for (int i = 0; i < ids.Length; i++) if (!OpenRouterPool.IsFreeModel(ids[i])) throw new InvalidOperationException("Only verified free model IDs can enter the ordered fallback array.");
            StringBuilder body = new StringBuilder("{\"models\":").Append(JsonArray(ids)).Append(",\"messages\":[").Append(JoinMessages(messages)).Append("],\"stream\":false,\"max_tokens\":").Append(Math.Max(1, maxTokens));
            if (tools) body.Append(",\"tools\":").Append(Hands.Tools).Append(",\"tool_choice\":\"auto\"");
            body.Append(",\"provider\":{\"max_price\":{\"prompt\":0,\"completion\":0}}");
            return body.Append('}').ToString();
        }

        private static string JoinMessages(IList<string> messages)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < messages.Count; i++) { if (i > 0) text.Append(','); text.Append(messages[i]); }
            return text.ToString();
        }

        internal static string Serialize(OpenRouterRequest request)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(OpenRouterRequest)).WriteObject(stream, request);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static string Build(string[] models, string system, string text, bool tools, IList<ChatMessage> history = null)
        {
            return BuildJson(models, system, text, tools, history, null);
        }

        internal static string BuildJson(string[] models, string system, string text, bool tools, IList<ChatMessage> history, string assistantToolCallJson)
        {
            string ids = JsonArray(OpenRouterPool.WithoutRouter(models));
            if (ids == "[]") throw new InvalidOperationException("No verified free OpenRouter models are configured.");
            StringBuilder body = new StringBuilder("{\"models\":").Append(ids).Append(",\"messages\":[{\"role\":\"system\",\"content\":").Append(Quote(system)).Append('}');
            if (history != null)
                foreach (ChatMessage message in history)
                    if (message != null && (message.Role == "user" || message.Role == "assistant" || message.Role == "system")) body.Append(",").Append(SerializeMessage(message));
            if (!string.IsNullOrEmpty(assistantToolCallJson)) body.Append(",").Append(assistantToolCallJson);
            body.Append(",{\"role\":\"user\",\"content\":").Append(Quote(text)).Append("}],\"stream\":false,\"max_tokens\":1000");
            if (tools) body.Append(",\"tools\":").Append(Hands.Tools).Append(",\"tool_choice\":\"auto\"");
            body.Append(",\"provider\":{\"max_price\":{\"prompt\":0,\"completion\":0}}");
            return body.Append('}').ToString();
        }

        internal static string ToolResult(string callId, string content)
        {
            return "{\"role\":\"tool\",\"tool_call_id\":" + Quote(callId) + ",\"content\":" + Quote(content) + "}";
        }

        internal static string ToolCallMessage(string content, IList<OpenRouterToolCall> calls)
        {
            StringBuilder result = new StringBuilder("{\"role\":\"assistant\",\"content\":").Append(content == null ? "null" : Quote(content)).Append(",\"tool_calls\":[");
            for (int i = 0; i < calls.Count; i++)
            {
                if (i > 0) result.Append(',');
                OpenRouterToolCall call = calls[i];
                result.Append("{\"id\":").Append(Quote(call.Id)).Append(",\"type\":\"function\",\"function\":{\"name\":").Append(Quote(call.Function.Name)).Append(",\"arguments\":").Append(Quote(call.Function.Arguments)).Append("}}");
                if (call.ExtraContent != null && call.ExtraContent.Google != null && !string.IsNullOrWhiteSpace(call.ExtraContent.Google.ThoughtSignature))
                    result.Insert(result.Length - 1, ",\"extra_content\":{\"google\":{\"thought_signature\":" + Quote(call.ExtraContent.Google.ThoughtSignature) + "}}");
            }
            return result.Append("]}").ToString();
        }

        internal static string ToolCallMessage(string rawAssistantMessage)
        {
            if (string.IsNullOrWhiteSpace(rawAssistantMessage)) throw new ArgumentException("Assistant tool-call message is empty.");
            string trimmed = rawAssistantMessage.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.Contains("\"tool_calls\"")) throw new ArgumentException("Assistant tool-call message is not valid tool-call JSON.");
            return trimmed;
        }

        private static string SerializeMessage(ChatMessage message)
        {
            if (message.Role == "assistant" && message.Calls != null && message.Calls.Length > 0)
            {
                StringBuilder content = new StringBuilder("{\"role\":\"assistant\",\"content\":").Append(message.Content == null ? "null" : Quote(message.Content)).Append(",\"tool_calls\":[");
                for (int i = 0; i < message.Calls.Length; i++)
                {
                    if (i > 0) content.Append(',');
                    ToolCall call = message.Calls[i];
                    string id = "call_" + i.ToString(CultureInfo.InvariantCulture);
                    content.Append("{\"id\":").Append(Quote(id)).Append(",\"type\":\"function\",\"function\":{\"name\":").Append(Quote(call.Function.Name)).Append(",\"arguments\":").Append(Quote(Arguments(call.Function.Arguments))).Append("}}");
                }
                return content.Append("]}").ToString();
            }
            if (message.Role == "tool") return "{\"role\":\"tool\",\"tool_call_id\":\"call_0\",\"content\":" + Quote(message.Content ?? "") + "}";
            return "{\"role\":" + Quote(message.Role) + ",\"content\":" + Quote(message.Content ?? "") + "}";
        }

        private static string Arguments(ToolArguments args)
        {
            if (args == null) return "{}";
            List<string> pairs = new List<string>();
            Add(pairs, "name", args.Name); Add(pairs, "site", args.Site); Add(pairs, "search", args.Search); Add(pairs, "action", args.Action); Add(pairs, "focus", args.Focus); Add(pairs, "song", args.Song); Add(pairs, "app", args.App); Add(pairs, "kind", args.Kind); Add(pairs, "task", args.Task);
            return "{" + string.Join(",", pairs.ToArray()) + "}";
        }

        private static void Add(List<string> pairs, string name, string value) { if (value != null) pairs.Add(Quote(name) + ":" + Quote(value)); }

        internal static string Send(string key, string json)
        {
            if (SendOverride != null) return SendOverride(key, json);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Headers["Authorization"] = "Bearer " + key;
            request.Headers["HTTP-Referer"] = "https://github.com/Wilz0n/capri-desktop-linux";
            request.Headers["X-OpenRouter-Title"] = "Capri Desktop";
            request.Timeout = Timeout;
            request.ReadWriteTimeout = Timeout;
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            request.ContentLength = bytes.Length;
            try
            {
                using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null) throw;
                int status = (int)response.StatusCode;
                string retryAfter = response.Headers["Retry-After"];
                string resetHeader = response.Headers["X-RateLimit-Reset"];
                string body = "";
                using (response)
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) body = reader.ReadToEnd();
                string safeMessage = null;
                try { OpenRouterApiError apiError = OpenRouterPool.ParseResponse(body).Error; safeMessage = apiError == null ? null : apiError.Message; }
                catch (Exception) { }
                string safeBody = safeMessage == null ? body : "{\"error\":{\"code\":" + status.ToString(CultureInfo.InvariantCulture) + ",\"message\":" + Quote(safeMessage) + "}}";
                throw new OpenRouterRequestException(status, safeBody, ParseReset(resetHeader), IsDailyLimitBody(status, body), ParseRetryAfter(retryAfter));
            }
        }

        private static DateTime? ParseReset(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed)) return parsed;
            double seconds;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds > 0) return DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime;
            return null;
        }

        private static int ParseRetryAfter(string value)
        {
            int seconds;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds)) return Math.Max(0, seconds);
            DateTime date;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date)) return Math.Max(0, (int)(date - DateTime.UtcNow).TotalSeconds);
            return 0;
        }

        private static bool IsDailyLimitBody(int status, string body)
        {
            if (status != 429) return false;
            string text = (body ?? "").ToLowerInvariant();
            return text.Contains("free-models-per-day") || text.Contains("free model daily") || text.Contains("daily free model") || text.Contains("daily limit");
        }

        internal static string JsonArray(string[] values)
        {
            StringBuilder result = new StringBuilder("[");
            if (values != null)
                for (int i = 0; i < values.Length; i++)
                {
                    if (!OpenRouterPool.IsFreeModel(values[i])) continue;
                    if (result.Length > 1) result.Append(',');
                    result.Append(Quote(values[i]));
                }
            return result.Append(']').ToString();
        }

        internal static string Quote(string text)
        {
            StringBuilder result = new StringBuilder("\"");
            foreach (char ch in text ?? "")
            {
                if (ch == '"' || ch == '\\') result.Append('\\').Append(ch);
                else if (ch == '\n') result.Append("\\n");
                else if (ch == '\r') result.Append("\\r");
                else if (ch == '\t') result.Append("\\t");
                else if (ch < ' ') result.Append("\\u").Append(((int)ch).ToString("x4"));
                else result.Append(ch);
            }
            return result.Append('"').ToString();
        }
    }

    internal sealed class OpenRouterRequestException : Exception
    {
        internal readonly int Status;
        internal readonly string Body;
        internal readonly DateTime? ResetAtUtc;
        internal readonly bool DailyLimit;
        internal readonly int RetryAfterSeconds;
        internal readonly string UserMessage;
        internal OpenRouterRequestException(int status, string body) : this(status, body, null, false, 0) { }
        internal OpenRouterRequestException(int status, string body, DateTime? resetAtUtc, bool dailyLimit, int retryAfterSeconds = 0) : base("OpenRouter request failed with HTTP " + status + ".")
        {
            Status = status;
            Body = body ?? "";
            ResetAtUtc = resetAtUtc;
            DailyLimit = dailyLimit;
            RetryAfterSeconds = retryAfterSeconds;
            UserMessage = SafeProviderMessage(Body);
        }

        private static string SafeProviderMessage(string body)
        {
            try
            {
                OpenRouterApiError error = OpenRouterPool.ParseResponse(body).Error;
                string message = error == null ? null : error.Message;
                if (string.IsNullOrWhiteSpace(message)) return null;
                message = Regex.Replace(message, @"(?i)(bearer\s+)[a-z0-9._-]+", "$1[redacted]");
                return message.Length > 240 ? message.Substring(0, 240) : message;
            }
            catch (Exception) { return null; }
        }
    }
}
