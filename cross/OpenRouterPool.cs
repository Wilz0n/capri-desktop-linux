using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class OpenRouterRequest
    {
        [DataMember(Name = "models")] internal string[] Models;
        [DataMember(Name = "messages")] internal ChatMessage[] Messages;
        [DataMember(Name = "tools", EmitDefaultValue = false)] internal object Tools;
        [DataMember(Name = "tool_choice", EmitDefaultValue = false)] internal string ToolChoice;
        [DataMember(Name = "stream")] internal bool Stream;
        [DataMember(Name = "max_tokens")] internal int MaxTokens;
    }

    [DataContract]
    internal sealed class OpenRouterResponse
    {
        [DataMember(Name = "model")] internal string Model;
        [DataMember(Name = "choices")] internal OpenRouterChoice[] Choices;
        [DataMember(Name = "error")] internal OpenRouterApiError Error;
    }

    [DataContract]
    internal sealed class OpenRouterChoice
    {
        [DataMember(Name = "message")] internal OpenRouterMessage Message;
        [DataMember(Name = "finish_reason")] internal string FinishReason;
    }

    [DataContract]
    internal sealed class OpenRouterMessage
    {
        [DataMember(Name = "content")] internal string Content;
        [DataMember(Name = "tool_calls")] internal OpenRouterToolCall[] ToolCalls;
        [DataMember(Name = "tool_call_id")] internal string ToolCallId;
    }

    [DataContract]
    internal sealed class OpenRouterToolCall
    {
        [DataMember(Name = "id")] internal string Id;
        [DataMember(Name = "type")] internal string Type;
        [DataMember(Name = "function")] internal OpenRouterToolFunction Function;
        internal ToolArguments ArgumentsObject;
    }

    [DataContract]
    internal sealed class OpenRouterToolFunction
    {
        [DataMember(Name = "name")] internal string Name;
        [DataMember(Name = "arguments")] internal string Arguments;
    }

    [DataContract]
    internal sealed class OpenRouterApiError
    {
        [DataMember(Name = "code")] internal int Code;
        [DataMember(Name = "message")] internal string Message;
        [DataMember(Name = "metadata")] internal OpenRouterMetadata Metadata;
    }

    [DataContract]
    internal sealed class OpenRouterMetadata
    {
        [DataMember(Name = "raw")] internal string Raw;
    }

    internal sealed class OpenRouterPool
    {
        internal const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";
        internal static readonly string[] Requested =
        {
            "cohere/north-mini-code:free",
            "poolside/laguna-s-2.1:free",
            "nvidia/nemotron-3-ultra-550b-a55b:free",
            "nvidia/nemotron-3.5-lightning:free",
            "google/gemma-4-31b-it:free",
            "thinkingmachines/inkling:free"
        };
        internal const string FreeRouter = "openrouter/free";
        private static readonly Regex FreeModel = new Regex(@"\A[a-z0-9][a-z0-9._-]*/[a-z0-9][a-z0-9._-]*:free\z", RegexOptions.IgnoreCase);
        private static readonly HashSet<string> VerifiedFree = new HashSet<string>(Requested.Where(delegate(string id) { return id != FreeRouter; }), StringComparer.OrdinalIgnoreCase);

        internal readonly string Key;
        internal readonly string[] Models;
        internal string LastModel;
        internal string LastUsage;

        internal OpenRouterPool(string key, IEnumerable<string> models = null)
        {
            Key = (key ?? "").Trim();
            Models = ValidatePool(models ?? Requested);
        }

        internal static string[] ValidatePool(IEnumerable<string> models)
        {
            List<string> safe = new List<string>();
            if (models == null) return safe.ToArray();
            foreach (string model in models)
            {
                string id = (model ?? "").Trim();
                if ((IsFreeRouter(id) || (FreeModel.IsMatch(id) && VerifiedFree.Contains(id))) && !safe.Contains(id, StringComparer.OrdinalIgnoreCase)) safe.Add(id);
            }
            return safe.ToArray();
        }

        internal static bool IsFreeModel(string id) { return id != null && FreeModel.IsMatch(id.Trim()); }

        internal static bool IsFreeRouter(string id) { return string.Equals(id, "openrouter/free", StringComparison.Ordinal); }

        internal static string[] WithoutRouter(IEnumerable<string> models)
        {
            string[] valid = ValidatePool(models);
            return valid.Length > 0 && IsFreeRouter(valid[0]) ? new[] { valid[0] } : valid.Where(delegate(string id) { return !IsFreeRouter(id); }).ToArray();
        }

        internal static OpenRouterRequest Request(IEnumerable<string> models, ChatMessage[] messages, bool tools, int maxTokens = 1000)
        {
            string[] ids = ValidatePool(models);
            if (ids.Length == 0) throw new InvalidOperationException("The OpenRouter pool has no verified free models.");
            if (ids.Any(IsFreeRouter) && (ids.Length != 1 || !IsFreeRouter(ids[0]))) throw new InvalidOperationException("openrouter/free is a standalone router and cannot be mixed into the ordered model fallback list.");
            OpenRouterRequest request = new OpenRouterRequest { Models = ids, Messages = messages ?? new ChatMessage[0], Stream = false, MaxTokens = Math.Max(1, maxTokens) };
            if (tools) request.Tools = ParseTools(Hands.Tools);
            return request;
        }

        private static object ParseTools(string json)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json ?? "[]")))
                return new DataContractJsonSerializer(typeof(object[])).ReadObject(stream);
        }

        internal static OpenRouterResponse ParseResponse(string json)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json ?? "{}")))
                return (OpenRouterResponse)new DataContractJsonSerializer(typeof(OpenRouterResponse)).ReadObject(stream);
        }

        internal static bool Retryable(int httpStatus, int providerCode)
        {
            if (httpStatus == 401 || httpStatus == 403) return false;
            return httpStatus == 408 || httpStatus == 409 || httpStatus == 429 || httpStatus >= 500 || providerCode == 429 || providerCode >= 500;
        }

        internal static string ErrorMessage(int status)
        {
            if (status == 401 || status == 403) return "OpenRouter rechazó la clave. Revisa la conexión en la configuración de Capri.";
            if (status == 402) return "OpenRouter no aceptó la solicitud gratuita; no se intentó usar ningún modelo de pago.";
            if (status == 429) return "Los modelos gratuitos de OpenRouter están limitados temporalmente. Capri probará el siguiente modelo gratuito.";
            if (status == 408 || status >= 500) return "OpenRouter está temporalmente indisponible. Capri probará el siguiente modelo gratuito.";
            return "OpenRouter no pudo completar la solicitud gratuita (HTTP " + status + ").";
        }

        internal static string Next(string current, IEnumerable<string> models, ISet<string> skipped)
        {
            foreach (string id in ValidatePool(models)) if (id != current && (skipped == null || !skipped.Contains(id))) return id;
            return null;
        }

        internal string EndpointForTest { get { return Endpoint; } }
    }
}
