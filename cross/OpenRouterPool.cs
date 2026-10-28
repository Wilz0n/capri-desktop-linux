using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
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
    internal sealed class OpenRouterQuotaResponse
    {
        [DataMember(Name = "data")] internal OpenRouterQuotaData Data;
    }

    [DataContract]
    internal sealed class OpenRouterDailyQuota
    {
        [DataMember(Name = "used")] internal int Used;
        [DataMember(Name = "limit")] internal int Limit;
        [DataMember(Name = "remaining")] internal int Remaining;
    }

    [DataContract]
    internal sealed class OpenRouterQuotaData
    {
        [DataMember(Name = "free_model_daily_requests")] internal OpenRouterDailyQuota FreeModelDailyRequests;
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
        [DataMember(Name = "extra_content", EmitDefaultValue = false)] internal OpenRouterExtraContent ExtraContent;
        internal ToolArguments ArgumentsObject;
    }

    [DataContract]
    internal sealed class OpenRouterExtraContent
    {
        [DataMember(Name = "google", EmitDefaultValue = false)] internal OpenRouterGoogleExtra Google;
    }

    [DataContract]
    internal sealed class OpenRouterGoogleExtra
    {
        [DataMember(Name = "thought_signature", EmitDefaultValue = false)] internal string ThoughtSignature;
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
        internal static string[] Requested =
        {
            "nvidia/nemotron-3.5-lightning:free",
            "google/gemma-4-26b-a4b-it:free",
            "cohere/north-mini-code:free",
            "nvidia/nemotron-3-ultra-550b-a55b:free",
            "google/gemma-4-31b-it:free",
            "thinkingmachines/inkling:free",
            "thinkingmachines/inkling-small:free",
            "liquid/lfm-2.5-2.6b:free",
            "recursive/flint-v1.0:free",
            "xingchen-agi/xing4.0-29b-a4b:free",
            "nvidia/nemotron-3.5-super-vl:free",
            "dots-studio/dots-3-note-preview:free",
            "poolside/laguna-xs-2.1:free",
            "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free",
            "google/gemma-4-26b-a4b-it:free"
        };
        internal static readonly string[] PreferredDefaults =
        {
            "nvidia/nemotron-3.5-lightning:free",
            "google/gemma-4-26b-a4b-it:free",
            "cohere/north-mini-code:free"
        };
        internal static Func<string, string[]> CatalogOverride;
        internal const string FreeRouter = "openrouter/free";
        private static readonly Regex FreeModel = new Regex(@"\A[a-z0-9][a-z0-9._-]*/[a-z0-9][a-z0-9._-]*:free\z", RegexOptions.IgnoreCase);
        private static readonly HashSet<string> VerifiedFree = new HashSet<string>(Requested.Where(delegate(string id) { return id != FreeRouter; }), StringComparer.OrdinalIgnoreCase);

        internal readonly string Key;
        internal readonly string[] Models;
        internal string LastModel;
        internal string LastUsage;
        internal DateTime BlockedUntil;
        internal int DailyUsed = -1;
        internal int DailyLimit = -1;
        internal int DailyRemaining = -1;
        internal DateTime LastQuotaCheck;
        internal readonly Dictionary<string, DateTime> Cooling = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        internal OpenRouterPool(string key, IEnumerable<string> models = null)
        {
            Key = (key ?? "").Trim();
            Models = ValidatePool(models ?? Requested);
        }

        internal static OpenRouterPool Automatic(string key, string preferred = null)
        {
            string[] models = CatalogOverride == null ? OpenRouterCatalog.Load(preferred) : OpenRouterCatalog.Order(CatalogOverride(preferred), preferred);
            return new OpenRouterPool(key, models);
        }

        internal static string[] ValidatePool(IEnumerable<string> models)
        {
            List<string> safe = new List<string>();
            if (models == null) return safe.ToArray();
            foreach (string model in models)
            {
                string id = (model ?? "").Trim();
                if ((IsFreeRouter(id) || FreeModel.IsMatch(id)) && !safe.Contains(id, StringComparer.OrdinalIgnoreCase)) safe.Add(id);
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
            if (httpStatus == 402) return false;
            return httpStatus == 408 || httpStatus == 409 || httpStatus == 429 || httpStatus >= 500 || providerCode == 429 || providerCode >= 500;
        }

        internal static string ErrorMessage(int status)
        {
            if (status == 401 || status == 403) return "OpenRouter rechazó la clave. Revisa la conexión en la configuración de Capri.";
            if (status == 402) return "OpenRouter no aceptó la solicitud. Capri no usará modelos de pago.";
            if (status == 429) return "Los modelos gratuitos de OpenRouter están limitados temporalmente. Capri probará otros modelos gratuitos.";
            if (status == 408 || status >= 500) return "OpenRouter está temporalmente indisponible. Capri probará el siguiente modelo gratuito.";
            return "OpenRouter no pudo completar la solicitud gratuita (HTTP " + status + ").";
        }

        internal static string Next(string current, IEnumerable<string> models, ISet<string> skipped)
        {
            foreach (string id in ValidatePool(models)) if (id != current && (skipped == null || !skipped.Contains(id))) return id;
            return null;
        }

        internal bool IsBlocked { get { return BlockedUntil > DateTime.UtcNow; } }

        internal void SetDailyQuota(int used, int limit, int remaining)
        {
            DailyUsed = Math.Max(0, used);
            DailyLimit = Math.Max(0, limit);
            DailyRemaining = Math.Max(0, remaining);
            LastQuotaCheck = DateTime.UtcNow;
            if (DailyRemaining == 0) BlockedUntil = NextDailyResetUtc(DateTime.UtcNow);
            else BlockedUntil = DateTime.MinValue;
        }

        internal void BlockDaily(DateTime untilUtc)
        {
            BlockedUntil = untilUtc > DateTime.UtcNow ? untilUtc : NextDailyResetUtc(DateTime.UtcNow);
            DailyRemaining = 0;
        }

        internal void RefreshBlockedQuota()
        {
            if (!IsBlocked || DateTime.UtcNow - LastQuotaCheck < TimeSpan.FromMinutes(5)) return;
            OpenRouterWire.RefreshQuota(this);
        }

        internal string QuotaText()
        {
            if (DailyLimit < 0) return "Free requests quota not checked";
            return "Today: " + DailyUsed + "/" + DailyLimit + " free requests · " + DailyRemaining + " remaining";
        }

        internal static DateTime NextDailyResetUtc(DateTime nowUtc)
        {
            DateTime tomorrow = nowUtc.Date.AddDays(1);
            return DateTime.SpecifyKind(tomorrow, DateTimeKind.Utc);
        }

        internal void Cooldown(string model, TimeSpan duration)
        {
            if (!string.IsNullOrWhiteSpace(model)) Cooling[model] = DateTime.UtcNow.Add(duration);
        }

        internal bool IsCooling(string model)
        {
            DateTime until;
            return Cooling.TryGetValue(model ?? "", out until) && until > DateTime.UtcNow;
        }

        internal static string DailyLimitMessage(DateTime resetUtc)
        {
            DateTime local = resetUtc.ToLocalTime();
            return "Se agotó el límite diario gratuito de OpenRouter. Se restablece a las " + local.ToString("h:mm tt", CultureInfo.CurrentCulture) + " (hora local). Capri no usa modelos de pago.";
        }

        internal string EndpointForTest { get { return Endpoint; } }
    }
}
