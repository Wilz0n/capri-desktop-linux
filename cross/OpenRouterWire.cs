using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Globalization;

namespace CapriDesktop
{
    internal static class OpenRouterWire
    {
        internal static string Endpoint = OpenRouterPool.Endpoint;
        internal static int Timeout = 45000;
        internal static Func<string, string, string> SendOverride;

        internal static OpenRouterResponse Complete(OpenRouterPool pool, ChatMessage[] messages, bool tools, int maxTokens = 1000)
        {
            if (pool == null || string.IsNullOrWhiteSpace(pool.Key)) throw new InvalidOperationException("OpenRouter is not configured.");
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
            if (pool.Models.Length == 1 && OpenRouterPool.IsFreeRouter(pool.Models[0]))
            {
                string routerBody = "{\"model\":\"openrouter/free\",\"messages\":[" + JoinMessages(messages) + "],\"tools\":" + Hands.Tools + ",\"tool_choice\":\"auto\",\"stream\":false,\"max_tokens\":" + maxTokens + "}";
                OpenRouterResponse routed = OpenRouterPool.ParseResponse(Send(pool.Key, routerBody));
                if (routed.Error != null) throw new OpenRouterRequestException(routed.Error.Code, routed.Error.Message);
                if (routed.Choices == null || routed.Choices.Length == 0 || routed.Choices[0].Message == null) throw new OpenRouterRequestException(502, "Empty free-router response.");
                if (!OpenRouterPool.IsFreeModel(routed.Model) || !pool.Models.Contains(routed.Model, StringComparer.OrdinalIgnoreCase)) throw new OpenRouterRequestException(402, "Free router selected a model outside the verified allowlist; response rejected.");
                pool.LastModel = routed.Model;
                return routed;
            }
            string[] models = OpenRouterPool.WithoutRouter(pool.Models);
            if (models.Length == 0) throw new InvalidOperationException("No verified free models are configured.");
            string body = BuildPoolJson(models, messages, true, maxTokens);
            OpenRouterResponse response = OpenRouterPool.ParseResponse(Send(pool.Key, body));
            if (response.Error != null) throw new OpenRouterRequestException(response.Error.Code, response.Error.Message);
            if (response.Choices == null || response.Choices.Length == 0 || response.Choices[0].Message == null) throw new OpenRouterRequestException(502, "Empty response.");
            if (!OpenRouterPool.IsFreeModel(response.Model) || !models.Contains(response.Model, StringComparer.OrdinalIgnoreCase)) throw new OpenRouterRequestException(402, "Unverified or non-free model response rejected.");
            pool.LastModel = response.Model;
            return response;
        }

        internal static string BuildPoolJson(IEnumerable<string> models, IList<string> messages, bool tools, int maxTokens = 1000)
        {
            string[] ids = OpenRouterPool.WithoutRouter(models);
            if (ids.Length == 0) throw new InvalidOperationException("Only verified free model IDs can enter the ordered fallback array.");
            for (int i = 0; i < ids.Length; i++) if (!OpenRouterPool.IsFreeModel(ids[i])) throw new InvalidOperationException("Only verified free model IDs can enter the ordered fallback array.");
            StringBuilder body = new StringBuilder("{\"models\":").Append(JsonArray(ids)).Append(",\"messages\":[").Append(JoinMessages(messages)).Append("],\"stream\":false,\"max_tokens\":").Append(Math.Max(1, maxTokens));
            if (tools) body.Append(",\"tools\":").Append(Hands.Tools).Append(",\"tool_choice\":\"auto\"");
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
            }
            return result.Append("]}").ToString();
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
                string body = "";
                using (response)
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) body = reader.ReadToEnd();
                throw new OpenRouterRequestException(status, body);
            }
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
        internal OpenRouterRequestException(int status, string body) : base("OpenRouter request failed with HTTP " + status + ".") { Status = status; Body = body ?? ""; }
    }
}
