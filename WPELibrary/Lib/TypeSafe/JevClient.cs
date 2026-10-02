using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.TypeSafe
{
    public sealed class JevClientOptions
    {
        public const string DefaultEndpoint = "https://api.typesafe.ai/v1/systemone";
        public const string DefaultModel = "jev-latest";

        public JevClientOptions(
            string apiKey,
            string endpoint = DefaultEndpoint,
            string model = DefaultModel,
            TimeSpan? timeout = null,
            int maxTransientRetries = 2)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new ArgumentException("A TypeSafe API endpoint is required.", "endpoint");
            }

            Uri endpointUri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out endpointUri))
            {
                throw new ArgumentException("The TypeSafe API endpoint must be an absolute URI.", "endpoint");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("A TypeSafe model is required.", "model");
            }

            if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException("timeout", "The TypeSafe request timeout must be positive.");
            }

            if (maxTransientRetries < 0 || maxTransientRetries > 5)
            {
                throw new ArgumentOutOfRangeException(
                    "maxTransientRetries",
                    "Transient retries must be between 0 and 5.");
            }

            ApiKey = apiKey ?? string.Empty;
            Endpoint = endpointUri;
            Model = model;
            Timeout = timeout ?? TimeSpan.FromSeconds(30);
            MaxTransientRetries = maxTransientRetries;
        }

        public static JevClientOptions FromEnvironment()
        {
            string apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable(
                    "TYPESAFE_API_KEY",
                    EnvironmentVariableTarget.User);
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable(
                    "TYPESAFE_API_KEY",
                    EnvironmentVariableTarget.Machine);
            }

            return new JevClientOptions(apiKey);
        }

        public string ApiKey { get; private set; }

        public Uri Endpoint { get; private set; }

        public string Model { get; private set; }

        public TimeSpan Timeout { get; private set; }

        public int MaxTransientRetries { get; private set; }

        internal void ValidateApiKey()
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new InvalidOperationException(
                    "TYPESAFE_API_KEY is not configured. Store the TypeSafe API key outside source control.");
            }
        }
    }

    public sealed class JevQuestion
    {
        private JevQuestion(string type, JToken instructions, JToken criteria)
        {
            Type = type;
            Instructions = instructions;
            Criteria = criteria;
        }

        public string Type { get; private set; }

        public JToken Instructions { get; private set; }

        public JToken Criteria { get; private set; }

        public static JevQuestion Choice(
            string instructions,
            IDictionary<string, string> criteria)
        {
            EnsureInstructions(instructions);
            if (criteria == null || criteria.Count == 0 || criteria.Count > 255)
            {
                throw new ArgumentException(
                    "A Choice question must contain between 1 and 255 criteria.",
                    "criteria");
            }

            return new JevQuestion(
                "choice",
                JValue.CreateString(instructions),
                JObject.FromObject(criteria));
        }

        public static JevQuestion Score(
            string instructions,
            IList<string> criteria)
        {
            EnsureInstructions(instructions);
            if (criteria == null || criteria.Count < 2 || criteria.Count > 10)
            {
                throw new ArgumentException(
                    "A Score question must contain between 2 and 10 criteria.",
                    "criteria");
            }

            for (int i = 0; i < criteria.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(criteria[i]))
                {
                    throw new ArgumentException(
                        "Score criteria cannot be empty.",
                        "criteria");
                }
            }

            return new JevQuestion(
                "score",
                JValue.CreateString(instructions),
                JArray.FromObject(criteria));
        }

        public static JevQuestion Noul(string instructions)
        {
            EnsureInstructions(instructions);
            return new JevQuestion(
                "noul",
                JValue.CreateString(instructions),
                null);
        }

        public static JevQuestion Noul(
            string instructions,
            string trueDescription,
            string falseDescription)
        {
            EnsureInstructions(instructions);
            if (string.IsNullOrWhiteSpace(trueDescription) ||
                string.IsNullOrWhiteSpace(falseDescription))
            {
                throw new ArgumentException(
                    "Noul criteria must describe both true and false.");
            }

            return new JevQuestion(
                "noul",
                JValue.CreateString(instructions),
                new JObject
                {
                    ["true"] = trueDescription,
                    ["false"] = falseDescription
                });
        }

        internal JObject ToJson()
        {
            JObject json = new JObject
            {
                ["type"] = Type,
                ["instructions"] = Instructions.DeepClone()
            };
            if (Criteria != null)
            {
                json["criteria"] = Criteria.DeepClone();
            }

            return json;
        }

        private static void EnsureInstructions(string instructions)
        {
            if (string.IsNullOrWhiteSpace(instructions))
            {
                throw new ArgumentException(
                    "A TypeSafe question must contain instructions.",
                    "instructions");
            }
        }
    }

    public sealed class JevRequest
    {
        public JevRequest(
            JToken state,
            IDictionary<string, JevQuestion> questions,
            string model = JevClientOptions.DefaultModel)
        {
            if (state == null || state.Type == JTokenType.Null || state.Type == JTokenType.Undefined)
            {
                throw new ArgumentNullException("state");
            }
            if (questions == null || questions.Count == 0)
            {
                throw new ArgumentException(
                    "At least one TypeSafe question is required.",
                    "questions");
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("A TypeSafe model is required.", "model");
            }

            State = state.DeepClone();
            Model = model;
            Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JevQuestion> entry in questions)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    throw new ArgumentException(
                        "TypeSafe question IDs cannot be empty.",
                        "questions");
                }
                if (entry.Value == null)
                {
                    throw new ArgumentException(
                        "TypeSafe questions cannot be null.",
                        "questions");
                }

                Questions.Add(entry.Key, entry.Value);
            }
        }

        public JToken State { get; private set; }

        public string Model { get; private set; }

        public IDictionary<string, JevQuestion> Questions { get; private set; }

        public JObject ToJson()
        {
            JObject questions = new JObject();
            foreach (KeyValuePair<string, JevQuestion> entry in Questions)
            {
                questions.Add(entry.Key, entry.Value.ToJson());
            }

            return new JObject
            {
                ["state"] = State.DeepClone(),
                ["model"] = Model,
                ["questions"] = questions
            };
        }
    }

    public sealed class JevResponse
    {
        private JevResponse(
            string model,
            IDictionary<string, JevAnswer> answers,
            JevUsage usage)
        {
            Model = model;
            Answers = answers;
            Usage = usage;
        }

        public string Model { get; private set; }

        public IDictionary<string, JevAnswer> Answers { get; private set; }

        public JevUsage Usage { get; private set; }

        public static JevResponse Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("TypeSafe returned an empty response.");
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "TypeSafe returned invalid JSON.",
                    ex);
            }

            string model = RequireString(root, "model");
            JObject answerObject = root["answers"] as JObject;
            if (answerObject == null || answerObject.Count == 0)
            {
                throw new InvalidOperationException(
                    "TypeSafe response is missing answers.");
            }

            Dictionary<string, JevAnswer> answers =
                new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
            foreach (JProperty property in answerObject.Properties())
            {
                JObject answer = property.Value as JObject;
                if (answer == null)
                {
                    throw new InvalidOperationException(
                        "TypeSafe answer '" + property.Name + "' is not an object.");
                }

                answers.Add(property.Name, JevAnswer.Parse(property.Name, answer));
            }

            JObject usageObject = root["usage"] as JObject;
            if (usageObject == null)
            {
                throw new InvalidOperationException(
                    "TypeSafe response is missing usage.");
            }

            return new JevResponse(
                model,
                answers,
                new JevUsage(
                    RequireInt(usageObject, "input_tokens"),
                    RequireInt(usageObject, "output_tokens")));
        }

        private static string RequireString(JObject json, string name)
        {
            string value = json.Value<string>(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    "TypeSafe response is missing '" + name + "'.");
            }

            return value;
        }

        private static int RequireInt(JObject json, string name)
        {
            JToken token = json[name];
            int value;
            if (token == null ||
                !int.TryParse(
                    token.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value) ||
                value < 0)
            {
                throw new InvalidOperationException(
                    "TypeSafe response contains an invalid '" + name + "'.");
            }

            return value;
        }
    }

    public sealed class JevAnswer
    {
        private JevAnswer(
            string id,
            string type,
            string choice,
            double? score,
            double? noul,
            IDictionary<string, double> probabilities,
            IDictionary<string, string> legend,
            double? confidence,
            JObject raw)
        {
            Id = id;
            Type = type;
            Choice = choice;
            Score = score;
            Noul = noul;
            Probabilities = probabilities;
            Legend = legend;
            Confidence = confidence;
            Raw = raw;
        }

        public string Id { get; private set; }

        public string Type { get; private set; }

        public string Choice { get; private set; }

        public double? Score { get; private set; }

        public double? Noul { get; private set; }

        public IDictionary<string, double> Probabilities { get; private set; }

        public IDictionary<string, string> Legend { get; private set; }

        public double? Confidence { get; private set; }

        public JObject Raw { get; private set; }

        internal static JevAnswer Parse(string id, JObject json)
        {
            string type = json.Value<string>("type");
            if (string.Equals(type, "noul", StringComparison.Ordinal))
            {
                double noul = RequireNumber(json, "noul");
                EnsureRange(noul, 0D, 1D, "noul");
                return new JevAnswer(
                    id,
                    type,
                    null,
                    null,
                    noul,
                    null,
                    null,
                    null,
                    (JObject)json.DeepClone());
            }

            if (string.Equals(type, "choice", StringComparison.Ordinal))
            {
                string choice = json.Value<string>("choice");
                if (string.IsNullOrWhiteSpace(choice))
                {
                    throw new InvalidOperationException(
                        "TypeSafe Choice answer '" + id + "' is missing choice.");
                }

                IDictionary<string, double> probabilities = ReadProbabilities(json, id);
                double confidence = RequireNumber(json, "confidence");
                EnsureRange(confidence, 0D, 1D, "confidence");
                if (!probabilities.ContainsKey(choice))
                {
                    throw new InvalidOperationException(
                        "TypeSafe Choice answer '" + id + "' has no probability for its choice.");
                }

                return new JevAnswer(
                    id,
                    type,
                    choice,
                    null,
                    null,
                    probabilities,
                    null,
                    confidence,
                    (JObject)json.DeepClone());
            }

            if (string.Equals(type, "score", StringComparison.Ordinal))
            {
                double score = RequireNumber(json, "score");
                IDictionary<string, string> legend = ReadLegend(json, id);
                IDictionary<string, double> probabilities = ReadProbabilities(json, id);
                double confidence = RequireNumber(json, "confidence");
                EnsureRange(confidence, 0D, 1D, "confidence");
                foreach (string level in probabilities.Keys)
                {
                    if (!legend.ContainsKey(level))
                    {
                        throw new InvalidOperationException(
                            "TypeSafe Score answer '" + id + "' has an unknown probability level.");
                    }
                }

                return new JevAnswer(
                    id,
                    type,
                    null,
                    score,
                    null,
                    probabilities,
                    legend,
                    confidence,
                    (JObject)json.DeepClone());
            }

            throw new InvalidOperationException(
                "TypeSafe returned unsupported answer type for '" + id + "'.");
        }

        private static IDictionary<string, double> ReadProbabilities(
            JObject json,
            string id)
        {
            JObject probabilityObject = json["probabilities"] as JObject;
            if (probabilityObject == null || probabilityObject.Count == 0)
            {
                throw new InvalidOperationException(
                    "TypeSafe answer '" + id + "' is missing probabilities.");
            }

            Dictionary<string, double> probabilities =
                new Dictionary<string, double>(StringComparer.Ordinal);
            double total = 0D;
            foreach (JProperty property in probabilityObject.Properties())
            {
                double value = RequireNumber(property.Value, property.Name);
                EnsureRange(value, 0D, 1D, "probability");
                total += value;
                probabilities.Add(property.Name, value);
            }

            if (Math.Abs(total - 1D) > 0.02D)
            {
                throw new InvalidOperationException(
                    "TypeSafe answer '" + id + "' probabilities do not sum to 1.");
            }

            return probabilities;
        }

        private static IDictionary<string, string> ReadLegend(
            JObject json,
            string id)
        {
            JObject legendObject = json["legend"] as JObject;
            if (legendObject == null || legendObject.Count == 0)
            {
                throw new InvalidOperationException(
                    "TypeSafe Score answer '" + id + "' is missing legend.");
            }

            Dictionary<string, string> legend =
                new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JProperty property in legendObject.Properties())
            {
                if (string.IsNullOrWhiteSpace(property.Value.Value<string>()))
                {
                    throw new InvalidOperationException(
                        "TypeSafe Score answer '" + id + "' has an empty legend entry.");
                }

                legend.Add(property.Name, property.Value.Value<string>());
            }

            return legend;
        }

        private static double RequireNumber(JObject json, string name)
        {
            return RequireNumber(json[name], name);
        }

        private static double RequireNumber(JToken token, string name)
        {
            double value;
            if (token == null ||
                !double.TryParse(
                    token.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) ||
                double.IsNaN(value) ||
                double.IsInfinity(value))
            {
                throw new InvalidOperationException(
                    "TypeSafe response contains an invalid '" + name + "'.");
            }

            return value;
        }

        private static void EnsureRange(
            double value,
            double minimum,
            double maximum,
            string name)
        {
            if (value < minimum || value > maximum)
            {
                throw new InvalidOperationException(
                    "TypeSafe response contains an out-of-range '" + name + "'.");
            }
        }
    }

    public sealed class JevUsage
    {
        internal JevUsage(int inputTokens, int outputTokens)
        {
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
        }

        public int InputTokens { get; private set; }

        public int OutputTokens { get; private set; }
    }

    public sealed class JevRequestException : Exception
    {
        internal JevRequestException(int statusCode, string responseBody)
            : base("TypeSafe Jev request failed with HTTP status " + statusCode + ".")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody ?? string.Empty;
        }

        public int StatusCode { get; private set; }

        public string ResponseBody { get; private set; }
    }

    public sealed class JevClient : IDisposable
    {
        private readonly JevClientOptions options;
        private readonly HttpClient httpClient;
        private readonly bool ownsHttpClient;
        private int disposed;

        public JevClient()
            : this(JevClientOptions.FromEnvironment())
        {
        }

        public JevClient(JevClientOptions options)
            : this(options, new HttpClient(), true)
        {
        }

        public JevClient(JevClientOptions options, HttpClient httpClient)
            : this(options, httpClient, false)
        {
        }

        public JevClientOptions Options
        {
            get { return options; }
        }

        public async Task<JevResponse> EvaluateAsync(
            JToken state,
            IDictionary<string, JevQuestion> questions,
            CancellationToken cancellationToken)
        {
            return await EvaluateAsync(
                new JevRequest(state, questions, options.Model),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<JevResponse> EvaluateAsync(
            JevRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException("JevClient");
            }

            options.ValidateApiKey();
            string payload = request.ToJson().ToString(Formatting.None);
            int retry = 0;
            while (true)
            {
                using (HttpRequestMessage httpRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    options.Endpoint))
                {
                    httpRequest.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", options.ApiKey);
                    httpRequest.Content = new StringContent(
                        payload,
                        Encoding.UTF8,
                        "application/json");

                    using (HttpResponseMessage response = await httpClient.SendAsync(
                        httpRequest,
                        HttpCompletionOption.ResponseContentRead,
                        cancellationToken).ConfigureAwait(false))
                    {
                        string responseBody = await response.Content.ReadAsStringAsync()
                            .ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            return JevResponse.Parse(responseBody);
                        }

                        if (!IsTransient(response.StatusCode) ||
                            retry >= options.MaxTransientRetries)
                        {
                            throw new JevRequestException(
                                (int)response.StatusCode,
                                responseBody);
                        }

                        int delayMilliseconds = GetRetryDelayMilliseconds(response, retry);
                        retry++;
                        await Task.Delay(delayMilliseconds, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            if (ownsHttpClient)
            {
                httpClient.Dispose();
            }
        }

        private JevClient(
            JevClientOptions options,
            HttpClient httpClient,
            bool ownsHttpClient)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            if (httpClient == null)
            {
                throw new ArgumentNullException("httpClient");
            }

            this.options = options;
            this.httpClient = httpClient;
            this.ownsHttpClient = ownsHttpClient;
            this.httpClient.Timeout = options.Timeout;
        }

        private static bool IsTransient(System.Net.HttpStatusCode statusCode)
        {
            return (int)statusCode == 429 ||
                (int)statusCode == 529;
        }

        private static int GetRetryDelayMilliseconds(
            HttpResponseMessage response,
            int retry)
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    double milliseconds = response.Headers.RetryAfter.Delta.Value.TotalMilliseconds;
                    return (int)Math.Min(30000D, Math.Max(1D, milliseconds));
                }

                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    double milliseconds =
                        (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalMilliseconds;
                    return (int)Math.Min(30000D, Math.Max(1D, milliseconds));
                }
            }

            int exponent = Math.Min(retry, 6);
            return 250 * (1 << exponent);
        }
    }
}
